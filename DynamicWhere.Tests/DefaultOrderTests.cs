using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Policies.Validation;
using DynamicWhere.ex.Source;
using DynamicWhere.Tests.Policies;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

namespace DynamicWhere.Tests;

/// <summary>A ticket queue read most urgent first, oldest first within an urgency.</summary>
[DwEntity(DefaultOrder = "Priority desc, Id")]
public class Ticket
{
    public int Id { get; set; }

    public int Priority { get; set; }

    public string Title { get; set; } = string.Empty;
}

/// <summary>The same default written carelessly: an unknown field, a bad direction, odd case, a trailing comma.</summary>
[DwEntity(DefaultOrder = "Missing desc, priority DESC, Id sideways, id,")]
public class SloppyTicket
{
    public int Id { get; set; }

    public int Priority { get; set; }
}

/// <summary>A default order leading with a field no caller may order by.</summary>
[DwEntity(DefaultOrder = "Secret desc, Id")]
public class SecretTicket
{
    public int Id { get; set; }

    [DwNoOrder]
    public int Secret { get; set; }
}

/// <summary>A type that declares no default, and so is never ordered by one.</summary>
public class PlainTicket
{
    public int Id { get; set; }

    public int Priority { get; set; }
}

public sealed class TicketContext : DbContext
{
    private readonly SqliteConnection _connection;

    public TicketContext(SqliteConnection connection) => _connection = connection;

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<SloppyTicket> SloppyTickets => Set<SloppyTicket>();

    public DbSet<SecretTicket> SecretTickets => Set<SecretTicket>();

    public DbSet<PlainTicket> PlainTickets => Set<PlainTicket>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
}

/// <summary>
/// <c>[DwEntity(DefaultOrder = ...)]</c>: the order a query takes when its caller sends none.
/// </summary>
/// <remarks>
/// Five tickets with priorities 1, 3, 2, 3, 1 and titles a to e, so the default <c>Priority desc, Id</c>
/// reads 2, 4, 3, 1, 5 — neither insertion order nor either field alone.
/// </remarks>
public sealed class DefaultOrderTests : IDisposable
{
    private static readonly int[] ByDefault = { 2, 4, 3, 1, 5 };

    private readonly SqliteConnection _connection;
    private readonly TicketContext _db;

    public DefaultOrderTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _db = new TicketContext(_connection);
        _db.Database.EnsureCreated();

        int[] priorities = { 1, 3, 2, 3, 1 };

        for (int i = 0; i < priorities.Length; i++)
        {
            _db.Tickets.Add(new Ticket { Id = i + 1, Priority = priorities[i], Title = ((char)('a' + i)).ToString() });
            _db.SloppyTickets.Add(new SloppyTicket { Id = i + 1, Priority = priorities[i] });
            _db.SecretTickets.Add(new SecretTicket { Id = i + 1, Secret = priorities[i] });
            _db.PlainTickets.Add(new PlainTicket { Id = 6 - (i + 1), Priority = priorities[i] });
        }

        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static int[] Ids(IEnumerable<Ticket> rows) => rows.Select(row => row.Id).ToArray();

    private static OrderBy By(string field, Direction direction = Direction.Ascending) =>
        new() { Sort = 0, Field = field, Direction = direction };

    // ------------------------------------------------------------------------------ unguarded

    [Fact]
    public void An_unordered_request_takes_the_declared_default()
    {
        FilterResult<Ticket> result = _db.Tickets.ToList(new Filter());

        Assert.Equal(ByDefault, Ids(result.Data));
    }

