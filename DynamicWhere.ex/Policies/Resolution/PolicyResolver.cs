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
    /// <exception cref="ArgumentException">
    /// Thrown when any element of <paramref name="providers"/> is null.
    /// </exception>
    public PolicyResolver(IEnumerable<IDwPolicyProvider> providers)
    {
        if (providers is null)
        {
            throw new ArgumentNullException(nameof(providers));
        }

        List<IDwPolicyProvider> materialized = providers.ToList();

        // Reported here rather than at the first Resolve: a null element has no type to name, so
        // the position it sits at is the only thing that leads back to the mistake.
        for (int index = 0; index < materialized.Count; index++)
        {
            if (materialized[index] is null)
            {
                throw new ArgumentException(
                    $"The policy provider at index {index} is null. A provider that is not there " +
                    "supplies no fragments, and a field with no fragments is allowed.",
                    nameof(providers));
            }
        }

        _providers = materialized;
    }

    /// <summary>
    /// Resolves the policy for one field of one type, for one caller.
    /// </summary>
    /// <param name="entityType">The type being queried.</param>
    /// <param name="fieldPath">The field path, as it appears after alias resolution.</param>
    /// <param name="context">The caller.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="fieldPath"/> is blank, or names no segment once normalized.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="entityType"/> or <paramref name="context"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a provider returns null, or a null fragment, naming that provider.
    /// </exception>
    public FieldPolicy Resolve(Type entityType, string fieldPath, DwPolicyContext context)
    {
        // Both of these are handed straight to every provider. IDwPolicyProvider is public, and
        // returning empty for a null argument is a natural defensive style for an implementation
        // this library did not write — against such a provider a null here yields no fragments at
        // all, turning a sealed Deny into an Allow with nothing thrown.
        if (entityType is null)
        {
            throw new ArgumentNullException(nameof(entityType));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            throw new ArgumentException("A policy lookup requires a field path.", nameof(fieldPath));
        }

        // Normalize once, here, because this is where paths enter the policy system, and normalize
        // through the same routine PolicyFragment uses at construction. Any difference between the
        // two spellings means a lookup fails to match a fragment that targets the same field — and
        // a Deny fragment that fails to match is access granted.
        string path = PolicyFragment.NormalizePath(fieldPath);

        if (path.Length == 0)
        {
            throw new ArgumentException(
                "A policy lookup requires a field path with at least one segment.", nameof(fieldPath));
        }

        List<PolicyFragment> candidates = new();

        foreach (IDwPolicyProvider provider in _providers)
        {
            // A provider that misbehaves fails the lookup closed rather than quietly contributing
            // nothing. Name it: the implementation is not necessarily one this library wrote, and
            // an unattributed NullReferenceException from in here leads nowhere.
            IReadOnlyList<PolicyFragment> fragments = provider.GetFragments(entityType, context)
                ?? throw new InvalidOperationException(
                    $"Policy provider '{provider.GetType().FullName}' returned null from " +
                    "GetFragments. Return an empty list instead: a null result cannot be told " +
                    "apart from a field no provider speaks to, and that field would be allowed.");

            foreach (PolicyFragment fragment in fragments)
            {
                if (fragment is null)
                {
                    throw new InvalidOperationException(
                        $"Policy provider '{provider.GetType().FullName}' returned a null " +
                        "fragment. Every element of the returned list must be a real fragment.");
                }

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
    /// <remarks>
    /// Comparison runs in four passes. Level first: the most authoritative level that supplies any
    /// fragment wins outright, and levels below it are discarded rather than merged. Then
    /// specificity, so a rule naming the field beats a wildcard. Then priority, highest first.
    /// Whatever still ties is settled by effect, where Deny beats Mask and Mask beats Allow — which
    /// is what makes a caller holding two roles fall to the stricter of them.
    /// </remarks>
    private static PolicyFragment? Decide(IReadOnlyList<PolicyFragment> candidates, PolicyFeature feature)
    {
        List<PolicyFragment> speaking = candidates.Where(f => f.Covers(feature)).ToList();

        if (speaking.Count == 0)
        {
            return null;
        }

        PolicyLevel best = speaking.Min(f => f.Level);

        List<PolicyFragment> atLevel = speaking.Where(f => f.Level == best).ToList();

        List<PolicyFragment> exact = atLevel.Where(f => !f.IsWildcard).ToList();

        if (exact.Count > 0)
        {
            atLevel = exact;
        }

        int topPriority = atLevel.Max(f => f.Priority);

        List<PolicyFragment> contenders = atLevel.Where(f => f.Priority == topPriority).ToList();

        PolicyEffect strongest = contenders.Max(f => f.Effect);

        return contenders.First(f => f.Effect == strongest);
    }
}
