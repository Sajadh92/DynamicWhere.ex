using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers <see cref="FieldPolicy"/>, the immutable answer the resolver produces and the only
/// policy type enforcement code sees.
/// </summary>
public class FieldPolicyTests
{
    [Fact]
    public void A_policy_built_from_effects_reports_each_feature()
    {
        Dictionary<PolicyFeature, PolicyEffect> effects = new()
        {
            [PolicyFeature.Where] = PolicyEffect.Allow,
            [PolicyFeature.Select] = PolicyEffect.Deny,
            [PolicyFeature.Order] = PolicyEffect.Mask
        };

        FieldPolicy policy = new("Salary", effects, Array.Empty<PolicySource>(), isSealed: false);

        Assert.True(policy.Allows(PolicyFeature.Where));
        Assert.False(policy.Allows(PolicyFeature.Select));
        Assert.True(policy.Allows(PolicyFeature.Order));
        Assert.Equal(PolicyEffect.Mask, policy.EffectFor(PolicyFeature.Order));
    }

    [Fact]
    public void A_feature_with_no_recorded_effect_is_allowed()
    {
        FieldPolicy policy = new(
            "Name",
            new Dictionary<PolicyFeature, PolicyEffect>(),
            Array.Empty<PolicySource>(),
            isSealed: false);

        Assert.True(policy.Allows(PolicyFeature.Group));
        Assert.Equal(PolicyEffect.Allow, policy.EffectFor(PolicyFeature.Group));
    }

    [Fact]
    public void Masked_reports_only_features_whose_effect_is_mask()
    {
        Dictionary<PolicyFeature, PolicyEffect> effects = new()
        {
            [PolicyFeature.Select] = PolicyEffect.Mask,
            [PolicyFeature.Where] = PolicyEffect.Allow
        };

        FieldPolicy policy = new("Salary", effects, Array.Empty<PolicySource>(), isSealed: false);

        Assert.True(policy.IsMasked(PolicyFeature.Select));
        Assert.False(policy.IsMasked(PolicyFeature.Where));
    }

    [Fact]
    public void Sources_are_exposed_for_tracing()
    {
        PolicySource[] sources = { PolicySource.FromAttribute("DwDeniedAttribute", isSealed: true) };

        FieldPolicy policy = new(
            "NationalId",
            new Dictionary<PolicyFeature, PolicyEffect> { [PolicyFeature.Select] = PolicyEffect.Deny },
            sources,
            isSealed: true);

        Assert.True(policy.IsSealed);
        Assert.Single(policy.Sources);
        Assert.Equal("DwDeniedAttribute", policy.Sources[0].Origin);
    }
}
