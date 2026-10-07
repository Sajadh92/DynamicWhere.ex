using System.Text.RegularExpressions;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Audit;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Discovery;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Policies.Validation;
using DynamicWhere.ex.Source;
using DynamicWhere.Tests.Policies;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Linq.Dynamic.Core;

namespace DynamicWhere.Tests;

/// <summary>Instructions read by a status many of them share; the default ends on the key.</summary>
[DwEntity(DefaultOrder = "Batch desc, Id desc", DefaultOrderAsTiebreak = true)]
public class TbInstruction
{
    public int Id { get; set; }

    public int Status { get; set; }

    public int Batch { get; set; }

    public string Code { get; set; } = string.Empty;
}

/// <summary>The same default without the opt-in.</summary>
[DwEntity(DefaultOrder = "Batch desc, Id desc")]
public class TbPlainInstruction
{
    public int Id { get; set; }

    public int Status { get; set; }

    public int Batch { get; set; }
}

/// <summary>A type that declares no default, so even the posture's switch appends nothing to it.</summary>
public class TbUnorderedInstruction
{
    public int Id { get; set; }

    public int Status { get; set; }
}

/// <summary>The opt-in with no default to append.</summary>
[DwEntity(DefaultOrderAsTiebreak = true)]
public class TbEmptyTiebreak
{
    public int Id { get; set; }
}

/// <summary>The opt-in on a default whose every entry is unusable, so there is still nothing to append.</summary>
[DwEntity(DefaultOrder = "Missing desc", DefaultOrderAsTiebreak = true)]
public class TbUnusableTiebreak
{
    public int Id { get; set; }
}

/// <summary>The opt-in on a default whose leading field is audited whenever a query orders by it.</summary>
[DwEntity(DefaultOrder = "Rank desc, Id", DefaultOrderAsTiebreak = true)]
public class TbAudited
{
    public int Id { get; set; }

    public int Status { get; set; }

    [DwAudit(PolicyFeature.Order)]
    public int Rank { get; set; }
}

/// <summary>The opt-in on a default whose leading field may not take part in a segment.</summary>
[DwEntity(DefaultOrder = "Rank desc, Id desc", DefaultOrderAsTiebreak = true)]
public class TbUnsegmented
{
    public int Id { get; set; }

    public int Status { get; set; }

    [DwDeny(PolicyFeature.Segment)]
    public int Rank { get; set; }
}

/// <summary>Inherits the base type's <c>[DwEntity]</c>, opt-in included.</summary>
public class TbInheritingInstruction : TbInstruction
{
}

/// <summary>Replaces the base type's <c>[DwEntity]</c> with one that does not opt in.</summary>
[DwEntity(DefaultOrder = "Batch desc, Id desc")]
public class TbReplacingInstruction : TbInstruction
{
}

public sealed class TbContext : DbContext
{
    private readonly SqliteConnection _connection;

    public TbContext(SqliteConnection connection) => _connection = connection;

    public DbSet<TbInstruction> Instructions => Set<TbInstruction>();

    public DbSet<TbPlainInstruction> PlainInstructions => Set<TbPlainInstruction>();

    public DbSet<TbUnorderedInstruction> UnorderedInstructions => Set<TbUnorderedInstruction>();

    public DbSet<TbUnsegmented> Unsegmented => Set<TbUnsegmented>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

    // Each row type is its own table: the derived types above exist for the attribute, not the model.
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Ignore<TbInheritingInstruction>();
        model.Ignore<TbReplacingInstruction>();
    }
}

/// <summary>
/// <c>DefaultOrderAsTiebreak</c> (GitHub #13): a caller's orders end with the type's default, so rows tied on
/// the caller's fields keep one order from page to page.
/// </summary>
/// <remarks>
/// Twelve rows in two statuses and three batches. SQLite returns ties in the order they were written,
/// <c>Id</c> ascending, so a tiebreak of <c>Batch desc, Id desc</c> is visible in the rows as well as in the SQL.
/// </remarks>
public sealed class DefaultOrderTiebreakTests : IDisposable
{
    private static readonly (int Id, int Status, int Batch)[] Seed = Enumerable.Range(1, 12)
        .Select(id => (id, Status: id % 3 == 0 ? 2 : 1, Batch: (id * 7) % 3 + 1))
        .ToArray();

