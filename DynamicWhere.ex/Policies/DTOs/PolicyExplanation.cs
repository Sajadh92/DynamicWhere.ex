using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.DTOs;

/// <summary>
/// Why one field resolved the way it did, for one caller: the decision, and the chain behind each
/// feature of it.
/// </summary>
/// <remarks>
/// Produced by the resolver rather than reconstructed by whatever is displaying it. Precedence is
/// four comparison rules applied in order, and a second implementation of them would eventually
/// disagree with the first — leaving an operator a decision chain that contradicts the decision,
/// which is worse than showing no chain at all.
/// </remarks>
public sealed class PolicyExplanation
{
    /// <summary>Initializes an explanation.</summary>
    /// <param name="entityType">The full type name the field belongs to.</param>
    /// <param name="fieldPath">The field, in canonical form.</param>
    /// <param name="policy">The decision being explained.</param>
    /// <param name="features">The chain behind each feature.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public PolicyExplanation(
        string entityType,
        string fieldPath,
        FieldPolicy policy,
        IReadOnlyList<FeatureExplanation> features)
    {
        EntityType = entityType ?? throw new ArgumentNullException(nameof(entityType));
        FieldPath = fieldPath ?? throw new ArgumentNullException(nameof(fieldPath));
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        Features = features ?? throw new ArgumentNullException(nameof(features));
    }

    /// <summary>The full type name the field belongs to.</summary>
    public string EntityType { get; }

    /// <summary>The field, in canonical form.</summary>
    public string FieldPath { get; }

    /// <summary>
    /// The decision being explained — the same object the enforcement path would resolve.
    /// </summary>
    public FieldPolicy Policy { get; }

    /// <summary>The chain behind each feature, one entry per feature.</summary>
    public IReadOnlyList<FeatureExplanation> Features { get; }
}

/// <summary>
/// How one feature of one field was decided: by what, over what, and alongside what.
/// </summary>
public sealed class FeatureExplanation
{
    /// <summary>Initializes the chain behind one feature.</summary>
    /// <param name="feature">The feature.</param>
    /// <param name="effect">What was decided.</param>
    /// <param name="decidedBy">The fragment that decided it, or null when none spoke.</param>
    /// <param name="level">The level it decided at, or null when none spoke.</param>
    /// <param name="tiedWith">Fragments equal to the winner on every pass.</param>
    /// <param name="overrode">Fragments the winner outranked.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="tiedWith"/> or <paramref name="overrode"/> is null.
    /// </exception>
    public FeatureExplanation(
        PolicyFeature feature,
        PolicyEffect effect,
        PolicySource? decidedBy,
        PolicyLevel? level,
        IReadOnlyList<PolicySource> tiedWith,
        IReadOnlyList<PolicySource> overrode)
    {
        Feature = feature;
        Effect = effect;
        DecidedBy = decidedBy;
        Level = level;
        TiedWith = tiedWith ?? throw new ArgumentNullException(nameof(tiedWith));
        Overrode = overrode ?? throw new ArgumentNullException(nameof(overrode));
    }

    /// <summary>The feature this chain explains.</summary>
    public PolicyFeature Feature { get; }

    /// <summary>
    /// What was decided. <see cref="PolicyEffect.Allow"/> when nothing spoke, which is what a
    /// feature with no fragment resolves to.
    /// </summary>
    public PolicyEffect Effect { get; }

    /// <summary>
    /// The fragment credited with the decision, or null when nothing spoke to this feature.
    /// </summary>
    /// <remarks>
    /// Read it with <see cref="IsAttributionAmbiguous"/>. When fragments tie on every pass the
    /// resolver keeps whichever it swept first, so this names one of several equals rather than the
    /// reason.
    /// </remarks>
    public PolicySource? DecidedBy { get; }

    /// <summary>The level the decision was made at, or null when nothing spoke.</summary>
    public PolicyLevel? Level { get; }

    /// <summary>
    /// Fragments equal to the winner on level, specificity, priority and effect.
    /// </summary>
    /// <remarks>
    /// Reported rather than discarded. The decided effect is identical whichever of them is
    /// credited — they are equal in force, which is what tying on all four passes means — but
    /// naming one as "the" reason would credit a rule that contributed no more than its twin, and
    /// an operator deleting that rule would be surprised to find nothing changed.
    /// </remarks>
    public IReadOnlyList<PolicySource> TiedWith { get; }

    /// <summary>
    /// Fragments the winner outranked, which were discarded rather than merged.
    /// </summary>
    /// <remarks>
    /// The reason an operator's rule can appear to do nothing. A level below the winner's is not
    /// blended into the decision at all.
    /// </remarks>
    public IReadOnlyList<PolicySource> Overrode { get; }

    /// <summary>
    /// True when more than one fragment is equally responsible, so <see cref="DecidedBy"/> names
    /// one of several rather than the cause.
    /// </summary>
    public bool IsAttributionAmbiguous => TiedWith.Count > 0;
}
