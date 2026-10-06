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
using DynamicWhere.ex.Policies.Validation;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests.Policies;

/// <summary>A read-only row that refuses a caller's projection, with one member denied for select.</summary>
[DwEntity(RefuseSelects = true, RequirePolicy = true)]
public class RsTicketRow
{
    public int Id { get; set; }

    public string? Code { get; set; }

    public string? Title { get; set; }

    [DwNoSelect]
    public string? Secret { get; set; }
}

/// <summary>The same flag standing alone, which the model scan warns about.</summary>
[DwEntity(RefuseSelects = true)]
public class RsLooseRow
{
    public int Id { get; set; }

    public string? Code { get; set; }
}

/// <summary>Inherits the base type's <c>[DwEntity]</c>, flag included.</summary>
public class RsDerivedRow : RsTicketRow
{
}

/// <summary>Declares its own <c>[DwEntity]</c>, which replaces the base type's.</summary>
[DwEntity(RequirePolicy = true)]
public class RsReplacedRow : RsTicketRow
{
}

/// <summary>The entity the read-only row is projected from. It carries no policy of its own.</summary>
public class RsTicket
{
    public int Id { get; set; }

    public string? Code { get; set; }

    public string? Title { get; set; }

    public string? Secret { get; set; }
}

/// <summary>The database behind it, since a segment is read asynchronously.</summary>
public sealed class RsContext : DbContext
{
    private readonly SqliteConnection _connection;

    public RsContext(SqliteConnection connection) => _connection = connection;

    public DbSet<RsTicket> Tickets => Set<RsTicket>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
}

/// <summary>
/// <c>[DwEntity(RefuseSelects = true)]</c>: a guarded query refuses a projection the caller writes, once its
/// names have been gated, on every surface that takes one, in both tiers (issue #10).
/// </summary>
public class RefuseSelectsTests
{
    private static List<T> Rows<T>() where T : RsTicketRow, new() => new()
    {
        new T { Id = 1, Code = "A-1", Title = "first", Secret = "s1" },
        new T { Id = 2, Code = "B-2", Title = "second", Secret = "s2" }
    };

    private static DwPolicyContext Caller() =>
        new DwPolicyContext { Purpose = "support" }.WithSubject(DwSubjectKind.User, "u1");

    private static DwPolicyOptions Options(DwTier tier, bool audit = false, bool dryRun = false) =>
        new() { Tier = tier, AuditRefusals = audit, DryRun = dryRun, Caps = { MinGroupSize = 1 } };

    private static PolicyResolver Attributes() => new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    private static PolicyQueryable<T> Guard<T>(DwTier tier, DwPolicyContext? caller = null, bool audit = false, bool dryRun = false)
        where T : RsTicketRow, new() =>
        Rows<T>().AsQueryable().ApplyPolicy(caller ?? Caller(), Options(tier, audit, dryRun), Attributes());

    private static Filter Selecting(params string[] names) => new() { Selects = names.ToList() };

