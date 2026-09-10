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
    /// <param name="transform">
    /// How this field's value is changed on its way out, or null when it is emitted as stored.
    /// </param>
    /// <param name="facts">
    /// What is known about this field that is not an access decision, or null when nothing is.
    /// Elected fact by fact, so this is an assembled view rather than any one fragment's.
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
        IReadOnlyList<Operator>? requiredOperators = null,
        ValueTransform? transform = null,
        FieldFacts? facts = null)
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
        Transform = transform;
        Facts = facts;
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

    /// <summary>
    /// How this field's value is changed on its way out, or null when it is emitted as stored.
    /// </summary>
    /// <remarks>
    /// Applied after materialization, on a detached object, so filtering and sorting still run
    /// against the real value in SQL while the caller sees the transformed one.
    /// </remarks>
    public ValueTransform? Transform { get; }

    /// <summary>True when this field's value is changed on its way out.</summary>
    public bool IsTransformed => Transform is not null && !Transform.IsEmpty;

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

    /// <summary>
    /// What is known about this field that is not an access decision, or null when nothing is.
    /// </summary>
    /// <remarks>
    /// Assembled fact by fact from every fragment that matched, so a rule that renamed the field
    /// and an attribute that listed its values both contribute. The individual facts are surfaced
    /// as properties below; this is here for a caller who wants them as one object.
    /// </remarks>
    public FieldFacts? Facts { get; }

    /// <summary>A short human name for this field, or null when nothing names it.</summary>
    public string? Label => Facts?.Label;

    /// <summary>A longer explanation of what this field holds, or null when nothing explains it.</summary>
    public string? Description => Facts?.Description;

    /// <summary>The section a schema endpoint lists this field under, or null.</summary>
    public string? Group => Facts?.Group;

    /// <summary>Where this field sorts within its group, or null when nothing orders it.</summary>
    public int? Order => Facts?.Order;

    /// <summary>
    /// The values a caller may usefully filter this field for, or null when it is not enumerable.
    /// </summary>
    /// <remarks>
    /// Advisory. A filter naming a value outside this list is not refused — the list says what is
    /// worth offering, not what is permitted.
    /// </remarks>
    public IReadOnlyList<string>? AllowedValues => Facts?.AllowedValues;

    /// <summary>
    /// What one reference to this field spends against the query budget, or null when nothing
    /// weighed it and the standing default applies.
    /// </summary>
    public int? CostWeight => Facts?.CostWeight;

    /// <summary>The features whose use of this field is recorded, or null when none is.</summary>
    public PolicyFeature? AuditedFeatures => Facts?.AuditedFeatures;

    /// <summary>True when this field is audited for anything at all.</summary>
    public bool IsAudited() => AuditedFeatures is not null;

    /// <summary>
    /// True when using this field for the given feature is recorded.
    /// </summary>
    /// <param name="feature">The feature being used.</param>
    public bool IsAudited(PolicyFeature feature) =>
        AuditedFeatures is PolicyFeature audited && (audited & feature) != 0;
}
