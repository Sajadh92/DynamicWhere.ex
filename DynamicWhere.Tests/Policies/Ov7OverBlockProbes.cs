using System.Reflection;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Policies.Validation;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ---- entity shapes ------------------------------------------------------------------------

    /// <summary>Several audited members, one denied member, one owned member with an audited leaf.</summary>
    public class Ov7Aud
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwAudit(PolicyFeature.Select)]
        public string Email { get; set; } = string.Empty;

        [DwAudit(PolicyFeature.Select)]
        public string Phone { get; set; } = string.Empty;

        [DwAudit]
        public string Ssn { get; set; } = string.Empty;

        [DwAudit(PolicyFeature.Select)]
        public DateTime Dob { get; set; }

        public Ov7Own Own { get; set; } = new();
    }

    public class Ov7Own
    {
        public string Street { get; set; } = string.Empty;

        [DwAudit(PolicyFeature.Select)]
        public string Zip { get; set; } = string.Empty;
    }

    /// <summary>Same shape, plus a member denied for select so a projection is synthesized.</summary>
    public class Ov7AudDenied
    {
        public int Id { get; set; }

        [DwAudit(PolicyFeature.Select)]
        public string Email { get; set; } = string.Empty;

        [DwAudit(PolicyFeature.Select)]
        public string Phone { get; set; } = string.Empty;

        [DwNoSelect]
        public decimal Salary { get; set; }
    }

    /// <summary>Nothing audited at all.</summary>
    public class Ov7Plain
    {
        public int Id { get; set; }

        public string City { get; set; } = string.Empty;

        public int Capacity { get; set; }
    }

    /// <summary>An alias that differs from its own member only in case.</summary>
    public class Ov7Case
    {
        public int Id { get; set; }

        [DwAlias("total")]
        public decimal Total { get; set; }

        public string Note { get; set; } = string.Empty;
    }

    /// <summary>A chain: A answers to B, and B answers to C. No two names collide in the output.</summary>
    public class Ov7Chain
    {
        public int Id { get; set; }

        [DwAlias("B")]
        public string A { get; set; } = string.Empty;

        [DwAlias("C")]
        public string B { get; set; } = string.Empty;
    }

    /// <summary>An ordinary alias, as a control.</summary>
    public class Ov7Ref
    {
        public int Id { get; set; }

        [DwAlias("reference")]
        public string Number { get; set; } = string.Empty;

        public decimal Amount { get; set; }
    }

    /// <summary>An alias naming a member the group-size column would have taken.</summary>
    public class Ov7Floor
    {
        public int Id { get; set; }

        public string Bucket { get; set; } = string.Empty;

        [DwAlias("Bucket2")]
        public decimal Amount { get; set; }
    }

    // ---- validator-only shapes (never queried) --------------------------------------------------

    public class Ov7ShadowBase
    {
        public object Value { get; set; } = string.Empty;
    }

    /// <summary>A member hidden with new, and an alias naming it: reflection sees the name twice.</summary>
    public class Ov7ShadowDerived : Ov7ShadowBase
    {
        public new string Value { get; set; } = string.Empty;

        [DwAlias("Value")]
        public string Other { get; set; } = string.Empty;
    }

    /// <summary>Two members differing only in case, and an alias naming one of them.</summary>
    public class Ov7DualCase
    {
        public string Name { get; set; } = string.Empty;

        public string name { get; set; } = string.Empty;

        [DwAlias("Name")]
        public string Other { get; set; } = string.Empty;
    }

    /// <summary>An alias naming another member of the same type: the collision the round-6 rule adds.</summary>
    public class Ov7Shadow
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        [DwAlias("Code")]
        public string Reference { get; set; } = string.Empty;
    }

    public sealed class Ov7Context : DbContext
    {
        private readonly SqliteConnection _connection;

        public Ov7Context(SqliteConnection connection) => _connection = connection;

        public DbSet<Ov7Aud> Auds => Set<Ov7Aud>();

        public DbSet<Ov7AudDenied> Denied => Set<Ov7AudDenied>();

        public DbSet<Ov7Plain> Plains => Set<Ov7Plain>();

        public DbSet<Ov7Case> Cases => Set<Ov7Case>();

        public DbSet<Ov7Chain> Chains => Set<Ov7Chain>();

        public DbSet<Ov7Ref> Refs => Set<Ov7Ref>();

        public DbSet<Ov7Floor> Floors => Set<Ov7Floor>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model) => model.Entity<Ov7Aud>().OwnsOne(a => a.Own);
    }

    /// <summary>
    /// Round 7 of the over-blocking hunt: what rounds 5 and 6 changed, measured against 3.2.0.
    /// </summary>
    public sealed class Ov7OverBlockProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly Ov7Context _db;

        public Ov7OverBlockProbes(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new Ov7Context(_connection);
            _db.Database.EnsureCreated();

            for (int i = 1; i <= 5; i++)
            {
                _db.Auds.Add(new Ov7Aud
                {
                    Name = $"n{i}",
                    Email = $"e{i}@x",
                    Phone = $"p{i}",
                    Ssn = $"s{i}",
                    Dob = new DateTime(1990, 1, 1).AddDays(i),
                    Own = new Ov7Own { Street = $"st{i}", Zip = $"z{i}" }
                });

                _db.Denied.Add(new Ov7AudDenied { Email = $"e{i}@x", Phone = $"p{i}", Salary = i * 100 });
                _db.Plains.Add(new Ov7Plain { City = $"c{i}", Capacity = i });
                _db.Cases.Add(new Ov7Case { Total = i * 10, Note = $"note{i}" });
                _db.Chains.Add(new Ov7Chain { A = $"a{i}", B = $"b{i}" });
                _db.Refs.Add(new Ov7Ref { Number = $"NUM{i}", Amount = i });
                _db.Floors.Add(new Ov7Floor { Bucket = i <= 3 ? "x" : "y", Amount = i });
            }

            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        // ---- harness ---------------------------------------------------------------------------

        private static PolicyResolver Resolver() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyContext Ctx() =>
            new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyQueryable<T> Guard<T>(
            IQueryable<T> source, DwPolicyContext context, DwTier tier = DwTier.Strict, int auditCap = 10_000)
            where T : class
        {
            DwPolicyOptions options = new() { Tier = tier };
            options.Caps.MinGroupSize = 1;
            options.Caps.MaxAuditEvents = auditCap;

            return source.ApplyPolicy(context, options, Resolver());
        }

        private static string Outcome(Func<int> run)
        {
            try
            {
                return $"OK({run()})";
            }
            catch (PolicyException refusal)
            {
                return $"POLICY({refusal.ErrorCode}/{refusal.FieldPath})";
            }
            catch (LogicException logic)
            {
                return $"LOGIC({logic.Message})";
            }
            catch (Exception other)
            {
                return other.GetType().Name;
            }
        }

        private void Say(string probe, string value) => _out.WriteLine($"OV7 {probe} = {value}");

        // ---- 1. what [DwAudit] now records -------------------------------------------------------

        [Fact]
        public void Ov7_Audit_Counts()
        {
            // Five rows in the table: the count must not move with them.
            DwPolicyContext whole = Ctx();
            Say("a1.no-selects.5rows", Outcome(() => Guard(_db.Auds, whole).ToList(new Filter()).Data.Count));
            Say("a1.events", whole.PendingAuditEvents.Count.ToString());
            Say("a1.paths", string.Join("|", whole.PendingAuditEvents.Select(e => e.FieldPath + ":" + e.Feature)));

            // One row only, same request.
            DwPolicyContext one = Ctx();
            Filter justOne = new()
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition
                        {
                            Field = "Id", DataType = DataType.Number,
                            Operator = Operator.Equal, Values = { "1" }
                        }
                    }
                }
            };
            Say("a2.one-row", Outcome(() => Guard(_db.Auds, one).ToList(justOne).Data.Count));
            Say("a2.events", one.PendingAuditEvents.Count.ToString());

            // Nothing audited on the type.
            DwPolicyContext plain = Ctx();
            Say("a3.nothing-audited", Outcome(() => Guard(_db.Plains, plain).ToList(new Filter()).Data.Count));
            Say("a3.events", plain.PendingAuditEvents.Count.ToString());

            // A projection is synthesized because something is denied.
            DwPolicyContext denied = Ctx();
            Say("a4.synthesized", Outcome(() => Guard(_db.Denied, denied).ToList(new Filter()).Data.Count));
            Say("a4.events", denied.PendingAuditEvents.Count.ToString());
            Say("a4.paths", string.Join("|", denied.PendingAuditEvents.Select(e => e.FieldPath)));

            // The caller names a projection themselves: only what they named.
            DwPolicyContext named = Ctx();
            Filter spelled = new() { Selects = new List<string> { "Email" } };
            Say("a5.named-selects", Outcome(() => Guard(_db.Auds, named).ToList(spelled).Data.Count));
            Say("a5.events", named.PendingAuditEvents.Count.ToString());

            // Composed clauses must not multiply the recording.
            DwPolicyContext composed = Ctx();
            Say("a6.composed", Outcome(() =>
                Guard(_db.Auds, composed)
                    .Where(new Condition
                    {
                        Field = "Id", DataType = DataType.Number,
                        Operator = Operator.GreaterThan, Values = { "0" }
                    })
                    .Order(new OrderBy { Field = "Id", Direction = Direction.Ascending, Sort = 1 })
                    .ToList(new Filter()).Data.Count));
            Say("a6.events", composed.PendingAuditEvents.Count.ToString());

            // A segment.
            DwPolicyContext segment = Ctx();
            Segment seg = new()
            {
                ConditionSets =
                {
                    new ConditionSet
                    {
                        Sort = 1,
                        Intersection = Intersection.Union,
                        ConditionGroup = new ConditionGroup
                        {
                            Conditions =
                            {
                                new Condition
                                {
                                    Field = "Id", DataType = DataType.Number,
                                    Operator = Operator.GreaterThan, Values = { "0" }
                                }
                            }
                        }
                    },
                    new ConditionSet
                    {
                        Sort = 2,
                        Intersection = Intersection.Union,
                        ConditionGroup = new ConditionGroup
                        {
                            Conditions =
                            {
                                new Condition
                                {
                                    Field = "Id", DataType = DataType.Number,
                                    Operator = Operator.LessThan, Values = { "99" }
                                }
                            }
                        }
                    }
                }
            };
            Say("a7.segment-2sets", Outcome(() =>
                Guard(_db.Auds, segment).ToListAsync(seg).GetAwaiter().GetResult().Data.Count));
            Say("a7.events", segment.PendingAuditEvents.Count.ToString());

            // A summary.
            DwPolicyContext summary = Ctx();
            Summary sum = new()
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "Name" },
                    AggregateBy = new List<AggregateBy>
                    {
                        new() { Aggregator = Aggregator.Count, Alias = "n" }
                    }
                }
            };
            Say("a8.summary", Outcome(() => Guard(_db.Auds, summary).ToList(sum).Data.Count));
            Say("a8.events", summary.PendingAuditEvents.Count.ToString());

            // Two terminals on one context: the buffer accumulates per query.
            DwPolicyContext twice = Ctx();
            Guard(_db.Auds, twice).ToList(new Filter());
            Guard(_db.Auds, twice).ToList(new Filter());
            Say("a9.two-queries.events", twice.PendingAuditEvents.Count.ToString());

            // The cap, at exactly what one query now costs and at one less.
            DwPolicyContext capped = Ctx();
            Say("a10.cap-4", Outcome(() => Guard(_db.Auds, capped, auditCap: 4).ToList(new Filter()).Data.Count));
            DwPolicyContext capped5 = Ctx();
            Say("a10.cap-5", Outcome(() => Guard(_db.Auds, capped5, auditCap: 5).ToList(new Filter()).Data.Count));

            // Convenience tier at the same cap: the refusal's own shape.
            DwPolicyContext conv = Ctx();
            Say("a11.cap-4-convenience", Outcome(() =>
                Guard(_db.Auds, conv, DwTier.Convenience, auditCap: 4).ToList(new Filter()).Data.Count));

            // ToListDynamic, the other filter terminal.
            DwPolicyContext dyn = Ctx();
            Say("a12.dynamic", Outcome(() => Guard(_db.Auds, dyn).ToListDynamic(new Filter()).Data.Count));
            Say("a12.events", dyn.PendingAuditEvents.Count.ToString());
        }

        // ---- 2. a blank name, in every clause ----------------------------------------------------

        [Fact]
        public void Ov7_Blank_Names()
        {
            string[] names = { "", " ", "\t", ".", "A.", ".A", "A..B", "Name.", "..", "Name..Name" };

            foreach (string name in names)
            {
                string label = name.Replace("\t", "\\t");

                Say($"b.group[{label}].guarded", Outcome(() =>
                    Guard(_db.Auds, Ctx()).ToList(SummaryOn(name)).Data.Count));
                Say($"b.group[{label}].unguarded", Outcome(() =>
                    _db.Auds.AsQueryable().ToList(SummaryOn(name)).Data.Count));

                Say($"b.aggfield[{label}].guarded", Outcome(() =>
                    Guard(_db.Auds, Ctx()).ToList(SummaryAgg(name)).Data.Count));
                Say($"b.aggfield[{label}].unguarded", Outcome(() =>
                    _db.Auds.AsQueryable().ToList(SummaryAgg(name)).Data.Count));

                Say($"b.where[{label}].guarded", Outcome(() =>
                    Guard(_db.Auds, Ctx()).ToList(WhereOn(name)).Data.Count));
                Say($"b.where[{label}].unguarded", Outcome(() =>
                    _db.Auds.AsQueryable().ToList(WhereOn(name)).Data.Count));

                Say($"b.order[{label}].guarded", Outcome(() =>
                    Guard(_db.Auds, Ctx()).ToList(OrderOn(name)).Data.Count));
                Say($"b.order[{label}].unguarded", Outcome(() =>
                    _db.Auds.AsQueryable().ToList(OrderOn(name)).Data.Count));

                Say($"b.select[{label}].guarded", Outcome(() =>
                    Guard(_db.Auds, Ctx()).ToListDynamic(SelectOn(name)).Data.Count));
                Say($"b.select[{label}].unguarded", Outcome(() =>
                    _db.Auds.AsQueryable().ToListDynamic(SelectOn(name)).Data.Count));

                Say($"b.segsel[{label}].guarded", Outcome(() =>
                    Guard(_db.Auds, Ctx()).ToListAsync(SegmentSelect(name)).GetAwaiter().GetResult().Data.Count));
                Say($"b.segsel[{label}].unguarded", Outcome(() =>
                    _db.Auds.AsQueryable().ToListAsync(SegmentSelect(name)).GetAwaiter().GetResult().Data.Count));
            }

            // A legitimate key still resolves, guarded and unguarded.
            Say("b.legit.group.guarded", Outcome(() =>
                Guard(_db.Auds, Ctx()).ToList(SummaryOn("Name")).Data.Count));
            Say("b.legit.group.unguarded", Outcome(() =>
                _db.Auds.AsQueryable().ToList(SummaryOn("Name")).Data.Count));
            Say("b.legit.group.owned.guarded", Outcome(() =>
                Guard(_db.Auds, Ctx()).ToList(SummaryOn("Own.Street")).Data.Count));
            Say("b.legit.group.owned.unguarded", Outcome(() =>
                _db.Auds.AsQueryable().ToList(SummaryOn("Own.Street")).Data.Count));
            Say("b.legit.group.case.guarded", Outcome(() =>
                Guard(_db.Auds, Ctx()).ToList(SummaryOn("nAmE")).Data.Count));
            Say("b.legit.group.spaces.guarded", Outcome(() =>
                Guard(_db.Auds, Ctx()).ToList(SummaryOn("  Name  ")).Data.Count));
            Say("b.legit.group.spaces.unguarded", Outcome(() =>
                _db.Auds.AsQueryable().ToList(SummaryOn("  Name  ")).Data.Count));

            // The same, on an aliased type, which takes the other branch of ResolveName.
            Say("b.alias.group.guarded", Outcome(() =>
                Guard(_db.Refs, Ctx()).ToList(SummaryOn("reference")).Data.Count));
            Say("b.alias.group.blank.guarded", Outcome(() =>
                Guard(_db.Refs, Ctx()).ToList(SummaryOn(" ")).Data.Count));
            Say("b.alias.group.blank.unguarded", Outcome(() =>
                _db.Refs.AsQueryable().ToList(SummaryOn(" ")).Data.Count));
        }

        private static Summary SummaryOn(string field) => new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { field },
                AggregateBy = new List<AggregateBy> { new() { Aggregator = Aggregator.Count, Alias = "n" } }
            }
        };

        private static Summary SummaryAgg(string field) => new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Name" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Aggregator = Aggregator.Count, Field = field, Alias = "n" }
                }
            }
        };

        private static Filter WhereOn(string field) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = field, DataType = DataType.Text,
                        Operator = Operator.Equal, Values = { "x" }
                    }
                }
            }
        };

        private static Filter OrderOn(string field) => new()
        {
            Orders = new List<OrderBy> { new() { Field = field, Direction = Direction.Ascending, Sort = 1 } }
        };

        private static Filter SelectOn(string field) => new() { Selects = new List<string> { field } };

        private static Segment SegmentSelect(string field) => new()
        {
            Selects = new List<string> { field },
            ConditionSets =
            {
                new ConditionSet
                {
                    Sort = 1,
                    Intersection = Intersection.Union,
                    ConditionGroup = new ConditionGroup
                    {
                        Conditions =
                        {
                            new Condition
                            {
                                Field = "Id", DataType = DataType.Number,
                                Operator = Operator.GreaterThan, Values = { "0" }
                            }
                        }
                    }
                }
            }
        };

        // ---- 3. an alias spelled like another member ---------------------------------------------

        [Fact]
        public void Ov7_Alias_Inbound_And_Out()
        {
            // The startup scan.
            foreach (Type type in new[]
            {
                typeof(Ov7Case), typeof(Ov7Chain), typeof(Ov7Ref), typeof(Ov7Shadow),
                typeof(Ov7ShadowDerived), typeof(Ov7DualCase), typeof(Ov7Floor)
            })
            {
                string verdict;

                try
                {
                    PolicyModelReport report = PolicyModelValidator.Inspect(new[] { type });
                    verdict = report.IsValid
                        ? "VALID"
                        : "ERRORS:" + string.Join(" ;; ", report.Errors);
                }
                catch (Exception failure)
                {
                    verdict = "THREW:" + failure.GetType().Name + ":" + failure.Message;
                }

                Say($"c.inspect[{type.Name}]", verdict);

                // The scan reports; it never throws. A type reflection sees two properties on under
                // one name — a base member hidden with new, two spelled in different cases — used to
                // take an AmbiguousMatchException out of the scan, so a host calling ValidateModel
                // at startup failed to start with no report at all.
                Assert.DoesNotContain("THREW", verdict);
            }

            // Plain reflection, for the same question the new rule asks.
            foreach (Type type in new[] { typeof(Ov7ShadowDerived), typeof(Ov7DualCase) })
            {
                string verdict;

                try
                {
                    PropertyInfo? found = type.GetProperty(
                        "Value" == type.Name ? "Value" : type == typeof(Ov7DualCase) ? "Name" : "Value",
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    verdict = found?.DeclaringType?.Name + "." + found?.Name;
                }
                catch (Exception failure)
                {
                    verdict = "THREW:" + failure.GetType().Name;
                }

                Say($"c.getproperty[{type.Name}]", verdict);
            }

            // Outbound: an alias that differs from its member only in case.
            string spelled = Columns(() =>
                Guard(_db.Cases, Ctx()).ToListDynamic(
                    new Filter { Selects = new List<string> { "Total", "Note" } }));

            string wholeRow = Columns(() => Guard(_db.Cases, Ctx()).ToListDynamic(new Filter()));

            Say("c.out.case", spelled);
            Say("c.out.case.nosel", wholeRow);

            // A column does not collide with itself. The rule that stops a rename landing on a name
            // the row already carries reads a case-insensitive map, so an alias spelling its own
            // member differently looked like a shadow and the public name stopped being emitted.
            Assert.Contains("total", spelled);
            Assert.Contains("total", wholeRow);

            // Outbound: the chain. Both renames land on free names.
            Say("c.out.chain", Columns(() =>
                Guard(_db.Chains, Ctx()).ToListDynamic(
                    new Filter { Selects = new List<string> { "A", "B" } })));
            Say("c.out.chain.onlyA", Columns(() =>
                Guard(_db.Chains, Ctx()).ToListDynamic(
                    new Filter { Selects = new List<string> { "A" } })));

            // Outbound control: an ordinary alias.
            Say("c.out.ref", Columns(() =>
                Guard(_db.Refs, Ctx()).ToListDynamic(
                    new Filter { Selects = new List<string> { "Number", "Amount" } })));

            // Outbound: an alias naming a member the row also carries.
            Say("c.out.shadowlike", Columns(() =>
                Guard(_db.Floors, Ctx()).ToListDynamic(
                    new Filter { Selects = new List<string> { "Bucket", "Amount" } })));

            // Inbound: a caller may still write either spelling.
            Say("c.in.case.alias", Outcome(() =>
                Guard(_db.Cases, Ctx()).ToList(WhereNum("total", "10")).Data.Count));
            Say("c.in.case.member", Outcome(() =>
                Guard(_db.Cases, Ctx()).ToList(WhereNum("Total", "10")).Data.Count));
            Say("c.in.chain.B", Outcome(() =>
                Guard(_db.Chains, Ctx()).ToList(WhereText("B", "b1")).Data.Count));
            Say("c.in.chain.C", Outcome(() =>
                Guard(_db.Chains, Ctx()).ToList(WhereText("C", "b1")).Data.Count));
            Say("c.in.chain.A", Outcome(() =>
                Guard(_db.Chains, Ctx()).ToList(WhereText("A", "a1")).Data.Count));
            Say("c.in.ref.alias", Outcome(() =>
                Guard(_db.Refs, Ctx()).ToList(WhereText("reference", "NUM1")).Data.Count));

            // A summary, whose rows are generated and whose keys are renamed too.
            Say("c.out.summary.case", SummaryColumns(() =>
                Guard(_db.Cases, Ctx()).ToList(new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = new List<string> { "Total" },
                        AggregateBy = new List<AggregateBy>
                        {
                            new() { Aggregator = Aggregator.Count, Alias = "n" }
                        }
                    }
                })));
        }

        private static Filter WhereNum(string field, string value) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = field, DataType = DataType.Number,
                        Operator = Operator.Equal, Values = { value }
                    }
                }
            }
        };

        private static Filter WhereText(string field, string value) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = field, DataType = DataType.Text,
                        Operator = Operator.Equal, Values = { value }
                    }
                }
            }
        };

        private static string Columns(Func<FilterResult<dynamic>> run)
        {
            try
            {
                FilterResult<dynamic> result = run();

                if (result.Data.Count == 0)
                {
                    return "EMPTY";
                }

                return string.Join(",", Names(result.Data[0]!));
            }
            catch (Exception failure)
            {
                return failure.GetType().Name + ":" + failure.Message;
            }
        }

        private static string SummaryColumns(Func<SummaryResult> run)
        {
            try
            {
                SummaryResult result = run();

                if (result.Data.Count == 0)
                {
                    return "EMPTY";
                }

                return string.Join(",", Names(result.Data[0]!));
            }
            catch (Exception failure)
            {
                return failure.GetType().Name + ":" + failure.Message;
            }
        }

        private static IEnumerable<string> Names(object row) =>
            row is IDictionary<string, object?> expando
                ? expando.Keys.OrderBy(k => k, StringComparer.Ordinal)
                : row.GetType().GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal);
    }
}
