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

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// <c>[DwAudit]</c>, from the field it decorates to the sink that records the access.
/// </summary>
/// <remarks>
/// Both halves are recorded — an access that was allowed and one that was refused. A log holding
/// only refusals answers "who was stopped" and cannot answer "who read this", which is the question
/// an audit of a sensitive field exists to answer.
/// </remarks>
public class PolicyAuditTests
{
    /// <summary>A sink that remembers, and can be told to fail.</summary>
    private sealed class Recorder : IDwAuditSink
    {
        internal List<DwAuditEvent> Written { get; } = new();

        internal int FailAfter { get; set; } = int.MaxValue;

        public ValueTask WriteAsync(DwAuditEvent auditEvent, CancellationToken ct = default)
        {
            if (Written.Count >= FailAfter)
            {
                throw new InvalidOperationException("the sink is down");
            }

            Written.Add(auditEvent);

            return default;
        }
    }

    private static DwPolicyOptions Options(int? maxEvents = null, bool dryRun = false)
    {
        DwPolicyOptions options = new() { DryRun = dryRun };

        if (maxEvents is int max)
        {
            options.Caps.MaxAuditEvents = max;
        }

        options.Freeze();

        return options;
    }

    private static PolicyQueryable<DescribedEmployee> Query(
        DwPolicyContext context, DwPolicyOptions? options = null) =>
        Array.Empty<DescribedEmployee>()
            .AsQueryable()
            .ApplyPolicy(
                context,
                options ?? Options(),
                new PolicyResolver(new[] { new AttributePolicyProvider() }));

    private static Filter Where(string field, DataType type = DataType.Text) =>
        new()
        {
            ConditionGroup = new ConditionGroup
            {
                Sort = 1,
                Conditions =
                {
                    new Condition
                    {
                        Sort = 1,
                        Field = field,
                        DataType = type,
                        Operator = Operator.Equal,
                        Values = { type == DataType.Number ? 1 : "x" }
                    }
                }
            }
        };

    // ---- what is recorded ----------------------------------------------------------------------

    [Fact]
    public void Filtering_on_an_audited_field_is_recorded()
    {
        DwPolicyContext context = new();

        Query(context).ToList(Where("Salary", DataType.Number));

        DwAuditEvent recorded = Assert.Single(context.PendingAuditEvents);

        Assert.Equal("Salary", recorded.FieldPath);
        Assert.Equal(PolicyFeature.Where, recorded.Feature);
        Assert.Equal(typeof(DescribedEmployee).FullName, recorded.EntityType);
    }

    /// <summary>
    /// An allowed access is the one worth recording. A field that is audited and permitted produces
    /// no refusal, no exception and no trace entry — so without this the log is silent about every
    /// successful read of the field it was put there to watch.
    /// </summary>
    [Fact]
    public void An_allowed_access_is_recorded()
    {
        DwPolicyContext context = new();

        Query(context).ToList(Where("Salary", DataType.Number));

        Assert.Equal(PolicyEffect.Allow, Assert.Single(context.PendingAuditEvents).Effect);
    }

    [Fact]
    public void An_unaudited_field_records_nothing()
    {
        DwPolicyContext context = new();

        Query(context).ToList(Where("Name"));

        Assert.Empty(context.PendingAuditEvents);
    }

    /// <summary>
    /// The attribute narrows to Select, so filtering on the field is not recorded and projecting it
    /// is. An audit that fires on every feature regardless would bury the one that matters.
    /// </summary>
    [Fact]
    public void An_audit_narrowed_to_a_feature_ignores_the_others()
    {
        DwPolicyContext filtering = new();

        Query(filtering).ToList(Where("Email"));

        Assert.Empty(filtering.PendingAuditEvents);

        DwPolicyContext projecting = new();

        Query(projecting).ToList(new Filter { Selects = new List<string> { "Email" } });

        Assert.Equal(PolicyFeature.Select, Assert.Single(projecting.PendingAuditEvents).Feature);
    }

    [Fact]
    public void The_caller_is_recorded_beside_the_access()
    {
        DwPolicyContext context = new DwPolicyContext()
            .WithSubject(DwSubjectKind.User, "u-42")
            .WithSubject(DwSubjectKind.Tenant, "acme");

        context.Purpose = "support";

        Query(context).ToList(Where("Salary", DataType.Number));

        DwAuditEvent recorded = Assert.Single(context.PendingAuditEvents);

        Assert.Equal("support", recorded.Purpose);
        Assert.Contains(recorded.Subjects, s => s.Kind == DwSubjectKind.User && s.Identity == "u-42");
        Assert.Contains(recorded.Subjects, s => s.Kind == DwSubjectKind.Tenant && s.Identity == "acme");
    }

    /// <summary>
    /// A dry run changes what the policy does, not what it records. Recording only when enforcing
    /// would leave a canary rollout with no evidence of what it was about to refuse.
    /// </summary>
    [Fact]
    public void A_dry_run_still_records()
    {
        DwPolicyContext context = new() { DryRun = true };

        Query(context).ToList(Where("Salary", DataType.Number));

        DwAuditEvent recorded = Assert.Single(context.PendingAuditEvents);

        Assert.True(recorded.DryRun);
    }

