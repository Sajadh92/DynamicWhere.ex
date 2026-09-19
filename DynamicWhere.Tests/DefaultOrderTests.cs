using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Audit;
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

/// <summary>A default leading with a collection of entities, which holds no single value to sort by.</summary>
[DwEntity(DefaultOrder = "Watchers desc, Priority desc, Id")]
public class WatchedTicket
{
    public int Id { get; set; }

    public int Priority { get; set; }

    public List<TicketWatcher> Watchers { get; set; } = new();
}

public class TicketWatcher
{
    public int Id { get; set; }

    public int WatchedTicketId { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>A default reaching through a collection to a value, which the core sorts by its smallest or largest.</summary>
[DwEntity(DefaultOrder = "Watchers.Name desc, Id")]
public class ReachingTicket
{
    public int Id { get; set; }

    public List<TicketWatcher> Watchers { get; set; } = new();
}

/// <summary>A default whose leading field only a rule can open for ordering.</summary>
[DwEntity(DefaultOrder = "Rank desc, Id")]
public class GrantedTicket
{
    public int Id { get; set; }

    [DwNoOrder(Overridable = true)]
    public int Rank { get; set; }
}

/// <summary>A default whose leading field may not take part in a segment.</summary>
[DwEntity(DefaultOrder = "Rank desc, Id")]
public class UnsegmentedTicket
{
    public int Id { get; set; }

    [DwDeny(PolicyFeature.Segment)]
    public int Rank { get; set; }
}

/// <summary>A default whose leading field is audited whenever a query orders by it.</summary>
[DwEntity(DefaultOrder = "Rank desc, Id")]
public class AuditedTicket
{
    public int Id { get; set; }

    [DwAudit(PolicyFeature.Order)]
    public int Rank { get; set; }
}

/// <summary>A default reaching through a reference, for the projections that do and do not assign it.</summary>
[DwEntity(DefaultOrder = "Owner.Name desc, Id")]
public class OwnedTicket
{
    public int Id { get; set; }

    public TicketOwner? Owner { get; set; }
}

public class TicketOwner
{
    public string Name { get; set; } = string.Empty;
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

    public DbSet<WatchedTicket> WatchedTickets => Set<WatchedTicket>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
}

/// <summary>
/// <c>[DwEntity(DefaultOrder = ...)]</c>: the order a guarded query takes when its caller sends none.
/// </summary>
/// <remarks>
/// Five tickets with priorities 1, 3, 2, 3, 1 and titles a to e, so the default <c>Priority desc, Id</c>
/// reads 2, 4, 3, 1, 5 — neither the order they were written in nor either field alone.
/// </remarks>
public sealed class DefaultOrderTests : IDisposable
{
    private static readonly int[] ByDefault = { 2, 4, 3, 1, 5 };

    /// <summary>The order the rows were written in, which SQLite returns when nothing orders them.</summary>
    private static readonly int[] AsWritten = { 1, 2, 3, 4, 5 };

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
            _db.WatchedTickets.Add(new WatchedTicket { Id = i + 1, Priority = priorities[i] });
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

    private static Filter IdsOnly() => new() { Selects = new List<string> { "Id" } };

    private static PageBy FirstTwo() => new() { PageNumber = 1, PageSize = 2 };

    /// <summary>The urgent tickets, 2, 3 and 4, together with ticket 5.</summary>
    private static Segment UrgentOrFive() => new()
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

    // ------------------------------------------------------------------------------ unguarded

    [Fact]
    public async Task No_unguarded_terminal_reads_the_declaration()
    {
        // The declaration belongs to the policy layer: a plain query is ordered only as its caller asks.
        FilterResult<Ticket> result = _db.Tickets.ToList(
            new Filter { Page = new PageBy { PageNumber = 1, PageSize = 5 } }, getQueryString: true);

        Assert.DoesNotContain("ORDER BY", result.QueryString!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AsWritten, Ids(result.Data));
        Assert.Equal(AsWritten, Ids((await _db.Tickets.ToListAsync(new Filter())).Data));
        Assert.Equal(AsWritten, _db.Tickets.ToListDynamic(IdsOnly()).Data.Select(row => (int)row.Id));
        Assert.Equal(AsWritten, (await _db.Tickets.ToListAsyncDynamic(IdsOnly())).Data.Select(row => (int)row.Id));
        Assert.Equal(AsWritten, Ids(_db.Tickets.AsEnumerable().ToList(new Filter()).Data));
        Assert.Equal(new[] { 2, 3, 4, 5 }, Ids((await _db.Tickets.ToListAsync(UrgentOrFive())).Data!));
    }

    [Fact]
    public void No_unguarded_composable_reads_the_declaration()
    {
        Assert.False(DefaultOrder.IsOrdered(_db.Tickets.Filter(new Filter()).Expression));
        Assert.False(DefaultOrder.IsOrdered(_db.Tickets.FilterDynamic(IdsOnly()).Expression));
        Assert.False(DefaultOrder.IsOrdered(_db.Tickets.Page(FirstTwo()).Expression));
        Assert.Equal(new[] { 1, 2 }, Ids(_db.Tickets.Page(FirstTwo()).ToList()));
    }

    // ------------------------------------------------------------------------------ guarded

    private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    private static DwPolicyOptions Options(DwTier tier = DwTier.Convenience, bool dryRun = false) =>
        new() { Tier = tier, DryRun = dryRun, Caps = { MinGroupSize = 1 } };

    private static PolicyResolver Resolver(params IDwPolicyProvider[] more) =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() }.Concat(more).ToArray());

