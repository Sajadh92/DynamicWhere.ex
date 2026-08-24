using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers <see cref="PolicyResolver"/> and the models it merges. No database is involved;
/// every test constructs fragments directly or through <see cref="FakePolicyProvider"/>.
/// </summary>
public class PolicyResolutionTests
{
    [Fact]
    public void PolicyFeature_All_covers_every_individual_flag()
    {
        PolicyFeature all = PolicyFeature.All;

        Assert.True(all.HasFlag(PolicyFeature.Where));
        Assert.True(all.HasFlag(PolicyFeature.Select));
        Assert.True(all.HasFlag(PolicyFeature.Order));
        Assert.True(all.HasFlag(PolicyFeature.Group));
        Assert.True(all.HasFlag(PolicyFeature.Aggregate));
        Assert.True(all.HasFlag(PolicyFeature.Segment));
        Assert.Equal(63, (int)all);
    }

    [Fact]
    public void PolicyFeature_flags_are_distinct_powers_of_two()
    {
        int[] singles =
        {
            (int)PolicyFeature.Where, (int)PolicyFeature.Select, (int)PolicyFeature.Order,
            (int)PolicyFeature.Group, (int)PolicyFeature.Aggregate, (int)PolicyFeature.Segment
        };

        Assert.Equal(singles.Length, singles.Distinct().Count());
        Assert.All(singles, v => Assert.Equal(0, v & (v - 1)));
    }

    [Fact]
    public void PolicyEffect_orders_by_authority_so_the_highest_value_wins_a_tie()
    {
        Assert.True(PolicyEffect.Deny > PolicyEffect.Mask);
        Assert.True(PolicyEffect.Mask > PolicyEffect.Allow);
    }

    [Fact]
    public void PolicyLevel_orders_most_authoritative_first()
    {
        Assert.True(PolicyLevel.SealedAttribute < PolicyLevel.DynamicUser);
        Assert.True(PolicyLevel.DynamicUser < PolicyLevel.DynamicRole);
        Assert.True(PolicyLevel.DynamicRole < PolicyLevel.DynamicTenant);
        Assert.True(PolicyLevel.DynamicTenant < PolicyLevel.DynamicGlobal);
        Assert.True(PolicyLevel.DynamicGlobal < PolicyLevel.OverridableAttribute);
    }

    [Fact]
    public void Fake_provider_returns_the_fragments_it_was_given()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        IReadOnlyList<PolicyFragment> fragments =
            provider.GetFragments(typeof(object), new DwPolicyContext());

        Assert.Single(fragments);
        Assert.Equal("Salary", fragments[0].FieldPath);
        Assert.Equal(PolicyLevel.DynamicRole, fragments[0].Level);
    }
}