    private readonly SqliteConnection _connection;
    private readonly TbContext _db;

    public DefaultOrderTiebreakTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _db = new TbContext(_connection);
        _db.Database.EnsureCreated();

        foreach ((int id, int status, int batch) in Seed)
        {
            _db.Instructions.Add(new TbInstruction { Id = id, Status = status, Batch = batch, Code = $"c{13 - id:00}" });
            _db.PlainInstructions.Add(new TbPlainInstruction { Id = id, Status = status, Batch = batch });
            _db.UnorderedInstructions.Add(new TbUnorderedInstruction { Id = id, Status = status });
            _db.Unsegmented.Add(new TbUnsegmented { Id = id, Status = status, Rank = batch });
        }

        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ---------------------------------------------------------------------------------- helpers

    /// <summary>Status, then the tiebreak: the order every opted-in read below must come back in.</summary>
    private static int[] ByStatusThenDefault() => Seed
        .OrderBy(row => row.Status).ThenByDescending(row => row.Batch).ThenByDescending(row => row.Id)
        .Select(row => row.Id)
        .ToArray();

    /// <summary>
    /// Rows ordered by status alone: every row once, statuses never decreasing. Which way ties fall is the
    /// database's to choose, which is the gap the tiebreak closes, so nothing here asserts it.
    /// </summary>
    private static void AssertByStatusAlone(IEnumerable<int> ids)
    {
        int[] read = ids.ToArray();
        Dictionary<int, int> status = Seed.ToDictionary(row => row.Id, row => row.Status);

        Assert.Equal(Seed.Select(row => row.Id).OrderBy(id => id), read.OrderBy(id => id));
        Assert.True(read.Zip(read.Skip(1), (a, b) => status[a] <= status[b]).All(sorted => sorted));
    }

    private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    private static DwPolicyOptions Options(DwTier tier = DwTier.Convenience, bool dryRun = false, bool everywhere = false) =>
        new() { Tier = tier, DryRun = dryRun, DefaultOrderAsTiebreak = everywhere, Caps = { MinGroupSize = 1 } };

