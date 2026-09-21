using System.Reflection;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Audit;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Discovery;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

// Round three of the docs-against-code probe for 3.3.0. Every fact here is read by running the
// library, never by reading it: each test prints what happened so the report can quote an outcome.
// Nothing here fixes anything.

namespace DynamicWhere.Tests.Policies
{
    // ---- a model whose computed member and whose mapped member are both audited ------------------

    public class DrcText
    {
        public string Ar { get; set; } = string.Empty;

        [DwAudit(PolicyFeature.All)]
        public string En { get; set; } = string.Empty;

        /// <summary>Audited too, so a refusal that recorded a use would show here.</summary>
        [DwAudit(PolicyFeature.All)]
        public bool IsEmpty => string.IsNullOrWhiteSpace(Ar) && string.IsNullOrWhiteSpace(En);
    }

    public class DrcRole
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public DrcText Name { get; set; } = new();
    }

    public sealed class DrcContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public DrcContext(SqliteConnection connection) => _connection = connection;

        public DbSet<DrcRole> Roles => Set<DrcRole>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model) => model.Entity<DrcRole>().OwnsOne(role => role.Name);
    }

    /// <summary>A value whose text answers differently on every read.</summary>
    public sealed class DrcDrifting
    {
        private int _reads;

        public int Reads => _reads;

        public override string ToString() => (++_reads).ToString();
    }

    public sealed class DrcSink : IDwAuditSink
    {
        public List<DwAuditEvent> Events { get; } = new();

        public ValueTask WriteAsync(DwAuditEvent auditEvent, CancellationToken ct = default)
        {
            Events.Add(auditEvent);

            return default;
        }
    }

    public sealed class DrcTypeOne
    {
    }

    // =============================================================================================
    // 1. The rows of the source table that no test covers: a provider standing in front of EF Core.
    // =============================================================================================

    public sealed class DrcProviderInFrontProbe : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZyContext _db;

        public DrcProviderInFrontProbe(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZyContext(_connection);
            _db.Database.EnsureCreated();
            _db.Roles.Add(new ZyRole { Code = "admin", Name = new ZyLocalizedText { En = "Admin", Ar = "مدير" } });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        internal static bool? Ask(IQueryable source, string path)
        {
            object shape = typeof(RowShape)
                .GetMethod("Of", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
                .MakeGenericMethod(source.ElementType)
                .Invoke(null, new object[] { source })!;

            return (bool?)typeof(RowShape)
                .GetMethod("Expresses", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(shape, new object[] { path });
        }

        internal static string Show(bool? answer) => answer is null ? "null (left alone)" : answer.ToString()!;

        internal static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        internal static Filter Where(string field, string value, DataType type = DataType.Text) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition { Field = field, DataType = type, Operator = Operator.Equal, Values = { value } }
                }
            }
        };

        /// <summary>
        /// The table's row "a provider in front of EF Core", over an entity. The wrapper leaves the EF
        /// Core query root in the expression, so the model is readable; the provider is not EF Core's.
        /// </summary>
        [Fact]
        public void A_provider_in_front_of_EF_Core_over_an_entity_is_left_alone()
        {
            IQueryable<ZyRole> wrapped = new ZyOwnProvider<ZyRole>(_db.Roles);

            bool? bare = Ask(_db.Roles, "Name.IsEmpty");
            bool? front = Ask(wrapped, "Name.IsEmpty");

            _out.WriteLine($"DbSet<ZyRole>                       Expresses(\"Name.IsEmpty\") = {Show(bare)}");
            _out.WriteLine($"a provider in front of it, entity   Expresses(\"Name.IsEmpty\") = {Show(front)}");

            Exception? guarded = Record.Exception(
                () => Guard(wrapped, DwTier.Strict).ToList(Where("Name.IsEmpty", "false", DataType.Boolean)));

            Exception? unguarded = Record.Exception(() => _db.Roles.Where(role => role.Name.IsEmpty).ToList());

            _out.WriteLine($"  guarded  -> {guarded?.GetType().Name ?? "ran"}");
            _out.WriteLine($"  unguarded-> {unguarded?.GetType().Name ?? "ran"}");

            Assert.False(bare);
            Assert.Null(front);
            Assert.False(guarded is PolicyException, $"refused: {guarded?.Message}");
        }

        /// <summary>The table's row "a projection a provider that is not EF Core's ran", EF-backed.</summary>
        [Fact]
        public void A_provider_in_front_of_EF_Core_over_a_projection_is_left_alone()
        {
            IQueryable<ZyRoleRow> built = _db.Roles.Select(role => new ZyRoleRow
            {
                Id = role.Id,
                Code = role.Code,
                Name = new ZyLocalizedText { Ar = role.Name.Ar, En = role.Name.En }
            });

            IQueryable<ZyRoleRow> front = new ZyOwnProvider<ZyRoleRow>(built);

            _out.WriteLine($"EF Core's own projection            Expresses(\"Name.IsEmpty\") = {Show(Ask(built, "Name.IsEmpty"))}");
            _out.WriteLine($"the same behind another provider    Expresses(\"Name.IsEmpty\") = {Show(Ask(front, "Name.IsEmpty"))}");

            Assert.False(Ask(built, "Name.IsEmpty"));
            Assert.Null(Ask(front, "Name.IsEmpty"));
        }

        /// <summary>What the library matches on, so the report can say which providers answer which way.</summary>
        [Fact]
        public void EF_Core_s_own_provider_is_what_decides()
        {
            MethodInfo owns = typeof(RowShape).GetMethod("EfCoreOwns", BindingFlags.Static | BindingFlags.NonPublic)!;

            IQueryable<ZyRole> efCore = _db.Roles;

            bool ef = (bool)owns.Invoke(null, new object[] { efCore.Provider })!;
            bool wrapper = (bool)owns.Invoke(null, new object[] { new ZyOwnProvider<ZyRole>(efCore).Provider })!;
            bool memory = (bool)owns.Invoke(null, new object[] { new[] { new ZyRole() }.AsQueryable().Provider })!;

            _out.WriteLine($"EF Core's own provider  -> {ef}   ({efCore.Provider.GetType().FullName})");
            _out.WriteLine($"a provider in front     -> {wrapper}");
            _out.WriteLine($"EnumerableQuery         -> {memory}");

            Assert.True(ef);
            Assert.False(wrapper);
            Assert.False(memory);
        }
    }

    // =============================================================================================
    // 2. Every clause the text lists, and the two the text lists inside a Segment.
    // =============================================================================================

    public sealed class DrcClauseProbe : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZyContext _db;

        public DrcClauseProbe(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZyContext(_connection);
            _db.Database.EnsureCreated();
            _db.Roles.Add(new ZyRole { Code = "admin", Name = new ZyLocalizedText { En = "Admin", Ar = "مدير" } });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private PolicyQueryable<ZyRole> Guarded(DwTier tier = DwTier.Strict) =>
            DrcProviderInFrontProbe.Guard(_db.Roles, tier);

        private static ConditionSet Set(int sort, string field, Intersection? intersection = null) => new()
        {
            Sort = sort,
            Intersection = intersection,
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = field,
                        DataType = field.EndsWith("IsEmpty", StringComparison.Ordinal) ? DataType.Boolean : DataType.Text,
                        Operator = Operator.Equal,
                        Values = { field.EndsWith("IsEmpty", StringComparison.Ordinal) ? "false" : "Admin" }
                    }
                }
            }
        };

        [Fact]
        public void The_five_clauses_the_text_names_each_refuse()
        {
            Report("a filter", Record.Exception(
                () => Guarded().ToList(DrcProviderInFrontProbe.Where("Name.IsEmpty", "false", DataType.Boolean))),
                PolicyErrorCode.FieldDeniedForWhere);

            Report("an order", Record.Exception(() => Guarded().ToList(new Filter
            {
                Orders = new List<OrderBy> { new() { Sort = 1, Field = "Name.IsEmpty", Direction = Direction.Ascending } }
            })), PolicyErrorCode.FieldDeniedForOrder);

            Report("a grouping key", Record.Exception(() => Guarded().ToList(new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "Name.IsEmpty" },
                    AggregateBy = new List<AggregateBy> { new() { Alias = "N", Aggregator = Aggregator.Count } }
                }
            })), PolicyErrorCode.FieldDeniedForGroup);

            Report("an aggregated field", Record.Exception(() => Guarded().ToList(new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "Code" },
                    AggregateBy = new List<AggregateBy>
                    {
                        new() { Alias = "N", Aggregator = Aggregator.Count },
                        new() { Alias = "M", Field = "Name.IsEmpty", Aggregator = Aggregator.Maximum }
                    }
                }
            })), PolicyErrorCode.FieldDeniedForAggregate);

            void Report(string label, Exception? error, PolicyErrorCode expected)
            {
                PolicyException refusal = Assert.IsType<PolicyException>(error);

                _out.WriteLine($"{label,-22} -> {refusal.ErrorCode} FieldPath={refusal.FieldPath}");

                Assert.Equal(expected, refusal.ErrorCode);
                Assert.Equal("*", refusal.FieldPath);
            }
        }

        [Fact]
        public async Task A_filter_inside_a_Segment_is_refused()
        {
            PolicyException refusal = await Assert.ThrowsAnyAsync<PolicyException>(
                () => Guarded().ToListAsync(new Segment { ConditionSets = { Set(1, "Name.IsEmpty") } }));

            _out.WriteLine($"a filter inside a Segment -> {refusal.ErrorCode} FieldPath={refusal.FieldPath}");

            Assert.Equal(PolicyErrorCode.FieldDeniedForSegment, refusal.ErrorCode);
            Assert.Equal("*", refusal.FieldPath);
        }

        [Fact]
        public async Task An_order_inside_a_Segment_is_refused()
        {
            PolicyException refusal = await Assert.ThrowsAnyAsync<PolicyException>(
                () => Guarded().ToListAsync(new Segment
                {
                    ConditionSets = { Set(1, "Name.En") },
                    Orders = new List<OrderBy> { new() { Sort = 1, Field = "Name.IsEmpty", Direction = Direction.Ascending } }
                }));

            _out.WriteLine($"an order inside a Segment -> {refusal.ErrorCode} FieldPath={refusal.FieldPath}");

            Assert.Equal(PolicyErrorCode.FieldDeniedForSegment, refusal.ErrorCode);
            Assert.Equal("*", refusal.FieldPath);
        }

        /// <summary>The composable pair reaches the same refusal the terminals do.</summary>
        [Fact]
        public void The_composable_Group_and_Summary_refuse_it_too()
        {
            Exception? group = Record.Exception(() => Guarded().Group(new GroupBy
            {
                Fields = new List<string> { "Name.IsEmpty" },
                AggregateBy = new List<AggregateBy> { new() { Alias = "N", Aggregator = Aggregator.Count } }
            }));

            Exception? summary = Record.Exception(() => Guarded().Summary(new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "Name.IsEmpty" },
                    AggregateBy = new List<AggregateBy> { new() { Alias = "N", Aggregator = Aggregator.Count } }
                }
            }));

            _out.WriteLine($"composable Group   -> {Show(group)}");
            _out.WriteLine($"composable Summary -> {Show(summary)}");

            Assert.Equal(PolicyErrorCode.FieldDeniedForGroup, Assert.IsType<PolicyException>(group).ErrorCode);
            Assert.Equal(PolicyErrorCode.FieldDeniedForGroup, Assert.IsType<PolicyException>(summary).ErrorCode);

            static string Show(Exception? error) =>
                error is PolicyException policy ? $"{policy.ErrorCode} FieldPath={policy.FieldPath}" : error?.GetType().Name ?? "ran";
        }

        /// <summary>
        /// The aggregation entry DOC.md leaves without the floor note: a guarded in-memory summary.
        /// </summary>
        [Fact]
        public void An_in_memory_summary_reached_through_ApplyPolicy_is_floored_too()
        {
            List<ZyRole> rows = new();

            for (int i = 0; i < 7; i++)
            {
                rows.Add(new ZyRole { Id = i + 1, Code = i < 5 ? "big" : "small", Name = new ZyLocalizedText() });
            }

            Summary summary = new()
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "Code" },
                    AggregateBy = new List<AggregateBy> { new() { Alias = "N", Aggregator = Aggregator.Count } }
                }
            };

            SummaryResult unguarded = rows.ToList(summary);

            SummaryResult guarded = rows
                .AsQueryable()
                .ApplyPolicy(
                    new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                    new DwPolicyOptions { Tier = DwTier.Strict },
                    new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }))
                .ToList(summary.Clone());

            _out.WriteLine($"in-memory summary: unguarded {unguarded.Data.Count} group(s), guarded {guarded.Data.Count}");

            Assert.Equal(2, unguarded.Data.Count);
            Assert.Single(guarded.Data);
        }

        /// <summary>Selects is the exemption, in a Filter and in a Segment, in both tiers.</summary>
        [Fact]
        public async Task Selects_stays_exempt_everywhere()
        {
            FilterResult<ZyRole> strict = Guarded().ToList(new Filter { Selects = new List<string> { "Id", "Name.IsEmpty" } });
            FilterResult<ZyRole> easy = Guarded(DwTier.Convenience)
                .ToList(new Filter { Selects = new List<string> { "Id", "Name.IsEmpty" } });

            SegmentResult<ZyRole> segment = await Guarded().ToListAsync(new Segment
            {
                Selects = new List<string> { "Id", "Name.IsEmpty" },
                ConditionSets = { Set(1, "Name.En") }
            });

            _out.WriteLine($"Selects: strict={strict.Data.Count} convenience={easy.Data.Count} segment={segment.Data.Count}");

            Assert.Single(strict.Data);
            Assert.Single(easy.Data);
            Assert.Single(segment.Data);
        }
    }

    // =============================================================================================
    // 3. Both branches of a conditional, including the core's own typed Select.
    // =============================================================================================

    public sealed class DrcConditionalProbe : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZyContext _db;

        public DrcConditionalProbe(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZyContext(_connection);
            _db.Database.EnsureCreated();
            _db.Roles.Add(new ZyRole { Code = "admin", Name = new ZyLocalizedText { En = "Admin", Ar = "مدير" } });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        /// <summary>
        /// The claim in every document: "the core's typed <c>Select</c> null-guards every nested node it
        /// builds, and both branches of that guard are read, so composing <c>Select</c> and then
        /// filtering refuses exactly what the bare handle refuses."
        /// </summary>
        [Fact]
        public void The_cores_own_typed_Select_composed_then_filtered_refuses_what_the_bare_handle_refuses()
        {
            IQueryable<ZyRole> projected = _db.Roles.Select(new List<string> { "Id", "Code", "Name.En" });

            _out.WriteLine("the core's typed Select builds:");
            _out.WriteLine("  " + projected.Expression.ToString());
            _out.WriteLine($"  Expresses(\"Name.IsEmpty\") = {DrcProviderInFrontProbe.Show(DrcProviderInFrontProbe.Ask(projected, "Name.IsEmpty"))}");
            _out.WriteLine($"  Expresses(\"Name.En\")      = {DrcProviderInFrontProbe.Show(DrcProviderInFrontProbe.Ask(projected, "Name.En"))}");

            Exception? bare = Record.Exception(() => DrcProviderInFrontProbe
                .Guard(_db.Roles, DwTier.Strict)
                .ToList(DrcProviderInFrontProbe.Where("Name.IsEmpty", "false", DataType.Boolean)));

            Exception? composed = Record.Exception(() => DrcProviderInFrontProbe
                .Guard(projected, DwTier.Strict)
                .ToList(DrcProviderInFrontProbe.Where("Name.IsEmpty", "false", DataType.Boolean)));

            _out.WriteLine($"  bare handle refuses with  {Name(bare)}");
            _out.WriteLine($"  composed Select refuses   {Name(composed)}");

            Assert.IsType<PolicyException>(bare);
            Assert.IsType<PolicyException>(composed);
            Assert.Equal(((PolicyException)bare!).ErrorCode, ((PolicyException)composed!).ErrorCode);

            static string Name(Exception? error) =>
                error is PolicyException policy ? $"{policy.ErrorCode}" : error?.GetType().Name ?? "no refusal";
        }

        /// <summary>A conditional written by hand, each branch a nested initializer.</summary>
        [Fact]
        public void A_conditional_whose_branches_are_both_initializers_is_read_on_both()
        {
            IQueryable<ZyRoleRow> rows = _db.Roles.Select(role => new ZyRoleRow
            {
                Id = role.Id,
                Code = role.Code,
                Name = role.Code == "admin"
                    ? new ZyLocalizedText { En = role.Name.En }
                    : new ZyLocalizedText { En = role.Name.En, Ar = role.Name.Ar }
            });

            bool? isEmpty = DrcProviderInFrontProbe.Ask(rows, "Name.IsEmpty");
            bool? en = DrcProviderInFrontProbe.Ask(rows, "Name.En");
            bool? ar = DrcProviderInFrontProbe.Ask(rows, "Name.Ar");

            _out.WriteLine($"two initializer branches: IsEmpty={DrcProviderInFrontProbe.Show(isEmpty)} "
                           + $"En={DrcProviderInFrontProbe.Show(en)} Ar={DrcProviderInFrontProbe.Show(ar)} (union of the branches)");

            Assert.False(isEmpty);
            Assert.True(en);
            Assert.True(ar);
        }
    }

    // =============================================================================================
    // 4. The audit: the refusal raises no [DwAudit] event, and AuditRefusals records it.
    // =============================================================================================

    public sealed class DrcAuditProbe : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly DrcContext _db;

        public DrcAuditProbe(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new DrcContext(_connection);
            _db.Database.EnsureCreated();
            _db.Roles.Add(new DrcRole { Code = "admin", Name = new DrcText { En = "Admin", Ar = "مدير" } });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private (DwPolicyContext Context, PolicyQueryable<DrcRole> Query) Guard(DwPolicyOptions options)
        {
            DwPolicyContext context = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

            return (context, _db.Roles.ApplyPolicy(
                context, options, new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() })));
        }

        [Fact]
        public void An_audited_member_that_answers_records_a_use()
        {
            (DwPolicyContext context, PolicyQueryable<DrcRole> query) = Guard(new DwPolicyOptions { Tier = DwTier.Strict });

            query.ToList(DrcProviderInFrontProbe.Where("Name.En", "Admin"));

            _out.WriteLine($"filter on the audited Name.En -> {context.PendingAuditEvents.Count} event(s): "
                           + string.Join(", ", context.PendingAuditEvents.Select(e => $"{e.FieldPath}/{e.Feature}/{e.Effect}")));

            Assert.NotEmpty(context.PendingAuditEvents);
        }

        /// <summary>The claim: the refusal raises no <c>[DwAudit]</c> event, as an unknown name raises none.</summary>
        [Fact]
        public void The_refusal_raises_no_DwAudit_event()
        {
            (DwPolicyContext computed, PolicyQueryable<DrcRole> one) = Guard(new DwPolicyOptions { Tier = DwTier.Strict });

            Assert.ThrowsAny<PolicyException>(
                () => one.ToList(DrcProviderInFrontProbe.Where("Name.IsEmpty", "false", DataType.Boolean)));

            (DwPolicyContext unknown, PolicyQueryable<DrcRole> two) = Guard(new DwPolicyOptions { Tier = DwTier.Strict });

            Assert.ThrowsAny<PolicyException>(() => two.ToList(DrcProviderInFrontProbe.Where("NoSuchMember", "x")));

            _out.WriteLine($"a path the query cannot compute -> {computed.PendingAuditEvents.Count} [DwAudit] event(s)");
            _out.WriteLine($"a name that matches nothing     -> {unknown.PendingAuditEvents.Count} [DwAudit] event(s)");

            Assert.Empty(computed.PendingAuditEvents);
            Assert.Empty(unknown.PendingAuditEvents);
        }

        /// <summary>The other half of the same sentence: <c>AuditRefusals</c> records it, and so does the trace.</summary>
        [Fact]
        public void AuditRefusals_records_it_and_so_does_the_trace()
        {
            (DwPolicyContext context, PolicyQueryable<DrcRole> query) =
                Guard(new DwPolicyOptions { Tier = DwTier.Strict, AuditRefusals = true });

            Assert.ThrowsAny<PolicyException>(
                () => query.ToList(DrcProviderInFrontProbe.Where("Name.IsEmpty", "false", DataType.Boolean)));

            _out.WriteLine($"AuditRefusals on -> {context.PendingAuditEvents.Count} event(s): "
                           + string.Join(", ", context.PendingAuditEvents.Select(e => $"{e.FieldPath}/{e.ErrorCode}")));

            foreach (PolicyDecision decision in query.LastTrace!.Decisions)
            {
                _out.WriteLine($"  trace: {decision.FieldPath} {decision.Feature} {decision.Action} — {decision.Reason}");
            }

            Assert.Single(context.PendingAuditEvents);
            Assert.Contains(
                query.LastTrace!.Decisions,
                decision => decision.Reason is not null
                            && decision.Reason.Contains("the member exists on the type and the query cannot compute it",
                                StringComparison.Ordinal));
        }
    }

    // =============================================================================================
    // 5. A simulation has no source, so it cannot refuse such a path at all.
    // =============================================================================================

    public sealed class DrcSimulationProbe
    {
        private readonly ITestOutputHelper _out;

        public DrcSimulationProbe(ITestOutputHelper output) => _out = output;

        private static PolicyResolver Resolver() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        [Fact]
        public void A_simulation_cannot_refuse_a_path_no_database_can_compute()
        {
            DwPolicyOptions options = new() { Tier = DwTier.Strict };

            PolicySimulation<Filter> computed = PolicySimulator.Simulate<ZyRole>(
                DrcProviderInFrontProbe.Where("Name.IsEmpty", "false", DataType.Boolean),
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"), options, Resolver());

            PolicySimulation<Filter> denied = PolicySimulator.Simulate<ZyRole>(
                DrcProviderInFrontProbe.Where("Name", "x"),
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"), options, Resolver());

            PolicySimulation<Filter> unknown = PolicySimulator.Simulate<ZyRole>(
                DrcProviderInFrontProbe.Where("NoSuchMember", "x"),
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"), options, Resolver());

            _out.WriteLine($"simulate Name.IsEmpty  -> WouldRun={computed.WouldRun} refusal={computed.Refusal?.ErrorCode.ToString() ?? "none"}");
            _out.WriteLine($"simulate Name (denied) -> WouldRun={denied.WouldRun} refusal={denied.Refusal?.ErrorCode.ToString() ?? "none"}");
            _out.WriteLine($"simulate NoSuchMember  -> WouldRun={unknown.WouldRun} refusal={unknown.Refusal?.ErrorCode.ToString() ?? "none"}");

            Assert.True(computed.WouldRun);
            Assert.False(denied.WouldRun);
            Assert.False(unknown.WouldRun);
        }

        /// <summary>A Segment and a Summary simulate the same way, so the claim holds for every clause.</summary>
        [Fact]
        public void A_simulated_segment_and_summary_run_it_too()
        {
            DwPolicyOptions options = new() { Tier = DwTier.Strict };

            PolicySimulation<Segment> segment = PolicySimulator.Simulate<ZyRole>(
                new Segment
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
                                        Field = "Name.IsEmpty",
                                        DataType = DataType.Boolean,
                                        Operator = Operator.Equal,
                                        Values = { "false" }
                                    }
                                }
                            }
                        }
                    }
                },
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"), options, Resolver());

            PolicySimulation<Summary> summary = PolicySimulator.Simulate<ZyRole>(
                new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = new List<string> { "Name.IsEmpty" },
                        AggregateBy = new List<AggregateBy> { new() { Alias = "N", Aggregator = Aggregator.Count } }
                    }
                },
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"), options, Resolver());

            _out.WriteLine($"simulate a Segment naming it -> WouldRun={segment.WouldRun}");
            _out.WriteLine($"simulate a Summary naming it -> WouldRun={summary.WouldRun}");

            Assert.True(segment.WouldRun);
            Assert.True(summary.WouldRun);
        }
    }

    // =============================================================================================
    // 6. The posture comparison: the two sentences round two added.
    // =============================================================================================

    public sealed class DrcPostureProbe
    {
        private readonly ITestOutputHelper _out;

        public DrcPostureProbe(ITestOutputHelper output) => _out = output;

        private static readonly MethodInfo SameAs = typeof(DwEntityCatalog)
            .GetMethod("SameAs", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly MethodInfo SamePosture = typeof(DwPolicy)
            .GetMethod("SamePosture", BindingFlags.Static | BindingFlags.NonPublic)!;

        private static bool Ask(DwEntityCatalog left, DwEntityCatalog right) =>
            (bool)SameAs.Invoke(left, new object[] { right })!;

        /// <summary>
        /// "A type exposed under two names is reported under the last one, so two catalogues resolving
        /// every name alike are still refused when the order differs."
        /// </summary>
        [Fact]
        public void A_type_exposed_under_two_names_in_a_different_order_is_refused()
        {
            DwEntityCatalog first = new();
            first.Expose<DrcTypeOne>("alpha");
            first.Expose<DrcTypeOne>("beta");

            DwEntityCatalog second = new();
            second.Expose<DrcTypeOne>("beta");
            second.Expose<DrcTypeOne>("alpha");

            _out.WriteLine($"first  resolves alpha -> {first.Resolve("alpha")?.Name}, beta -> {first.Resolve("beta")?.Name}, reported as '{first.NameOf(typeof(DrcTypeOne))}'");
            _out.WriteLine($"second resolves alpha -> {second.Resolve("alpha")?.Name}, beta -> {second.Resolve("beta")?.Name}, reported as '{second.NameOf(typeof(DrcTypeOne))}'");
            _out.WriteLine($"SameAs -> {Ask(first, second)}");

            Assert.Equal(first.Resolve("alpha"), second.Resolve("alpha"));
            Assert.Equal(first.Resolve("beta"), second.Resolve("beta"));
            Assert.NotEqual(first.NameOf(typeof(DrcTypeOne)), second.NameOf(typeof(DrcTypeOne)));
            Assert.False(Ask(first, second));
        }

        /// <summary>
        /// "<c>IncludeTraceInResult</c> is compared by the value that applies": under a tier whose own
        /// answer is the written one, the two postures are the same.
        /// </summary>
        [Fact]
        public void IncludeTraceInResult_is_compared_by_the_value_that_applies()
        {
            foreach (DwTier tier in new[] { DwTier.Convenience, DwTier.Strict })
            {
                bool tierAnswer = tier == DwTier.Convenience;

                DwPolicyOptions unset = Posture(tier, null);
                DwPolicyOptions written = Posture(tier, tierAnswer);
                DwPolicyOptions opposite = Posture(tier, !tierAnswer);

                bool sameWhenWritten = Compare(unset, written);
                bool sameWhenOpposite = Compare(unset, opposite);

                _out.WriteLine($"{tier}: null vs {tierAnswer} (the tier's own answer) -> {sameWhenWritten}; "
                               + $"null vs {!tierAnswer} -> {sameWhenOpposite}");

                Assert.True(sameWhenWritten);
                Assert.False(sameWhenOpposite);
            }

            static DwPolicyOptions Posture(DwTier tier, bool? trace) =>
                new() { Tier = tier, IncludeTraceInResult = trace };
        }

        /// <summary>The cap half of the same rule, on the one cap with a default of its own.</summary>
        [Fact]
        public void Every_cap_is_compared_by_the_value_that_applies()
        {
            DwPolicyOptions unset = new() { Tier = DwTier.Strict };
            DwPolicyOptions written = new() { Tier = DwTier.Strict, Caps = { MinGroupSize = DwCaps.DefaultMinGroupSize } };
            DwPolicyOptions higher = new() { Tier = DwTier.Strict, Caps = { MinGroupSize = DwCaps.DefaultMinGroupSize + 1 } };

            _out.WriteLine($"unset MinGroupSize reads {unset.Caps.MinGroupSize} (IsMinGroupSizeSet={unset.Caps.IsMinGroupSizeSet}); "
                           + $"written reads {written.Caps.MinGroupSize} (IsMinGroupSizeSet={written.Caps.IsMinGroupSizeSet})");
            _out.WriteLine($"unset vs written  -> {Compare(unset, written)}");
            _out.WriteLine($"unset vs {higher.Caps.MinGroupSize}        -> {Compare(unset, higher)}");

            Assert.True(Compare(unset, written));
            Assert.False(Compare(unset, higher));
        }

        /// <summary>
        /// The one cap whose applied value is not the value written down, beside
        /// <c>MinGroupSize</c>: <c>DefaultPageSize</c> is applied as <c>MaxPageSize</c> when it is
        /// larger, so two postures that page a caller identically are still told apart.
        /// </summary>
        [Fact]
        public void DefaultPageSize_above_MaxPageSize_is_compared_as_written_not_as_applied()
        {
            DwPolicyOptions inForce = new()
            {
                Tier = DwTier.Strict, Caps = { MaxPageSize = 100, DefaultPageSize = 100 }
            };

            DwPolicyOptions asked = new()
            {
                Tier = DwTier.Strict, Caps = { MaxPageSize = 100, DefaultPageSize = 1000 }
            };

            _out.WriteLine($"MaxPageSize {inForce.Caps.MaxPageSize}: DefaultPageSize "
                           + $"{inForce.Caps.DefaultPageSize} vs {asked.Caps.DefaultPageSize} "
                           + $"— both page a caller at {Math.Min(asked.Caps.DefaultPageSize, asked.Caps.MaxPageSize)}");
            _out.WriteLine($"SamePosture -> {Compare(inForce, asked)}");

            Assert.False(Compare(inForce, asked));
        }

        /// <summary>Compares two postures the way <c>Configure</c> does, without configuring anything.</summary>
        private static bool Compare(DwPolicyOptions inForce, DwPolicyOptions asked) =>
            (bool)SamePosture.Invoke(null, new object?[] { inForce, asked, Array.Empty<IDwPolicyProvider>() })!;
    }

    // =============================================================================================
    // 7. Clone's contract: a new Values list holding the caller's own objects, on all three shapes.
    // =============================================================================================

    public sealed class DrcCloneProbe
    {
        private readonly ITestOutputHelper _out;

        public DrcCloneProbe(ITestOutputHelper output) => _out = output;

        private static Condition Sample(object value) => new()
        {
            Sort = 1,
            Field = "Code",
            DataType = DataType.Text,
            Operator = Operator.Equal,
            Values = new List<object> { value }
        };

        [Fact]
        public void Every_shape_gives_a_new_Values_list_holding_the_callers_own_objects()
        {
            object value = new DrcDrifting();

            Filter filter = new()
            {
                ConditionGroup = new ConditionGroup { Conditions = { Sample(value) } }
            };

            Segment segment = new()
            {
                ConditionSets =
                {
                    new ConditionSet
                    {
                        Sort = 1,
                        Intersection = Intersection.Union,
                        ConditionGroup = new ConditionGroup { Conditions = { Sample(value) } }
                    }
                }
            };

            Summary summary = new()
            {
                ConditionGroup = new ConditionGroup { Conditions = { Sample(value) } },
                Having = new ConditionGroup { Conditions = { Sample(value) } },
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "Code" },
                    AggregateBy = new List<AggregateBy> { new() { Alias = "N", Aggregator = Aggregator.Count } }
                }
            };

            Filter filterCopy = filter.Clone();
            Segment segmentCopy = segment.Clone();
            Summary summaryCopy = summary.Clone();

            Check("Filter", filter.ConditionGroup!.Conditions[0], filterCopy.ConditionGroup!.Conditions[0]);
            Check("Segment", segment.ConditionSets[0].ConditionGroup.Conditions[0],
                segmentCopy.ConditionSets[0].ConditionGroup.Conditions[0]);
            Check("Summary/where", summary.ConditionGroup!.Conditions[0], summaryCopy.ConditionGroup!.Conditions[0]);
            Check("Summary/having", summary.Having!.Conditions[0], summaryCopy.Having!.Conditions[0]);

            // The clauses beside the condition tree.
            Assert.NotSame(segment.ConditionSets[0], segmentCopy.ConditionSets[0]);
            Assert.Equal(Intersection.Union, segmentCopy.ConditionSets[0].Intersection);
            Assert.NotSame(summary.GroupBy, summaryCopy.GroupBy);
            Assert.NotSame(summary.GroupBy!.AggregateBy[0], summaryCopy.GroupBy!.AggregateBy[0]);

            void Check(string label, Condition original, Condition copy)
            {
                _out.WriteLine($"{label,-16} condition new={!ReferenceEquals(original, copy)} "
                               + $"Values list new={!ReferenceEquals(original.Values, copy.Values)} "
                               + $"element shared={ReferenceEquals(original.Values[0], copy.Values[0])}");

                Assert.NotSame(original, copy);
                Assert.NotSame(original.Values, copy.Values);
                Assert.Same(original.Values[0], copy.Values[0]);
            }
        }

        [Fact]
        public void Clone_is_public_on_the_three_shapes_and_internal_on_the_parts()
        {
            foreach (Type type in new[] { typeof(Filter), typeof(Segment), typeof(Summary) })
            {
                MethodInfo? clone = type.GetMethod("Clone", BindingFlags.Instance | BindingFlags.Public);

                _out.WriteLine($"{type.Name,-8} public Clone -> {(clone is null ? "absent" : clone.ReturnType.Name)}");

                Assert.NotNull(clone);
                Assert.Equal(type, clone!.ReturnType);
            }

            foreach (Type type in new[]
                     {
                         typeof(ConditionGroup), typeof(Condition), typeof(ConditionSet),
                         typeof(GroupBy), typeof(AggregateBy), typeof(OrderBy), typeof(PageBy)
                     })
            {
                _out.WriteLine($"{type.Name,-16} public Clone -> "
                               + $"{(type.GetMethod("Clone", BindingFlags.Instance | BindingFlags.Public) is null ? "absent" : "present")}");
            }
        }
    }

    // =============================================================================================
    // 8. A condition's values are read more than once.
    // =============================================================================================

    public sealed class DrcValueReadProbe : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZyContext _db;

        public DrcValueReadProbe(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZyContext(_connection);
            _db.Database.EnsureCreated();

            foreach (string code in new[] { "1", "2", "3" })
            {
                _db.Roles.Add(new ZyRole { Code = code, Name = new ZyLocalizedText { En = code } });
            }

            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        /// <summary>
        /// "A value is read once to validate its format and again to build the predicate … one whose
        /// <c>ToString()</c> answers differently each time is validated as one value and queried as
        /// another."
        /// </summary>
        [Fact]
        public void A_value_is_read_more_than_once_so_an_unstable_one_is_queried_as_another()
        {
            DrcDrifting drifting = new();

            Filter filter = new()
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition
                        {
                            Sort = 1,
                            Field = "Code",
                            DataType = DataType.Text,
                            Operator = Operator.Equal,
                            Values = new List<object> { drifting }
                        }
                    }
                }
            };

            FilterResult<ZyRole> result = _db.Roles.ToList(filter, getQueryString: true);

            _out.WriteLine($"the value was read {drifting.Reads} time(s)");
            _out.WriteLine($"rows: {string.Join(", ", result.Data.Select(role => role.Code))}");
            _out.WriteLine($"SQL contains '= 2': {result.QueryString?.Contains("2", StringComparison.Ordinal)}");

            Assert.True(drifting.Reads > 1, $"read {drifting.Reads} time(s)");

            // Validated as the first reading, queried as a later one.
            Assert.Single(result.Data);
            Assert.NotEqual("1", result.Data[0].Code);
        }

        /// <summary>No policy decision reads a value's content — only how many there are.</summary>
        [Fact]
        public void A_guarded_query_decides_nothing_from_a_value()
        {
            DrcDrifting drifting = new();

            Filter filter = new()
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition
                        {
                            Sort = 1,
                            Field = "Code",
                            DataType = DataType.Text,
                            Operator = Operator.Equal,
                            Values = new List<object> { drifting }
                        }
                    }
                }
            };

            FilterResult<ZyRole> result = DrcProviderInFrontProbe.Guard(_db.Roles, DwTier.Strict).ToList(filter);

            _out.WriteLine($"guarded: the value was read {drifting.Reads} time(s), {result.Data.Count} row(s) came back");

            Assert.True(drifting.Reads > 1);
        }
    }
}
