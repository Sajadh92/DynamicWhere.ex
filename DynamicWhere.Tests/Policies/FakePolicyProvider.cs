using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// A provider that returns exactly the fragments a test hands it. Lets the precedence matrix be
/// exercised at every level before a real policy store exists, and keeps resolver tests free of
/// reflection and storage concerns.
/// </summary>
internal sealed class FakePolicyProvider : IDwPolicyProvider
{
    private readonly List<PolicyFragment> _fragments = new();

    /// <summary>
    /// Adds a fragment.
    /// </summary>
    /// <param name="fieldPath">A field path, or <c>"*"</c> for every field.</param>
    /// <param name="features">The features the fragment speaks to.</param>
    /// <param name="effect">What it does to them.</param>
    /// <param name="level">How authoritative it is.</param>
    /// <param name="priority">Tiebreak within the level. Higher wins.</param>
    /// <returns>This provider, for chaining.</returns>
    public FakePolicyProvider Add(
        string fieldPath,
        PolicyFeature features,
        PolicyEffect effect,
        PolicyLevel level,
        int priority = 0)
    {
        PolicySource source = level is PolicyLevel.SealedAttribute or PolicyLevel.OverridableAttribute
            ? PolicySource.FromAttribute($"Fake{effect}Attribute", level == PolicyLevel.SealedAttribute)
            : PolicySource.FromRule($"{level}-{_fragments.Count}", level.ToString());

        _fragments.Add(new PolicyFragment(fieldPath, features, effect, level, source, priority));

        return this;
    }

    /// <inheritdoc />
    public IReadOnlyList<PolicyFragment> GetFragments(Type entityType, DwPolicyContext context) => _fragments;
}