    private static PolicyResolver Resolver(params IDwPolicyProvider[] more) =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() }.Concat(more).ToArray());

    private static FakePolicyProvider Deny(string field, PolicyFeature feature = PolicyFeature.Order) =>
        new FakePolicyProvider().Add(field, feature, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);

    private PolicyQueryable<TbInstruction> Guarded(params IDwPolicyProvider[] more) =>
        _db.Instructions.ApplyPolicy(Caller(), Options(), Resolver(more));

    private static OrderBy By(string field, Direction direction = Direction.Ascending, int sort = 0) =>
        new() { Sort = sort, Field = field, Direction = direction };

    private static List<OrderBy> Orders(params OrderBy[] orders) => orders.ToList();

    private static Filter ByStatus(PageBy? page = null) => new() { Orders = Orders(By("Status")), Page = page };

    private static int[] Ids<TRow>(IEnumerable<TRow> rows) => rows.Select(row => (int)((dynamic)row!).Id).ToArray();

    /// <summary>The columns of the last ORDER BY in a query string, each with its direction.</summary>
    private static string[] OrderedBy(string sql)
    {
        int at = sql.LastIndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);

        return at < 0
            ? Array.Empty<string>()
            : Regex.Matches(sql[at..], "\"\\w+\"\\.\"(\\w+)\"( DESC)?")
                .Select(match => match.Groups[1].Value + (match.Groups[2].Success ? " desc" : string.Empty))
                .ToArray();
    }

    /// <summary>The orders the sanitizer hands the pipeline, as field and direction, in the order it applies them.</summary>
    private static string[] Sanitized(IEnumerable<OrderBy>? orders) =>
        (orders ?? Enumerable.Empty<OrderBy>())
            .OrderBy(order => order.Sort)
            .Select(order => order.Field + (order.Direction == Direction.Descending ? " desc" : string.Empty))
            .ToArray();

    private static PolicySimulation<Filter> Simulate<T>(Filter filter, DwPolicyOptions? options = null, params IDwPolicyProvider[] more)
        where T : class =>
        PolicySimulator.Simulate<T>(filter, Caller(), options ?? Options(), Resolver(more));

    /// <summary>Every row, as a union of the two statuses, so a segment reads all twelve.</summary>
    private static Segment EveryStatus(List<OrderBy>? orders = null, PageBy? page = null) => new()
    {
        ConditionSets =
        {
            new ConditionSet
            {
                Sort = 1,
                ConditionGroup = new ConditionGroup
                {
                    Conditions = { new Condition { Field = "Status", DataType = DataType.Number, Operator = Operator.Equal, Values = { 1 } } }
                }
            },
            new ConditionSet
            {
                Sort = 2,
                Intersection = Intersection.Union,
                ConditionGroup = new ConditionGroup
                {
                    Conditions = { new Condition { Field = "Status", DataType = DataType.Number, Operator = Operator.Equal, Values = { 2 } } }
                }
            }
        },
        Orders = orders ?? new List<OrderBy>(),
        Page = page
    };

    // ------------------------------------------------------------------------------ the gap it closes

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public void Every_row_appears_exactly_once_across_the_pages_and_ties_follow_the_default(int pageSize)
    {
        List<int> read = new();

        for (int page = 1; read.Count < Seed.Length; page++)
        {
            FilterResult<TbInstruction> result = Guarded().ToList(ByStatus(new PageBy { PageNumber = page, PageSize = pageSize }));

            Assert.NotEmpty(result.Data);
            read.AddRange(Ids(result.Data));
        }

        Assert.Equal(ByStatusThenDefault(), read);
    }

    [Fact]
    public void The_tiebreak_follows_the_callers_orders_in_the_sql()
    {
        FilterResult<TbInstruction> result = Guarded().ToList(ByStatus(), getQueryString: true);

        Assert.Equal(new[] { "Status", "Batch desc", "Id desc" }, OrderedBy(result.QueryString!));
        Assert.Equal(ByStatusThenDefault(), Ids(result.Data));
    }

    [Fact]
    public void Without_the_opt_in_a_caller_who_sends_orders_still_gets_exactly_those()
    {
        FilterResult<TbPlainInstruction> result = _db.PlainInstructions
            .ApplyPolicy(Caller(), Options(), Resolver())
            .ToList(ByStatus(), getQueryString: true);

        Assert.Equal(new[] { "Status" }, OrderedBy(result.QueryString!));
        AssertByStatusAlone(Ids(result.Data));
    }

    [Fact]
    public void A_caller_who_sends_no_orders_gets_the_default_as_before()
    {
        FilterResult<TbInstruction> result = Guarded().ToList(new Filter(), getQueryString: true);

        Assert.Equal(new[] { "Batch desc", "Id desc" }, OrderedBy(result.QueryString!));
    }

    // ------------------------------------------------------------------------------ the posture's switch

    [Fact]
    public void The_postures_switch_turns_it_on_for_every_type_with_a_default()
    {
        FilterResult<TbPlainInstruction> plain = _db.PlainInstructions
            .ApplyPolicy(Caller(), Options(everywhere: true), Resolver())
            .ToList(ByStatus(), getQueryString: true);

        Assert.Equal(new[] { "Status", "Batch desc", "Id desc" }, OrderedBy(plain.QueryString!));
        Assert.Equal(ByStatusThenDefault(), Ids(plain.Data));
    }

    [Fact]
    public void The_postures_switch_appends_nothing_to_a_type_without_a_default()
    {
        FilterResult<TbUnorderedInstruction> result = _db.UnorderedInstructions
            .ApplyPolicy(Caller(), Options(everywhere: true), Resolver())
            .ToList(ByStatus(), getQueryString: true);

        Assert.Equal(new[] { "Status" }, OrderedBy(result.QueryString!));
        Assert.DoesNotContain("ORDER BY", _db.UnorderedInstructions
            .ApplyPolicy(Caller(), Options(everywhere: true), Resolver())
            .ToList(new Filter(), getQueryString: true).QueryString!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_switch_is_part_of_the_posture_and_frozen_with_it()
    {
        DwPolicyOptions options = new() { DefaultOrderAsTiebreak = true };

        options.Freeze();

        Assert.True(options.DefaultOrderAsTiebreak);
        Assert.Throws<InvalidOperationException>(() => options.DefaultOrderAsTiebreak = false);
        Assert.False(new DwPolicyOptions().DefaultOrderAsTiebreak);
    }

    [Fact]
    public void The_switch_binds_from_configuration()
    {
        IConfiguration section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DefaultOrderAsTiebreak"] = "true" })
            .Build();

        Assert.True(new DwPolicyOptions().Bind(section).DefaultOrderAsTiebreak);
    }

    // ------------------------------------------------------------------------------ what is appended

    [Fact]
    public void A_field_the_caller_already_named_keeps_the_callers_place_and_direction_and_is_not_added_again()
    {
        Assert.Equal(
            new[] { "Id", "Batch desc" },
            Sanitized(Simulate<TbInstruction>(new Filter { Orders = Orders(By("Id")) }).Clause!.Orders));
        Assert.Equal(
            new[] { "Batch", "Status desc", "Id desc" },
            Sanitized(Simulate<TbInstruction>(new Filter { Orders = Orders(By("Batch"), By("Status", Direction.Descending, 1)) }).Clause!.Orders));

        // Naming every default field leaves nothing to append, whatever the case it was written in.
        FilterResult<TbInstruction> whole = Guarded().ToList(
            new Filter { Orders = Orders(By("id"), By("BATCH", Direction.Ascending, 1)) }, getQueryString: true);

        Assert.Equal(new[] { "Id", "Batch" }, OrderedBy(whole.QueryString!));
    }

    [Fact]
    public void A_default_field_the_caller_may_not_order_by_is_left_out_and_recorded()
    {
        PolicyQueryable<TbInstruction> guarded = Guarded(Deny("Batch"));

        FilterResult<TbInstruction> result = guarded.ToList(ByStatus(), getQueryString: true);

        Assert.Equal(new[] { "Status", "Id desc" }, OrderedBy(result.QueryString!));
        Assert.Contains(guarded.LastTrace!.Decisions, decision =>
            decision.FieldPath == "Batch"
            && decision.Action == PolicyAction.Dropped
            && decision.Reason?.Contains("left out of the default order") == true);
    }

    [Fact]
    public void A_caller_whose_orders_were_all_dropped_gets_the_whole_default()
    {
        FilterResult<TbInstruction> result = Guarded(Deny("Status")).ToList(ByStatus(), getQueryString: true);

        Assert.Equal(new[] { "Batch desc", "Id desc" }, OrderedBy(result.QueryString!));

        // Without the opt-in the same caller gets no default in their place, as before.
        FilterResult<TbPlainInstruction> plain = _db.PlainInstructions
            .ApplyPolicy(Caller(), Options(), Resolver(Deny("Status")))
            .ToList(ByStatus(), getQueryString: true);

        Assert.Empty(OrderedBy(plain.QueryString!));
    }

    [Fact]
    public void The_strict_tier_still_refuses_a_caller_order_it_denies()
    {
        PolicyException refusal = Assert.Throws<PolicyException>(() => _db.Instructions
            .ApplyPolicy(Caller(), Options(DwTier.Strict), Resolver(Deny("Status")))
            .ToList(ByStatus()));

        Assert.Equal(PolicyErrorCode.FieldDeniedForOrder, refusal.ErrorCode);
    }

    [Fact]
    public void A_dry_run_keeps_every_field_and_records_what_enforcement_would_leave_out()
    {
        PolicyQueryable<TbInstruction> guarded = _db.Instructions.ApplyPolicy(
            Caller(), Options(dryRun: true), Resolver(Deny("Status"), Deny("Batch")));

        FilterResult<TbInstruction> result = guarded.ToList(ByStatus(), getQueryString: false);

        Assert.Equal(ByStatusThenDefault(), Ids(result.Data));
        Assert.Contains(guarded.LastTrace!.Decisions, decision =>
            decision.FieldPath == "Batch" && decision.Reason?.Contains("left out of the default order") == true);
    }

    [Fact]
    public void The_appended_fields_count_toward_no_cap()
    {
        // MaxOrderFields bounds what the caller sends; the library's own additions are bounded by the declaration.
        DwPolicyOptions options = Options();
        options.Caps.MaxOrderFields = 1;

        FilterResult<TbInstruction> result = _db.Instructions
            .ApplyPolicy(Caller(), options, Resolver())
            .ToList(ByStatus(), getQueryString: true);

        Assert.Equal(new[] { "Status", "Batch desc", "Id desc" }, OrderedBy(result.QueryString!));
    }

    [Fact]
    public void The_tiebreak_follows_the_callers_last_sort_whatever_the_numbers_and_the_callers_filter_is_untouched()
    {
        Filter filter = new()
        {
            Orders = Orders(
                By("Status", Direction.Ascending, int.MaxValue),
                By("Code", Direction.Descending, -5),
                By("Status", Direction.Descending, int.MaxValue))
        };

        Filter sanitized = Simulate<TbInstruction>(filter).Clause!;

        // Stable, as the pipeline sorts: Code first, the two at int.MaxValue in the order written, then the default.
        Assert.Equal(new[] { "Code desc", "Status", "Status desc", "Batch desc", "Id desc" }, Sanitized(sanitized.Orders));
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, sanitized.Orders!.OrderBy(order => order.Sort).Select(order => order.Sort));

        Assert.Equal(3, filter.Orders!.Count);
        Assert.Equal(new[] { int.MaxValue, -5, int.MaxValue }, filter.Orders.Select(order => order.Sort));
    }

    [Fact]
    public void An_appended_audited_field_is_recorded_once_and_a_named_one_is_not_recorded_twice()
    {
        IQueryable<TbAudited> rows = new TbAudited[]
        {
            new() { Id = 1, Status = 1, Rank = 1 }, new() { Id = 2, Status = 1, Rank = 3 }, new() { Id = 3, Status = 1, Rank = 2 },
        }.AsQueryable();

        DwPolicyContext appended = Caller();
        Assert.Equal(
            new[] { 2, 3, 1 },
            rows.ApplyPolicy(appended, Options(), Resolver()).ToList(ByStatus()).Data.Select(row => row.Id));
        DwAuditEvent used = Assert.Single(appended.PendingAuditEvents);
        Assert.Equal(("Rank", PolicyFeature.Order), (used.FieldPath, used.Feature));

        DwPolicyContext named = Caller();
        Assert.Equal(
            new[] { 1, 3, 2 },
            rows.ApplyPolicy(named, Options(), Resolver()).ToList(new Filter { Orders = Orders(By("Rank")) }).Data.Select(row => row.Id));
        Assert.Equal("Rank", Assert.Single(named.PendingAuditEvents).FieldPath);
    }

    // ------------------------------------------------------------------------------ every caller order

    [Fact]
    public async Task Every_guarded_filter_terminal_appends_it()
    {
        int[] expected = ByStatusThenDefault();
        Filter idsByStatus = new() { Orders = Orders(By("Status")), Selects = new List<string> { "Id" } };

        Assert.Equal(expected, Ids((await Guarded().ToListAsync(ByStatus())).Data));
        Assert.Equal(expected, Ids(Guarded().ToListDynamic(idsByStatus).Data));
        Assert.Equal(expected, Ids((await Guarded().ToListAsyncDynamic(idsByStatus)).Data));
        Assert.Equal(expected, Ids(Guarded().ToList(new Filter { Orders = Orders(By("Status")), Selects = new List<string> { "Id", "Status" } }).Data));
    }

    [Fact]
    public void The_composable_filter_and_filter_dynamic_append_it()
    {
        int[] expected = ByStatusThenDefault();

        Assert.Equal(expected, Ids(Guarded().Filter(ByStatus()).AsUnguardedQueryable().ToList()));
        Assert.Equal(expected, Ids(Guarded().FilterDynamic(new Filter { Orders = Orders(By("Status")), Selects = new List<string> { "Id" } }).ToDynamicList()));
    }

    [Fact]
    public void A_composed_order_appends_it_so_a_composed_page_is_total_too()
    {
        int[] expected = ByStatusThenDefault();

        Assert.Equal(expected, Ids(Guarded().Order(By("Status")).AsUnguardedQueryable().ToList()));
        Assert.Equal(
            expected.Take(5),
            Ids(Guarded().Order(By("Status")).Page(new PageBy { PageNumber = 1, PageSize = 5 }).AsUnguardedQueryable().ToList()));
        Assert.Equal(
            expected.Skip(5).Take(5),
            Ids(Guarded().Order(new List<OrderBy> { By("Status") }).Page(new PageBy { PageNumber = 2, PageSize = 5 }).AsUnguardedQueryable().ToList()));

        // Without the opt-in a composed order is the whole order, as before.
        Assert.Equal(
            new[] { "Status" },
            OrderedBy(_db.PlainInstructions.ApplyPolicy(Caller(), Options(), Resolver()).Order(By("Status")).AsUnguardedQueryable().ToQueryString()));
    }

    [Fact]
    public void A_composed_order_whose_fields_were_all_dropped_takes_the_whole_default()
    {
        Assert.Equal(
            Seed.OrderByDescending(row => row.Batch).ThenByDescending(row => row.Id).Select(row => row.Id),
            Ids(Guarded(Deny("Status")).Order(By("Status")).AsUnguardedQueryable().ToList()));
    }

    [Fact]
    public async Task A_guarded_segment_appends_it()
    {
        SegmentResult<TbInstruction> result = await Guarded().ToListAsync(EveryStatus(Orders(By("Status"))));

        Assert.Equal(ByStatusThenDefault(), Ids(result.Data!));

        SegmentResult<TbInstruction> paged = await Guarded().ToListAsync(
            EveryStatus(Orders(By("Status")), new PageBy { PageNumber = 2, PageSize = 5 }));

        Assert.Equal(ByStatusThenDefault().Skip(5).Take(5), Ids(paged.Data!));
    }

    [Fact]
    public async Task A_segment_leaves_out_a_default_field_denied_for_segments()
    {
        SegmentResult<TbUnsegmented> result = await _db.Unsegmented
            .ApplyPolicy(Caller(), Options(), Resolver())
            .ToListAsync(EveryStatus(Orders(By("Status"))));

        Assert.Equal(
            Seed.OrderBy(row => row.Status).ThenByDescending(row => row.Id).Select(row => row.Id),
            Ids(result.Data!));

        // A filter over the same type keeps it: only a segment leaves it out.
        Assert.Equal(
            new[] { "Status", "Rank desc", "Id desc" },
            Sanitized(Simulate<TbUnsegmented>(ByStatus()).Clause!.Orders));
    }

    [Fact]
    public void The_simulator_shows_it_and_a_summary_never_takes_it()
    {
        Assert.Equal(new[] { "Status", "Batch desc", "Id desc" }, Sanitized(Simulate<TbInstruction>(ByStatus()).Clause!.Orders));

        PolicySimulation<Segment> segment = PolicySimulator.Simulate<TbInstruction>(
            EveryStatus(Orders(By("Status"))), Caller(), Options(), Resolver());

        Assert.Equal(new[] { "Status", "Batch desc", "Id desc" }, Sanitized(segment.Clause!.Orders));

        PolicySimulation<Summary> summary = PolicySimulator.Simulate<TbInstruction>(
            new Summary
            {
                GroupBy = new GroupBy { Fields = new List<string> { "Status" } },
                Orders = Orders(By("Status"))
            },
            Caller(), Options(), Resolver());

        Assert.True(summary.WouldRun);
        Assert.Equal(new[] { "Status" }, Sanitized(summary.Clause!.Orders));
    }

    // ------------------------------------------------------------------------------ rows that cannot take it

    [Fact]
    public void A_projection_that_leaves_a_default_field_out_keeps_the_callers_orders_as_they_are()
    {
        FilterResult<TbInstruction> result = _db.Instructions
            .Select(row => new TbInstruction { Id = row.Id, Status = row.Status })
            .ApplyPolicy(Caller(), Options(), Resolver())
            .ToList(ByStatus(), getQueryString: true);

        Assert.Equal(new[] { "Status" }, OrderedBy(result.QueryString!));
        AssertByStatusAlone(Ids(result.Data));
    }

    [Fact]
    public void A_projection_that_assigns_every_default_field_takes_it()
    {
        FilterResult<TbInstruction> result = _db.Instructions
            .Select(row => new TbInstruction { Id = row.Id, Status = row.Status, Batch = row.Batch })
            .ApplyPolicy(Caller(), Options(), Resolver())
            .ToList(ByStatus(), getQueryString: true);

        Assert.Equal(new[] { "Status", "Batch desc", "Id desc" }, OrderedBy(result.QueryString!));
        Assert.Equal(ByStatusThenDefault(), Ids(result.Data));
    }

    [Fact]
    public void A_select_composed_on_the_handle_keeps_the_callers_orders_as_they_are()
    {
        PolicyQueryable<TbInstruction> projected = Guarded().Select(new List<string> { "Id", "Status" });

        FilterResult<TbInstruction> filtered = projected.ToList(ByStatus(), getQueryString: true);

        Assert.Equal(new[] { "Status" }, OrderedBy(filtered.QueryString!));
        AssertByStatusAlone(Ids(filtered.Data));
        Assert.Equal(new[] { "Status" }, OrderedBy(projected.Order(By("Status")).AsUnguardedQueryable().ToQueryString()));

        // Even one keeping every field the default names, as the default stays off it: the rows a projection
        // composed on the handle makes are the caller's to order.
        List<string> everyDefaultField = new() { "Id", "Status", "Batch" };

        Assert.Equal(
            new[] { "Status" },
            OrderedBy(Guarded().Select(everyDefaultField).ToList(ByStatus(), getQueryString: true).QueryString!));
        Assert.Equal(
            new[] { "Status" },
            OrderedBy(Guarded().Select(everyDefaultField).Order(By("Status")).AsUnguardedQueryable().ToQueryString()));
        Assert.Equal(
            new[] { "Status" },
            OrderedBy(Guarded().Filter(new Filter { Selects = everyDefaultField }).ToList(ByStatus(), getQueryString: true).QueryString!));
    }

    [Fact]
    public void A_source_ordered_before_ApplyPolicy_still_takes_it_after_the_callers_orders()
    {
        FilterResult<TbInstruction> result = _db.Instructions
            .OrderBy(row => row.Code)
            .ApplyPolicy(Caller(), Options(), Resolver())
            .ToList(ByStatus(), getQueryString: true);

        Assert.Equal(new[] { "Status", "Batch desc", "Id desc" }, OrderedBy(result.QueryString!));
        Assert.Equal(ByStatusThenDefault(), Ids(result.Data));

        // With no orders of its own the caller keeps the source's order, as before.
        Assert.Equal(
            Seed.OrderByDescending(row => row.Id).Select(row => row.Id),
            Ids(_db.Instructions.OrderBy(row => row.Code).ApplyPolicy(Caller(), Options(), Resolver()).ToList(new Filter()).Data));
    }

    // ------------------------------------------------------------------------------ declaration

    [Fact]
    public void A_derived_type_inherits_the_opt_in_unless_its_own_attribute_replaces_it()
    {
        Assert.Equal(new[] { "Status", "Batch desc", "Id desc" }, Sanitized(Simulate<TbInheritingInstruction>(ByStatus()).Clause!.Orders));
        Assert.Equal(new[] { "Status" }, Sanitized(Simulate<TbReplacingInstruction>(ByStatus()).Clause!.Orders));
    }

    [Fact]
    public void The_startup_scan_warns_when_the_opt_in_has_nothing_to_append()
    {
        PolicyModelReport report = PolicyModelValidator.Inspect(
            new[] { typeof(TbEmptyTiebreak), typeof(TbUnusableTiebreak), typeof(TbInstruction), typeof(TbUnorderedInstruction) },
            Options(everywhere: true));

        Assert.Contains(report.Warnings, warning =>
            warning.StartsWith("TbEmptyTiebreak: DefaultOrderAsTiebreak is set", StringComparison.Ordinal));
        Assert.Contains(report.Warnings, warning =>
            warning.StartsWith("TbUnusableTiebreak: DefaultOrderAsTiebreak is set", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Warnings, warning => warning.StartsWith("TbInstruction:", StringComparison.Ordinal));

        // The posture's switch is not a declaration: a type without a default is simply unaffected by it.
        Assert.DoesNotContain(report.Warnings, warning => warning.StartsWith("TbUnorderedInstruction:", StringComparison.Ordinal));
    }
}