    private static Segment SegmentSelecting(params string[] names) => new()
    {
        ConditionSets = new List<ConditionSet>
        {
            new()
            {
                Sort = 1,
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions = new List<Condition>
                    {
                        new() { Sort = 1, Field = "Id", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = { "0" } }
                    }
                }
            }
        },
        Selects = names.ToList()
    };

    public static TheoryData<DwTier> Tiers() => new() { DwTier.Strict, DwTier.Convenience };

    private static void AssertRefused(PolicyException refusal)
    {
        Assert.Equal(PolicyErrorCode.SelectsRefused, refusal.ErrorCode);
        Assert.Equal(PolicyFeature.Select, refusal.Feature);
        Assert.Equal("*", refusal.FieldPath);
        Assert.Equal("DwEntityAttribute(RefuseSelects = true)", refusal.SourceOrigin);
    }

    // ---- every surface that takes a caller's projection ------------------------------------------

    [Theory]
    [MemberData(nameof(Tiers))]
    public async Task Every_filter_terminal_refuses_a_projection(DwTier tier)
    {
        AssertRefused(Assert.Throws<PolicyException>(() => Guard<RsTicketRow>(tier).ToList(Selecting("Code"))));
        AssertRefused(Assert.Throws<PolicyException>(() => Guard<RsTicketRow>(tier).ToListDynamic(Selecting("Code"))));
        AssertRefused(await Assert.ThrowsAsync<PolicyException>(() => Guard<RsTicketRow>(tier).ToListAsync(Selecting("Code"))));
        AssertRefused(await Assert.ThrowsAsync<PolicyException>(() => Guard<RsTicketRow>(tier).ToListAsync(Selecting("Code"), CancellationToken.None)));
        AssertRefused(await Assert.ThrowsAsync<PolicyException>(() => Guard<RsTicketRow>(tier).ToListAsyncDynamic(Selecting("Code"))));
        AssertRefused(await Assert.ThrowsAsync<PolicyException>(() => Guard<RsTicketRow>(tier).ToListAsyncDynamic(Selecting("Code"), CancellationToken.None)));
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void Every_composable_that_takes_a_projection_refuses_one(DwTier tier)
    {
        AssertRefused(Assert.Throws<PolicyException>(() => Guard<RsTicketRow>(tier).Filter(Selecting("Code"))));
        AssertRefused(Assert.Throws<PolicyException>(() => Guard<RsTicketRow>(tier).FilterDynamic(Selecting("Code"))));
        AssertRefused(Assert.Throws<PolicyException>(() => Guard<RsTicketRow>(tier).Select(new List<string> { "Code" })));
        AssertRefused(Assert.Throws<PolicyException>(() => Guard<RsTicketRow>(tier).SelectDynamic(new List<string> { "Code" })));
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public async Task A_segment_refuses_a_projection(DwTier tier)
    {
        AssertRefused(await Assert.ThrowsAsync<PolicyException>(() => Guard<RsTicketRow>(tier).ToListAsync(SegmentSelecting("Code"))));
        AssertRefused(await Assert.ThrowsAsync<PolicyException>(() => Guard<RsTicketRow>(tier).ToListAsync(SegmentSelecting("Code"), CancellationToken.None)));
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void The_simulator_reports_the_refusal(DwTier tier)
    {
        DwPolicyOptions options = Options(tier);

        options.Freeze();

        PolicySimulation<Filter> filter = PolicySimulator.Simulate<RsTicketRow>(Selecting("Code"), Caller(), options, Attributes());
        PolicySimulation<Segment> segment = PolicySimulator.Simulate<RsTicketRow>(SegmentSelecting("Code"), Caller(), options, Attributes());

        Assert.False(filter.WouldRun);
        AssertRefused(filter.Refusal!);
        Assert.False(segment.WouldRun);
        AssertRefused(segment.Refusal!);
    }

    // ---- what is not a caller's projection is never refused ---------------------------------------

    [Theory]
    [MemberData(nameof(Tiers))]
    public void No_projection_and_an_empty_one_return_whole_rows(DwTier tier)
    {
        foreach (Filter filter in new[] { new Filter(), new Filter { Selects = new List<string>() } })
        {
            FilterResult<RsTicketRow> rows = Guard<RsTicketRow>(tier).ToList(filter);

            Assert.Equal(2, rows.Data.Count);
            Assert.Equal(new[] { "A-1", "B-2" }, rows.Data.Select(row => row.Code).ToArray());
            Assert.Equal(new[] { "first", "second" }, rows.Data.Select(row => row.Title).ToArray());

            // The projection the policy synthesizes to withhold the member denied for select is the
            // library's own, not the caller's, so it runs.
            Assert.All(rows.Data, row => Assert.Null(row.Secret));
        }

        Assert.Equal(2, Guard<RsTicketRow>(tier).Where(new ConditionGroup()).ToList(new Filter()).Data.Count);
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public async Task Rows_projected_from_an_entity_before_the_policy_refuse_a_projection_and_come_back_whole_without_one(DwTier tier)
    {
        // The shape the issue came from: a read-only row type projected from the entity, then guarded.
        using SqliteConnection connection = new("DataSource=:memory:");

        connection.Open();

        using RsContext db = new(connection);

        db.Database.EnsureCreated();
        db.Tickets.AddRange(
            new RsTicket { Id = 1, Code = "A-1", Title = "first", Secret = "s1" },
            new RsTicket { Id = 2, Code = "B-2", Title = "second", Secret = "s2" });
        db.SaveChanges();

        PolicyQueryable<RsTicketRow> Rows() => db.Tickets
            .Select(t => new RsTicketRow { Id = t.Id, Code = t.Code, Title = t.Title, Secret = t.Secret })
            .ApplyPolicy(Caller(), Options(tier), Attributes());

        AssertRefused(await Assert.ThrowsAsync<PolicyException>(() => Rows().ToListAsync(Selecting("Code"))));
        AssertRefused(await Assert.ThrowsAsync<PolicyException>(() => Rows().ToListAsync(SegmentSelecting("Code"))));

        FilterResult<RsTicketRow> whole = await Rows().ToListAsync(new Filter());
        SegmentResult<RsTicketRow> segment = await Rows().ToListAsync(SegmentSelecting());

        foreach (List<RsTicketRow> data in new[] { whole.Data, segment.Data })
        {
            Assert.Equal(new[] { "A-1", "B-2" }, data.OrderBy(row => row.Id).Select(row => row.Code).ToArray());
            Assert.Equal(new[] { "first", "second" }, data.OrderBy(row => row.Id).Select(row => row.Title).ToArray());
            Assert.All(data, row => Assert.Null(row.Secret));
        }
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void An_empty_projection_means_what_it_means_on_any_type(DwTier tier)
    {
        // Never this flag's refusal. With nothing denied there is no projection to synthesize, so the empty
        // list reaches the pipeline, which refuses it as it refuses one on any type.
        LogicException refused = Assert.Throws<LogicException>(() => new List<RsLooseRow> { new() { Id = 1, Code = "A-1" } }
            .AsQueryable()
            .ApplyPolicy(Caller(), Options(tier), Attributes())
            .ToList(new Filter { Selects = new List<string>() }));

        Assert.IsNotType<PolicyException>(refused);
        Assert.Equal(ErrorCode.MustHaveFields, refused.Message);
    }

    [Fact]
    public void A_type_without_the_flag_still_takes_a_projection()
    {
        FilterResult<RsReplacedRow> rows = Guard<RsReplacedRow>(DwTier.Strict).ToList(Selecting("Code"));

        Assert.Equal(new[] { "A-1", "B-2" }, rows.Data.Select(row => row.Code).ToArray());
        Assert.All(rows.Data, row => Assert.Null(row.Title));
    }

    [Fact]
    public void A_derived_type_inherits_the_flag()
    {
        AssertRefused(Assert.Throws<PolicyException>(() => Guard<RsDerivedRow>(DwTier.Strict).ToList(Selecting("Code"))));
        AssertRefused(Assert.Throws<PolicyException>(() => Guard<RsDerivedRow>(DwTier.Convenience).Select(new List<string> { "Code" })));
    }

    [Fact]
    public void An_unguarded_query_never_reads_the_flag()
    {
        // Documented, and why the model scan warns when the flag stands without RequirePolicy.
        FilterResult<RsLooseRow> rows = new List<RsLooseRow> { new() { Id = 1, Code = "A-1" } }
            .AsQueryable()
            .ToList(Selecting("Code"));

        Assert.Equal("A-1", Assert.Single(rows.Data).Code);
    }

    // ---- the names are gated first ----------------------------------------------------------------

    [Fact]
    public void Under_strict_a_name_the_caller_may_not_select_is_refused_as_itself()
    {
        PolicyException refusal = Assert.Throws<PolicyException>(
            () => Guard<RsTicketRow>(DwTier.Strict).ToList(Selecting("Code", "Secret")));

        Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, refusal.ErrorCode);

        // A name that matches nothing is refused exactly as a denied one is, so the type's own refusal
        // cannot tell a caller which names exist.
        PolicyException unknown = Assert.Throws<PolicyException>(
            () => Guard<RsTicketRow>(DwTier.Strict).ToList(Selecting("Code", "password_hash")));

        Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, unknown.ErrorCode);
        Assert.Equal(refusal.FieldPath, unknown.FieldPath);
    }

    [Fact]
    public void Inside_a_strict_segment_a_denied_name_keeps_the_segment_code()
    {
        PolicyException refusal = Assert.ThrowsAsync<PolicyException>(
            () => Guard<RsTicketRow>(DwTier.Strict).ToListAsync(SegmentSelecting("Code", "Secret"))).GetAwaiter().GetResult();

        Assert.Equal(PolicyErrorCode.FieldDeniedForSegment, refusal.ErrorCode);
    }

    [Fact]
    public void Under_convenience_a_denied_name_is_dropped_and_the_rest_refused()
    {
        PolicyQueryable<RsTicketRow> guarded = Guard<RsTicketRow>(DwTier.Convenience);

        AssertRefused(Assert.Throws<PolicyException>(() => guarded.ToList(Selecting("Code", "Secret"))));

        PolicyTrace trace = guarded.LastTrace!;

        Assert.Contains(trace.Decisions, decision =>
            decision.FieldPath == "Secret" && decision.Feature == PolicyFeature.Select && decision.Action == PolicyAction.Dropped);
        Assert.Contains(trace.Decisions, decision =>
            decision.FieldPath == "*" && decision.Feature == PolicyFeature.Select && decision.Action == PolicyAction.Denied
            && decision.Reason == "DwEntityAttribute(RefuseSelects = true)");
    }

    [Theory]
    [MemberData(nameof(Tiers))]
    public void A_list_of_names_none_of_which_may_be_selected_is_refused_as_it_is_anywhere(DwTier tier)
    {
        PolicyException refusal = Assert.Throws<PolicyException>(() => Guard<RsTicketRow>(tier).ToList(Selecting("Secret")));

        Assert.Equal(
            tier == DwTier.Strict ? PolicyErrorCode.FieldDeniedForSelect : PolicyErrorCode.AllSelectsDenied,
            refusal.ErrorCode);
    }

    // ---- audit, trace and dry run -----------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Tiers))]
    public void The_refusal_is_audited(DwTier tier)
    {
        DwPolicyContext caller = Caller();

        Assert.Throws<PolicyException>(() => Guard<RsTicketRow>(tier, caller, audit: true).ToList(Selecting("Code")));

        DwAuditEvent recorded = Assert.Single(caller.PendingAuditEvents);

        Assert.Equal(PolicyErrorCode.SelectsRefused, recorded.ErrorCode);
        Assert.Equal(PolicyFeature.Select, recorded.Feature);
        Assert.Equal(PolicyEffect.Deny, recorded.Effect);
        Assert.Equal("*", recorded.FieldPath);
        Assert.Equal(typeof(RsTicketRow).FullName, recorded.EntityType);
        Assert.Equal(tier, recorded.Tier);
    }

    [Fact]
    public void A_probe_for_a_denied_member_is_audited_under_its_own_path()
    {
        // What a host refusing the list in its endpoint, before the gate, could never record.
        DwPolicyContext caller = Caller();

        Assert.Throws<PolicyException>(() => Guard<RsTicketRow>(DwTier.Strict, caller, audit: true).ToList(Selecting("Code", "Secret")));

        DwAuditEvent recorded = Assert.Single(caller.PendingAuditEvents);

        Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, recorded.ErrorCode);
        Assert.Equal("Secret", recorded.FieldPath);
    }

    [Fact]
    public void A_dry_run_records_the_refusal_and_runs_the_projection_as_written()
    {
        PolicyQueryable<RsTicketRow> guarded = Guard<RsTicketRow>(DwTier.Strict, dryRun: true);

        FilterResult<RsTicketRow> rows = guarded.ToList(Selecting("Code"));

        Assert.Equal(new[] { "A-1", "B-2" }, rows.Data.Select(row => row.Code).ToArray());
        Assert.All(rows.Data, row => Assert.Null(row.Title));
        Assert.Contains(guarded.LastTrace!.Decisions, decision =>
            decision.FieldPath == "*" && decision.Feature == PolicyFeature.Select && decision.Action == PolicyAction.Denied
            && decision.Reason == "DwEntityAttribute(RefuseSelects = true)");
    }

    // ---- schema, model scan, error code -----------------------------------------------------------

    [Fact]
    public void The_schema_says_so_once_for_the_entity_and_leaves_can_select_alone()
    {
        DwEntityCatalog catalog = new();

        catalog.Expose(typeof(RsTicketRow));
        catalog.Expose(typeof(RsReplacedRow));
        catalog.Freeze();

        DwPolicyOptions options = new();

        options.Freeze();

        PolicySchema refusing = PolicySchemaBuilder.Describe(
            typeof(RsTicketRow), catalog, new DwPolicyContext(), options, Attributes(), new PolicySchemaRequest());
        PolicySchema taking = PolicySchemaBuilder.Describe(
            typeof(RsReplacedRow), catalog, new DwPolicyContext(), options, Attributes(), new PolicySchemaRequest());

        Assert.True(refusing.RefusesSelects);
        Assert.False(taking.RefusesSelects);
        Assert.True(refusing.Fields.Single(field => field.Path == "Code").CanSelect);
        Assert.False(refusing.Fields.Single(field => field.Path == "Secret").CanSelect);
    }

    [Fact]
    public void The_model_scan_warns_when_the_flag_stands_without_require_policy()
    {
        PolicyModelReport loose = PolicyModelValidator.Inspect(new[] { typeof(RsLooseRow) });
        PolicyModelReport paired = PolicyModelValidator.Inspect(new[] { typeof(RsTicketRow), typeof(RsDerivedRow) });

        Assert.Contains(loose.Warnings, warning => warning.StartsWith("RsLooseRow: RefuseSelects is set without RequirePolicy", StringComparison.Ordinal));
        Assert.Empty(loose.Errors);
        Assert.DoesNotContain(paired.Warnings, warning => warning.Contains("RefuseSelects", StringComparison.Ordinal));
    }

    [Fact]
    public void The_code_is_appended_as_twenty_three()
    {
        Assert.Equal(23, (int)PolicyErrorCode.SelectsRefused);
        Assert.Equal("SelectsRefused", new PolicyException(PolicyErrorCode.SelectsRefused, "*", PolicyFeature.Select, DwTier.Strict).Code);
    }
}
