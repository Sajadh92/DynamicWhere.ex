using System.Linq.Expressions;
using System.Reflection;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Discovery;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

// A docs-against-code probe for the 3.3.0 claims. Nothing here is a fix; every fact reads what the
// code does and prints it, so the report can quote an outcome rather than an inference.

namespace DynamicWhere.Tests.Policies
{
    // ---- a model with a converted column, a date, a nullable and an unmapped getter --------------

    public class QpTag
    {
        public string Value { get; set; } = string.Empty;

        /// <summary>A getter over the one column the converter stores.</summary>
        public bool IsEmpty => string.IsNullOrWhiteSpace(Value);
    }

    public class QpTicket
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public int? Score { get; set; }

        /// <summary>Stored through a value converter, so the model maps it as one column.</summary>
        public QpTag Tag { get; set; } = new();

        [DwDenied]
        public string Secret { get; set; } = string.Empty;

        /// <summary>Unmapped: a getter over two columns of the entity itself.</summary>
        public string Display => $"{Code}:{Id}";
    }

    public sealed class QpContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public QpContext(SqliteConnection connection) => _connection = connection;

        public DbSet<QpTicket> Tickets => Set<QpTicket>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<QpTicket>().Property(ticket => ticket.Tag)
                .HasConversion(tag => tag.Value, value => new QpTag { Value = value });

            model.Entity<QpTicket>().Ignore(ticket => ticket.Display);
        }
    }

    // ---- default-order shapes --------------------------------------------------------------------

    [DwEntity(DefaultOrder = "Code desc")]
    public class QpCtorRow
    {
        public QpCtorRow()
        {
        }

        /// <summary>A constructor with arguments. Which member each one sets is not recorded.</summary>
        public QpCtorRow(int id, string code)
        {
            Id = id;
            Code = code;
        }

        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;
    }

    [DwEntity(DefaultOrder = "Code desc")]
    public class QpPlainRow
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;
    }

    [DwEntity(DefaultOrder = "Code desc")]
    public class QpTicketRow
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;
    }

    /// <summary>A catalogue-comparison stand-in, exposed nowhere else.</summary>
    public sealed class QpOnlyHere
    {
    }

    public sealed class QpAlsoOnlyHere
    {
    }

    public sealed class QpProviderA : IDwPolicyProvider
    {
        public IReadOnlyList<PolicyFragment> GetFragments(Type entityType, DwPolicyContext context) =>
            Array.Empty<PolicyFragment>();
    }

    public sealed class QpProviderB : IDwPolicyProvider
    {
        public IReadOnlyList<PolicyFragment> GetFragments(Type entityType, DwPolicyContext context) =>
            Array.Empty<PolicyFragment>();
    }

    // =============================================================================================
    // 1. The source table, row by row, and the Selects exemption.
    // =============================================================================================

    public sealed class QpSourceTableProbe : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly QpContext _db;

        public QpSourceTableProbe(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new QpContext(_connection);
            _db.Database.EnsureCreated();
            _db.Tickets.Add(new QpTicket
            {
                Code = "alpha",
                CreatedAt = new DateTime(2026, 3, 4),
                Score = 7,
                Tag = new QpTag { Value = "red" },
                Secret = "s"
            });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static Filter Where(string field, string value, DataType type = DataType.Text) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions = { new Condition { Field = field, DataType = type, Operator = Operator.Equal, Values = { value } } }
            }
        };

        private void Row(string label, IQueryable source, string path)
        {
            object shape = typeof(RowShape)
                .GetMethod("Of", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!
                .MakeGenericMethod(source.ElementType)
                .Invoke(null, new object[] { source })!;

            bool? answer = (bool?)typeof(RowShape)
                .GetMethod("Expresses", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(shape, new object[] { path });

            _out.WriteLine($"{label,-52} Expresses(\"{path}\") = {(answer is null ? "null (left alone)" : answer.ToString())}");
        }

        // ---- every row of the table, read straight off RowShape -----------------------------------

        [Fact]
        public void The_source_table_row_by_row()
        {
            Row("an entity", _db.Tickets, "Tag.IsEmpty");
            Row("an entity, unmapped getter on itself", _db.Tickets, "Display");
            Row("an entity, a framework member (Length)", _db.Tickets, "Code.Length");
            Row("an entity, a framework member (Year)", _db.Tickets, "CreatedAt.Year");
            Row("an entity, a framework member (HasValue)", _db.Tickets, "Score.HasValue");
            Row("beneath a converted column", _db.Tickets, "Tag.Value");

            Row("rows in memory", new[] { new QpTicket() }.AsQueryable(), "Tag.IsEmpty");
            Row("a source the library cannot read",
                new ZyOwnProvider<QpTicket>(new[] { new QpTicket() }.AsQueryable()), "Tag.IsEmpty");
        }

        /// <summary>Beneath a column the converter decides, so the policy leaves it alone either way.</summary>
        [Fact]
        public void Beneath_a_column_is_left_alone_and_behaves_as_it_does_unguarded()
        {
            Exception? unguarded = Record.Exception(
                () => _db.Tickets.Where(ticket => ticket.Tag.IsEmpty == false).ToList());

            Exception? guarded = Record.Exception(
                () => Guard(_db.Tickets, DwTier.Strict).ToList(Where("Tag.IsEmpty", "false", DataType.Boolean)));

            _out.WriteLine($"converted column: unguarded={unguarded?.GetType().Name ?? "ran"} guarded={guarded?.GetType().Name ?? "ran"}");

            Assert.IsNotType<PolicyException>(guarded);
        }

        // ---- the framework members the text names, guarded and unguarded --------------------------

        [Fact]
        public void Length_runs_guarded_and_unguarded()
        {
            Assert.Single(_db.Tickets.Where(ticket => ticket.Code.Length == 5).ToList());

            FilterResult<QpTicket> guarded = Guard(_db.Tickets, DwTier.Strict)
                .ToList(Where("Code.Length", "5", DataType.Number));

            _out.WriteLine($"Length: unguarded=1 guarded={guarded.Data.Count}");

            Assert.Single(guarded.Data);
        }

        [Fact]
        public void Year_runs_guarded_and_unguarded()
        {
            Assert.Single(_db.Tickets.Where(ticket => ticket.CreatedAt.Year == 2026).ToList());

            FilterResult<QpTicket> guarded = Guard(_db.Tickets, DwTier.Strict)
                .ToList(Where("CreatedAt.Year", "2026", DataType.Number));

            _out.WriteLine($"Year: unguarded=1 guarded={guarded.Data.Count}");

            Assert.Single(guarded.Data);
        }

        [Fact]
        public void HasValue_runs_guarded_and_unguarded()
        {
            Assert.Single(_db.Tickets.Where(ticket => ticket.Score.HasValue).ToList());

            Exception? guarded = Record.Exception(
                () => Guard(_db.Tickets, DwTier.Strict).ToList(Where("Score.HasValue", "true", DataType.Boolean)));

            _out.WriteLine($"HasValue: unguarded=1 guarded={guarded?.GetType().Name ?? "ran"} {guarded?.Message}");

            Assert.IsNotType<PolicyException>(guarded);
            Assert.Null(guarded);
        }

        // ---- Selects, both tiers, filter and segment -----------------------------------------------

        [Fact]
        public void Selects_is_exempt_under_strict()
        {
            FilterResult<QpTicket> result = Guard(_db.Tickets, DwTier.Strict)
                .ToList(new Filter { Selects = new List<string> { "Id", "Display" } });

            _out.WriteLine($"strict Selects of an unmapped getter: {result.Data.Count} row(s)");

            Assert.Single(result.Data);
        }

        [Fact]
        public void Selects_is_exempt_under_convenience()
        {
            FilterResult<QpTicket> result = Guard(_db.Tickets, DwTier.Convenience)
                .ToList(new Filter { Selects = new List<string> { "Id", "Display" } });

            _out.WriteLine($"convenience Selects of an unmapped getter: {result.Data.Count} row(s)");

            Assert.Single(result.Data);
        }

        [Fact]
        public async Task Selects_is_exempt_inside_a_segment()
        {
            SegmentResult<QpTicket> result = await Guard(_db.Tickets, DwTier.Strict).ToListAsync(new Segment
            {
                Selects = new List<string> { "Id", "Display" },
                ConditionSets =
                {
                    new ConditionSet
                    {
                        Sort = 1,
                        ConditionGroup = new ConditionGroup
                        {
                            Conditions = { new Condition { Field = "Code", DataType = DataType.Text, Operator = Operator.Equal, Values = { "alpha" } } }
                        }
                    }
                }
            });

            _out.WriteLine($"segment Selects of an unmapped getter: {result.Data.Count} row(s)");

            Assert.Single(result.Data);
        }

        [Fact]
        public void A_denied_field_named_in_Selects_is_still_refused()
        {
            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(_db.Tickets, DwTier.Strict).ToList(new Filter { Selects = new List<string> { "Id", "Secret" } }));

            _out.WriteLine($"denied field in Selects: {refusal.ErrorCode} FieldPath={refusal.FieldPath}");

            Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, refusal.ErrorCode);
        }

        [Fact]
        public void An_unknown_name_in_Selects_is_still_refused()
        {
            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(_db.Tickets, DwTier.Strict).ToList(new Filter { Selects = new List<string> { "Id", "NoSuchColumn" } }));

            _out.WriteLine($"unknown name in Selects: {refusal.ErrorCode}");

            Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, refusal.ErrorCode);
        }

        // ---- which clauses refuse -------------------------------------------------------------------

        [Fact]
        public void An_aggregated_field_is_refused()
        {
            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(_db.Tickets, DwTier.Strict).ToList(new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = new List<string> { "Code" },
                        AggregateBy = new List<AggregateBy>
                        {
                            new() { Alias = "N", Aggregator = Aggregator.Count },
                            new() { Alias = "D", Field = "Display", Aggregator = Aggregator.Maximum }
                        }
                    }
                }));

            _out.WriteLine($"aggregated field: {refusal.ErrorCode}");

            Assert.Equal(PolicyErrorCode.FieldDeniedForAggregate, refusal.ErrorCode);
        }

        [Fact]
        public void The_composed_clauses_each_report_their_own_answer()
        {
            PolicyQueryable<QpTicket> guarded = Guard(_db.Tickets, DwTier.Strict);

            Exception? where = Record.Exception(() => guarded.Where(new Condition
            {
                Field = "Display", DataType = DataType.Text, Operator = Operator.Equal, Values = { "alpha:1" }
            }));

            Exception? order = Record.Exception(
                () => guarded.Order(new OrderBy { Field = "Display", Direction = Direction.Ascending }));

            Exception? select = Record.Exception(() => guarded.Select(new List<string> { "Id", "Display" }));

            Exception? page = Record.Exception(() => guarded.Page(new PageBy { PageNumber = 1, PageSize = 10 }));

            _out.WriteLine($"composed .Where   -> {Name(where)}");
            _out.WriteLine($"composed .Order   -> {Name(order)}");
            _out.WriteLine($"composed .Select  -> {Name(select)}");
            _out.WriteLine($"composed .Page    -> {Name(page)}");

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, ((PolicyException)where!).ErrorCode);
            Assert.Equal(PolicyErrorCode.FieldDeniedForOrder, ((PolicyException)order!).ErrorCode);
            Assert.Null(select);
            Assert.Null(page);

            static string Name(Exception? error) =>
                error is PolicyException policy
                    ? $"{policy.GetType().Name} {policy.ErrorCode}"
                    : error?.GetType().Name ?? "no refusal";
        }

        // ---- LastTrace on every terminal ------------------------------------------------------------

        [Fact]
        public async Task LastTrace_is_readable_after_a_refusal_on_every_terminal()
        {
            Filter filter = Where("Display", "alpha:1");

            Report("ToList(Filter)", g => g.ToList(filter));
            await ReportAsync("ToListAsync(Filter)", g => g.ToListAsync(Where("Display", "alpha:1")));
            Report("ToListDynamic(Filter)", g => g.ToListDynamic(Where("Display", "alpha:1")));
            await ReportAsync("ToListAsyncDynamic(Filter)", g => g.ToListAsyncDynamic(Where("Display", "alpha:1")));

            Report("ToList(Summary)", g => g.ToList(new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "Display" },
                    AggregateBy = new List<AggregateBy> { new() { Alias = "N", Aggregator = Aggregator.Count } }
                }
            }));

            await ReportAsync("ToListAsync(Summary)", g => g.ToListAsync(new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "Display" },
                    AggregateBy = new List<AggregateBy> { new() { Alias = "N", Aggregator = Aggregator.Count } }
                }
            }));

            await ReportAsync("ToListAsync(Segment)", g => g.ToListAsync(new Segment
            {
                ConditionSets =
                {
                    new ConditionSet
                    {
                        Sort = 1,
                        ConditionGroup = new ConditionGroup
                        {
                            Conditions = { new Condition { Field = "Display", DataType = DataType.Text, Operator = Operator.Equal, Values = { "alpha:1" } } }
                        }
                    }
                }
            }));

            Report("composed .Where", g => g.Where(new Condition
            {
                Field = "Display", DataType = DataType.Text, Operator = Operator.Equal, Values = { "alpha:1" }
            }));

            Report("composed .Order", g => g.Order(new OrderBy { Field = "Display", Direction = Direction.Ascending }));

            Report("composed .Group", g => g.Group(new GroupBy
            {
                Fields = new List<string> { "Display" },
                AggregateBy = new List<AggregateBy> { new() { Alias = "N", Aggregator = Aggregator.Count } }
            }));

            void Report(string label, Action<PolicyQueryable<QpTicket>> call)
            {
                PolicyQueryable<QpTicket> guarded = Guard(_db.Tickets, DwTier.Strict);

                Assert.ThrowsAny<PolicyException>(() => call(guarded));

                Describe(label, guarded.LastTrace);
            }

            async Task ReportAsync(string label, Func<PolicyQueryable<QpTicket>, Task> call)
            {
                PolicyQueryable<QpTicket> guarded = Guard(_db.Tickets, DwTier.Strict);

                await Assert.ThrowsAnyAsync<PolicyException>(() => call(guarded));

                Describe(label, guarded.LastTrace);
            }

            void Describe(string label, PolicyTrace? trace)
            {
                string reason = trace?.Decisions
                    .Where(decision => decision.Reason is not null && decision.Reason.Contains("cannot compute"))
                    .Select(decision => decision.FieldPath)
                    .FirstOrDefault() ?? "(no 'cannot compute' decision)";

                _out.WriteLine($"{label,-28} LastTrace={(trace is null ? "null" : "set")} reason on: {reason}");

                Assert.NotNull(trace);
            }
        }
    }

    // =============================================================================================
    // 2. DefaultOrder: the four shapes the corrected remarks name.
    // =============================================================================================

    public sealed class QpDefaultOrderProbe : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly QpContext _db;

        public QpDefaultOrderProbe(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new QpContext(_connection);
            _db.Database.EnsureCreated();

            // Inserted B, C, A: none of the expected orders is the insertion order.
            foreach (string code in new[] { "B", "C", "A" })
            {
                _db.Tickets.Add(new QpTicket { Code = code, CreatedAt = new DateTime(2026, 1, 1), Tag = new QpTag() });
            }

            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = DwTier.Strict },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        [Fact]
        public void A_constructor_with_arguments_and_an_initializer_still_takes_the_default()
        {
            IQueryable<QpCtorRow> rows = _db.Tickets
                .Select(ticket => new QpCtorRow(ticket.Id, ticket.Code) { Code = ticket.Code });

            string order = string.Join(",", Guard(rows).ToList(new Filter()).Data.Select(row => row.Code));

            _out.WriteLine($"ctor with args + initializer: {order}");

            Assert.Equal("C,B,A", order);
        }

        [Fact]
        public void A_projection_with_no_initializer_at_all_stays_in_its_own_order()
        {
            IQueryable<QpCtorRow> rows = _db.Tickets.Select(ticket => new QpCtorRow(ticket.Id, ticket.Code));

            string order = string.Join(",", Guard(rows).ToList(new Filter()).Data.Select(row => row.Code));

            _out.WriteLine($"ctor with args, no initializer: {order}");

            Assert.NotEqual("C,B,A", order);
        }

        [Fact]
        public void A_filter_carrying_Selects_is_ordered_as_any_other_filter_is()
        {
            IQueryable<QpPlainRow> rows = _db.Tickets
                .Select(ticket => new QpPlainRow { Id = ticket.Id, Code = ticket.Code });

            string order = string.Join(
                ",",
                Guard(rows).ToList(new Filter { Selects = new List<string> { "Id", "Code" } })
                    .Data.Select(row => row.Code));

            _out.WriteLine($"filter carrying Selects: {order}");

            Assert.Equal("C,B,A", order);
        }

        [Fact]
        public void A_Select_composed_on_the_guarded_handle_leaves_the_chain_unordered()
        {
            IQueryable<QpPlainRow> rows = _db.Tickets
                .Select(ticket => new QpPlainRow { Id = ticket.Id, Code = ticket.Code });

            string order = string.Join(
                ",",
                Guard(rows).Select(new List<string> { "Id", "Code" })
                    .ToList(new Filter()).Data.Select(row => row.Code));

            _out.WriteLine($"composed .Select then ToList(new Filter()): {order}");

            Assert.NotEqual("C,B,A", order);
        }
    }

    // =============================================================================================
    // 3. Posture comparison, property by property.
    // =============================================================================================

    public sealed class QpPostureProbe
    {
        private readonly ITestOutputHelper _out;

        public QpPostureProbe(ITestOutputHelper output)
        {
            _out = output;
            PolicyBootstrap.Ensure();
        }

        private static readonly MethodInfo SamePosture = typeof(DwPolicy)
            .GetMethod("SamePosture", BindingFlags.Static | BindingFlags.NonPublic)!;

        private static readonly MethodInfo ProviderKinds = typeof(DwPolicy)
            .GetMethod("ProviderTypes", BindingFlags.Static | BindingFlags.NonPublic)!;

        /// <summary>The comparison key a list of sources reduces to, which is what a second call is compared by.</summary>
        private static Type[] Kinds(params IDwPolicyProvider[] providers) =>
            (Type[])ProviderKinds.Invoke(null, new object?[] { providers })!;

        private static bool Same(DwPolicyOptions asked, params IDwPolicyProvider[] providers) =>
            (bool)SamePosture.Invoke(null, new object?[] { DwPolicy.Options, asked, providers })!;

        /// <summary>A posture holding exactly what is in force, value by value.</summary>
        private static DwPolicyOptions Copy()
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

            foreach (PropertyInfo cap in Caps())
            {
                if (cap.Name == "MinGroupSize" && !inForce.Caps.IsMinGroupSizeSet)
                {
                    continue;
                }

                cap.SetValue(copy.Caps, cap.GetValue(inForce.Caps));
            }

            foreach (KeyValuePair<Type, string> exposed in inForce.Entities.Entities)
            {
                copy.Entities.Expose(exposed.Key, exposed.Value);
            }

            return copy;
        }

        private static PropertyInfo[] Caps() => typeof(DwCaps)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanWrite && property.PropertyType == typeof(int))
            .OrderBy(property => property.Name)
            .ToArray();

        // ---- the options, one property at a time ----------------------------------------------------

        [Fact]
        public void An_identical_posture_is_the_same_posture()
        {
            Assert.True(Same(Copy()));
        }

        [Fact]
        public void Every_option_the_text_lists_as_compared_really_is()
        {
            Check("Tier", o => o.Tier = o.Tier == DwTier.Strict ? DwTier.Convenience : DwTier.Strict);
            Check("DryRun", o => o.DryRun = !o.DryRun);
            // Compared by the value that applies: the flag defaults to the tier's own answer, so what
            // has to be refused is a value answering differently, not a value written down.
            Check("IncludeTraceInResult", o => o.IncludeTraceInResult =
                !(o.IncludeTraceInResult ?? o.Tier == DwTier.Convenience));
            Check("AuditRefusals", o => o.AuditRefusals = !o.AuditRefusals);
            Check("HashSalt", o => o.HashSalt = "a-salt-long-enough-for-the-minimum");
            Check("StoreFailure", o => o.StoreFailure =
                o.StoreFailure == StoreFailureMode.FailClosed ? StoreFailureMode.LastKnownGood : StoreFailureMode.FailClosed);
            Check("MaxSnapshotAge", o => o.MaxSnapshotAge = o.MaxSnapshotAge + TimeSpan.FromMinutes(1));
            Check("RefreshInterval", o => o.RefreshInterval = o.RefreshInterval + TimeSpan.FromSeconds(1));

            void Check(string label, Action<DwPolicyOptions> change)
            {
                DwPolicyOptions asked = Copy();

                change(asked);

                bool same = Same(asked);

                _out.WriteLine($"{label,-22} differs -> SamePosture = {same}");

                Assert.False(same);
            }
        }

        [Fact]
        public void The_two_objects_the_text_lists_as_not_compared_really_are_not()
        {
            DwPolicyOptions vault = Copy();
            vault.TokenVault = new QpVault();

            DwPolicyOptions services = Copy();
            services.Services = new QpServices();

            _out.WriteLine($"TokenVault differs -> SamePosture = {Same(vault)}");
            _out.WriteLine($"Services   differs -> SamePosture = {Same(services)}");

            Assert.True(Same(vault));
            Assert.True(Same(services));
        }

        // ---- every cap, one at a time ---------------------------------------------------------------

        [Fact]
        public void Every_cap_value_is_compared()
        {
            PropertyInfo[] caps = Caps();

            _out.WriteLine($"settable int caps on DwCaps: {caps.Length} -> {string.Join(", ", caps.Select(c => c.Name))}");

            foreach (PropertyInfo cap in caps)
            {
                DwPolicyOptions asked = Copy();

                int inForce = (int)cap.GetValue(DwPolicy.Options.Caps)!;

                // Every cap setter refuses below one, so move up rather than down.
                cap.SetValue(asked.Caps, inForce + 1);

                bool same = Same(asked);

                _out.WriteLine($"  {cap.Name,-22} {inForce} -> {inForce + 1}: SamePosture = {same}");

                Assert.False(same);
            }
        }

        [Fact]
        public void IsMinGroupSizeSet_is_not_compared()
        {
            DwPolicyOptions asked = Copy();

            asked.Caps.MinGroupSize = DwPolicy.Options.Caps.MinGroupSize;

            _out.WriteLine(
                $"in force: MinGroupSize={DwPolicy.Options.Caps.MinGroupSize} set={DwPolicy.Options.Caps.IsMinGroupSizeSet}; "
                + $"asked: MinGroupSize={asked.Caps.MinGroupSize} set={asked.Caps.IsMinGroupSizeSet}; "
                + $"SamePosture = {Same(asked)}");

            Assert.NotEqual(DwPolicy.Options.Caps.IsMinGroupSizeSet, asked.Caps.IsMinGroupSizeSet);
            Assert.True(Same(asked));
        }

        [Fact]
        public void The_default_MinGroupSize_is_five_and_unset_reads_as_five()
        {
            DwCaps caps = new();

            _out.WriteLine($"fresh DwCaps: MinGroupSize={caps.MinGroupSize} IsMinGroupSizeSet={caps.IsMinGroupSizeSet} DefaultMinGroupSize={DwCaps.DefaultMinGroupSize}");

            Assert.Equal(5, caps.MinGroupSize);
            Assert.False(caps.IsMinGroupSizeSet);
            Assert.Equal(5, DwCaps.DefaultMinGroupSize);
        }

        // ---- the catalogue, every name ---------------------------------------------------------------

        [Fact]
        public void The_catalogue_is_compared_by_every_name_it_answers_to()
        {
            MethodInfo sameAs = typeof(DwEntityCatalog)
                .GetMethod("SameAs", BindingFlags.Instance | BindingFlags.NonPublic)!;

            bool Ask(DwEntityCatalog left, DwEntityCatalog right) =>
                (bool)sameAs.Invoke(left, new object[] { right })!;

            DwEntityCatalog one = new();
            one.Expose<QpOnlyHere>("only");

            DwEntityCatalog same = new();
            same.Expose<QpOnlyHere>("only");

            DwEntityCatalog extraName = new();
            extraName.Expose<QpOnlyHere>("also");
            extraName.Expose<QpOnlyHere>("only");

            DwEntityCatalog otherName = new();
            otherName.Expose<QpOnlyHere>("different");

            DwEntityCatalog extraType = new();
            extraType.Expose<QpOnlyHere>("only");
            extraType.Expose<QpAlsoOnlyHere>("second");

            _out.WriteLine($"identical                         -> {Ask(one, same)}");
            _out.WriteLine($"Entities reports the same pair, one extra resolvable name -> {Ask(one, extraName)}");
            _out.WriteLine($"  (its Entities count: {one.Entities.Count} vs {extraName.Entities.Count}; "
                           + $"NameOf: {one.NameOf(typeof(QpOnlyHere))} vs {extraName.NameOf(typeof(QpOnlyHere))})");
            _out.WriteLine($"a different public name           -> {Ask(one, otherName)}");
            _out.WriteLine($"one more type                     -> {Ask(one, extraType)}");

            Assert.True(Ask(one, same));
            Assert.Equal(one.Entities.Count, extraName.Entities.Count);
            Assert.Equal(one.NameOf(typeof(QpOnlyHere)), extraName.NameOf(typeof(QpOnlyHere)));
            Assert.False(Ask(one, extraName));
            Assert.False(Ask(one, otherName));
            Assert.False(Ask(one, extraType));
        }

        // ---- the providers ----------------------------------------------------------------------------

        [Fact]
        public void The_provider_rules_hold_type_by_type()
        {
            // Read through the comparison key rather than by driving DwPolicy's static list: that
            // list is process-wide, and a test holding it hostage refuses every suite configuring
            // the assembly's posture in parallel.
            Type[] inForce = Kinds(new QpProviderA(), new QpProviderB());

            bool Ask(params IDwPolicyProvider[] asked) => inForce.SequenceEqual(Kinds(asked));

            bool sameKinds = Ask(new QpProviderA(), new QpProviderB());
            bool reordered = Ask(new QpProviderB(), new QpProviderA());
            bool dropped = Ask(new QpProviderA());
            bool added = Ask(new QpProviderA(), new QpProviderB(), new QpProviderA());
            bool attributeIgnored = Ask(new AttributePolicyProvider(), new QpProviderA(), new QpProviderB());
            bool none = Ask();

            _out.WriteLine($"in force = [QpProviderA, QpProviderB]");
            _out.WriteLine($"  same kinds, new instances        -> {sameKinds}");
            _out.WriteLine($"  reordered                        -> {reordered}");
            _out.WriteLine($"  one dropped                      -> {dropped}");
            _out.WriteLine($"  one added                        -> {added}");
            _out.WriteLine($"  AttributePolicyProvider prefixed -> {attributeIgnored}");
            _out.WriteLine($"  none supplied                    -> {none}");

            Assert.True(sameKinds);
            Assert.False(reordered);
            Assert.False(dropped);
            Assert.False(added);
            Assert.True(attributeIgnored);
            Assert.False(none);
        }

        [Fact]
        public void Handing_the_posture_in_force_back_with_a_source_beside_it_is_refused()
        {
            InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
                () => DwPolicy.Configure(DwPolicy.Options, new QpProviderA()));

            _out.WriteLine($"Configure(DwPolicy.Options, provider) -> {refusal.GetType().Name}");

            Assert.Same(DwPolicy.Options, DwPolicy.Options);
        }

        [Fact]
        public void Handing_the_posture_in_force_back_alone_is_a_no_op()
        {
            DwPolicyOptions inForce = DwPolicy.Options;

            DwPolicy.Configure(DwPolicy.Options);

            _out.WriteLine($"Configure(DwPolicy.Options) -> same instance still in force: {ReferenceEquals(inForce, DwPolicy.Options)}");

            Assert.Same(inForce, DwPolicy.Options);
        }

        private sealed class QpVault : DynamicWhere.ex.Policies.Tokens.IDwTokenVault
        {
            public string GetOrCreate(string scope, string value) => value;
        }

        private sealed class QpServices : IServiceProvider
        {
            public object? GetService(Type serviceType) => null;
        }
    }

    // =============================================================================================
    // 4. Clone, branch by branch.
    // =============================================================================================

    public sealed class QpCloneProbe
    {
        private readonly ITestOutputHelper _out;

        public QpCloneProbe(ITestOutputHelper output) => _out = output;

        [Fact]
        public void Clone_is_public_on_all_three()
        {
            foreach (Type type in new[] { typeof(Filter), typeof(Segment), typeof(Summary) })
            {
                MethodInfo? clone = type.GetMethod("Clone", BindingFlags.Instance | BindingFlags.Public);

                _out.WriteLine($"{type.Name}.Clone: {(clone is null ? "not public" : $"public, returns {clone.ReturnType.Name}")}");

                Assert.NotNull(clone);
                Assert.Equal(type, clone!.ReturnType);
            }
        }

        [Fact]
        public void A_filter_clone_shares_no_branch()
        {
            Filter filter = new()
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions = { new Condition { Field = "Code", DataType = DataType.Text, Operator = Operator.Equal, Values = { "a" } } },
                    SubConditionGroups = { new ConditionGroup { Sort = 1, Conditions = { new Condition { Field = "Id", DataType = DataType.Number, Operator = Operator.Equal, Values = { "1" } } } } }
                },
                Selects = new List<string> { "Id" },
                Orders = new List<OrderBy> { new() { Field = "Id", Direction = Direction.Ascending } },
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            Filter copy = filter.Clone();

            Assert.NotSame(filter.ConditionGroup, copy.ConditionGroup);
            Assert.NotSame(filter.ConditionGroup!.Conditions, copy.ConditionGroup!.Conditions);
            Assert.NotSame(filter.ConditionGroup.Conditions[0], copy.ConditionGroup.Conditions[0]);
            Assert.NotSame(filter.ConditionGroup.SubConditionGroups[0], copy.ConditionGroup.SubConditionGroups[0]);
            Assert.NotSame(filter.ConditionGroup.SubConditionGroups[0].Conditions[0], copy.ConditionGroup.SubConditionGroups[0].Conditions[0]);
            Assert.NotSame(filter.Selects, copy.Selects);
            Assert.NotSame(filter.Orders, copy.Orders);
            Assert.NotSame(filter.Orders![0], copy.Orders![0]);
            Assert.NotSame(filter.Page, copy.Page);

            copy.Page!.PageNumber = 2;
            copy.ConditionGroup.Conditions[0].Field = "Other";
            copy.Selects!.Add("Code");

            _out.WriteLine($"filter: original page={filter.Page!.PageNumber} field={filter.ConditionGroup.Conditions[0].Field} selects={filter.Selects!.Count}");

            Assert.Equal(1, filter.Page.PageNumber);
            Assert.Equal("Code", filter.ConditionGroup.Conditions[0].Field);
            Assert.Single(filter.Selects);
        }

        [Fact]
        public void A_null_branch_stays_null()
        {
            Filter filter = new();
            Filter copy = filter.Clone();

            _out.WriteLine($"filter nulls kept: group={copy.ConditionGroup is null} selects={copy.Selects is null} orders={copy.Orders is null} page={copy.Page is null}");

            Assert.Null(copy.ConditionGroup);
            Assert.Null(copy.Selects);
            Assert.Null(copy.Orders);
            Assert.Null(copy.Page);

            Summary summary = new();
            Summary summaryCopy = summary.Clone();

            _out.WriteLine($"summary nulls kept: group={summaryCopy.ConditionGroup is null} groupBy={summaryCopy.GroupBy is null} having={summaryCopy.Having is null} orders={summaryCopy.Orders is null} page={summaryCopy.Page is null}");

            Assert.Null(summaryCopy.ConditionGroup);
            Assert.Null(summaryCopy.GroupBy);
            Assert.Null(summaryCopy.Having);
            Assert.Null(summaryCopy.Orders);
            Assert.Null(summaryCopy.Page);

            Segment segment = new() { Orders = null };
            Segment segmentCopy = segment.Clone();

            _out.WriteLine($"segment nulls kept: selects={segmentCopy.Selects is null} orders={segmentCopy.Orders is null} page={segmentCopy.Page is null}");

            Assert.Null(segmentCopy.Selects);
            Assert.Null(segmentCopy.Orders);
            Assert.Null(segmentCopy.Page);
        }

        [Fact]
        public void A_segment_clone_gives_each_set_its_own_condition_group()
        {
            ConditionGroup shared = new()
            {
                Conditions = { new Condition { Field = "Code", DataType = DataType.Text, Operator = Operator.Equal, Values = { "a" } } }
            };

            Segment segment = new()
            {
                ConditionSets =
                {
                    new ConditionSet { Sort = 1, ConditionGroup = shared },
                    new ConditionSet { Sort = 2, Intersection = Intersection.Union, ConditionGroup = shared }
                },
                Selects = new List<string> { "Id" },
                Orders = new List<OrderBy> { new() { Field = "Id", Direction = Direction.Ascending } },
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            Segment copy = segment.Clone();

            _out.WriteLine(
                "segment: sets share one group before the clone: "
                + $"{ReferenceEquals(segment.ConditionSets[0].ConditionGroup, segment.ConditionSets[1].ConditionGroup)}; after: "
                + $"{ReferenceEquals(copy.ConditionSets[0].ConditionGroup, copy.ConditionSets[1].ConditionGroup)}");

            Assert.NotSame(segment.ConditionSets, copy.ConditionSets);
            Assert.NotSame(segment.ConditionSets[0], copy.ConditionSets[0]);
            Assert.NotSame(segment.ConditionSets[0].ConditionGroup, copy.ConditionSets[0].ConditionGroup);
            Assert.NotSame(copy.ConditionSets[0].ConditionGroup, copy.ConditionSets[1].ConditionGroup);
            Assert.NotSame(segment.Selects, copy.Selects);
            Assert.NotSame(segment.Orders, copy.Orders);
            Assert.NotSame(segment.Orders![0], copy.Orders![0]);
            Assert.NotSame(segment.Page, copy.Page);
        }

        [Fact]
        public void A_summary_clone_reaches_the_group_by_the_aggregates_and_the_having()
        {
            Summary summary = new()
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions = { new Condition { Field = "Code", DataType = DataType.Text, Operator = Operator.Equal, Values = { "a" } } }
                },
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "Code" },
                    AggregateBy = new List<AggregateBy> { new() { Alias = "N", Aggregator = Aggregator.Count } }
                },
                Having = new ConditionGroup
                {
                    Conditions = { new Condition { Field = "N", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = { "1" } } }
                },
                Orders = new List<OrderBy> { new() { Field = "N", Direction = Direction.Descending } },
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            Summary copy = summary.Clone();

            Assert.NotSame(summary.ConditionGroup, copy.ConditionGroup);
            Assert.NotSame(summary.GroupBy, copy.GroupBy);
            Assert.NotSame(summary.GroupBy!.Fields, copy.GroupBy!.Fields);
            Assert.NotSame(summary.GroupBy.AggregateBy, copy.GroupBy.AggregateBy);
            Assert.NotSame(summary.GroupBy.AggregateBy[0], copy.GroupBy.AggregateBy[0]);
            Assert.NotSame(summary.Having, copy.Having);
            Assert.NotSame(summary.Having!.Conditions[0], copy.Having!.Conditions[0]);
            Assert.NotSame(summary.Orders, copy.Orders);
            Assert.NotSame(summary.Orders![0], copy.Orders![0]);
            Assert.NotSame(summary.Page, copy.Page);

            copy.GroupBy.AggregateBy[0].Alias = "Changed";
            copy.Having.Conditions[0].Field = "Changed";

            _out.WriteLine($"summary: original alias={summary.GroupBy.AggregateBy[0].Alias} having field={summary.Having.Conditions[0].Field}");

            Assert.Equal("N", summary.GroupBy.AggregateBy[0].Alias);
            Assert.Equal("N", summary.Having.Conditions[0].Field);
        }

        [Fact]
        public void A_segment_whose_ConditionSets_is_null_is_what_Clone_gives_back()
        {
            Segment segment = new() { ConditionSets = null! };

            Segment copy = segment.Clone();

            _out.WriteLine($"Segment.Clone with null ConditionSets -> {(copy.ConditionSets is null ? "null" : "empty list")}");

            Assert.Null(copy.ConditionSets);
        }
    }
}