    [Fact]
    public void A_caller_who_sends_orders_gets_exactly_those()
    {
        // The default is not appended as a tiebreak: the caller's orders are the whole order.
        Filter filter = new() { Orders = new List<OrderBy> { By("Title", Direction.Descending) } };

        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, Ids(_db.Tickets.ToList(filter).Data));
    }

    [Fact]
    public void Pages_follow_the_default()
    {
        Assert.Equal(new[] { 2, 4 }, Ids(_db.Tickets.ToList(new Filter { Page = new PageBy { PageNumber = 1, PageSize = 2 } }).Data));
        Assert.Equal(new[] { 3, 1 }, Ids(_db.Tickets.ToList(new Filter { Page = new PageBy { PageNumber = 2, PageSize = 2 } }).Data));
    }

    [Fact]
    public async Task Every_filter_terminal_takes_the_default()
    {
        Assert.Equal(ByDefault, Ids((await _db.Tickets.ToListAsync(new Filter())).Data));
        Assert.Equal(ByDefault, _db.Tickets.ToListDynamic(new Filter { Selects = new List<string> { "Id" } }).Data.Select(row => (int)row.Id));
        Assert.Equal(
            ByDefault,
            (await _db.Tickets.ToListAsyncDynamic(new Filter { Selects = new List<string> { "Id" } })).Data.Select(row => (int)row.Id));
        Assert.Equal(ByDefault, Ids(_db.Tickets.AsEnumerable().ToList(new Filter()).Data));
    }

    [Fact]
    public void The_composable_filter_and_page_take_the_default()
    {
        Assert.Equal(ByDefault, Ids(_db.Tickets.Filter(new Filter()).ToList()));
        Assert.Equal(new[] { 2, 4 }, Ids(_db.Tickets.Page(new PageBy { PageNumber = 1, PageSize = 2 }).ToList()));
        Assert.Equal(
            ByDefault,
            _db.Tickets.FilterDynamic(new Filter { Selects = new List<string> { "Id" } }).ToDynamicList().Select(row => (int)row.Id));
    }

    [Fact]
    public void A_query_the_caller_already_ordered_keeps_that_order()
    {
        // Ordered before it reached the library, and ordered by a composed clause before a page.
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Ids(_db.Tickets.OrderBy(t => t.Title).ToList(new Filter()).Data));
        Assert.Equal(
            new[] { 1, 2 },
            Ids(_db.Tickets.Order(By("Title")).Page(new PageBy { PageNumber = 1, PageSize = 2 }).ToList()));
        Assert.Equal(
            new[] { 5, 4 },
            Ids(_db.Tickets.Order(By("Id", Direction.Descending)).Where(t => t.Priority > 0).Page(new PageBy { PageNumber = 1, PageSize = 2 }).ToList()));
    }

    [Fact]
    public async Task A_segment_takes_the_default()
    {
        Segment segment = new()
        {
            ConditionSets =
            {
                new ConditionSet
                {
                    Sort = 1,
                    ConditionGroup = new ConditionGroup
                    {
                        Conditions = { new Condition { Field = "Priority", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = { 2 } } }
                    }
                },
                new ConditionSet
                {
                    Sort = 2,
                    Intersection = Intersection.Union,
                    ConditionGroup = new ConditionGroup
                    {
                        Conditions = { new Condition { Field = "Id", DataType = DataType.Number, Operator = Operator.Equal, Values = { 5 } } }
                    }
                }
            }
        };

        SegmentResult<Ticket> result = await _db.Tickets.ToListAsync(segment);

        Assert.Equal(new[] { 2, 4, 3, 5 }, Ids(result.Data!));
    }

    [Fact]
    public void A_type_that_declares_nothing_is_never_ordered_by_the_library()
    {
        string sql = _db.PlainTickets.ToList(new Filter { Page = new PageBy { PageNumber = 1, PageSize = 2 } }, getQueryString: true).QueryString!;

        Assert.DoesNotContain("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.False(DefaultOrder.IsOrdered(_db.PlainTickets.Filter(new Filter()).Expression));
        Assert.False(DefaultOrder.IsOrdered(_db.PlainTickets.Page(new PageBy { PageNumber = 1, PageSize = 2 }).Expression));
    }

    [Fact]
    public void Unknown_fields_and_unreadable_entries_are_skipped_and_reported_at_startup()
    {
        // "Missing desc" names nothing and "Id sideways" is not a direction; "priority DESC" and "id"
        // still read, in any letter case, and the trailing comma says nothing at all.
        Assert.Equal(ByDefault, _db.SloppyTickets.ToList(new Filter()).Data.Select(row => row.Id));

        PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(SloppyTicket) });

        Assert.Equal(new[] { "SloppyTicket: DefaultOrder entry 'Id sideways' is not a field optionally followed by asc or desc, so queries skip it." }, report.Errors);
        Assert.Equal(new[] { "SloppyTicket: DefaultOrder names 'Missing', which SloppyTicket does not have, so queries skip it." }, report.Warnings);
        Assert.Empty(PolicyModelValidator.Inspect(new[] { typeof(Ticket), typeof(PlainTicket) }).Errors);
    }

    // ------------------------------------------------------------------------------ guarded

    private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    private static DwPolicyOptions Options(DwTier tier = DwTier.Convenience, bool dryRun = false) =>
        new() { Tier = tier, DryRun = dryRun, Caps = { MinGroupSize = 1 } };

    private static PolicyResolver Resolver(params IDwPolicyProvider[] more) =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() }.Concat(more).ToArray());

    private static FakePolicyProvider NoOrderingByPriority() =>
        new FakePolicyProvider().Add("Priority", PolicyFeature.Order, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);

    [Theory]
    [InlineData(DwTier.Convenience)]
    [InlineData(DwTier.Strict)]
    public void A_guarded_query_takes_the_default(DwTier tier)
    {
        FilterResult<Ticket> result = _db.Tickets.ApplyPolicy(Caller(), Options(tier), Resolver()).ToList(new Filter());

        Assert.Equal(ByDefault, Ids(result.Data));
    }

    [Theory]
    [InlineData(DwTier.Convenience)]
    [InlineData(DwTier.Strict)]
    public void A_default_field_the_caller_may_not_order_by_is_left_out_not_refused(DwTier tier)
    {
        PolicyQueryable<Ticket> guarded = _db.Tickets.ApplyPolicy(Caller(), Options(tier), Resolver(NoOrderingByPriority()));

        FilterResult<Ticket> result = guarded.ToList(new Filter());

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Ids(result.Data));
        Assert.Contains(
            guarded.LastTrace!.Decisions,
            decision => decision.FieldPath == "Priority"
                        && decision.Action == PolicyAction.Dropped
                        && decision.Reason!.StartsWith("left out of the default order", StringComparison.Ordinal));
    }

    [Fact]
    public void A_field_the_attributes_deny_is_left_out_under_a_policy_and_fails_the_startup_scan()
    {
        Assert.Equal(
            new[] { 1, 2, 3, 4, 5 },
            _db.SecretTickets.ApplyPolicy(Caller(), Options(), Resolver()).ToList(new Filter()).Data.Select(row => row.Id));

        // Unguarded, no policy applies, so the declared order is the order.
        Assert.Equal(new[] { 2, 4, 3, 1, 5 }, _db.SecretTickets.ToList(new Filter()).Data.Select(row => row.Id));

        Assert.Contains(
            "SecretTicket: DefaultOrder names 'Secret', which its attributes deny for ordering, so every guarded query leaves it out.",
            PolicyModelValidator.Inspect(new[] { typeof(SecretTicket) }).Errors);
    }

    [Fact]
    public void A_dry_run_keeps_the_field_and_records_what_it_would_have_left_out()
    {
        PolicyQueryable<Ticket> guarded = _db.Tickets.ApplyPolicy(
            Caller(), Options(dryRun: true), Resolver(NoOrderingByPriority()));

        Assert.Equal(ByDefault, Ids(guarded.ToList(new Filter()).Data));
        Assert.Contains(guarded.LastTrace!.Decisions, decision => decision.FieldPath == "Priority" && decision.Action == PolicyAction.Dropped);
    }

    [Fact]
    public void A_caller_whose_orders_were_all_dropped_gets_no_default_in_their_place()
    {
        PolicyQueryable<Ticket> guarded = _db.Tickets.ApplyPolicy(Caller(), Options(), Resolver(NoOrderingByPriority()));

        FilterResult<Ticket> result = guarded.ToList(
            new Filter { Orders = new List<OrderBy> { By("Priority") } }, getQueryString: true);

        Assert.DoesNotContain("ORDER BY", result.QueryString!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(guarded.LastTrace!.Decisions, decision => decision.Reason?.Contains("default order") == true);
    }

    [Fact]
    public void The_guarded_page_takes_the_default_only_when_nothing_ordered_the_query()
    {
        PolicyQueryable<Ticket> guarded = _db.Tickets.ApplyPolicy(Caller(), Options(), Resolver());

        Assert.Equal(new[] { 2, 4 }, Ids(guarded.Page(new PageBy { PageNumber = 1, PageSize = 2 }).AsUnguardedQueryable().ToList()));
        Assert.Equal(
            new[] { 1, 2 },
            Ids(guarded.Order(By("Title")).Page(new PageBy { PageNumber = 1, PageSize = 2 }).AsUnguardedQueryable().ToList()));
        Assert.Equal(
            new[] { 1, 2 },
            Ids(_db.Tickets.ApplyPolicy(Caller(), Options(), Resolver(NoOrderingByPriority()))
                .Page(new PageBy { PageNumber = 1, PageSize = 2 }).AsUnguardedQueryable().ToList()));
    }

    [Fact]
    public async Task A_guarded_segment_takes_the_default_less_what_the_caller_may_not_order_by()
    {
        Segment segment = new()
        {
            ConditionSets =
            {
                new ConditionSet
                {
                    Sort = 1,
                    ConditionGroup = new ConditionGroup
                    {
                        Conditions = { new Condition { Field = "Priority", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = { 1 } } }
                    }
                }
            }
        };

        SegmentResult<Ticket> allowed = await _db.Tickets.ApplyPolicy(Caller(), Options(), Resolver()).ToListAsync(segment);
        SegmentResult<Ticket> denied = await _db.Tickets.ApplyPolicy(Caller(), Options(), Resolver(NoOrderingByPriority())).ToListAsync(segment);

        Assert.Equal(ByDefault, Ids(allowed.Data!));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Ids(denied.Data!));
    }
}
