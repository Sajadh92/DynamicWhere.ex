using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Resolution;

/// <summary>
/// Merges the fragments supplied by every provider into one immutable <see cref="FieldPolicy"/>.
/// </summary>
/// <remarks>
/// Each feature is decided independently. For one feature, the most authoritative level that
/// supplies any fragment decides it outright; less authoritative levels are discarded rather than
/// merged, which is what makes a sealed attribute absolute and lets a role rule replace an
/// overridable attribute default.
/// </remarks>
public sealed class PolicyResolver
{
    private static readonly PolicyFeature[] Features =
    {
        PolicyFeature.Where, PolicyFeature.Select, PolicyFeature.Order,
        PolicyFeature.Group, PolicyFeature.Aggregate, PolicyFeature.Segment
    };

    private readonly IReadOnlyList<IDwPolicyProvider> _providers;

    /// <summary>
    /// Initializes the resolver.
    /// </summary>
    /// <param name="providers">The fragment sources, in any order.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="providers"/> is null.</exception>
    public PolicyResolver(IEnumerable<IDwPolicyProvider> providers) =>
        _providers = providers?.ToList() ?? throw new ArgumentNullException(nameof(providers));

    /// <summary>
    /// Resolves the policy for one field of one type, for one caller.
    /// </summary>
    /// <param name="entityType">The type being queried.</param>
    /// <param name="fieldPath">The field path, as it appears after alias resolution.</param>
    /// <param name="context">The caller.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="fieldPath"/> is blank.</exception>
    public FieldPolicy Resolve(Type entityType, string fieldPath, DwPolicyContext context)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            throw new ArgumentException("A policy lookup requires a field path.", nameof(fieldPath));
        }

        // Normalize once, here, because this is where paths enter the policy system. PolicyFragment
        // trims its own path at construction, so an untrimmed lookup would fail to match a fragment
        // that targets the same field — and a Deny fragment that fails to match is access granted.
        string path = fieldPath.Trim();

        List<PolicyFragment> candidates = new();

        foreach (IDwPolicyProvider provider in _providers)
        {
            foreach (PolicyFragment fragment in provider.GetFragments(entityType, context))
            {
                if (fragment.Matches(path))
                {
                    candidates.Add(fragment);
                }
            }
        }

        Dictionary<PolicyFeature, PolicyEffect> effects = new();
        List<PolicySource> sources = new();
        bool isSealed = false;

        foreach (PolicyFeature feature in Features)
        {
            PolicyFragment? winner = Decide(candidates, feature);

            if (winner is null)
            {
                continue;
            }

            effects[feature] = winner.Effect;

            if (!sources.Contains(winner.Source))
            {
                sources.Add(winner.Source);
            }

            isSealed |= winner.Level == PolicyLevel.SealedAttribute;
        }

        return new FieldPolicy(path, effects, sources, isSealed);
    }

    /// <summary>
    /// Picks the single fragment that decides one feature, or null when none speaks to it.
    /// </summary>
    private static PolicyFragment? Decide(IReadOnlyList<PolicyFragment> candidates, PolicyFeature feature)
    {
        List<PolicyFragment> speaking = candidates.Where(f => f.Covers(feature)).ToList();

        return speaking.Count == 0 ? null : speaking[0];
    }
}
