using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>A row to ask malformed requests about.</summary>
    public class Rd8Stocked
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }
    }

    /// <summary>The database behind it, since a segment is read asynchronously.</summary>
    public sealed class Rd8StockContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public Rd8StockContext(SqliteConnection connection) => _connection = connection;

        public DbSet<Rd8Stocked> Items => Set<Rd8Stocked>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    /// <summary>
    /// A request whose lists hold a null entry is refused as a malformed request, with a
    /// <see cref="LogicException"/> naming the list, by every method that takes the shape, with or
    /// without a policy.
    /// </summary>
    /// <remarks>
    /// A request body can say <c>"conditions": [null]</c>. It used to surface as a
    /// <see cref="NullReferenceException"/> or an <see cref="ArgumentNullException"/> from wherever
    /// the entry was first touched: the sort-order check, the ordering, the name lookup, or, under a
    /// policy, the copy the sanitizer takes before it reads anything.
    /// </remarks>
    public sealed class Rd8NullEntryTests : IDisposable
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");
        private readonly Rd8StockContext _db;

        public Rd8NullEntryTests()
        {
            _connection.Open();
            _db = new Rd8StockContext(_connection);
            _db.Database.EnsureCreated();
            _db.Items.Add(new Rd8Stocked { Id = 1, Name = "a", Price = 2m });
            _db.Items.Add(new Rd8Stocked { Id = 2, Name = "b", Price = 3m });
            _db.SaveChanges();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static readonly PolicyResolver Attributes = new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private PolicyQueryable<Rd8Stocked> Guarded(DwTier tier) => _db.Items.ApplyPolicy(
            new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
            new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } },
            Attributes);

        private static Condition Real() =>
            new() { Sort = 1, Field = "Id", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = { 0 } };

        private static ConditionGroup Group(List<Condition>? conditions = null, List<ConditionGroup>? subs = null) => new()
        {
            Connector = Connector.And,
            Conditions = conditions ?? new List<Condition> { Real() },
            SubConditionGroups = subs ?? new List<ConditionGroup>()
        };

        private static GroupBy Grouping(List<AggregateBy>? aggregates = null) => new()
        {
            Fields = { "Name" },
            AggregateBy = aggregates ?? new List<AggregateBy> { new() { Aggregator = Aggregator.Count, Alias = "n" } }
        };

        /// <summary>Every malformed filter, with the refusal it earns.</summary>
        public static TheoryData<string, string> Filters() => new()
        {
            { "conditions", ErrorCode.NullEntry("Conditions") },
            { "subgroups", ErrorCode.NullEntry("SubConditionGroups") },
            { "nested", ErrorCode.NullEntry("Conditions") },
            { "orders", ErrorCode.NullEntry("Orders") },
            { "selects", ErrorCode.InvalidField },
            { "blank-select", ErrorCode.InvalidField },
            { "white-select", ErrorCode.InvalidField },
        };

        private static Filter Malformed(string which) => which switch
        {
            "conditions" => new Filter { ConditionGroup = Group(new List<Condition> { Real(), null! }) },
            "subgroups" => new Filter { ConditionGroup = Group(subs: new List<ConditionGroup> { null! }) },
            "nested" => new Filter { ConditionGroup = Group(subs: new List<ConditionGroup> { Group(new List<Condition> { null! }) }) },
            "orders" => new Filter { Orders = new List<OrderBy> { new() { Sort = 1, Field = "Id" }, null! } },
            "selects" => new Filter { Selects = new List<string> { "Id", null! } },
            "blank-select" => new Filter { Selects = new List<string> { "Id", "" } },
            "white-select" => new Filter { Selects = new List<string> { "  " } },
            _ => throw new ArgumentOutOfRangeException(nameof(which))
        };

        [Theory]
        [MemberData(nameof(Filters))]
        public void A_malformed_filter_is_refused_as_one_without_a_policy(string which, string message)
        {
            Assert.Equal(message, Assert.Throws<LogicException>(() => _db.Items.ToList(Malformed(which))).Message);
            Assert.Equal(message, Assert.Throws<LogicException>(() => _db.Items.ToListDynamic(Malformed(which))).Message);
            Assert.Equal(message, Assert.Throws<LogicException>(() => _db.Items.Filter(Malformed(which)).ToList()).Message);
            Assert.Equal(message, Assert.Throws<LogicException>(() => _db.Items.FilterDynamic(Malformed(which))).Message);
        }

        [Theory]
        [MemberData(nameof(Filters))]
        public async Task A_malformed_filter_is_refused_as_one_when_read_asynchronously(string which, string message)
        {
            Assert.Equal(message, (await Assert.ThrowsAsync<LogicException>(() => _db.Items.ToListAsync(Malformed(which)))).Message);
            Assert.Equal(message, (await Assert.ThrowsAsync<LogicException>(() => _db.Items.ToListAsyncDynamic(Malformed(which)))).Message);
        }

        [Theory]
        [MemberData(nameof(Filters))]
        public void A_malformed_filter_is_refused_as_one_under_a_policy(string which, string message)
        {
            foreach (DwTier tier in new[] { DwTier.Strict, DwTier.Convenience })
            {
                Assert.Equal(message, Assert.Throws<LogicException>(() => Guarded(tier).ToList(Malformed(which))).Message);
                Assert.Equal(message, Assert.Throws<LogicException>(() => Guarded(tier).ToListDynamic(Malformed(which))).Message);
                Assert.Equal(message, Assert.Throws<LogicException>(() => Guarded(tier).Filter(Malformed(which))).Message);
            }
        }

        [Fact]
        public void The_composable_methods_refuse_it_too()
        {
            string conditions = ErrorCode.NullEntry("Conditions");
            string orders = ErrorCode.NullEntry("Orders");
            string aggregates = ErrorCode.NullEntry("AggregateBy");

            Assert.Equal(conditions, Assert.Throws<LogicException>(() => _db.Items.Where(Group(new List<Condition> { null! }))).Message);
            Assert.Equal(orders, Assert.Throws<LogicException>(() => _db.Items.Order(new List<OrderBy> { null! })).Message);
            Assert.Equal(ErrorCode.InvalidField, Assert.Throws<LogicException>(() => _db.Items.Select(new List<string> { null! })).Message);
            Assert.Equal(ErrorCode.InvalidField, Assert.Throws<LogicException>(() => _db.Items.SelectDynamic(new List<string> { "" })).Message);
            Assert.Equal(aggregates, Assert.Throws<LogicException>(() => _db.Items.Group(Grouping(new List<AggregateBy> { null! }))).Message);

            foreach (DwTier tier in new[] { DwTier.Strict, DwTier.Convenience })
            {
                Assert.Equal(conditions, Assert.Throws<LogicException>(() => Guarded(tier).Where(Group(new List<Condition> { null! }))).Message);
                Assert.Equal(orders, Assert.Throws<LogicException>(() => Guarded(tier).Order(new List<OrderBy> { null! })).Message);
                Assert.Equal(ErrorCode.InvalidField, Assert.Throws<LogicException>(() => Guarded(tier).Select(new List<string> { null! })).Message);
                Assert.Equal(ErrorCode.InvalidField, Assert.Throws<LogicException>(() => Guarded(tier).SelectDynamic(new List<string> { "" })).Message);
                Assert.Equal(aggregates, Assert.Throws<LogicException>(() => Guarded(tier).Group(Grouping(new List<AggregateBy> { null! }))).Message);
            }
        }

        /// <summary>Every malformed summary, with the refusal it earns.</summary>
        public static TheoryData<string, string> Summaries() => new()
        {
            { "conditions", ErrorCode.NullEntry("Conditions") },
            { "aggregates", ErrorCode.NullEntry("AggregateBy") },
            { "having", ErrorCode.NullEntry("Conditions") },
            { "having-subgroups", ErrorCode.NullEntry("SubConditionGroups") },
            { "orders", ErrorCode.NullEntry("Orders") },
        };

        private static Summary MalformedSummary(string which) => which switch
        {
            "conditions" => new Summary { ConditionGroup = Group(new List<Condition> { null! }), GroupBy = Grouping() },
            "aggregates" => new Summary { GroupBy = Grouping(new List<AggregateBy> { null! }) },
            "having" => new Summary { GroupBy = Grouping(), Having = Group(new List<Condition> { null! }) },
            "having-subgroups" => new Summary { GroupBy = Grouping(), Having = Group(new List<Condition>(), new List<ConditionGroup> { null! }) },
            "orders" => new Summary { GroupBy = Grouping(), Orders = new List<OrderBy> { null! } },
            _ => throw new ArgumentOutOfRangeException(nameof(which))
        };

        [Theory]
        [MemberData(nameof(Summaries))]
        public async Task A_malformed_summary_is_refused_as_one(string which, string message)
        {
            Assert.Equal(message, Assert.Throws<LogicException>(() => _db.Items.ToList(MalformedSummary(which))).Message);
            Assert.Equal(message, Assert.Throws<LogicException>(() => _db.Items.Summary(MalformedSummary(which))).Message);
            Assert.Equal(message, (await Assert.ThrowsAsync<LogicException>(() => _db.Items.ToListAsync(MalformedSummary(which)))).Message);

            foreach (DwTier tier in new[] { DwTier.Strict, DwTier.Convenience })
            {
                Assert.Equal(message, Assert.Throws<LogicException>(() => Guarded(tier).ToList(MalformedSummary(which))).Message);
                Assert.Equal(message, Assert.Throws<LogicException>(() => Guarded(tier).Summary(MalformedSummary(which))).Message);
                Assert.Equal(message, (await Assert.ThrowsAsync<LogicException>(() => Guarded(tier).ToListAsync(MalformedSummary(which)))).Message);
            }
        }

        /// <summary>Every malformed segment, with the refusal it earns.</summary>
        public static TheoryData<string, string> Segments() => new()
        {
            { "sets", ErrorCode.NullEntry("ConditionSets") },
            { "second-set", ErrorCode.NullEntry("ConditionSets") },
            { "conditions", ErrorCode.NullEntry("Conditions") },
            { "orders", ErrorCode.NullEntry("Orders") },
            { "selects", ErrorCode.InvalidField },
        };

        private static ConditionSet Set(int sort, ConditionGroup? group = null) =>
            new() { Sort = sort, Intersection = sort == 1 ? null : Intersection.Union, ConditionGroup = group ?? Group() };

        private static Segment MalformedSegment(string which) => which switch
        {
            "sets" => new Segment { ConditionSets = new List<ConditionSet> { null! } },
            "second-set" => new Segment { ConditionSets = new List<ConditionSet> { Set(1), null! } },
            "conditions" => new Segment { ConditionSets = new List<ConditionSet> { Set(1, Group(new List<Condition> { null! })) } },
            "orders" => new Segment { ConditionSets = new List<ConditionSet> { Set(1) }, Orders = new List<OrderBy> { null! } },
            "selects" => new Segment { ConditionSets = new List<ConditionSet> { Set(1) }, Selects = new List<string> { null! } },
            _ => throw new ArgumentOutOfRangeException(nameof(which))
        };

        [Theory]
        [MemberData(nameof(Segments))]
        public async Task A_malformed_segment_is_refused_as_one(string which, string message)
        {
            Assert.Equal(message, (await Assert.ThrowsAsync<LogicException>(() => _db.Items.ToListAsync(MalformedSegment(which)))).Message);

            foreach (DwTier tier in new[] { DwTier.Strict, DwTier.Convenience })
            {
                Assert.Equal(message, (await Assert.ThrowsAsync<LogicException>(() => Guarded(tier).ToListAsync(MalformedSegment(which)))).Message);
            }
        }

        /// <summary>Precision: a list that is absent is not a list with an absent entry, and means what it meant.</summary>
        [Fact]
        public async Task An_absent_list_still_means_what_it_meant()
        {
            Filter filter = new() { ConditionGroup = new ConditionGroup { Connector = Connector.And, Conditions = null!, SubConditionGroups = null! } };
            Summary summary = new() { GroupBy = Grouping(), Having = new ConditionGroup { Connector = Connector.And, Conditions = null!, SubConditionGroups = null! } };
            Segment segment = new() { ConditionSets = new List<ConditionSet> { Set(1) }, Orders = null, Selects = null };

            Assert.Equal(2, _db.Items.ToList(filter).Data!.Count);
            Assert.Equal(2, _db.Items.ToList(summary).Data!.Count);
            Assert.Equal(2, (await _db.Items.ToListAsync(segment)).Data!.Count);

            foreach (DwTier tier in new[] { DwTier.Strict, DwTier.Convenience })
            {
                Assert.Equal(2, Guarded(tier).ToList(filter.Clone()).Data!.Count);
                Assert.Equal(2, Guarded(tier).ToList(summary.Clone()).Data!.Count);
                Assert.Equal(2, (await Guarded(tier).ToListAsync(segment.Clone())).Data!.Count);
            }
        }

        /// <summary>A copy copies what is there, so the refusal is the running method's and reads the same for both.</summary>
        [Fact]
        public void A_copy_keeps_a_null_entry_rather_than_failing_on_it()
        {
            Filter filter = Malformed("conditions").Clone();
            Segment segment = MalformedSegment("second-set").Clone();
            Summary summary = MalformedSummary("aggregates").Clone();

            Assert.Null(filter.ConditionGroup!.Conditions[1]);
            Assert.Null(segment.ConditionSets[1]);
            Assert.Null(summary.GroupBy!.AggregateBy[0]);
            Assert.Null(Malformed("orders").Clone().Orders![1]);
            Assert.Null(Malformed("subgroups").Clone().ConditionGroup!.SubConditionGroups[0]);
            Assert.Null(MalformedSegment("orders").Clone().Orders![0]);
            Assert.Null(MalformedSummary("orders").Clone().Orders![0]);
        }
    }
}
