using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Re-runs the dynamic half of the precedence grid through a real store, rather than through the
/// test-only provider that established it in Phase 1.
/// </summary>
/// <remarks>
/// Phase 1 proved the resolver's ranking exhaustively against fragments a test handed it directly,
/// which is why this phase only has to prove that a store emits the same fragments. That is exactly
/// what this file does, and it is deliberately not a replacement for
/// <see cref="PolicyPrecedenceMatrixTests"/>: two of the six levels are compile-time attributes, and
/// no store can produce one — a rule that could reach <see cref="PolicyLevel.SealedAttribute"/> is
/// the single outcome the whole ordering exists to prevent.
/// <para>
/// The dynamic levels are not chosen by a rule either. They are derived from its subject, so the
/// grid is exercised by writing rules for different subjects and giving the caller those subjects.
/// </para>
/// </remarks>
public class StorePrecedenceTests
{
    private const string StaffType = "DynamicWhere.Tests.Policies.Staff";

    /// <summary>The four dynamic levels, paired with the subject that produces each.</summary>
    public static IEnumerable<object[]> DynamicLevels() =>
        new List<object[]>
        {
            new object[] { DwSubjectKind.User, "alice", PolicyLevel.DynamicUser },
            new object[] { DwSubjectKind.Role, "Manager", PolicyLevel.DynamicRole },
            new object[] { DwSubjectKind.Tenant, "acme", PolicyLevel.DynamicTenant },
            new object[] { DwSubjectKind.Custom, "region-eu", PolicyLevel.DynamicTenant },
            new object[] { DwSubjectKind.Global, null!, PolicyLevel.DynamicGlobal }
        };

    /// <summary>Every ordered pair of subjects whose levels genuinely differ.</summary>
    public static IEnumerable<object[]> SubjectPairs() =>
        from higher in DynamicLevels()
        from lower in DynamicLevels()
        where (PolicyLevel)higher[2] < (PolicyLevel)lower[2]
        select new[] { higher[0], higher[1], lower[0], lower[1] };

    [Theory]
    [MemberData(nameof(DynamicLevels))]
    public async Task A_rule_lands_at_the_level_its_subject_implies(
        DwSubjectKind kind, string? key, PolicyLevel expected)
    {
        (StorePolicyProvider provider, DwPolicyContext context) = await Given(
            new[] { Rule(kind, key, PolicyEffect.Deny) }, (kind, key));

        using (provider)
        {
            Assert.Equal(expected, Assert.Single(provider.GetFragments(typeof(Staff), context)).Level);
        }
    }

    [Theory]
    [MemberData(nameof(SubjectPairs))]
    public async Task The_more_authoritative_subject_decides(
        DwSubjectKind higherKind, string? higherKey, DwSubjectKind lowerKind, string? lowerKey)
    {
        // Both directions of every pair, and the losing rule is given the maximum priority so that
        // a ranking accidentally reading priority before level would be caught rather than hidden.
        foreach (PolicyEffect winning in new[] { PolicyEffect.Allow, PolicyEffect.Mask, PolicyEffect.Deny })
        {
            foreach (PolicyEffect losing in new[] { PolicyEffect.Allow, PolicyEffect.Mask, PolicyEffect.Deny })
            {
                (StorePolicyProvider provider, DwPolicyContext context) = await Given(
                    new[]
                    {
                        Rule(lowerKind, lowerKey, losing, priority: int.MaxValue),
                        Rule(higherKind, higherKey, winning)
                    },
                    (higherKind, higherKey),
                    (lowerKind, lowerKey));

                using (provider)
                {
                    FieldPolicy policy = Resolve(provider, context);

                    Assert.Equal(winning, policy.EffectFor(PolicyFeature.Select));
                }
            }
        }
    }

    [Fact]
    public async Task A_rule_naming_the_field_beats_the_same_subject_wildcard()
    {
        // Specificity, the second ranking pass. It is what lets a blanket denial be relaxed field by
        // field without deleting it.
        (StorePolicyProvider provider, DwPolicyContext context) = await Given(
            new[]
            {
                Rule(DwSubjectKind.Role, "Manager", PolicyEffect.Deny, field: "*"),
                Rule(DwSubjectKind.Role, "Manager", PolicyEffect.Allow)
            },
            (DwSubjectKind.Role, "Manager"));

        using (provider)
        {
            Assert.Equal(
                PolicyEffect.Allow,
                Resolve(provider, context).EffectFor(PolicyFeature.Select));

            // A field the exception does not name is still denied by the wildcard.
            Assert.Equal(
                PolicyEffect.Deny,
                new PolicyResolver(new IDwPolicyProvider[] { provider })
                    .Resolve(typeof(Staff), "Name", context)
                    .EffectFor(PolicyFeature.Select));
        }
    }

    [Fact]
    public async Task Priority_breaks_a_tie_within_one_subject()
    {
        (StorePolicyProvider provider, DwPolicyContext context) = await Given(
            new[]
            {
                Rule(DwSubjectKind.Role, "Manager", PolicyEffect.Deny, priority: 1),
                Rule(DwSubjectKind.Role, "Manager", PolicyEffect.Allow, priority: 2)
            },
            (DwSubjectKind.Role, "Manager"));

        using (provider)
        {
            Assert.Equal(
                PolicyEffect.Allow,
                Resolve(provider, context).EffectFor(PolicyFeature.Select));
        }
    }

    [Fact]
    public async Task Two_roles_of_one_caller_resolve_to_the_stricter()
    {
        // The last ranking pass. A caller holding two roles that disagree lands on the stricter of
        // them, which is what makes holding an extra role never widen access.
        (StorePolicyProvider provider, DwPolicyContext context) = await Given(
            new[]
            {
                Rule(DwSubjectKind.Role, "Manager", PolicyEffect.Allow),
                Rule(DwSubjectKind.Role, "Auditor", PolicyEffect.Deny)
            },
            (DwSubjectKind.Role, "Manager"),
            (DwSubjectKind.Role, "Auditor"));

        using (provider)
        {
            Assert.Equal(
                PolicyEffect.Deny,
                Resolve(provider, context).EffectFor(PolicyFeature.Select));
        }
    }

    /// <summary>Builds a rule on the fixture field.</summary>
    private static PolicyRule Rule(
        DwSubjectKind kind,
        string? key,
        PolicyEffect effect,
        int priority = 0,
        string field = "Department") =>
        new(kind, key, StaffType, field, PolicyFeature.Select, effect, priority: priority);

    /// <summary>Seeds a store, builds a provider, and prepares a context holding the subjects.</summary>
    private static async Task<(StorePolicyProvider Provider, DwPolicyContext Context)> Given(
        PolicyRule[] rules, params (DwSubjectKind Kind, string? Key)[] subjects)
    {
        InMemoryPolicyStore store = new();

        store.Seed(rules);

        DwPolicyOptions options = new();

        options.Freeze();

        StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, options, autoRefresh: false);

        DwPolicyContext context = new();

        foreach ((DwSubjectKind kind, string? key) in subjects)
        {
            if (kind != DwSubjectKind.Global)
            {
                context.WithSubject(kind, key!);
            }
        }

        return (provider, await provider.PrepareAsync(context));
    }

    /// <summary>Resolves the fixture field through the store provider alone.</summary>
    private static FieldPolicy Resolve(StorePolicyProvider provider, DwPolicyContext context) =>
        new PolicyResolver(new IDwPolicyProvider[] { provider })
            .Resolve(typeof(Staff), "Department", context);
}
