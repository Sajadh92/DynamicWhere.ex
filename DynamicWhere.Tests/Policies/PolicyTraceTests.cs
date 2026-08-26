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
}
