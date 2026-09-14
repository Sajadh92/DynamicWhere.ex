using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Fragments that carry something without deciding anything: an alias, an operator restriction, a
/// forced predicate, a filtering requirement, and the facts Phase 7 adds.
/// </summary>
/// <remarks>
/// Every one of these rides on a typed property that the resolver elects, intersects or accumulates
/// outside the per-feature contest — so none of them needs to win that contest, and none of them
/// may. An attribute is sealed by default, so a carrier claiming a feature with
/// <see cref="PolicyEffect.Allow"/> would outrank every runtime denial of that feature: decorating
/// a field with <c>[DwAlias]</c> would quietly make it impossible to deny.
/// </remarks>
public class CarrierFragmentTests
{
    private static PolicyEffect Effect(FakePolicyProvider provider, PolicyFeature feature)
    {
        PolicyResolver resolver = new(new[] { provider });

        return resolver
            .Resolve(typeof(PlainProduct), "Name", new DwPolicyContext())
            .EffectFor(feature);
    }

    [Fact]
    public void A_sealed_alias_does_not_block_a_dynamic_deny()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .AddAlias("Name", "public_name", PolicyLevel.SealedAttribute)
            .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        Assert.Equal(PolicyEffect.Deny, Effect(provider, PolicyFeature.Where));
    }

    [Fact]
    public void A_sealed_operator_restriction_does_not_block_a_dynamic_deny()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .AddOperators("Name", PolicyLevel.SealedAttribute, Operator.Equal)
            .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);

        Assert.Equal(PolicyEffect.Deny, Effect(provider, PolicyFeature.Where));
    }

    [Fact]
    public void A_sealed_filtering_requirement_does_not_block_a_dynamic_deny()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .AddRequired("Name", PolicyLevel.SealedAttribute, Operator.Equal)
            .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicTenant);

        Assert.Equal(PolicyEffect.Deny, Effect(provider, PolicyFeature.Where));
    }

    [Fact]
    public void A_sealed_forced_predicate_does_not_block_a_dynamic_deny()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .AddForced(
                ForcedPredicate.FromConstant("Name", Operator.Equal, DataType.Text, "x"),
                PolicyLevel.SealedAttribute)
            .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicUser);

        Assert.Equal(PolicyEffect.Deny, Effect(provider, PolicyFeature.Where));
    }

    /// <summary>
    /// The carrier still applies once it has stopped winning the contest. Removing it from the
    /// election must not remove it from the intersection.
    /// </summary>
    [Fact]
    public void An_operator_restriction_still_narrows_after_it_stops_competing()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .AddOperators("Name", PolicyLevel.SealedAttribute, Operator.Equal, Operator.In);

        PolicyResolver resolver = new(new[] { provider });
        FieldPolicy policy = resolver.Resolve(typeof(PlainProduct), "Name", new DwPolicyContext());

        Assert.Equal(new[] { Operator.Equal, Operator.In }, policy.AllowedOperators);
        Assert.False(policy.AllowsOperator(Operator.Contains));
    }

    [Fact]
    public void An_alias_still_names_the_field_after_it_stops_competing()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .AddAlias("Name", "public_name", PolicyLevel.SealedAttribute);

        PolicyResolver resolver = new(new[] { provider });

        Assert.Equal(
            "public_name",
            resolver.Resolve(typeof(PlainProduct), "Name", new DwPolicyContext()).Alias);
    }

    /// <summary>
    /// A carrier decides nothing, so a field carrying only carriers is left exactly as permissive
    /// as a field carrying nothing at all — not more, and not less.
    /// </summary>
    [Fact]
    public void A_field_carrying_only_carriers_decides_no_feature()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .AddAlias("Name", "public_name", PolicyLevel.SealedAttribute)
            .AddOperators("Name", PolicyLevel.SealedAttribute, Operator.Equal);

        PolicyResolver resolver = new(new[] { provider });
        FieldPolicy policy = resolver.Resolve(typeof(PlainProduct), "Name", new DwPolicyContext());

        Assert.False(policy.IsSealed);
        Assert.Empty(policy.Sources);
    }

    /// <summary>
    /// The one carrier that is an effect keeps competing: masking genuinely decides what happens to
    /// a projection, so a transform stage is not a carrier in this sense.
    /// </summary>
    [Fact]
    public void A_transform_still_decides_the_projection()
    {
        PolicyResolver resolver = new(new[] { new AttributePolicyProvider() });

        FieldPolicy policy =
            resolver.Resolve(typeof(Person), "NationalId", new DwPolicyContext());

        Assert.Equal(PolicyEffect.Mask, policy.EffectFor(PolicyFeature.Select));
    }

    /// <summary>
    /// The whole point, end to end through the real attribute provider: a field decorated only for
    /// discovery is still deniable by a runtime rule.
    /// </summary>
    [Fact]
    public void An_attributed_alias_on_a_real_type_does_not_block_a_rule()
    {
        FakePolicyProvider rules = new FakePolicyProvider()
            .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);

        PolicyResolver resolver = new(new IDwPolicyProvider[] { new AttributePolicyProvider(), rules });

        FieldPolicy policy =
            resolver.Resolve(typeof(AliasedCustomer), "Name", new DwPolicyContext());

        Assert.Equal(PolicyEffect.Deny, policy.EffectFor(PolicyFeature.Where));
        Assert.NotNull(policy.Alias);
    }
}
