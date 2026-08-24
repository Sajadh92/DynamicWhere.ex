using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Exercises the full precedence grid rather than a sample of it. Precedence is where a subtle
/// error stays invisible until it leaks data, so every pair of levels is checked in both
/// directions, and every level is checked against every effect.
/// </summary>
public class PolicyPrecedenceMatrixTests
{
    private static readonly PolicyLevel[] Levels =
    {
        PolicyLevel.SealedAttribute,
        PolicyLevel.DynamicUser,
        PolicyLevel.DynamicRole,
        PolicyLevel.DynamicTenant,
        PolicyLevel.DynamicGlobal,
        PolicyLevel.OverridableAttribute
    };

    private static readonly PolicyEffect[] Effects =
    {
        PolicyEffect.Allow,
        PolicyEffect.Mask,
        PolicyEffect.Deny
    };

    private static readonly PolicyFeature[] Features =
    {
        PolicyFeature.Where, PolicyFeature.Select, PolicyFeature.Order,
        PolicyFeature.Group, PolicyFeature.Aggregate, PolicyFeature.Segment
    };

    private static FieldPolicy Resolve(FakePolicyProvider provider) =>
        new PolicyResolver(new[] { provider }).Resolve(typeof(object), "Field", new DwPolicyContext());

    public static IEnumerable<object[]> LevelPairs() =>
        from higher in Levels
        from lower in Levels
        where higher < lower
        select new object[] { higher, lower };

    public static IEnumerable<object[]> LevelsAndEffects() =>
        from level in Levels
        from effect in Effects
        select new object[] { level, effect };

    public static IEnumerable<object[]> EveryFeature() =>
        Features.Select(f => new object[] { f });

    /// <summary>
    /// For every ordered pair of levels, the more authoritative one decides — regardless of which
    /// effect each carries, and regardless of the order they were supplied in.
    /// </summary>
    [Theory]
    [MemberData(nameof(LevelPairs))]
    public void The_more_authoritative_level_always_decides(PolicyLevel higher, PolicyLevel lower)
    {
        foreach (PolicyEffect winning in Effects)
        {
            foreach (PolicyEffect losing in Effects)
            {
                FakePolicyProvider provider = new FakePolicyProvider()
                    .Add("Field", PolicyFeature.Select, losing, lower, priority: 999)
                    .Add("Field", PolicyFeature.Select, winning, higher);

                FieldPolicy policy = Resolve(provider);

                Assert.Equal(winning, policy.EffectFor(PolicyFeature.Select));
            }
        }
    }

    /// <summary>
    /// A single fragment at any level, carrying any effect, produces exactly that effect.
    /// </summary>
    [Theory]
    [MemberData(nameof(LevelsAndEffects))]
    public void A_lone_fragment_decides_its_feature(PolicyLevel level, PolicyEffect effect)
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Field", PolicyFeature.Select, effect, level);

        Assert.Equal(effect, Resolve(provider).EffectFor(PolicyFeature.Select));
    }

    /// <summary>
    /// Every feature resolves independently — a decision on one never leaks onto another.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryFeature))]
    public void Each_feature_resolves_independently(PolicyFeature feature)
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Field", feature, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolve(provider);

        Assert.False(policy.Allows(feature));

        foreach (PolicyFeature other in Features.Where(f => f != feature))
        {
            Assert.True(policy.Allows(other));
        }
    }

    /// <summary>
    /// Within one level, ties fall to the strictest effect present. This is the multi-role case:
    /// a caller holding both a permissive and a restrictive role gets the restrictive one.
    /// </summary>
    [Theory]
    [MemberData(nameof(LevelsAndEffects))]
    public void Ties_within_a_level_fall_to_the_strictest_effect(PolicyLevel level, PolicyEffect effect)
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Field", PolicyFeature.Select, PolicyEffect.Allow, level)
            .Add("Field", PolicyFeature.Select, effect, level);

        PolicyEffect expected = effect > PolicyEffect.Allow ? effect : PolicyEffect.Allow;

        Assert.Equal(expected, Resolve(provider).EffectFor(PolicyFeature.Select));
    }

    /// <summary>
    /// A sealed attribute is never overridden, by any level, effect, or priority.
    /// </summary>
    [Theory]
    [MemberData(nameof(LevelsAndEffects))]
    public void A_sealed_attribute_survives_every_competing_fragment(PolicyLevel level, PolicyEffect effect)
    {
        if (level == PolicyLevel.SealedAttribute)
        {
            return;
        }

        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Field", PolicyFeature.All, PolicyEffect.Deny, PolicyLevel.SealedAttribute)
            .Add("Field", PolicyFeature.All, effect, level, priority: int.MaxValue);

        FieldPolicy policy = Resolve(provider);

        Assert.Equal(PolicyEffect.Deny, policy.EffectFor(PolicyFeature.Select));
        Assert.True(policy.IsSealed);
    }

    /// <summary>
    /// Within one level, the fragment naming the field beats the one addressing every field — at
    /// every level, under every pairing of effects, and whichever order the two arrive in.
    /// </summary>
    /// <remarks>
    /// The pairings that carry the weight are the ones where the wildcard is the stricter of the
    /// two. Specificity is settled before the effect tiebreak, so a broad denial is relaxed field
    /// by field rather than winning on strictness alone; were the two keys swapped, only these
    /// pairings would notice.
    /// </remarks>
    [Theory]
    [MemberData(nameof(LevelsAndEffects))]
    public void An_exact_path_beats_a_wildcard_within_a_level(PolicyLevel level, PolicyEffect exact)
    {
        foreach (PolicyEffect wildcard in Effects)
        {
            FakePolicyProvider wildcardFirst = new FakePolicyProvider()
                .Add(PolicyFragment.Wildcard, PolicyFeature.Select, wildcard, level)
                .Add("Field", PolicyFeature.Select, exact, level);

            Assert.Equal(exact, Resolve(wildcardFirst).EffectFor(PolicyFeature.Select));

            FakePolicyProvider exactFirst = new FakePolicyProvider()
                .Add("Field", PolicyFeature.Select, exact, level)
                .Add(PolicyFragment.Wildcard, PolicyFeature.Select, wildcard, level);

            Assert.Equal(exact, Resolve(exactFirst).EffectFor(PolicyFeature.Select));
        }
    }

    /// <summary>
    /// Specificity only breaks a tie inside a level. For every ordered pair of levels, a wildcard
    /// at the more authoritative one beats a rule naming the field at the weaker one — even when
    /// the weaker rule carries both the stricter effect and a higher priority.
    /// </summary>
    [Theory]
    [MemberData(nameof(LevelPairs))]
    public void The_more_authoritative_level_beats_the_more_specific_path(PolicyLevel higher, PolicyLevel lower)
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Field", PolicyFeature.Select, PolicyEffect.Deny, lower, priority: 999)
            .Add(PolicyFragment.Wildcard, PolicyFeature.Select, PolicyEffect.Allow, higher);

        Assert.Equal(PolicyEffect.Allow, Resolve(provider).EffectFor(PolicyFeature.Select));
    }
}
