using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // =============================================================================================
    // Round 4, part B. Two questions the first file does not reach:
    //
    //   * does anything on the strict path answer a denied field differently from a name that
    //     matches nothing, so a caller can probe for which fields exist;
    //   * which projected sources the uncomputable-path refusal does not reach.
    // =============================================================================================

    public class S4Watched
    {
        public int Id { get; set; }

        /// <summary>Allowed, and audited: every use of it fills a slot in the caller's buffer.</summary>
        [DwAudit]
        public string Watched { get; set; } = string.Empty;

        /// <summary>Denied for filtering, and audited.</summary>
        [DwAudit]
        [DwDeny(PolicyFeature.Where)]
        public string Marked { get; set; } = string.Empty;

        /// <summary>Denied for filtering, not audited.</summary>
        [DwDeny(PolicyFeature.Where)]
        public string Plain { get; set; } = string.Empty;

        /// <summary>Denied for filtering, and expensive.</summary>
        [DwCost(10_000)]
        [DwDeny(PolicyFeature.Where)]
        public string Heavy { get; set; } = string.Empty;
    }

    public sealed class S4WatchedContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public S4WatchedContext(SqliteConnection connection) => _connection = connection;

        public DbSet<S4Watched> Rows => Set<S4Watched>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    public sealed class S4SecurityProbesB : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly S4WatchedContext _db;
        private readonly SqliteConnection _orderConnection;
        private readonly S4Context _orders;

        public S4SecurityProbesB(ITestOutputHelper output)
        {
            _out = output;

            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new S4WatchedContext(_connection);
            _db.Database.EnsureCreated();
            _db.Rows.Add(new S4Watched { Watched = "w", Marked = "m", Plain = "p", Heavy = "h" });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();

            _orderConnection = new SqliteConnection("DataSource=:memory:");
            _orderConnection.Open();
            _orders = new S4Context(_orderConnection);
            _orders.Database.EnsureCreated();

            S4Customer customer = new() { Name = "Acme" };

            _orders.Customers.Add(customer);
            _orders.Orders.Add(new S4Order
            {
                Code = "AB123", Qty = 2, Customer = customer,
                Total = new S4Money { Amount = 10m, Currency = "USD" }
            });
            _orders.SaveChanges();
            _orders.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
            _orders.Dispose();
            _orderConnection.Dispose();
        }

        // ---- harness -----------------------------------------------------------------------------

        private static Condition On(string field) => new()
        {
            Field = field, DataType = DataType.Text, Operator = Operator.Equal, Values = { "x" }
        };

        /// <summary>A strict posture whose audit buffer holds exactly one event.</summary>
        private static DwPolicyOptions OneAuditSlot()
        {
            DwPolicyOptions options = new() { Tier = DwTier.Strict };

            options.Caps.MaxAuditEvents = 1;

            return options;
        }

        private static string Refusal(IQueryable<S4Watched> source, DwPolicyOptions options, params string[] fields)
        {
            Filter filter = new() { ConditionGroup = new ConditionGroup() };

            foreach (string field in fields)
            {
                filter.ConditionGroup.Conditions.Add(On(field));
            }

            try
            {
                source.ApplyPolicy(
                        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                        options,
                        new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }))
                    .ToList(filter);

                return "NO REFUSAL";
            }
            catch (PolicyException refusal)
            {
                return $"{refusal.ErrorCode}|{refusal.FieldPath}|{refusal.Feature}";
            }
            catch (Exception failure)
            {
                return failure.GetType().Name;
            }
        }

        // =========================================================================================
        // S4-L. The audit buffer as an oracle.
        //
        // PolicyFor records the [DwAudit] event BEFORE the caller's policy is consulted, so a field
        // that is denied AND audited spends a slot the refusal it is about to raise does not need.
        // With the buffer full the audited field answers CapExceeded while a name matching nothing
        // answers FieldDeniedForWhere — which is the one thing the strict tier promises a caller
        // cannot tell apart.
        // =========================================================================================

        [Fact]
        public void S4_L_The_audit_cap_tells_a_denied_audited_field_from_a_name_matching_nothing()
        {
            DwPolicyOptions options = OneAuditSlot();

            // The first condition fills the single slot with an allowed, audited field.
            string denied = Refusal(_db.Rows, options, "Watched", "Marked");
            string unknown = Refusal(_db.Rows, options, "Watched", "Zzzzz");
            string plain = Refusal(_db.Rows, options, "Watched", "Plain");
            string heavy = Refusal(_db.Rows, options, "Watched", "Heavy");

            _out.WriteLine($"denied + audited      {denied}");
            _out.WriteLine($"name matching nothing {unknown}");
            _out.WriteLine($"denied, not audited   {plain}");
            _out.WriteLine($"denied, weighted      {heavy}");

            Assert.Equal(unknown, plain);
            Assert.Equal(unknown, heavy);

            Assert.True(
                string.Equals(denied, unknown, StringComparison.Ordinal),
                $"a denied audited field answers '{denied}' where a name matching nothing answers "
                + $"'{unknown}': the strict tier's refusals tell the two apart");
        }

        // =========================================================================================
        // S4-M. Projected sources the refusal does not reach. Pins what 56d169d does, so a change
        //       to any of them is deliberate. Each line is the shape, then what the strict tier
        //       answered where the release promises a refusal.
        // =========================================================================================

        [Fact]
        public void S4_M_Which_projected_sources_the_refusal_does_not_reach()
        {
            List<string> notRefused = new();

            void Probe(string shape, Func<string> guarded)
            {
                string answer;

                try
                {
                    answer = guarded();
                }
                catch (Exception failure)
                {
                    answer = failure.GetType().Name;
                }

                _out.WriteLine($"{shape,-56} {answer}");

                if (!answer.StartsWith("REFUSED", StringComparison.Ordinal))
                {
                    notRefused.Add($"{shape} -> {answer}");
                }
            }

            string Guarded<T>(IQueryable<T> source, string field, DataType type) where T : class
            {
                try
                {
                    source.ApplyPolicy(
                            new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                            new DwPolicyOptions { Tier = DwTier.Strict },
                            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }))
                        .ToList(new Filter
                        {
                            ConditionGroup = new ConditionGroup
                            {
                                Conditions =
                                {
                                    new Condition
                                    {
                                        Field = field, DataType = type,
                                        Operator = Operator.Equal, Values = { "x" }
                                    }
                                }
                            }
                        });

                    return "OK";
                }
                catch (PolicyException refusal)
                {
                    return $"REFUSED({refusal.ErrorCode})";
                }
            }

            // The shape the release's own promise names: an initializer with no constructor
            // arguments, over EF Core's own provider. This one is refused.
            Probe("member-init projection, unassigned member",
                () => Guarded(
                    _orders.Orders.Select(o => new S4Row { Id = o.Id, Nest = new S4Nest { A = o.Code } }),
                    "Nest.B", DataType.Text));

            // Everything below is left alone. Each is a projection a caller composes before
            // ApplyPolicy, and in each the provider decides rather than the policy.
            Probe("anonymous-type projection, getter over owned columns",
                () => Guarded(
                    _orders.Orders.Select(o => new { o.Id, o.Total }),
                    "Total.IsZero", DataType.Boolean));

            Probe("constructor with arguments, member never in the initializer",
                () => Guarded(
                    _orders.Orders.Select(o => new S4Row(o.Id) { Code = o.Code }),
                    "Tag", DataType.Text));

            Probe("constructor with arguments, getter beneath an unnamed member",
                () => Guarded(
                    _orders.Orders.Select(o => new S4Row(o.Id) { Code = o.Code }),
                    "Money.IsZero", DataType.Boolean));

            Probe("member assigned from a method call, a path beneath it",
                () => Guarded(
                    _orders.Orders.Select(o => new S4Row { Id = o.Id, Nest = S4Build(o.Code) }),
                    "Nest.B", DataType.Text));

            _out.WriteLine($"not refused: {notRefused.Count} of 5");

            // The one the release names must be refused; the rest are this commit's open edge.
            Assert.DoesNotContain(
                notRefused,
                entry => entry.StartsWith("member-init projection", StringComparison.Ordinal));
        }

        private static S4Nest S4Build(string code) => new() { A = code };

        // =========================================================================================
        // S4-N. Configure: every knob a second host can hand over, one at a time.
        //
        // Drives DwPolicy.Configure itself rather than its static fields. A posture that differs is
        // refused before anything is written, and one that matches is the no-op the release adds,
        // so no probe here changes what any other suite sees.
        // =========================================================================================

        [Fact]
        public void S4_N_A_second_host_cannot_hand_over_a_posture_that_enforces_differently()
        {
            PolicyBootstrap.Ensure();

            List<string> accepted = new();

            // The posture in force, rebuilt. Accepted as a no-op, which is the feature itself.
            DwPolicy.Configure(Posture());

            void Differs(string knob, Action<DwPolicyOptions> change)
            {
                DwPolicyOptions options = Posture();

                change(options);

                try
                {
                    DwPolicy.Configure(options);

                    _out.WriteLine($"{knob,-30} ACCEPTED");
                    accepted.Add(knob);
                }
                catch (InvalidOperationException)
                {
                    _out.WriteLine($"{knob,-30} refused");
                }
            }

            Differs("Tier", o => o.Tier = DwTier.Strict);
            Differs("DryRun", o => o.DryRun = true);
            Differs("IncludeTraceInResult", o => o.IncludeTraceInResult = false);
            Differs("AuditRefusals", o => o.AuditRefusals = true);
            Differs("HashSalt", o => o.HashSalt = new string('s', DwPolicyOptions.MinimumHashSaltLength));
            Differs("StoreFailure", o => o.StoreFailure = StoreFailureMode.FailClosed);
            Differs("MaxSnapshotAge", o => o.MaxSnapshotAge = TimeSpan.FromMinutes(31));
            Differs("RefreshInterval", o => o.RefreshInterval = TimeSpan.FromSeconds(31));
            Differs("Caps.MaxPageSize", o => o.Caps.MaxPageSize = 17);
            Differs("Caps.DefaultPageSize", o => o.Caps.DefaultPageSize = 17);
            Differs("Caps.Purposes", o => o.Caps.Purposes["s4-export"] = new DwPageCaps { MaxPageSize = 17 });
            Differs("Caps.MaxConditions", o => o.Caps.MaxConditions = 17);
            Differs("Caps.MaxConditionDepth", o => o.Caps.MaxConditionDepth = 17);
            Differs("Caps.MaxConditionSets", o => o.Caps.MaxConditionSets = 17);
            Differs("Caps.MaxConditionValues", o => o.Caps.MaxConditionValues = 17);
            Differs("Caps.MaxAggregates", o => o.Caps.MaxAggregates = 17);
            Differs("Caps.MaxOrderFields", o => o.Caps.MaxOrderFields = 17);
            Differs("Caps.MaxNavigationDepth", o => o.Caps.MaxNavigationDepth = 7);
            Differs("Caps.MaxQueryCost", o => o.Caps.MaxQueryCost = 17_000);
            Differs("Caps.DefaultFieldCost", o => o.Caps.DefaultFieldCost = 7);
            Differs("Caps.MaxAuditEvents", o => o.Caps.MaxAuditEvents = 17);
            Differs("Caps.MinGroupSize", o => o.Caps.MinGroupSize = 17);
            Differs("Caps.SchemaDepth", o => o.Caps.SchemaDepth = 7);
            Differs("Caps.SchemaCycleLimit", o => o.Caps.SchemaCycleLimit = 7);
            Differs("Caps.MaxSchemaFields", o => o.Caps.MaxSchemaFields = 17);
            Differs("Entities: one more type", o => o.Entities.Expose<S4Watched>("s4watched"));
            Differs("Entities: one more name for a type", o => o.Entities.Expose<Staff>("s4staff"));
            Differs("a policy source the first host did not have",
                _ => { /* handled below: providers are a Configure argument, not an option */ });

            // A second host adding a runtime policy source is refused: the resolver is never
            // rebuilt, so every rule in that source would quietly not apply.
            try
            {
                DwPolicy.Configure(Posture(), new S4NoRules());

                _out.WriteLine($"{"extra provider",-30} ACCEPTED");
                accepted.Add("extra provider");
            }
            catch (InvalidOperationException)
            {
                _out.WriteLine($"{"extra provider",-30} refused");
            }

            accepted.Remove("a policy source the first host did not have");

            Assert.True(
                accepted.Count == 0,
                "a second host handed over a posture that enforces differently and it was accepted: "
                + string.Join(", ", accepted));
        }

        // =========================================================================================
        // S4-O. _translated gates Expresses and nothing else. A provider the name test rejects must
        //       still have every denial enforced over it: the refusal, and the projection that
        //       keeps a denied value out of the rows.
        // =========================================================================================

        [Fact]
        public void S4_O_A_provider_the_name_test_rejects_still_has_every_denial_enforced()
        {
            using SqliteConnection connection = new("DataSource=:memory:");

            connection.Open();

            using S4Context replaced = new(connection, typeof(S4PassThroughProvider));

            replaced.Database.EnsureCreated();

            S4Customer customer = new() { Name = "Acme" };

            replaced.Customers.Add(customer);
            replaced.Orders.Add(new S4Order
            {
                Code = "AB123", Qty = 2, Secret = "top", Customer = customer,
                Total = new S4Money { Amount = 10m, Currency = "USD" }
            });
            replaced.SaveChanges();
            replaced.ChangeTracker.Clear();

            _out.WriteLine(
                $"provider = {((IQueryable<S4Order>)replaced.Orders).Provider.GetType().FullName} "
                + "(EfCoreOwns = false)");

            PolicyQueryable<S4Order> guarded = replaced.Orders.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = DwTier.Strict },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

            PolicyException refusal = Assert.Throws<PolicyException>(() => guarded.ToList(new Filter
            {
                ConditionGroup = new ConditionGroup { Conditions = { On("Secret") } }
            }));

            _out.WriteLine($"filter on the denied field -> {refusal.ErrorCode}");

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);

            // And the denied value must not come back in a row the caller asked nothing about.
            List<S4Order> rows = replaced.Orders.ApplyPolicy(
                    new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                    new DwPolicyOptions { Tier = DwTier.Strict },
                    new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }))
                .ToList(new Filter()).Data;

            _out.WriteLine($"rows={rows.Count} Code='{rows[0].Code}' Secret='{rows[0].Secret}'");

            Assert.Equal("AB123", rows[0].Code);
            Assert.Equal(string.Empty, rows[0].Secret);
        }

        // =========================================================================================
        // S4-P. A condition's values are the caller's own objects, in Clone and out of it. The
        //       release documents that a value is read more than once; this pins that no policy
        //       decision rests on which read won.
        // =========================================================================================

        [Fact]
        public void S4_P_A_value_that_answers_differently_each_time_changes_no_policy_decision()
        {
            S4Counting shifting = new();

            Filter moving = new()
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition
                        {
                            Field = "Code", DataType = DataType.Text,
                            Operator = Operator.Equal, Values = { shifting }
                        }
                    }
                }
            };

            Filter steady = new()
            {
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
            };

            string Run(Filter filter)
            {
                try
                {
                    return "OK(" + _orders.Orders.ApplyPolicy(
                            new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                            new DwPolicyOptions { Tier = DwTier.Strict },
                            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }))
                        .ToList(filter).Data.Count + ")";
                }
                catch (PolicyException refusal)
                {
                    return $"REFUSED({refusal.ErrorCode})";
                }
                catch (Exception failure)
                {
                    return failure.GetType().Name;
                }
            }

            string shifted = Run(moving);
            string fixedValue = Run(steady);

            _out.WriteLine($"value read {shifting.Reads} time(s); shifting -> {shifted}, steady -> {fixedValue}");

            // The documented limit: a value is read more than once.
            Assert.True(shifting.Reads > 1, $"the value was read {shifting.Reads} time(s)");

            // What matters here: the policy reached the same decision either way. Nothing the guard
            // decides reads a value's content, so a value that shifts cannot move a denial.
            Assert.Equal(
                fixedValue.StartsWith("REFUSED", StringComparison.Ordinal),
                shifted.StartsWith("REFUSED", StringComparison.Ordinal));
        }

        /// <summary>A value whose <c>ToString()</c> answers differently on every read.</summary>
        private sealed class S4Counting
        {
            internal int Reads { get; private set; }

            public override string ToString()
            {
                Reads++;

                return Reads == 1 ? "AB123" : "ZZZZZ";
            }
        }

        /// <summary>
        /// A posture holding exactly what is in force, value by value, so a knob changed below is
        /// the only difference a second call carries.
        /// </summary>
        private static DwPolicyOptions Posture()
        {
            DwPolicyOptions inForce = DwPolicy.Options;

            DwPolicyOptions copy = new()
            {
                Tier = inForce.Tier,
                DryRun = inForce.DryRun,
                IncludeTraceInResult = inForce.IncludeTraceInResult,
                AuditRefusals = inForce.AuditRefusals,
                StoreFailure = inForce.StoreFailure,
                MaxSnapshotAge = inForce.MaxSnapshotAge,
                RefreshInterval = inForce.RefreshInterval
            };

            if (!string.IsNullOrEmpty(inForce.HashSalt))
            {
                copy.HashSalt = inForce.HashSalt;
            }

            copy.Caps.MaxPageSize = inForce.Caps.MaxPageSize;
            copy.Caps.DefaultPageSize = inForce.Caps.DefaultPageSize;
            copy.Caps.MaxConditions = inForce.Caps.MaxConditions;
            copy.Caps.MaxConditionDepth = inForce.Caps.MaxConditionDepth;
            copy.Caps.MaxConditionSets = inForce.Caps.MaxConditionSets;
            copy.Caps.MaxConditionValues = inForce.Caps.MaxConditionValues;
            copy.Caps.MaxAggregates = inForce.Caps.MaxAggregates;
            copy.Caps.MaxOrderFields = inForce.Caps.MaxOrderFields;
            copy.Caps.MaxNavigationDepth = inForce.Caps.MaxNavigationDepth;
            copy.Caps.MaxQueryCost = inForce.Caps.MaxQueryCost;
            copy.Caps.DefaultFieldCost = inForce.Caps.DefaultFieldCost;
            copy.Caps.MaxAuditEvents = inForce.Caps.MaxAuditEvents;
            copy.Caps.SchemaDepth = inForce.Caps.SchemaDepth;
            copy.Caps.SchemaCycleLimit = inForce.Caps.SchemaCycleLimit;
            copy.Caps.MaxSchemaFields = inForce.Caps.MaxSchemaFields;

            if (inForce.Caps.IsMinGroupSizeSet)
            {
                copy.Caps.MinGroupSize = inForce.Caps.MinGroupSize;
            }

            foreach (KeyValuePair<string, DwPageCaps> purpose in inForce.Caps.Purposes)
            {
                copy.Caps.Purposes[purpose.Key] = new DwPageCaps
                {
                    MaxPageSize = purpose.Value.MaxPageSize,
                    DefaultPageSize = purpose.Value.DefaultPageSize
                };
            }

            foreach (KeyValuePair<Type, string> exposed in inForce.Entities.Entities)
            {
                copy.Entities.Expose(exposed.Key, exposed.Value);
            }

            return copy;
        }

        /// <summary>A runtime policy source that grants and denies nothing.</summary>
        private sealed class S4NoRules : IDwPolicyProvider
        {
            public IReadOnlyList<DynamicWhere.ex.Policies.DTOs.PolicyFragment> GetFragments(
                Type entityType, DwPolicyContext context) =>
                Array.Empty<DynamicWhere.ex.Policies.DTOs.PolicyFragment>();
        }
    }
}
