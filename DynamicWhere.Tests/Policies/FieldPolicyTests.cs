using DynamicWhere.ex.Enums;
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

    [Fact]
    public void A_policy_requires_a_field_path()
    {
        Dictionary<PolicyFeature, PolicyEffect> effects = new();

        Assert.Throws<ArgumentException>(() =>
            new FieldPolicy(null!, effects, Array.Empty<PolicySource>(), isSealed: false));

        Assert.Throws<ArgumentException>(() =>
            new FieldPolicy(string.Empty, effects, Array.Empty<PolicySource>(), isSealed: false));

        Assert.Throws<ArgumentException>(() =>
            new FieldPolicy(" ", effects, Array.Empty<PolicySource>(), isSealed: false));
    }

    [Fact]
    public void A_policy_requires_effects()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new FieldPolicy("Salary", null!, Array.Empty<PolicySource>(), isSealed: false));
    }

    [Fact]
    public void A_policy_requires_sources()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new FieldPolicy(
                "Salary",
                new Dictionary<PolicyFeature, PolicyEffect>(),
                null!,
                isSealed: false));
    }

    [Fact]
    public void A_policy_with_no_alias_reports_null_rather_than_its_own_path()
    {
        FieldPolicy policy = new(
            "Salary", new Dictionary<PolicyFeature, PolicyEffect>(), Array.Empty<PolicySource>(),
            isSealed: false);

        Assert.Null(policy.Alias);
    }

    [Fact]
    public void A_policy_carries_the_alias_that_won_the_election()
    {
        FieldPolicy policy = new(
            "Customer.Name", new Dictionary<PolicyFeature, PolicyEffect>(),
            Array.Empty<PolicySource>(), isSealed: false, alias: "customer_name");

        Assert.Equal("customer_name", policy.Alias);
    }

    [Fact]
    public void A_policy_with_no_requirement_is_not_required_in_where()
    {
        FieldPolicy policy = new(
            "Salary", new Dictionary<PolicyFeature, PolicyEffect>(), Array.Empty<PolicySource>(),
            isSealed: false);

        Assert.False(policy.IsRequiredInWhere);
        Assert.Null(policy.RequiredOperators);
    }

    [Fact]
    public void A_requirement_with_no_satisfying_operator_is_still_a_requirement()
    {
        // Empty is not absent. A requirement no operator satisfies refuses every query on the type,
        // which is the fail-closed reading of a set that was declared and left empty.
        FieldPolicy policy = new(
            "TenantId", new Dictionary<PolicyFeature, PolicyEffect>(), Array.Empty<PolicySource>(),
            isSealed: false, requiredOperators: Array.Empty<Operator>());

        Assert.True(policy.IsRequiredInWhere);
        Assert.False(policy.SatisfiesRequirement(Operator.Equal));
    }

    [Fact]
    public void A_requirement_is_satisfied_only_by_an_operator_in_its_set()
    {
        FieldPolicy policy = new(
            "TenantId", new Dictionary<PolicyFeature, PolicyEffect>(), Array.Empty<PolicySource>(),
            isSealed: false, requiredOperators: new[] { Operator.Equal, Operator.In });

        Assert.True(policy.SatisfiesRequirement(Operator.Equal));
        Assert.True(policy.SatisfiesRequirement(Operator.In));
        Assert.False(policy.SatisfiesRequirement(Operator.NotEqual));
        Assert.False(policy.SatisfiesRequirement(Operator.GreaterThan));
    }

    [Fact]
    public void Forced_predicates_accumulate_rather_than_electing_one()
    {
        ForcedPredicate low = ForcedPredicate.FromConstant(
            "Age", Operator.GreaterThanOrEqual, DataType.Number, "18");
        ForcedPredicate high = ForcedPredicate.FromConstant(
            "Age", Operator.LessThanOrEqual, DataType.Number, "65");

        FieldPolicy policy = new(
            "Age", new Dictionary<PolicyFeature, PolicyEffect>(), Array.Empty<PolicySource>(),
            isSealed: false, forced: new[] { low, high });

        Assert.Equal(2, policy.ForcedPredicates.Count);
    }

    [Fact]
    public void A_policy_with_no_forced_predicate_reports_an_empty_list_not_null()
    {
        FieldPolicy policy = new(
            "Salary", new Dictionary<PolicyFeature, PolicyEffect>(), Array.Empty<PolicySource>(),
            isSealed: false);

        Assert.Empty(policy.ForcedPredicates);
    }
}
