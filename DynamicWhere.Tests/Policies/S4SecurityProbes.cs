using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Xunit.Abstractions;

#pragma warning disable xUnit1031 // the segment surface is async only; these probes drive it from a synchronous table

namespace DynamicWhere.Tests.Policies
{
    // =============================================================================================
    // Round 4, adversarial security review of 3.3.0 at 56d169d.
    //
    // Round 3's probes asked the OVER-block question: "unguarded ran AND guarded refused".
    // These ask the UNDER-block one the release's own promise rests on:
    //
    //     unguarded threw the provider's translation failure
    //     AND guarded (Strict) threw it too
    //     -> the strict tier answered with neither an answer nor a refusal.
    //
    // Everything here drives ApplyPolicy through its explicit posture overload, so no probe
    // touches DwPolicy's process-wide state.
    // =============================================================================================
    public sealed class S4SecurityProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly S4Context _db;
        private readonly List<string> _findings = new();

        public S4SecurityProbes(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new S4Context(_connection);
            _db.Database.EnsureCreated();

            S4Customer customer = new() { Name = "Acme" };

            S4Order order = new()
            {
                Code = "AB123",
                Qty = 2,
                Customer = customer,
                Total = new S4Money { Amount = 10m, Currency = "USD" }
            };

            order.Lines.Add(new S4Line { Price = 4m });
            order.Lines.Add(new S4Line { Price = 6m });

            _db.Customers.Add(customer);
            _db.Orders.Add(order);
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        // ---- harness -----------------------------------------------------------------------------

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict)
            where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static Filter WhereOn(string field, DataType type, string value) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition { Field = field, DataType = type, Operator = Operator.Equal, Values = { value } }
                }
            }
        };

        private static string Outcome(Func<object?> run)
        {
            try
            {
                object? value = run();

                return value is System.Collections.ICollection rows ? $"OK({rows.Count})" : "OK";
            }
            catch (PolicyException refusal)
            {
                return $"REFUSED({refusal.ErrorCode})";
            }
            catch (LogicException failure)
            {
                return $"LogicException({failure.Message.Split('.')[0]})";
            }
            catch (Exception failure)
            {
                return failure.GetType().Name;
            }
        }

        private static string GuardedWhere<T>(IQueryable<T> source, string field, DataType type, string value)
            where T : class =>
            Outcome(() => Guard(source).ToList(WhereOn(field, type, value)).Data);

        private static string RawWhere<T>(IQueryable<T> source, Expression<Func<T, bool>> predicate) =>
            Outcome(() => source.Where(predicate).ToList());

        /// <summary>True for the provider's own "I cannot translate this" failure.</summary>
        private static bool Untranslatable(string outcome) =>
            outcome is "InvalidOperationException" or "NotSupportedException";

        /// <summary>
        /// Records one probe. Flags it when the unguarded query fails inside the provider and the
        /// guarded strict one fails the same way: neither an answer nor a refusal.
        /// </summary>
        private void Under(string probe, string unguarded, string guarded)
        {
            _out.WriteLine($"{probe,-62} unguarded={unguarded,-28} guarded={guarded}");

            if (Untranslatable(unguarded) && Untranslatable(guarded))
            {
                _findings.Add($"{probe}: unguarded {unguarded}, guarded {guarded}");
            }
        }

        /// <summary>Logged, never flagged: a shape the release's own text puts outside the refusal.</summary>
        private void Documented(string probe, string unguarded, string guarded) =>
            _out.WriteLine($"{probe,-62} unguarded={unguarded,-28} guarded={guarded}   [documented limit]");

        private void Done() => Assert.True(_findings.Count == 0, string.Join(" || ", _findings));

        // =========================================================================================
        // S4-A. Every clause the release names, over an entity source. The regression floor.
        // =========================================================================================

        [Fact]
        public void S4_A_Every_clause_the_release_names_refuses_an_uncomputable_path()
        {
            IQueryable<S4Order> orders = _db.Orders;

            Under("A1 where, unmapped getter on the entity",
                RawWhere(orders, o => o.Slug == "AB123-1"),
                GuardedWhere(orders, "Slug", DataType.Text, "AB123-1"));

            Under("A2 where, getter over an owned type's columns",
                RawWhere(orders, o => o.Total.IsZero),
                GuardedWhere(orders, "Total.IsZero", DataType.Boolean, "false"));

            Under("A3 where, unmapped getter one navigation away",
                RawWhere(orders, o => o.Customer.Handle == "Acme!"),
                GuardedWhere(orders, "Customer.Handle", DataType.Text, "Acme!"));

            Under("A4 order by the unmapped getter",
                Outcome(() => orders.OrderBy(o => o.Slug).ToList()),
                Outcome(() => Guard(orders)
                    .ToList(new Filter { Orders = new List<OrderBy> { new() { Field = "Slug" } } }).Data));

            Under("A5 grouping key is the unmapped getter",
                Outcome(() => orders.GroupBy(o => o.Slug).Select(g => g.Key).ToList()),
                Outcome(() => Guard(orders).ToList(new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = { "Slug" },
                        AggregateBy = { new AggregateBy { Alias = "n", Aggregator = Aggregator.Count } }
                    }
                }).Data));

            Under("A6 aggregated field is the getter over owned columns",
                Outcome(() => orders.GroupBy(o => o.Code).Select(g => g.Max(o => o.Total.IsZero)).ToList()),
                Outcome(() => Guard(orders).ToList(new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = { "Code" },
                        AggregateBy = { new AggregateBy { Field = "Total.IsZero", Alias = "z", Aggregator = Aggregator.Maximum } }
                    }
                }).Data));

            Under("A7 a filter inside a Segment",
                RawWhere(orders, o => o.Slug == "AB123-1"),
                Outcome(() => Guard(orders).ToListAsync(new Segment
                {
                    ConditionSets =
                    {
                        new ConditionSet
                        {
                            Sort = 1,
                            ConditionGroup = new ConditionGroup
                            {
                                Conditions =
                                {
                                    new Condition
                                    {
                                        Field = "Slug", DataType = DataType.Text,
                                        Operator = Operator.Equal, Values = { "AB123-1" }
                                    }
                                }
                            }
                        }
                    }
                }).GetAwaiter().GetResult().Data));

            Under("A8 an order inside a Segment",
                Outcome(() => orders.OrderBy(o => o.Slug).ToList()),
                Outcome(() => Guard(orders).ToListAsync(new Segment
                {
                    ConditionSets =
                    {
                        new ConditionSet
                        {
                            Sort = 1,
                            ConditionGroup = new ConditionGroup
                            {
                                Conditions =
                                {
                                    new Condition
                                    {
                                        Field = "Code", DataType = DataType.Text,
                                        Operator = Operator.Equal, Values = { "AB123" }
                                    }
                                }
                            }
                        }
                    },
                    Orders = new List<OrderBy> { new() { Field = "Slug" } }
                }).GetAwaiter().GetResult().Data));

            Done();
        }

        // =========================================================================================
        // S4-B. The projection branch: which projected sources the refusal reaches, and which the
        //       release's own text leaves out.
        // =========================================================================================

        [Fact]
        public void S4_B_A_projection_the_shape_can_read_refuses_what_it_cannot_produce()
        {
            IQueryable<S4Row> built = _db.Orders.Select(o => new S4Row
            {
                Id = o.Id,
                Nest = new S4Nest { A = o.Code }
            });

            Under("B1 initializer, a member it never assigns",
                Outcome(() => _db.Orders.Select(o => new S4Row { Id = o.Id, Nest = new S4Nest { A = o.Code } })
                    .Where(r => r.Nest.B == "x").ToList()),
                GuardedWhere(built, "Nest.B", DataType.Text, "x"));

            Under("B2 initializer, a getter over what it assigns",
                Outcome(() => _db.Orders.Select(o => new S4Row { Id = o.Id, Nest = new S4Nest { A = o.Code } })
                    .Where(r => r.Nest.Blank).ToList()),
                GuardedWhere(built, "Nest.Blank", DataType.Boolean, "false"));

            IQueryable<S4Row> copied = _db.Orders.Select(o => new S4Row { Id = o.Id, Money = o.Total });

            Under("B3 owned member copied whole, the getter over its columns",
                Outcome(() => _db.Orders.Select(o => new S4Row { Id = o.Id, Money = o.Total })
                    .Where(r => r.Money.IsZero).ToList()),
                GuardedWhere(copied, "Money.IsZero", DataType.Boolean, "false"));

            // A member assigned from something the shape cannot read. Round 3 made this answer
            // "cannot say" instead of "yes"; neither answer refuses, so the provider still decides.
            IQueryable<S4Row> opaque = _db.Orders.Select(o => new S4Row
            {
                Id = o.Id,
                Nest = Build(o.Code)
            });

            Documented("B4 member assigned from a method call, a path beneath it",
                Outcome(() => _db.Orders.Select(o => new S4Row { Id = o.Id, Nest = Build(o.Code) })
                    .Where(r => r.Nest.B == "x").ToList()),
                GuardedWhere(opaque, "Nest.B", DataType.Text, "x"));

            Done();
        }

        private static S4Nest Build(string code) => new() { A = code };

        // =========================================================================================
        // S4-D. RowShape.Expresses, read directly. The tri-state answers the round-3 _opaque set
        //       produces, and whether any of them turned a proven "no" into "cannot say".
        // =========================================================================================

        [Fact]
        public void S4_D_The_opaque_set_never_takes_back_a_proven_no()
        {
            List<string> wrong = new();

            void Expect(string probe, IQueryable<S4Row> source, string path, bool? expected)
            {
                bool? answer = RowShape.Of(source).Expresses(path);

                _out.WriteLine($"{probe,-62} Expresses(\"{path}\") = {Show(answer)}   expected {Show(expected)}");

                if (answer != expected)
                {
                    wrong.Add($"{probe}: Expresses(\"{path}\") = {Show(answer)}, expected {Show(expected)}");
                }
            }

            // An ordinary initializer: the whole member set is known at every level.
            IQueryable<S4Row> plain = _db.Orders.Select(o => new S4Row
            {
                Id = o.Id,
                Nest = new S4Nest { A = o.Code }
            });

            Expect("D1 assigned member", plain, "Nest.A", true);
            Expect("D2 member the nested initializer leaves out", plain, "Nest.B", false);
            Expect("D3 getter over what it assigns", plain, "Nest.Blank", false);
            Expect("D4 member the outer initializer leaves out", plain, "Tag", false);

            // One sibling opaque: it must take back nothing from the sibling beside it.
            IQueryable<S4Row> sibling = _db.Orders.Select(o => new S4Row
            {
                Id = o.Id,
                Nest = new S4Nest { A = o.Code },
                Tag = Label(o.Code)
            });

            Expect("D5 opaque sibling, the readable one is unchanged", sibling, "Nest.B", false);
            Expect("D6 opaque sibling, the opaque one says nothing", sibling, "Tag", null);
            Expect("D7 unassigned member beside an opaque one", sibling, "Money.Amount", false);

            // The opaque member itself, and everything beneath it.
            IQueryable<S4Row> whole = _db.Orders.Select(o => new S4Row { Id = o.Id, Nest = Build(o.Code) });

            Expect("D8 opaque member", whole, "Nest", null);
            Expect("D9 beneath an opaque member", whole, "Nest.A", null);
            Expect("D10 deeper beneath an opaque member", whole, "Nest.Money.Amount", null);
            Expect("D11 a sibling the initializer never assigns", whole, "Tag", false);

            // One branch readable, the other not: the member is read from neither.
            IQueryable<S4Row> twoWays = _db.Orders.Select(o => new S4Row
            {
                Id = o.Id,
                Nest = o.Code == null ? new S4Nest { A = o.Code! } : Build(o.Code)
            });

            Expect("D12 one unreadable branch makes the member unreadable", twoWays, "Nest.B", null);
            Expect("D13 the other members are untouched", twoWays, "Tag", false);

            // Spelling: the caller writes a path in any case, with padding.
            Expect("D14 letter case", plain, "nest.b", false);
            Expect("D15 padded with spaces", plain, "Nest . B", false);
            Expect("D16 padded with dots", plain, "Nest..B", false);
            Expect("D17 opaque path in another case", whole, "NEST.A", null);

            Assert.True(wrong.Count == 0, string.Join(" || ", wrong));
        }

        private static string Label(string code) => code + "!";

        private static string Show(bool? value) => value is null ? "null" : value.Value ? "true" : "false";

        // =========================================================================================
        // S4-E. Path collisions in the _opaque set: members differing only in case, and member
        //       names that are prefixes of one another.
        // =========================================================================================

        [Fact]
        public void S4_E_Colliding_member_names_never_manufacture_a_refusal()
        {
            List<string> wrong = new();

            void Expect(string probe, bool? answer, bool?[] allowed)
            {
                _out.WriteLine($"{probe,-62} Expresses = {Show(answer)}");

                if (Array.IndexOf(allowed, answer) < 0)
                {
                    wrong.Add($"{probe}: Expresses = {Show(answer)}");
                }
            }

            // "Value" builds a readable nested row; "value" is opaque and collides with it under the
            // ordinal-ignore-case comparison every path string is matched with.
            IQueryable<S4CaseRow> collide = _db.Orders.Select(o => new S4CaseRow
            {
                Id = o.Id,
                Value = new S4Nest { A = o.Code },
                value = Label(o.Code)
            });

            RowShape shape = RowShape.Of(collide);

            // Only false is acted on. A collision may cost certainty; it must never invent a refusal
            // for a member the projection does assign.
            Expect("E1 assigned member under the colliding name", shape.Expresses("Value.A"), new bool?[] { true, null });
            Expect("E2 unassigned member under the colliding name", shape.Expresses("Value.B"), new bool?[] { false, null });
            Expect("E3 the opaque spelling itself", shape.Expresses("value"), new bool?[] { null, true });

            // Member names that are prefixes of one another: "AB" must not be read as beneath "A".
            IQueryable<S4PrefixRow> prefixes = _db.Orders.Select(o => new S4PrefixRow
            {
                Id = o.Id,
                A = new S4Nest { A = o.Code },
                AB = Label(o.Code)
            });

            RowShape prefixShape = RowShape.Of(prefixes);

            Expect("E4 a sibling whose name starts with an opaque one",
                prefixShape.Expresses("A.A"), new bool?[] { true });
            Expect("E5 the member the sibling's prefix would have hidden",
                prefixShape.Expresses("A.B"), new bool?[] { false });
            Expect("E6 the opaque sibling itself", prefixShape.Expresses("AB"), new bool?[] { null });

            // End to end: the readable member must still be filterable.
            _out.WriteLine("E7 guarded filter on the readable member of a colliding row: "
                           + GuardedWhere(collide, "Value.A", DataType.Text, "AB123"));

            Assert.True(wrong.Count == 0, string.Join(" || ", wrong));
        }

        // =========================================================================================
        // S4-F. EfCoreOwns. Which providers a real EF Core query reaches the shape with, and what
        //       a host's own registration does to the refusal.
        // =========================================================================================

        [Fact]
        public void S4_F_Every_EF_Core_shape_this_census_reaches_carries_EF_Cores_own_provider()
        {
            (string Name, IQueryable Source)[] shapes =
            {
                ("Set<T>()", _db.Set<S4Order>()),
                ("DbSet.AsQueryable()", _db.Orders.AsQueryable()),
                ("Include(string)", _db.Orders.Include("Customer")),
                ("Union", _db.Orders.Union(_db.Orders)),
                ("Concat", _db.Orders.Concat(_db.Orders)),
                ("Intersect", _db.Orders.Intersect(_db.Orders)),
                ("Except", _db.Orders.Except(_db.Orders)),
                ("Reverse", _db.Orders.OrderBy(o => o.Id).Reverse()),
                ("DefaultIfEmpty", _db.Orders.DefaultIfEmpty()),
                ("FromSqlInterpolated", _db.Orders.FromSqlInterpolated($"SELECT * FROM Orders")),
                ("Where+Select+Where", _db.Orders.Where(o => o.Id > 0)
                    .Select(o => new S4Row { Id = o.Id }).Where(r => r.Id > 0)),
                ("Entry().Collection().Query()", EntryQuery()),
                ("Cast<object>", _db.Orders.Cast<object>()),
                ("Skip+Take+Distinct", _db.Orders.OrderBy(o => o.Id).Skip(0).Take(5).Distinct())
            };

            List<string> lost = new();

            foreach ((string name, IQueryable source) in shapes)
            {
                string full = source.Provider.GetType().FullName ?? "?";
                bool owned = full == "Microsoft.EntityFrameworkCore.Query.Internal.EntityQueryProvider";

                _out.WriteLine($"{name,-34} EfCoreOwns={owned,-6} {full}");

                if (!owned)
                {
                    lost.Add($"{name} -> {full}");
                }
            }

            Assert.True(lost.Count == 0, "refusal silently lost on: " + string.Join(", ", lost));
        }

        private IQueryable<S4Line> EntryQuery()
        {
            S4Order tracked = _db.Orders.Include(o => o.Lines).First();

            return _db.Entry(tracked).Collection(o => o.Lines).Query();
        }

        // =========================================================================================
        // S4-G. A host that puts its own provider in EF Core's place, which is the documented
        //       ReplaceService extension point and leaves EF Core translating exactly as before.
        // =========================================================================================

        [Fact]
        public void S4_G_A_host_replaced_query_provider_still_refuses_what_EF_Core_cannot_compute()
        {
            using SqliteConnection connection = new("DataSource=:memory:");

            connection.Open();

            using S4Context replaced = new(connection, typeof(S4PassThroughProvider));

            replaced.Database.EnsureCreated();

            S4Customer customer = new() { Name = "Acme" };

            replaced.Customers.Add(customer);
            replaced.Orders.Add(new S4Order
            {
                Code = "AB123", Qty = 2, Customer = customer,
                Total = new S4Money { Amount = 10m, Currency = "USD" }
            });
            replaced.SaveChanges();
            replaced.ChangeTracker.Clear();

            IQueryable<S4Order> orders = replaced.Orders;

            _out.WriteLine($"provider = {orders.Provider.GetType().FullName}");
            _out.WriteLine($"rewrites anything = no (a plain pass-through)");

            // A host that replaces EF Core's query provider through ReplaceService gets a provider
            // of its own type, and the library cannot tell one that rewrites what EF Core cannot
            // translate from one that passes straight through. It leaves both alone: refusing on a
            // guess would take back a query the rewriting host answers today. Documented as a limit.
            string entityRaw = RawWhere(orders, o => o.Slug == "AB123-1");
            string entityGuarded = GuardedWhere(orders, "Slug", DataType.Text, "AB123-1");

            Documented("G1 entity branch, unmapped getter behind a replaced provider", entityRaw, entityGuarded);

            IQueryable<S4Row> projected = orders.Select(o => new S4Row
            {
                Id = o.Id,
                Nest = new S4Nest { A = o.Code }
            });

            string projectedRaw = Outcome(
                () => orders.Select(o => new S4Row { Id = o.Id, Nest = new S4Nest { A = o.Code } })
                    .Where(r => r.Nest.B == "x").ToList());

            string projectedGuarded = GuardedWhere(projected, "Nest.B", DataType.Text, "x");

            Documented("G2 projection branch, unassigned member behind a replaced provider", projectedRaw, projectedGuarded);

            // The guarded query does what the unguarded one does, which is the whole of the claim:
            // the refusal does not reach here, and nothing is refused that would otherwise run.
            Assert.Equal(entityRaw, entityGuarded);
            Assert.Equal(projectedRaw, projectedGuarded);

            Done();
        }

        // =========================================================================================
        // S4-H. The other direction of the name comparison: can anything make EfCoreOwns answer
        //       true for rows EF Core does not translate?
        // =========================================================================================

        [Fact]
        public void S4_H_A_type_of_EF_Cores_own_name_is_taken_for_EF_Cores_own_provider()
        {
            // A type declared in any assembly, with that namespace and that name. Emitted rather
            // than written, so the test assembly itself does not shadow EF Core's real type.
            Type spoofed = SpoofedProviderType();

            _out.WriteLine($"emitted type      = {spoofed.FullName}");
            _out.WriteLine($"from EF Core?     = {spoofed.Assembly.GetName().Name}");

            Assert.Equal("Microsoft.EntityFrameworkCore.Query.Internal.EntityQueryProvider", spoofed.FullName);

            // Rows in memory: LINQ to Objects runs the getter and no database is involved.
            List<S4Order> rows = _db.Orders.AsNoTracking().ToList();

            S4ForwardingProvider spoof = (S4ForwardingProvider)Activator.CreateInstance(spoofed)!;

            spoof.Inner = rows.AsQueryable().Provider;

            IQueryable<S4Row> wrapped = new S4SpoofedQueryable<S4Row>(
                spoof,
                spoof.Inner.CreateQuery<S4Row>(
                    rows.AsQueryable()
                        .Select(o => new S4Row { Id = o.Id, Nest = new S4Nest { A = o.Code } })
                        .Expression));

            RowShape shape = RowShape.Of(wrapped);

            string unguarded = Outcome(() => wrapped.Where(r => r.Nest.B == "x").ToList());
            string guarded = GuardedWhere(wrapped, "Nest.B", DataType.Text, "x");

            _out.WriteLine($"RowShape kind     = {shape.Kind}");
            _out.WriteLine($"Expresses(Nest.B) = {Show(shape.Expresses("Nest.B"))}");
            _out.WriteLine($"unguarded         = {unguarded}");
            _out.WriteLine($"guarded (strict)  = {guarded}");

            // The harm, if any, is over-block: rows that run in memory refused because a type name
            // said EF Core owned them. Recorded either way, asserted only as a statement of fact.
            _out.WriteLine(shape.Expresses("Nest.B") == false
                ? "SPOOFED: a name alone put these rows under EF Core's model"
                : "not spoofable through this route");

            // The provider test reads the assembly the type came from as well as its name, so a
            // type declared under EF Core's name elsewhere is not taken for EF Core's provider. The
            // rows run, as they do unguarded, rather than being refused on the strength of a name.
            Assert.Null(shape.Expresses("Nest.B"));
            Assert.StartsWith("OK", unguarded, StringComparison.Ordinal);
            Assert.StartsWith("OK", guarded, StringComparison.Ordinal);
        }

        private static Type SpoofedProviderType()
        {
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName("S4Spoof"), AssemblyBuilderAccess.Run);

            ModuleBuilder module = assembly.DefineDynamicModule("S4Spoof");

            TypeBuilder type = module.DefineType(
                "Microsoft.EntityFrameworkCore.Query.Internal.EntityQueryProvider",
                TypeAttributes.Public | TypeAttributes.Class,
                typeof(S4ForwardingProvider));

            type.DefineDefaultConstructor(MethodAttributes.Public);

            return type.CreateTypeInfo()!.AsType();
        }

        // =========================================================================================
        // S4-I. The refusal itself: a caller must not be able to tell a denied field, a name that
        //       matches nothing and a path the query cannot compute apart.
        // =========================================================================================

        [Fact]
        public void S4_I_Three_refusals_a_caller_must_not_be_able_to_tell_apart()
        {
            (string What, string Field)[] probes =
            {
                ("a name that matches nothing", "Zzzzz"),
                ("a path the query cannot compute", "Slug"),
                ("a two-segment name matching nothing", "Zzzzz.Yyyyy"),
                ("a two-segment path it cannot compute", "Total.IsZero")
            };

            List<string> seen = new();

            foreach ((string what, string field) in probes)
            {
                try
                {
                    Guard(_db.Orders).ToList(WhereOn(field, DataType.Text, "x"));

                    seen.Add($"{what}: NO REFUSAL");
                }
                catch (PolicyException refusal)
                {
                    string shape =
                        $"{refusal.ErrorCode}|{refusal.FieldPath}|{refusal.Feature}|"
                        + $"rule={refusal.RuleId ?? "-"}|origin={refusal.SourceOrigin ?? "-"}|"
                        + $"message={refusal.Message}";

                    _out.WriteLine($"{what,-42} {shape}");
                    seen.Add(shape);
                }
                catch (Exception failure)
                {
                    _out.WriteLine($"{what,-42} {failure.GetType().Name}: {failure.Message}");
                    seen.Add($"{what}: {failure.GetType().Name}");
                }
            }

            Assert.True(
                seen.Distinct(StringComparer.Ordinal).Count() == 1,
                "the four refusals differ: " + string.Join("  ||  ", seen.Distinct(StringComparer.Ordinal)));
        }

        // =========================================================================================
        // S4-J. Public Clone(): the caller's own request must be untouched by a guarded read, and
        //       nothing the library injects may become visible on it.
        // =========================================================================================

        [Fact]
        public void S4_J_A_guarded_read_leaves_the_callers_own_request_alone()
        {
            List<string> changed = new();

            Filter filter = new()
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition
                        {
                            Field = "code", DataType = DataType.Text,
                            Operator = Operator.Equal, Values = { "AB123" }
                        }
                    }
                },
                Selects = new List<string> { "code" },
                Orders = new List<OrderBy> { new() { Field = "code" } }
            };

            int conditions = filter.ConditionGroup.Conditions.Count;
            int groups = filter.ConditionGroup.SubConditionGroups.Count;
            string field = filter.ConditionGroup.Conditions[0].Field!;
            object value = filter.ConditionGroup.Conditions[0].Values[0];
            List<string> selects = new(filter.Selects);
            string orderField = filter.Orders[0].Field!;
            PageBy? page = filter.Page;

            _out.WriteLine("guarded read: " + Outcome(() => Guard(_db.Orders).ToList(filter).Data));

            if (filter.ConditionGroup.Conditions.Count != conditions
                || filter.ConditionGroup.SubConditionGroups.Count != groups)
            {
                changed.Add("the condition tree the caller sent grew");
            }

            if (!string.Equals(filter.ConditionGroup.Conditions[0].Field, field, StringComparison.Ordinal))
            {
                changed.Add($"the caller's field was rewritten: {field} -> {filter.ConditionGroup.Conditions[0].Field}");
            }

            if (!ReferenceEquals(filter.ConditionGroup.Conditions[0].Values[0], value))
            {
                changed.Add("the caller's value was replaced");
            }

            if (filter.Selects is null || !filter.Selects.SequenceEqual(selects, StringComparer.Ordinal))
            {
                changed.Add("the caller's projection list was rewritten");
            }

            if (!string.Equals(filter.Orders![0].Field, orderField, StringComparison.Ordinal))
            {
                changed.Add($"the caller's order was rewritten: {orderField} -> {filter.Orders[0].Field}");
            }

            if (!ReferenceEquals(filter.Page, page))
            {
                changed.Add("a page the caller never sent was written onto their filter");
            }

            // The same for a segment and a summary, whose Clone the release also made public.
            Segment segment = new()
            {
                ConditionSets =
                {
                    new ConditionSet
                    {
                        Sort = 1,
                        ConditionGroup = new ConditionGroup
                        {
                            Conditions =
                            {
                                new Condition
                                {
                                    Field = "code", DataType = DataType.Text,
                                    Operator = Operator.Equal, Values = { "AB123" }
                                }
                            }
                        }
                    }
                },
                Orders = new List<OrderBy> { new() { Field = "code" } }
            };

            string segmentField = segment.ConditionSets[0].ConditionGroup.Conditions[0].Field!;
            PageBy? segmentPage = segment.Page;

            _out.WriteLine("guarded segment: "
                           + Outcome(() => Guard(_db.Orders).ToListAsync(segment).GetAwaiter().GetResult().Data));

            if (!string.Equals(segment.ConditionSets[0].ConditionGroup.Conditions[0].Field, segmentField, StringComparison.Ordinal)
                || !ReferenceEquals(segment.Page, segmentPage))
            {
                changed.Add("the caller's segment was rewritten");
            }

            Summary summary = new()
            {
                GroupBy = new GroupBy
                {
                    Fields = { "code" },
                    AggregateBy = { new AggregateBy { Alias = "n", Aggregator = Aggregator.Count } }
                }
            };

            string key = summary.GroupBy.Fields[0];
            int aggregates = summary.GroupBy.AggregateBy.Count;
            ConditionGroup? having = summary.Having;

            _out.WriteLine("guarded summary: " + Outcome(() => Guard(_db.Orders).ToList(summary).Data));

            if (!string.Equals(summary.GroupBy.Fields[0], key, StringComparison.Ordinal)
                || summary.GroupBy.AggregateBy.Count != aggregates
                || !ReferenceEquals(summary.Having, having))
            {
                changed.Add("the caller's summary was rewritten — the group floor's own count is visible on it");
            }

            Assert.True(changed.Count == 0, string.Join(" || ", changed));
        }

        // =========================================================================================
        // S4-K. Clone() itself: what a caller now holds must share no node with what they cloned.
        // =========================================================================================

        [Fact]
        public void S4_K_A_public_clone_shares_no_node_a_later_rewrite_could_reach()
        {
            List<string> shared = new();

            Filter filter = new()
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions = { new Condition { Field = "Code", Values = { "a" } } },
                    SubConditionGroups =
                    {
                        new ConditionGroup { Conditions = { new Condition { Field = "Qty", Values = { "1" } } } }
                    }
                },
                Selects = new List<string> { "Code" },
                Orders = new List<OrderBy> { new() { Field = "Code" } },
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            Filter copy = filter.Clone();

            void Distinct(string what, object? left, object? right)
            {
                if (left is not null && ReferenceEquals(left, right))
                {
                    shared.Add(what);
                }
            }

            Distinct("Filter.ConditionGroup", filter.ConditionGroup, copy.ConditionGroup);
            Distinct("ConditionGroup.Conditions", filter.ConditionGroup!.Conditions, copy.ConditionGroup!.Conditions);
            Distinct("Conditions[0]", filter.ConditionGroup.Conditions[0], copy.ConditionGroup.Conditions[0]);
            Distinct("Conditions[0].Values", filter.ConditionGroup.Conditions[0].Values, copy.ConditionGroup.Conditions[0].Values);
            Distinct("SubConditionGroups", filter.ConditionGroup.SubConditionGroups, copy.ConditionGroup.SubConditionGroups);
            Distinct("SubConditionGroups[0]", filter.ConditionGroup.SubConditionGroups[0], copy.ConditionGroup.SubConditionGroups[0]);
            Distinct("SubConditionGroups[0].Conditions[0]",
                filter.ConditionGroup.SubConditionGroups[0].Conditions[0],
                copy.ConditionGroup.SubConditionGroups[0].Conditions[0]);
            Distinct("Selects", filter.Selects, copy.Selects);
            Distinct("Orders", filter.Orders, copy.Orders);
            Distinct("Orders[0]", filter.Orders![0], copy.Orders![0]);
            Distinct("Page", filter.Page, copy.Page);

            // Mutating the copy must not reach the original: this is what the release sells it for.
            copy.ConditionGroup.Conditions[0].Field = "Qty";
            copy.Selects!.Add("Qty");
            copy.Orders[0].Field = "Qty";
            copy.Page!.PageNumber = 2;

            if (filter.ConditionGroup.Conditions[0].Field != "Code") shared.Add("a rewrite of the copy reached the original's field");
            if (filter.Selects!.Count != 1) shared.Add("a rewrite of the copy reached the original's projection");
            if (filter.Orders[0].Field != "Code") shared.Add("a rewrite of the copy reached the original's order");
            if (filter.Page!.PageNumber != 1) shared.Add("a rewrite of the copy reached the original's page");

            _out.WriteLine($"shared nodes: {shared.Count}");

            Assert.True(shared.Count == 0, string.Join(" || ", shared));
        }
    }
}