    private static FakePolicyProvider NoOrderingByPriority() =>
        new FakePolicyProvider().Add("Priority", PolicyFeature.Order, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);

    private PolicyQueryable<Ticket> Guarded(params IDwPolicyProvider[] more) =>
        _db.Tickets.ApplyPolicy(Caller(), Options(), Resolver(more));

    /// <summary>What follows the last ORDER BY in a query string, or nothing when it has none.</summary>
    private static string OrderByClause(string sql)
    {
        int at = sql.LastIndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase);

        return at < 0 ? string.Empty : sql[at..];
    }

    [Theory]
    [InlineData(DwTier.Convenience)]
    [InlineData(DwTier.Strict)]
    public void A_guarded_query_takes_the_default(DwTier tier)
    {
        FilterResult<Ticket> result = _db.Tickets.ApplyPolicy(Caller(), Options(tier), Resolver()).ToList(new Filter());

        Assert.Equal(ByDefault, Ids(result.Data));
    }

    [Fact]
    public void A_caller_who_sends_orders_gets_exactly_those()
    {
        // The default is not appended as a tiebreak: the caller's orders are the whole order.
        FilterResult<Ticket> result = Guarded().ToList(
            new Filter { Orders = new List<OrderBy> { By("Title", Direction.Descending) } }, getQueryString: true);

        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, Ids(result.Data));
        Assert.DoesNotContain("Priority", OrderByClause(result.QueryString!), StringComparison.Ordinal);
    }

    [Fact]
    public void Pages_follow_the_default()
    {
        Assert.Equal(new[] { 2, 4 }, Ids(Guarded().ToList(new Filter { Page = FirstTwo() }).Data));
        Assert.Equal(new[] { 3, 1 }, Ids(Guarded().ToList(new Filter { Page = new PageBy { PageNumber = 2, PageSize = 2 } }).Data));
    }

    [Fact]
    public async Task Every_guarded_filter_terminal_takes_the_default()
    {
        Assert.Equal(ByDefault, Ids((await Guarded().ToListAsync(new Filter())).Data));
        Assert.Equal(ByDefault, Guarded().ToListDynamic(IdsOnly()).Data.Select(row => (int)row.Id));
        Assert.Equal(ByDefault, (await Guarded().ToListAsyncDynamic(IdsOnly())).Data.Select(row => (int)row.Id));
    }

    [Fact]
    public void The_guarded_composable_filter_takes_the_default()
    {
        Assert.Equal(ByDefault, Ids(Guarded().Filter(new Filter()).AsUnguardedQueryable().ToList()));
        Assert.Equal(ByDefault, Guarded().FilterDynamic(IdsOnly()).ToDynamicList().Select(row => (int)row.Id));
    }

    [Fact]
    public void A_query_the_caller_already_ordered_keeps_that_order()
    {
        // Ordered before it was guarded, and ordered by a composed clause before a filter or a page.
        Assert.Equal(
            new[] { 5, 4, 3, 2, 1 },
            Ids(_db.Tickets.OrderByDescending(t => t.Title).ApplyPolicy(Caller(), Options(), Resolver()).ToList(new Filter()).Data));
        Assert.Equal(
            new[] { 5, 4, 3, 2, 1 },
            Ids(Guarded().Order(By("Title", Direction.Descending)).ToList(new Filter()).Data));
        Assert.Equal(
            new[] { 5, 4 },
            Ids(Guarded()
                .Order(By("Id", Direction.Descending))
                .Where(new Condition { Field = "Priority", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = { 0 } })
                .Page(FirstTwo())
                .AsUnguardedQueryable()
                .ToList()));
    }

    [Fact]
    public async Task A_guarded_segment_takes_the_default_unless_its_source_is_ordered()
    {
        SegmentResult<Ticket> result = await Guarded().ToListAsync(UrgentOrFive());
        SegmentResult<Ticket> ordered = await _db.Tickets.OrderByDescending(t => t.Title)
            .ApplyPolicy(Caller(), Options(), Resolver())
            .ToListAsync(UrgentOrFive());

        Assert.Equal(new[] { 2, 4, 3, 5 }, Ids(result.Data!));
        Assert.Equal(new[] { 5, 4, 3, 2 }, Ids(ordered.Data!));
    }

    [Fact]
    public void A_type_that_declares_nothing_is_never_ordered_by_the_library()
    {
        PolicyQueryable<PlainTicket> guarded = _db.PlainTickets.ApplyPolicy(Caller(), Options(), Resolver());

        string sql = guarded.ToList(new Filter { Page = FirstTwo() }, getQueryString: true).QueryString!;

        Assert.DoesNotContain("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.False(DefaultOrder.IsOrdered(guarded.Filter(new Filter()).AsUnguardedQueryable().Expression));
        Assert.False(DefaultOrder.IsOrdered(guarded.Page(FirstTwo()).AsUnguardedQueryable().Expression));
    }

    [Theory]
    [InlineData(DwTier.Convenience)]
    [InlineData(DwTier.Strict)]
    public void Unknown_fields_and_unreadable_entries_are_skipped_and_reported_at_startup(DwTier tier)
    {
        // "Missing desc" names nothing and "Id sideways" is not a direction; "priority DESC" and "id"
        // still read, in any letter case, and the trailing comma says nothing at all.
        Assert.Equal(
            ByDefault,
            _db.SloppyTickets.ApplyPolicy(Caller(), Options(tier), Resolver()).ToList(new Filter()).Data.Select(row => row.Id));

        PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(SloppyTicket) });

        Assert.Equal(new[] { "SloppyTicket: DefaultOrder entry 'Id sideways' is not a field optionally followed by asc or desc, so guarded queries skip it." }, report.Errors);
        Assert.Equal(new[] { "SloppyTicket: DefaultOrder names 'Missing', which SloppyTicket does not have, so guarded queries skip it." }, report.Warnings);
        Assert.Empty(PolicyModelValidator.Inspect(new[] { typeof(Ticket), typeof(PlainTicket) }).Errors);
    }

    [Theory]
    [InlineData(DwTier.Convenience)]
    [InlineData(DwTier.Strict)]
    public void A_field_no_query_can_order_by_is_skipped_and_reported_at_startup(DwTier tier)
    {
        // Ordering by a collection of entities is refused by the core, so a default naming one would
        // refuse every guarded query that took it. A path through a collection to a value is sorted by
        // the core's aggregate, so it stays.
        Assert.Equal(
            ByDefault,
            _db.WatchedTickets.ApplyPolicy(Caller(), Options(tier), Resolver()).ToList(new Filter()).Data.Select(row => row.Id));

        Assert.Equal(new[] { "Priority", "Id" }, DefaultOrder.For(typeof(WatchedTicket)).Select(entry => entry.Field));
        Assert.Equal(new[] { "Watchers.Name", "Id" }, DefaultOrder.For(typeof(ReachingTicket)).Select(entry => entry.Field));

        Assert.Equal(
            new[] { "WatchedTicket: DefaultOrder names 'Watchers', which no query can order by, so guarded queries skip it." },
            PolicyModelValidator.Inspect(new[] { typeof(WatchedTicket) }).Errors);
        Assert.Empty(PolicyModelValidator.Inspect(new[] { typeof(ReachingTicket) }).Errors);
    }

    [Theory]
    [InlineData(DwTier.Convenience)]
    [InlineData(DwTier.Strict)]
    public void A_default_field_the_caller_may_not_order_by_is_left_out_not_refused(DwTier tier)
    {
        PolicyQueryable<Ticket> guarded = _db.Tickets.ApplyPolicy(Caller(), Options(tier), Resolver(NoOrderingByPriority()));

        FilterResult<Ticket> result = guarded.ToList(new Filter());

        Assert.Equal(AsWritten, Ids(result.Data));
        Assert.Contains(
            guarded.LastTrace!.Decisions,
            decision => decision.FieldPath == "Priority"
                        && decision.Action == PolicyAction.Dropped
                        && decision.Reason!.StartsWith("left out of the default order", StringComparison.Ordinal));
    }

    [Fact]
    public void A_field_the_attributes_deny_is_left_out_and_fails_the_startup_scan()
    {
        Assert.Equal(
            AsWritten,
            _db.SecretTickets.ApplyPolicy(Caller(), Options(), Resolver()).ToList(new Filter()).Data.Select(row => row.Id));

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
        PolicyQueryable<Ticket> guarded = Guarded(NoOrderingByPriority());

        FilterResult<Ticket> result = guarded.ToList(
            new Filter { Orders = new List<OrderBy> { By("Priority") } }, getQueryString: true);

        Assert.DoesNotContain("ORDER BY", result.QueryString!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(guarded.LastTrace!.Decisions, decision => decision.Reason?.Contains("default order") == true);
    }

    [Fact]
    public void The_guarded_page_takes_the_default_only_when_nothing_ordered_the_query()
    {
        Assert.Equal(new[] { 2, 4 }, Ids(Guarded().Page(FirstTwo()).AsUnguardedQueryable().ToList()));
        Assert.Equal(new[] { 1, 2 }, Ids(Guarded().Order(By("Title")).Page(FirstTwo()).AsUnguardedQueryable().ToList()));
        Assert.Equal(new[] { 1, 2 }, Ids(Guarded(NoOrderingByPriority()).Page(FirstTwo()).AsUnguardedQueryable().ToList()));
    }

    [Fact]
    public async Task A_guarded_segment_leaves_out_what_the_caller_may_not_order_by()
    {
        SegmentResult<Ticket> allowed = await Guarded().ToListAsync(UrgentOrFive());
        SegmentResult<Ticket> denied = await Guarded(NoOrderingByPriority()).ToListAsync(UrgentOrFive());

        Assert.Equal(new[] { 2, 4, 3, 5 }, Ids(allowed.Data!));
        Assert.Equal(new[] { 2, 3, 4, 5 }, Ids(denied.Data!));
    }

    // ---------------------------------------------------------------- found by the 3.1.0 review

    [Fact]
    public void A_projected_query_is_left_in_its_own_order()
    {
        // A default names fields a projection may not have kept: ordering Select(Id, Title) by Priority
        // could not be translated, and the chain worked before the default existed.
        PolicyQueryable<Ticket> projected = Guarded().Select(new List<string> { "Id", "Title" });

        Assert.Equal(new[] { 1, 2 }, Ids(projected.Page(FirstTwo()).AsUnguardedQueryable().ToList()));
        Assert.Equal(new[] { 1, 2 }, Ids(projected.ToList(new Filter { Page = FirstTwo() }).Data));
    }

    [Fact]
    public void A_composed_order_whose_fields_were_all_dropped_gets_no_default()
    {
        // The whole filter gives a caller whose orders were all dropped no default in their place, and
        // so does a chain: Order sent orders, whether or not any of them survived.
        PolicyQueryable<Ticket> ordered = Guarded(
                new FakePolicyProvider().Add("Title", PolicyFeature.Order, PolicyEffect.Deny, PolicyLevel.DynamicGlobal))
            .Order(By("Title"));

        Assert.False(DefaultOrder.IsOrdered(ordered.Page(FirstTwo()).AsUnguardedQueryable().Expression));
        Assert.DoesNotContain(
            "ORDER BY", ordered.ToList(new Filter(), getQueryString: true).QueryString!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_segment_leaves_out_a_default_field_denied_for_segments()
    {
        // A segment refuses an order the caller sends on a field denied for segments, so its default
        // leaves that field out as well. A filter, which may order by it, still takes it.
        PolicyQueryable<Ticket> guarded = Guarded(
            new FakePolicyProvider().Add("Priority", PolicyFeature.Segment, PolicyEffect.Deny, PolicyLevel.DynamicGlobal));

        Segment everyone = new()
        {
            ConditionSets =
            {
                new ConditionSet
                {
                    Sort = 1,
                    ConditionGroup = new ConditionGroup
                    {
                        Conditions = { new Condition { Field = "Id", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = { 1 } } }
                    }
                }
            }
        };

        Assert.Equal(AsWritten, Ids((await guarded.ToListAsync(everyone)).Data!));
        Assert.Contains(
            guarded.LastTrace!.Decisions,
            decision => decision.FieldPath == "Priority"
                        && decision.Action == PolicyAction.Dropped
                        && decision.Reason!.StartsWith("left out of the default order", StringComparison.Ordinal));
        Assert.Equal(ByDefault, Ids(guarded.ToList(new Filter()).Data));
    }

    [Fact]
    public void A_denial_a_rule_can_lift_is_a_warning_and_so_is_one_for_segments()
    {
        PolicyModelReport granted = PolicyModelValidator.Inspect(new[] { typeof(GrantedTicket) });
        PolicyModelReport unsegmented = PolicyModelValidator.Inspect(new[] { typeof(UnsegmentedTicket) });

        Assert.Empty(granted.Errors);
        Assert.Contains(
            "GrantedTicket: DefaultOrder names 'Rank', which its attributes deny for ordering unless a rule allows it, "
            + "so guarded queries leave it out until one does.",
            granted.Warnings);
        Assert.Empty(unsegmented.Errors);
        Assert.Contains(
            "UnsegmentedTicket: DefaultOrder names 'Rank', which its attributes deny for segments, so guarded segments leave it out.",
            unsegmented.Warnings);
    }

    // ------------------------------------------------------------------- a projected source (3.2.0)

    /// <summary>
    /// DCMP's A9. A projection that builds the type in an initializer and assigns every field the
    /// default names hides nothing, so the default applies: EF Core translates the order through the
    /// members the projection assigned. It used to be left unordered, like every projection.
    /// </summary>
    [Fact]
    public async Task A_projection_that_assigns_every_default_field_takes_the_default()
    {
        IQueryable<Ticket> rows = _db.Tickets.Select(t => new Ticket { Id = t.Id, Priority = t.Priority, Title = t.Title });

        PolicyQueryable<Ticket> guarded = rows.ApplyPolicy(Caller(), Options(), Resolver());

        Assert.Equal(ByDefault, Ids(guarded.ToList(new Filter()).Data));
        Assert.Equal(ByDefault, Ids((await guarded.ToListAsync(new Filter())).Data));
        Assert.Equal(new[] { 2, 4 }, Ids(guarded.Page(FirstTwo()).AsUnguardedQueryable().ToList()));
        Assert.Equal(new[] { 2, 4, 3, 5 }, Ids((await guarded.ToListAsync(UrgentOrFive())).Data!));
    }

    /// <summary>A projection that leaves out a field the default names keeps whatever order it had.</summary>
    [Fact]
    public void A_projection_that_leaves_a_default_field_out_keeps_its_own_order()
    {
        IQueryable<Ticket> rows = _db.Tickets.Select(t => new Ticket { Id = t.Id, Title = t.Title });

        Assert.Equal(AsWritten, Ids(rows.ApplyPolicy(Caller(), Options(), Resolver()).ToList(new Filter()).Data));
    }

    /// <summary>DCMP's A10 and A11: a source ordered before its projection keeps that order, and a caller's order wins.</summary>
    [Fact]
    public void A_source_ordered_before_its_projection_keeps_that_order_and_the_callers_order_wins()
    {
        IQueryable<Ticket> rows = _db.Tickets.OrderBy(t => t.Title)
            .Select(t => new Ticket { Id = t.Id, Priority = t.Priority, Title = t.Title });

        Assert.Equal(AsWritten, Ids(rows.ApplyPolicy(Caller(), Options(), Resolver()).ToList(new Filter()).Data));
        Assert.Equal(
            new[] { 5, 4, 3, 2, 1 },
            Ids(rows.ApplyPolicy(Caller(), Options(), Resolver())
                .ToList(new Filter { Orders = new List<OrderBy> { By("Id", Direction.Descending) } }).Data));
    }

    /// <summary>
    /// The default is for the rows the caller's source makes. A projection the caller composes on the
    /// guarded handle afterwards stays in its own order, even when it keeps every field the default names.
    /// </summary>
    [Fact]
    public void A_projection_composed_after_ApplyPolicy_stays_unordered_even_with_every_default_field()
    {
        PolicyQueryable<Ticket> projected = Guarded().Select(new List<string> { "Id", "Priority" });

        Assert.Equal(new[] { 1, 2 }, Ids(projected.Page(FirstTwo()).AsUnguardedQueryable().ToList()));
        Assert.Equal(AsWritten, Ids(projected.ToList(new Filter()).Data));
    }

    /// <summary>
    /// A composed Filter whose orders were all dropped sent orders, so nothing later in the chain adds a
    /// default in their place, exactly as a composed Order whose fields were all dropped does.
    /// </summary>
    [Fact]
    public void A_filter_composed_with_orders_that_were_all_dropped_gets_no_default_later()
    {
        PolicyQueryable<Ticket> guarded = Guarded(
            new FakePolicyProvider().Add("Title", PolicyFeature.Order, PolicyEffect.Deny, PolicyLevel.DynamicGlobal));

        PolicyQueryable<Ticket> filtered = guarded.Filter(new Filter { Orders = new List<OrderBy> { By("Title") } });

        Assert.Equal(new[] { 1, 2 }, Ids(filtered.Page(FirstTwo()).AsUnguardedQueryable().ToList()));
    }

    /// <summary>
    /// A nested default is assigned only when every level of its path is an initializer that assigns
    /// the next member. Any other expression along the way, or a member left out, hides it.
    /// </summary>
    [Fact]
    public void A_nested_default_is_assigned_only_through_initializers()
    {
        OwnedTicket[] source =
        {
            new() { Id = 1, Owner = new TicketOwner { Name = "b" } },
            new() { Id = 2, Owner = new TicketOwner { Name = "c" } },
            new() { Id = 3, Owner = new TicketOwner { Name = "a" } }
        };

        IQueryable<OwnedTicket> initialized = source.AsQueryable()
            .Select(t => new OwnedTicket { Id = t.Id, Owner = new TicketOwner { Name = t.Owner!.Name } });
        IQueryable<OwnedTicket> assignedWhole = source.AsQueryable()
            .Select(t => new OwnedTicket { Id = t.Id, Owner = t.Owner });
        IQueryable<OwnedTicket> leftOut = source.AsQueryable()
            .Select(t => new OwnedTicket { Id = t.Id });

        Assert.False(DefaultOrder.HidesDefault(initialized.Expression, typeof(OwnedTicket)));
        Assert.True(DefaultOrder.HidesDefault(assignedWhole.Expression, typeof(OwnedTicket)));
        Assert.True(DefaultOrder.HidesDefault(leftOut.Expression, typeof(OwnedTicket)));
        Assert.False(DefaultOrder.HidesDefault(source.AsQueryable().Expression, typeof(OwnedTicket)));

        Assert.Equal(
            new[] { 2, 1, 3 },
            initialized.ApplyPolicy(Caller(), Options(), Resolver()).ToList(new Filter()).Data.Select(row => row.Id));
        Assert.Equal(
            new[] { 1, 2, 3 },
            assignedWhole.ApplyPolicy(Caller(), Options(), Resolver()).ToList(new Filter()).Data.Select(row => row.Id));
    }

    [Fact]
    public void An_audited_default_field_is_recorded_only_when_the_query_orders_by_it()
    {
        // A field the default keeps is used, so it is recorded as a caller's own order is. A field left
        // out is not used and the caller never named it: an event for it would say they tried to.
        IQueryable<AuditedTicket> rows = new AuditedTicket[]
        {
            new() { Id = 1, Rank = 1 }, new() { Id = 2, Rank = 3 }, new() { Id = 3, Rank = 2 },
        }.AsQueryable();
        FakePolicyProvider noOrderingByRank =
            new FakePolicyProvider().Add("Rank", PolicyFeature.Order, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);

        DwPolicyContext kept = Caller();
        Assert.Equal(new[] { 2, 3, 1 }, rows.ApplyPolicy(kept, Options(), Resolver()).ToList(new Filter()).Data.Select(row => row.Id));
        DwAuditEvent used = Assert.Single(kept.PendingAuditEvents);
        Assert.Equal(("Rank", PolicyFeature.Order, PolicyEffect.Allow, false), (used.FieldPath, used.Feature, used.Effect, used.DryRun));

        DwPolicyContext leftOut = Caller();
        Assert.Equal(
            new[] { 1, 2, 3 },
            rows.ApplyPolicy(leftOut, Options(), Resolver(noOrderingByRank)).ToList(new Filter()).Data.Select(row => row.Id));
        Assert.Empty(leftOut.PendingAuditEvents);

        // A dry run orders by it after all, so the use is recorded, with the effect enforcement would have had.
        DwPolicyContext dryRun = Caller();
        Assert.Equal(
            new[] { 2, 3, 1 },
            rows.ApplyPolicy(dryRun, Options(dryRun: true), Resolver(noOrderingByRank)).ToList(new Filter()).Data.Select(row => row.Id));
        DwAuditEvent wouldDeny = Assert.Single(dryRun.PendingAuditEvents);
        Assert.Equal(("Rank", PolicyEffect.Deny, true), (wouldDeny.FieldPath, wouldDeny.Effect, wouldDeny.DryRun));
    }
}
