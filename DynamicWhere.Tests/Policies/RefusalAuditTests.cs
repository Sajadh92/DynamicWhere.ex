using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Audit;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Extensions.Configuration;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// <c>DwPolicyOptions.AuditRefusals</c>: a refused guarded query written to the audit (DW-10).
/// </summary>
/// <remarks>
/// <c>[DwAudit]</c> records uses. A caller probing for columns they may not read is refused at every
/// guess, so without this the probe leaves nothing behind.
/// </remarks>
[Collection(PolicyCollection.Name)]
public class RefusalAuditTests : IDisposable
{
    private readonly PolicyContext _db;

    public RefusalAuditTests(PolicyFixture fixture) => _db = fixture.CreateContext();

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class Recorder : IDwAuditSink
    {
        internal List<DwAuditEvent> Written { get; } = new();

        public ValueTask WriteAsync(DwAuditEvent auditEvent, CancellationToken ct = default)
        {
            Written.Add(auditEvent);

            return default;
        }
    }

    private static readonly IQueryable<SecuredEmployee> People =
        new List<SecuredEmployee> { new() { Id = 1, Name = "Ada" } }.AsQueryable();

    private static DwPolicyContext Caller() =>
        new DwPolicyContext { Purpose = "support" }.WithSubject(DwSubjectKind.User, "u1");

    private static DwPolicyOptions Options(bool audit, DwTier tier = DwTier.Strict, int? maxEvents = null, bool dryRun = false)
    {
        DwPolicyOptions options = new() { Tier = tier, AuditRefusals = audit, DryRun = dryRun, Caps = { MinGroupSize = 1 } };

        if (maxEvents is int max)
        {
            options.Caps.MaxAuditEvents = max;
        }

        return options;
    }

