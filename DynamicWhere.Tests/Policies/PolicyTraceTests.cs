using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers the decision record the sanitizer emits, which is the only way a caller can tell why a
/// field is missing from a result.
/// </summary>
public class PolicyTraceTests
{
    [Fact]
    public void A_decision_records_what_happened_to_which_field_and_why()
    {
        PolicyDecision decision = new("Salary", PolicyFeature.Select, PolicyAction.Dropped, "DwNoSelectAttribute");

        Assert.Equal("Salary", decision.FieldPath);
        Assert.Equal(PolicyFeature.Select, decision.Feature);
        Assert.Equal(PolicyAction.Dropped, decision.Action);
        Assert.Equal("DwNoSelectAttribute", decision.Reason);
    }

    [Fact]
    public void A_decision_requires_a_field_path()
    {
        Assert.Throws<ArgumentException>(() =>
            new PolicyDecision(" ", PolicyFeature.Select, PolicyAction.Dropped, "why"));
    }

    [Fact]
    public void A_trace_carries_the_posture_the_query_ran_under()
    {
        PolicyTrace trace = new(DwTier.Strict, dryRun: true);

        Assert.Equal(DwTier.Strict, trace.Tier);
        Assert.True(trace.DryRun);
        Assert.Empty(trace.Decisions);
    }

    [Fact]
    public void Decisions_appear_in_the_order_they_were_added()
    {
        PolicyTrace trace = new(DwTier.Convenience, dryRun: false);

        trace.Add(new PolicyDecision("Salary", PolicyFeature.Select, PolicyAction.Dropped, "first"));
        trace.Add(new PolicyDecision("NationalId", PolicyFeature.Where, PolicyAction.Denied, "second"));

        Assert.Collection(
            trace.Decisions,
            d => Assert.Equal("Salary", d.FieldPath),
            d => Assert.Equal("NationalId", d.FieldPath));
    }

    [Fact]
    public void A_trace_rejects_a_null_decision()
    {
        PolicyTrace trace = new(DwTier.Strict, dryRun: false);

        Assert.Throws<ArgumentNullException>(() => trace.Add(null!));
    }

    [Fact]
    public void The_decision_list_cannot_be_cast_back_to_a_mutable_list()
    {
        // The trace is the audit record of the caller's own query. Handing out the backing list
        // would let the subject whose access was refused delete the evidence of the refusal.
        PolicyTrace trace = new(DwTier.Strict, dryRun: false);

        trace.Add(new PolicyDecision("Salary", PolicyFeature.Select, PolicyAction.Dropped, "why"));

        Assert.Null(trace.Decisions as List<PolicyDecision>);

        // A read-only wrapper still implements ICollection<T>, so proving the cast fails is not
        // enough -- prove the mutation does.
        ICollection<PolicyDecision>? mutable = trace.Decisions as ICollection<PolicyDecision>;

        if (mutable is not null)
        {
            Assert.True(mutable.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => mutable.Add(
                new PolicyDecision("Injected", PolicyFeature.Where, PolicyAction.Allowed, null)));
        }
    }
}