    [Fact]
    public void Two_references_to_one_field_are_recorded_twice()
    {
        DwPolicyContext context = new();

        Filter filter = Where("Salary", DataType.Number);

        filter.Orders = new List<OrderBy>
        {
            new() { Sort = 1, Field = "Salary", Direction = Direction.Ascending }
        };

        Query(context).ToList(filter);

        Assert.Equal(2, context.PendingAuditEvents.Count);
        Assert.Contains(context.PendingAuditEvents, e => e.Feature == PolicyFeature.Where);
        Assert.Contains(context.PendingAuditEvents, e => e.Feature == PolicyFeature.Order);
    }

    /// <summary>
    /// The library resolving a policy on its own behalf is not an access by the caller. Charging a
    /// synthesized projection to the audit log would record every field of the type as read on
    /// every query that named none.
    /// </summary>
    [Fact]
    public void A_synthesized_projection_records_nothing()
    {
        DwPolicyContext context = new();

        Query(context).ToList(new Filter());

        Assert.Empty(context.PendingAuditEvents);
    }

    // ---- the drain -----------------------------------------------------------------------------

    [Fact]
    public async Task Draining_writes_every_event_and_empties_the_buffer()
    {
        DwPolicyContext context = new();
        Recorder sink = new();

        Query(context).ToList(Where("Salary", DataType.Number));

        int written = await DwPolicy.DrainAuditAsync(context, sink);

        Assert.Equal(1, written);
        Assert.Single(sink.Written);
        Assert.Empty(context.PendingAuditEvents);
    }

    [Fact]
    public async Task Draining_an_empty_buffer_does_nothing()
    {
        Recorder sink = new();

        Assert.Equal(0, await DwPolicy.DrainAuditAsync(new DwPolicyContext(), sink));
        Assert.Empty(sink.Written);
    }

    /// <summary>
    /// A sink that fails part way must not swallow the events it never saw. They go back on the
    /// buffer so a retry — or a later drain — still has them, and the failure is rethrown rather
    /// than reported as a successful write.
    /// </summary>
    [Fact]
    public async Task A_failing_sink_keeps_what_it_never_wrote()
    {
        DwPolicyContext context = new();
        Recorder sink = new() { FailAfter = 1 };

        Filter filter = Where("Salary", DataType.Number);

        filter.Orders = new List<OrderBy>
        {
            new() { Sort = 1, Field = "Salary", Direction = Direction.Ascending }
        };

        Query(context).ToList(filter);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DwPolicy.DrainAuditAsync(context, sink).AsTask());

        Assert.Single(sink.Written);
        Assert.Single(context.PendingAuditEvents);
    }

    [Fact]
    public async Task Draining_without_a_sink_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => DwPolicy.DrainAuditAsync(new DwPolicyContext(), null!).AsTask());
    }

    // ---- the buffer's bound --------------------------------------------------------------------

    /// <summary>
    /// Fail closed. If the access cannot be recorded, the access does not happen — an audited field
    /// whose log has quietly stopped being written is the one outcome this attribute exists to make
    /// impossible.
    /// </summary>
    [Fact]
    public void A_full_buffer_refuses_the_query_rather_than_dropping_the_record()
    {
        DwPolicyContext context = new();
        DwPolicyOptions options = Options(maxEvents: 2);

        Filter filter = Where("Salary", DataType.Number);

        filter.Orders = new List<OrderBy>
        {
            new() { Sort = 1, Field = "Salary", Direction = Direction.Ascending }
        };

        filter.Selects = new List<string> { "Salary" };

        PolicyException error =
            Assert.Throws<PolicyException>(() => Query(context, options).ToList(filter));

        Assert.Equal(PolicyErrorCode.CapExceeded, error.ErrorCode);
        Assert.Contains("MaxAuditEvents", error.SourceOrigin);
    }

    [Fact]
    public async Task Draining_makes_room_again()
    {
        DwPolicyContext context = new();
        DwPolicyOptions options = Options(maxEvents: 1);
        Recorder sink = new();

        Query(context, options).ToList(Where("Salary", DataType.Number));

        await DwPolicy.DrainAuditAsync(context, sink);

        Query(context, options).ToList(Where("Salary", DataType.Number));

        Assert.Single(context.PendingAuditEvents);
    }

    [Fact]
    public void An_audit_buffer_below_one_is_refused()
    {
        DwPolicyOptions options = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Caps.MaxAuditEvents = 0);
    }

    // ---- a rule's audit ------------------------------------------------------------------------

    /// <summary>
    /// The same path from a runtime rule rather than an attribute, since an operator switching an
    /// audit on for one role is the reason rules may state facts at all.
    /// </summary>
    [Fact]
    public void A_rule_can_switch_auditing_on()
    {
        FakePolicyProvider rules = new FakePolicyProvider(new[]
        {
            new ex.Policies.DTOs.PolicyFragment(
                "Name",
                PolicyFeature.None,
                PolicyEffect.Allow,
                PolicyLevel.DynamicRole,
                ex.Policies.DTOs.PolicySource.FromRule("r-1", "Role=Ops"),
                facts: ex.Policies.DTOs.FieldFacts.ForAudit(PolicyFeature.Where))
        });

        DwPolicyContext context = new();

        Array.Empty<DescribedEmployee>()
            .AsQueryable()
            .ApplyPolicy(
                context,
                Options(),
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider(), rules }))
            .ToList(Where("Name"));

        Assert.Equal("Name", Assert.Single(context.PendingAuditEvents).FieldPath);
    }
}
