using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Discovery;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Filter in, sanitized filter and trace out, nothing executed.
/// </summary>
/// <remarks>
/// The point of the endpoint is that an operator can ask "what would this caller's filter actually
/// do" without running it against the data — so a refusal is an answer here rather than an error,
/// and the trace that explains it survives.
/// </remarks>
public class PolicySimulateTests
{
    private static DwPolicyOptions Frozen(DwTier tier = DwTier.Convenience)
    {
        DwPolicyOptions options = new() { Tier = tier };

        options.Freeze();

        return options;
    }

    private static PolicyResolver Resolver(params IDwPolicyProvider[] extra)
    {
        List<IDwPolicyProvider> providers = new() { new AttributePolicyProvider() };

        providers.AddRange(extra);

        return new PolicyResolver(providers);
    }

    private static Filter On(string field, DataType type = DataType.Text) =>
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

    [Fact]
    public void A_filter_that_would_run_comes_back_sanitized()
    {
        PolicySimulation<Filter> result = PolicySimulator.Simulate<DescribedEmployee>(
            On("Name"), new DwPolicyContext(), Frozen(), Resolver());

        Assert.True(result.WouldRun);
        Assert.NotNull(result.Clause);
        Assert.Null(result.Refusal);
    }

    /// <summary>
    /// The caller's own filter is never touched, so an operator can simulate one filter against
    /// several callers and compare the answers.
    /// </summary>
    [Fact]
    public void The_filter_handed_in_is_not_modified()
    {
        Filter original = On("Name");

        PolicySimulation<Filter> result = PolicySimulator.Simulate<DescribedEmployee>(
            original, new DwPolicyContext(), Frozen(), Resolver());

        Assert.NotSame(original, result.Clause);
        Assert.Null(original.Selects);
    }

    /// <summary>
    /// A refusal is the answer, not an exception, and the trace explaining it survives — losing the
    /// trace with the exception would leave the operator the one thing they asked for missing.
    /// </summary>
    [Fact]
    public void A_refused_filter_reports_the_refusal_and_keeps_the_trace()
    {
        PolicySimulation<Filter> result = PolicySimulator.Simulate<SecuredEmployee>(
            On("NationalId"), new DwPolicyContext(), Frozen(), Resolver());

        Assert.False(result.WouldRun);
        Assert.Null(result.Clause);
        Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, result.Refusal!.ErrorCode);
        Assert.NotEmpty(result.Trace.Decisions);
    }

    [Fact]
    public void A_dropped_projection_field_shows_in_the_sanitized_clause()
    {
        Filter filter = new() { Selects = new List<string> { "Name", "Salary" } };

        PolicySimulation<Filter> result = PolicySimulator.Simulate<SecuredEmployee>(
            filter, new DwPolicyContext(), Frozen(), Resolver());

        Assert.True(result.WouldRun);
        Assert.Equal(new[] { "Name" }, result.Clause!.Selects);
    }

    /// <summary>
    /// The predicates the library would add are the whole reason an operator simulates: they are
    /// invisible in the filter that was written and decisive in the rows that come back.
    /// </summary>
    [Fact]
    public void An_injected_predicate_shows_in_the_sanitized_clause()
    {
        DwPolicyContext context = new DwPolicyContext().WithValue("TenantId", 7);

        PolicySimulation<Filter> result = PolicySimulator.Simulate<HiddenTenantLedger>(
            On("Amount", DataType.Number), context, Frozen(), Resolver());

        Assert.True(result.WouldRun);
        Assert.Contains(
            result.Clause!.ConditionGroup!.Conditions!,
            c => c.Field == "TenantId");
    }

    // ---- the audit log stays clean -------------------------------------------------------------

    /// <summary>
    /// A simulation is not an access. Recording one would write the caller into the log kept
    /// precisely to establish which accesses happened, for a read of data that never occurred.
    /// </summary>
    [Fact]
    public void Simulating_records_no_audit_events()
    {
        DwPolicyContext context = new();

        PolicySimulator.Simulate<DescribedEmployee>(
            On("Salary", DataType.Number), context, Frozen(), Resolver());

        Assert.Empty(context.PendingAuditEvents);
    }

    /// <summary>
    /// The copy carries the caller, so the answer is about them and not about nobody.
    /// </summary>
    [Fact]
    public void The_simulation_answers_for_the_caller_who_was_named()
    {
        FakePolicyProvider rules = new FakePolicyProvider()
            .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicUser)
            .OnlyFor("u-1");

        PolicySimulation<Filter> denied = PolicySimulator.Simulate<PlainProduct>(
            On("Name"),
            new DwPolicyContext().WithSubject(DwSubjectKind.User, "u-1"),
            Frozen(),
            Resolver(rules));

        PolicySimulation<Filter> allowed = PolicySimulator.Simulate<PlainProduct>(
            On("Name"),
            new DwPolicyContext().WithSubject(DwSubjectKind.User, "u-2"),
            Frozen(),
            Resolver(rules));

        Assert.False(denied.WouldRun);
        Assert.True(allowed.WouldRun);
    }

    // ---- runtime dispatch ----------------------------------------------------------------------

    /// <summary>
    /// An administrative surface holds a type resolved from a name, so the generic overloads are
    /// unreachable from it.
    /// </summary>
    [Fact]
    public void A_type_known_only_at_runtime_can_be_simulated()
    {
        PolicySimulation<Filter> result = PolicySimulator.Simulate(
            typeof(SecuredEmployee), On("NationalId"), new DwPolicyContext(), Frozen(), Resolver());

        Assert.False(result.WouldRun);
    }

    [Fact]
    public void A_summary_can_be_simulated()
    {
        Summary summary = new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "InternalNotes" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = "Id", Aggregator = Aggregator.Count, Alias = "n" }
                }
            }
        };

        PolicySimulation<Summary> result = PolicySimulator.Simulate(
            typeof(SecuredEmployee), summary, new DwPolicyContext(), Frozen(), Resolver());

        Assert.False(result.WouldRun);
        Assert.Equal(PolicyErrorCode.FieldDeniedForGroup, result.Refusal!.ErrorCode);
    }

    [Fact]
    public void A_clause_this_library_does_not_simulate_is_refused()
    {
        Assert.Throws<ArgumentException>(() => PolicySimulator.Simulate(
            typeof(SecuredEmployee), new PageBy(), new DwPolicyContext(), Frozen(), Resolver()));
    }

    [Fact]
    public void Simulating_nothing_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => PolicySimulator.Simulate(
            null!, On("Name"), new DwPolicyContext(), Frozen(), Resolver()));

        Assert.Throws<ArgumentNullException>(() => PolicySimulator.Simulate<PlainProduct>(
            On("Name"), null!, Frozen(), Resolver()));
    }

    /// <summary>
    /// A simulation reports the posture it simulated under, so an operator comparing tiers can tell
    /// the two answers apart.
    /// </summary>
    [Fact]
    public void The_trace_reports_the_tier_simulated_under()
    {
        PolicySimulation<Filter> result = PolicySimulator.Simulate<DescribedEmployee>(
            On("Name"), new DwPolicyContext(), Frozen(DwTier.Strict), Resolver());

        Assert.Equal(DwTier.Strict, result.Trace.Tier);
    }
}