    private static PolicyResolver Attributes() => new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    private static PolicyResolver Attributes(IDwPolicyProvider extra) =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider(), extra });

    private static Filter Where(string field) => new()
    {
        ConditionGroup = new ConditionGroup
        {
            Conditions = { new Condition { Field = field, DataType = DataType.Text, Operator = Operator.Equal, Values = { "x" } } }
        }
    };

    [Fact]
    public void Nothing_is_recorded_unless_the_posture_asks()
    {
        DwPolicyContext caller = Caller();

        Assert.Throws<PolicyException>(() => People.ApplyPolicy(caller, Options(audit: false), Attributes()).ToList(Where("NationalId")));

        Assert.Empty(caller.PendingAuditEvents);
    }

    [Fact]
    public void A_refusal_is_recorded_with_the_field_the_caller_was_not_told()
    {
        DwPolicyContext caller = Caller();

        PolicyException refusal = Assert.Throws<PolicyException>(
            () => People.ApplyPolicy(caller, Options(audit: true), Attributes()).ToList(Where("NationalId")));

        DwAuditEvent recorded = Assert.Single(caller.PendingAuditEvents);

        Assert.Equal("*", refusal.FieldPath);
        Assert.Equal("NationalId", recorded.FieldPath);
        Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, recorded.ErrorCode);
        Assert.Equal(PolicyFeature.Where, recorded.Feature);
        Assert.Equal(PolicyEffect.Deny, recorded.Effect);
        Assert.Equal(DwTier.Strict, recorded.Tier);
        Assert.Equal(typeof(SecuredEmployee).FullName, recorded.EntityType);
        Assert.Equal("support", recorded.Purpose);
        Assert.Equal(DwSubjectKind.User, Assert.Single(recorded.Subjects).Kind);
        Assert.False(recorded.DryRun);
    }

    [Fact]
    public void A_probe_for_a_column_that_does_not_exist_is_recorded_as_sent()
    {
        DwPolicyContext caller = Caller();

        Assert.Throws<PolicyException>(
            () => People.ApplyPolicy(caller, Options(audit: true), Attributes()).ToList(Where("password_hash")));

        DwAuditEvent recorded = Assert.Single(caller.PendingAuditEvents);

        Assert.Equal("password_hash", recorded.FieldPath);
        Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, recorded.ErrorCode);
    }

    [Fact]
    public void A_refusal_through_an_alias_is_recorded_under_the_path_it_stands_for()
    {
        // The caller is told the name they used. The audit keeps the canonical path, as every audit
        // event does, so a search for one column finds each refusal of it whatever the caller typed.
        FakePolicyProvider denied = new FakePolicyProvider()
            .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicGlobal)
            .AddAlias("Name", "full_name", PolicyLevel.DynamicGlobal);
        FakePolicyProvider required = new FakePolicyProvider()
            .AddRequired("Name", PolicyLevel.DynamicGlobal, Operator.Equal)
            .AddAlias("Name", "full_name", PolicyLevel.DynamicGlobal);
        DwPolicyContext caller = Caller();

        PolicyException refused = Assert.Throws<PolicyException>(
            () => People.ApplyPolicy(caller, Options(audit: true, DwTier.Convenience), Attributes(denied)).ToList(Where("full_name")));
        PolicyException missing = Assert.Throws<PolicyException>(
            () => People.ApplyPolicy(caller, Options(audit: true, DwTier.Convenience), Attributes(required)).ToList(new Filter()));

        Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refused.ErrorCode);
        Assert.Equal("full_name", refused.FieldPath);
        Assert.Equal(PolicyErrorCode.RequiredFilterMissing, missing.ErrorCode);
        Assert.Equal("full_name", missing.FieldPath);
        Assert.Equal(new[] { "Name", "Name" }, caller.PendingAuditEvents.Select(e => e.FieldPath));
    }

    [Fact]
    public void A_name_the_caller_invented_is_recorded_cut_short_and_with_its_control_characters_escaped()
    {
        // Under the strict tier an unknown name is recorded as sent, so it is text the caller wrote: a
        // line break would forge a second line in a log, and a megabyte of it would be kept whole.
        DwPolicyContext caller = Caller();
        PolicyQueryable<SecuredEmployee> guarded = People.ApplyPolicy(caller, Options(audit: true), Attributes());

        Assert.Throws<PolicyException>(() => guarded.ToList(Where("probe\nSalary Select Allow")));
        Assert.Throws<PolicyException>(() => guarded.ToList(Where(new string('x', 1000))));

        Assert.Equal(
            new[] { "probe\\u000aSalary Select Allow", new string('x', 256) + "\u2026" },
            caller.PendingAuditEvents.Select(e => e.FieldPath));
    }

    [Fact]
    public void Line_separators_and_invisible_format_characters_are_escaped_too()
    {
        // A log viewer breaks a line at U+2028 and U+2029 as well, and U+202E reverses the text after it
        // without showing itself. A format character beyond the Basic Multilingual Plane is escaped whole.
        DwPolicyContext caller = Caller();
        PolicyQueryable<SecuredEmployee> guarded = People.ApplyPolicy(caller, Options(audit: true), Attributes());

        Assert.Throws<PolicyException>(() => guarded.ToList(Where("a\u2028b\u2029c\u202Ed\U000E0041e")));

        Assert.Equal(
            "a\\u2028b\\u2029c\\u202ed\\udb40\\udc41e",
            Assert.Single(caller.PendingAuditEvents).FieldPath);
    }

    [Fact]
    public void Refusals_that_name_no_field_are_recorded_too()
    {
        DwPolicyContext caller = Caller();
        PolicyQueryable<SecuredEmployee> guarded = People.ApplyPolicy(caller, Options(audit: true), Attributes());

        Assert.Throws<PolicyException>(() => guarded.ToList(new Filter(), getQueryString: true));
        Assert.Throws<PolicyException>(() => guarded.ToList(new Filter { Page = new PageBy { PageNumber = 1, PageSize = 100000 } }));

        Assert.Equal(
            new PolicyErrorCode?[] { PolicyErrorCode.QueryStringDenied, PolicyErrorCode.CapExceeded },
            caller.PendingAuditEvents.Select(e => e.ErrorCode));
    }

    [Fact]
    public void A_missing_scope_is_recorded_against_the_scoped_field()
    {
        DwPolicyContext unscoped = Caller();

        Assert.Throws<PolicyException>(
            () => _db.Invoices.ApplyPolicy(unscoped, Options(audit: true), Attributes()).ToList(new Filter()));

        DwAuditEvent recorded = Assert.Single(unscoped.PendingAuditEvents);

        Assert.Equal(PolicyErrorCode.MissingContextValue, recorded.ErrorCode);
        Assert.Equal("TenantId", recorded.FieldPath);
    }

    [Fact]
    public async Task Every_kind_of_entry_point_records_once()
    {
        DwPolicyContext caller = Caller();
        PolicyQueryable<SecuredEmployee> guarded = People.ApplyPolicy(caller, Options(audit: true), Attributes());

        Assert.Throws<PolicyException>(() => guarded.Where(Where("NationalId").ConditionGroup!));
        Assert.Throws<PolicyException>(() => guarded.Order(new OrderBy { Field = "NationalId" }));
        Assert.Throws<PolicyException>(() => guarded.Select(new List<string> { "NationalId" }));
        Assert.Throws<PolicyException>(() => guarded.ToList(new Summary { GroupBy = new GroupBy { Fields = new List<string> { "NationalId" } } }));

        PolicyQueryable<Staff> staff = _db.Staff.ApplyPolicy(caller, Options(audit: true), Attributes());

        await Assert.ThrowsAsync<PolicyException>(() => staff.ToListAsync(Where("NationalId")));
        await Assert.ThrowsAsync<PolicyException>(() => staff.ToListAsync(new Segment
        {
            ConditionSets = { new ConditionSet { Sort = 1, ConditionGroup = Where("NationalId").ConditionGroup! } }
        }));

        Assert.Equal(
            new PolicyErrorCode?[]
            {
                PolicyErrorCode.FieldDeniedForWhere,
                PolicyErrorCode.FieldDeniedForOrder,
                PolicyErrorCode.FieldDeniedForSelect,
                PolicyErrorCode.FieldDeniedForGroup,
                PolicyErrorCode.FieldDeniedForWhere,
                PolicyErrorCode.FieldDeniedForSegment
            },
            caller.PendingAuditEvents.Select(e => e.ErrorCode));
    }

    [Fact]
    public void A_full_buffer_records_nothing_and_keeps_the_refusal_the_caller_needs()
    {
        DwPolicyContext caller = Caller();

        PolicyException refusal = Assert.Throws<PolicyException>(
            () => People.ApplyPolicy(caller, Options(audit: true, maxEvents: 1), Attributes()).ToList(new Filter(), getQueryString: true));
        PolicyException second = Assert.Throws<PolicyException>(
            () => People.ApplyPolicy(caller, Options(audit: true, maxEvents: 1), Attributes()).ToList(Where("NationalId")));

        Assert.Equal(PolicyErrorCode.QueryStringDenied, refusal.ErrorCode);
        Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, second.ErrorCode);
        Assert.Single(caller.PendingAuditEvents);
    }

    [Fact]
    public void A_dry_run_refuses_nothing_and_records_no_refusal()
    {
        DwPolicyContext caller = Caller();

        People.ApplyPolicy(caller, Options(audit: true, dryRun: true), Attributes()).ToList(Where("NationalId"));

        Assert.DoesNotContain(caller.PendingAuditEvents, e => e.ErrorCode is not null);
    }

    [Fact]
    public async Task Refusals_drain_to_the_sink_with_their_code()
    {
        DwPolicyContext caller = Caller();
        Recorder sink = new();

        Assert.Throws<PolicyException>(
            () => People.ApplyPolicy(caller, Options(audit: true), Attributes()).ToList(Where("NationalId")));

        await DwPolicy.DrainAuditAsync(caller, sink);

        DwAuditEvent written = Assert.Single(sink.Written);

        Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, written.ErrorCode);
        Assert.Contains("FieldDeniedForWhere", written.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unprepared_context_is_recorded_as_refused()
    {
        DwPolicyContext unprepared = Caller();

        PolicyException refusal = Assert.Throws<PolicyException>(
            () => PolicyExtensions.RequirePrepared<SecuredEmployee>(unprepared, Options(audit: true)));

        Assert.Equal(PolicyErrorCode.PolicyContextNotPrepared, refusal.ErrorCode);
        Assert.Equal(PolicyErrorCode.PolicyContextNotPrepared, Assert.Single(unprepared.PendingAuditEvents).ErrorCode);
    }

    [Fact]
    public void A_refusal_seen_twice_on_its_way_out_is_recorded_once()
    {
        DwPolicyContext caller = Caller();
        PolicyException refusal = new(PolicyErrorCode.CapExceeded, "*", PolicyFeature.None, DwTier.Strict);

        RefusalAudit.Record(caller, Options(audit: true), typeof(SecuredEmployee), refusal);
        RefusalAudit.Record(caller, Options(audit: true), typeof(SecuredEmployee), refusal);

        Assert.Single(caller.PendingAuditEvents);
    }

    [Fact]
    public void A_use_of_an_audited_field_carries_no_code()
    {
        DwAuditEvent use = new(
            DateTimeOffset.UtcNow, "E", "Salary", PolicyFeature.Select, PolicyEffect.Allow,
            Array.Empty<DwSubject>(), null, DwTier.Strict, dryRun: false);

        Assert.Null(use.ErrorCode);
        Assert.DoesNotContain("FieldDenied", use.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_setting_freezes_and_binds_from_configuration()
    {
        DwPolicyOptions frozen = new();
        frozen.Freeze();

        Assert.Throws<InvalidOperationException>(() => frozen.AuditRefusals = true);

        IConfiguration section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DynamicWhere:Policies:AuditRefusals"] = "true" })
            .Build()
            .GetSection("DynamicWhere:Policies");

        Assert.True(new DwPolicyOptions().Bind(section).AuditRefusals);
        Assert.False(new DwPolicyOptions().AuditRefusals);
    }
}
