using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.DTOs;

/// <summary>
/// The resolved policy for one field of one type, for one caller. It is the only policy type the
/// enforcement code sees — attributes and runtime rules have already been merged away by the time
/// one of these exists. Its own state never changes after construction, but the collections it is
/// handed are stored by reference rather than copied, so it is only as immutable as the caller
/// leaves them. See the constructor for the ownership contract.
/// </summary>
public sealed class FieldPolicy
{
    private readonly IReadOnlyDictionary<PolicyFeature, PolicyEffect> _effects;

    /// <summary>
    /// Initializes a resolved policy. The instance takes ownership of <paramref name="effects"/>
    /// and <paramref name="sources"/>: both are stored by reference, not copied, because the
    /// resolver allocates them fresh for every field of every query and a defensive copy would
    /// double that cost on the hot path. Callers must not retain or mutate either collection
    /// after handing it over.
    /// </summary>
    /// <param name="fieldPath">The field this policy governs.</param>
    /// <param name="effects">The effect decided for each feature. A feature absent here is allowed.</param>
    /// <param name="sources">Every fragment source that contributed, for tracing.</param>
    /// <param name="isSealed">True when a compile-time attribute decided at least one feature absolutely.</param>
    /// <param name="allowedOperators">
    /// The operators permitted when filtering on this field, or null when nothing restricts them.
    /// </param>
    /// <param name="alias">The public name callers may use for this field, or null when it has none.</param>
    /// <param name="forced">
    /// Predicates the library adds to every query on this type, or null when there are none.
    /// </param>
    /// <param name="requiredOperators">
    /// The operators that satisfy a filtering requirement on this field, or null when the caller is
    /// not required to filter on it.
    /// </param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="fieldPath"/> is blank.</exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="effects"/> or <paramref name="sources"/> is null.
    /// </exception>
    public FieldPolicy(
        string fieldPath,
        IReadOnlyDictionary<PolicyFeature, PolicyEffect> effects,
        IReadOnlyList<PolicySource> sources,
        bool isSealed,
        IReadOnlyList<Operator>? allowedOperators = null,
        string? alias = null,
        IReadOnlyList<ForcedPredicate>? forced = null,
        IReadOnlyList<Operator>? requiredOperators = null)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            throw new ArgumentException("A policy requires a field path.", nameof(fieldPath));
        }

        FieldPath = fieldPath;
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        Sources = sources ?? throw new ArgumentNullException(nameof(sources));
        IsSealed = isSealed;
        AllowedOperators = allowedOperators;
        Alias = alias;
        ForcedPredicates = forced ?? Array.Empty<ForcedPredicate>();
        RequiredOperators = requiredOperators;
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

    /// <summary>
    /// The operators permitted when filtering on this field, or null when nothing restricts them.
    /// </summary>
    /// <remarks>
    /// Null and empty mean opposite things. Null is "no fragment spoke to operators", so every
    /// operator is permitted; empty is "the restrictions that applied left nothing", so none is.
    /// </remarks>
    public IReadOnlyList<Operator>? AllowedOperators { get; }

    /// <summary>
    /// True when the operator may be used against this field.
    /// </summary>
    public bool AllowsOperator(Operator op) =>
        AllowedOperators is null || AllowedOperators.Contains(op);

    /// <summary>
    /// The public name callers may use for this field, or null when it has none.
    /// </summary>
    /// <remarks>
    /// An alias adds a spelling and never removes one, so the field path here remains accepted
    /// whether or not this is set. The alias is what a schema endpoint advertises and what a
    /// refusal reports back to a caller who used it.
    /// </remarks>
    public string? Alias { get; }

    /// <summary>
    /// Predicates the library adds to every query on this type. Empty when there are none.
    /// </summary>
    /// <remarks>
    /// Never gated against this policy. These are the library filtering on the caller's behalf, so
    /// a field the caller may not filter on can still carry one — that pairing is the usual shape
    /// of a tenant boundary.
    /// </remarks>
    public IReadOnlyList<ForcedPredicate> ForcedPredicates { get; }

    /// <summary>
    /// The operators that satisfy a filtering requirement on this field, or null when the caller is
    /// not required to filter on it.
    /// </summary>
    /// <remarks>
    /// Null and empty differ, as they do on <see cref="AllowedOperators"/> and for the same reason.
    /// Null is "no requirement"; empty is "a requirement nothing satisfies".
    /// </remarks>
    public IReadOnlyList<Operator>? RequiredOperators { get; }

    /// <summary>True when the caller must filter on this field for the request to proceed.</summary>
    public bool IsRequiredInWhere => RequiredOperators is not null;

    /// <summary>
    /// True when a condition using this operator counts towards the field's filtering requirement.
    /// </summary>
    /// <remarks>
    /// The operator is only half the test. The condition must also be conjunctively binding — in an
    /// <c>And</c> group whose every ancestor is also <c>And</c> — because a requirement satisfied
    /// inside an <c>Or</c> has been met on paper and defeated in fact. The sanitizer owns that half.
    /// </remarks>
    public bool SatisfiesRequirement(Operator op) =>
        RequiredOperators is not null && RequiredOperators.Contains(op);
}
