using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.DTOs;

/// <summary>
/// The resolved policy for one field of one type, for one caller. Immutable, and the only policy
/// type the enforcement code sees — attributes and runtime rules have already been merged away by
/// the time one of these exists.
/// </summary>
public sealed class FieldPolicy
{
    private readonly IReadOnlyDictionary<PolicyFeature, PolicyEffect> _effects;

    /// <summary>
    /// Initializes a resolved policy.
    /// </summary>
    /// <param name="fieldPath">The field this policy governs.</param>
    /// <param name="effects">The effect decided for each feature. A feature absent here is allowed.</param>
    /// <param name="sources">Every fragment source that contributed, for tracing.</param>
    /// <param name="isSealed">True when a compile-time attribute decided at least one feature absolutely.</param>
    public FieldPolicy(
        string fieldPath,
        IReadOnlyDictionary<PolicyFeature, PolicyEffect> effects,
        IReadOnlyList<PolicySource> sources,
        bool isSealed)
    {
        FieldPath = fieldPath;
        _effects = effects;
        Sources = sources;
        IsSealed = isSealed;
    }

    /// <summary>The field this policy governs.</summary>
    public string FieldPath { get; }

    /// <summary>Every fragment source that contributed to this decision.</summary>
    public IReadOnlyList<PolicySource> Sources { get; }

    /// <summary>True when a compile-time attribute decided at least one feature absolutely.</summary>
    public bool IsSealed { get; }

    /// <summary>
    /// The effect decided for a feature. Features with no fragment resolve to
    /// <see cref="PolicyEffect.Allow"/>.
    /// </summary>
    public PolicyEffect EffectFor(PolicyFeature feature) =>
        _effects.TryGetValue(feature, out PolicyEffect effect) ? effect : PolicyEffect.Allow;

    /// <summary>
    /// True when the feature may proceed. A masked feature still proceeds — the value is
    /// transformed on output, not withheld from the query.
    /// </summary>
    public bool Allows(PolicyFeature feature) => EffectFor(feature) != PolicyEffect.Deny;

    /// <summary>True when the feature proceeds but its output value is transformed.</summary>
    public bool IsMasked(PolicyFeature feature) => EffectFor(feature) == PolicyEffect.Mask;
}
