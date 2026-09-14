using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.DTOs;

/// <summary>
/// What a fragment says about a field that is not an access decision: how to describe it, which
/// values it accepts, what querying it costs, and whether touching it is recorded.
/// </summary>
/// <remarks>
/// Carried on <see cref="PolicyFragment"/> rather than read off the type directly, because a schema
/// endpoint answers per caller. A runtime rule may relabel a field for one role, and a sealed
/// attribute may refuse to let it — reading attributes straight off the type would answer the same
/// for everybody and ignore that ceiling entirely.
/// <para>
/// Every fact is nullable and every one is elected on its own. Two attributes decorate one property
/// — <c>[DwDescribe]</c> and <c>[DwAllowedValues]</c> — and a single winner-takes-all election
/// between them would let whichever won erase the other. Independent election also means a rule
/// that renames a field keeps the allowed values the attribute declared.
/// </para>
/// <para>
/// Two of the seven are enforcement rather than decoration. <see cref="CostWeight"/> is charged
/// against a budget and <see cref="AuditedFeatures"/> decides whether an access is recorded, so
/// losing either in transit fails open — quietly. Both are round-tripped through
/// <c>PolicyRuleDocument</c> and mutation-checked on their own.
/// </para>
/// </remarks>
public sealed class FieldFacts
{
    /// <summary>
    /// Initializes a set of facts. At least one must be supplied.
    /// </summary>
    /// <param name="label">A short human name for the field, or null.</param>
    /// <param name="description">A longer explanation, or null.</param>
    /// <param name="group">The section a schema endpoint should list the field under, or null.</param>
    /// <param name="order">Where the field sorts within its group, or null.</param>
    /// <param name="allowedValues">
    /// The values a caller may usefully filter for, or null when the field is not enumerable.
    /// Copied, so a mutable attribute array cannot be edited afterwards.
    /// </param>
    /// <param name="costWeight">What one reference to this field costs against the budget, or null.</param>
    /// <param name="auditedFeatures">The features whose use is recorded, or null when none is.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when nothing at all is supplied, when a text fact is blank, when
    /// <paramref name="allowedValues"/> is empty or holds a blank, or when
    /// <paramref name="auditedFeatures"/> names no feature or a feature that does not exist.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="costWeight"/> is negative.
    /// </exception>
    public FieldFacts(
        string? label = null,
        string? description = null,
        string? group = null,
        int? order = null,
        IReadOnlyList<string>? allowedValues = null,
        int? costWeight = null,
        PolicyFeature? auditedFeatures = null)
    {
        Label = Text(label, nameof(label));
        Description = Text(description, nameof(description));
        Group = Text(group, nameof(group));
        Order = order;
        AllowedValues = Values(allowedValues);
        CostWeight = Weight(costWeight);
        AuditedFeatures = Audited(auditedFeatures);

        // A facts object saying nothing is a fragment carrying no decision, no restriction and no
        // description. It would be elected by nothing and reported by nothing, so it can only be a
        // mistake — most likely a caller who meant to set a fact and named the wrong parameter.
        if (!Describes && CostWeight is null && AuditedFeatures is null)
        {
            throw new ArgumentException(
                "A set of field facts must state at least one thing. Supply a label, a description, " +
                "a group, an order, allowed values, a cost weight, or audited features.");
        }
    }

    /// <summary>A short human name for the field, or null when nothing names it.</summary>
    public string? Label { get; }

    /// <summary>A longer explanation, or null when nothing explains it.</summary>
    public string? Description { get; }

    /// <summary>The section a schema endpoint lists the field under, or null.</summary>
    public string? Group { get; }

    /// <summary>Where the field sorts within its group, or null when nothing orders it.</summary>
    public int? Order { get; }

    /// <summary>
    /// The values a caller may usefully filter for, or null when the field is not enumerable.
    /// </summary>
    /// <remarks>
    /// Advisory, not enforcement. It tells a front end what to put in a dropdown; it does not refuse
    /// a filter naming something else, because a value list that drifts from the data would then
    /// start rejecting queries that are perfectly valid. Restricting what a caller may ask for is
    /// <c>[DwOperators]</c>'s job, and refusing a field outright is <c>[DwDeny]</c>'s.
    /// </remarks>
    public IReadOnlyList<string>? AllowedValues { get; }

    /// <summary>
    /// What one reference to this field costs against <c>DwCaps.MaxQueryCost</c>, or null when
    /// nothing weighs it and the standing default applies.
    /// </summary>
    /// <remarks>
    /// Null rather than a default of one, deliberately. "Nobody weighed this field" is a different
    /// statement from "this field weighs one", and only the first can be overridden by a default
    /// the host configures.
    /// </remarks>
    public int? CostWeight { get; }

    /// <summary>
    /// The features whose use of this field is recorded, or null when none is.
    /// </summary>
    public PolicyFeature? AuditedFeatures { get; }

    /// <summary>True when anything here is worth showing a caller.</summary>
    public bool Describes =>
        Label is not null || Description is not null || Group is not null
        || Order is not null || AllowedValues is not null;

    /// <summary>Facts stating only a cost weight.</summary>
    /// <param name="weight">What one reference to the field costs.</param>
    public static FieldFacts ForCost(int weight) => new(costWeight: weight);

    /// <summary>Facts stating only that a field is audited.</summary>
    /// <param name="features">The features whose use is recorded.</param>
    public static FieldFacts ForAudit(PolicyFeature features) => new(auditedFeatures: features);

    /// <summary>Facts stating only a label.</summary>
    /// <param name="label">A short human name for the field.</param>
    public static FieldFacts ForLabel(string label) => new(label: label);

    /// <summary>
    /// Refuses a text fact that is present but blank.
    /// </summary>
    /// <remarks>
    /// A blank label is worse than an absent one: absent leaves a schema endpoint free to fall back
    /// to the field name, where blank makes it advertise a field with no name at all.
    /// </remarks>
    private static string? Text(string? value, string what)
    {
        if (value is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"A blank {what} states nothing. Leave it null instead.", what);
        }

        return value.Trim();
    }

    /// <summary>
    /// Copies an allowed-value list, refusing one that is empty or holds a blank.
    /// </summary>
    /// <remarks>
    /// Empty is refused rather than treated as "no value is permitted", because this list does not
    /// restrict anything — an empty one could only advertise a field with nothing to choose from,
    /// which nobody configures on purpose.
    /// </remarks>
    private static IReadOnlyList<string>? Values(IReadOnlyList<string>? values)
    {
        if (values is null)
        {
            return null;
        }

        if (values.Count == 0)
        {
            throw new ArgumentException(
                "An empty allowed-value list advertises a field with nothing to choose from. Leave " +
                "it null when the field is not enumerable.", nameof(values));
        }

        string[] copy = new string[values.Count];

        for (int i = 0; i < values.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(values[i]))
            {
                throw new ArgumentException(
                    "An allowed value cannot be blank.", nameof(values));
            }

            copy[i] = values[i];
        }

        return copy;
    }

    /// <summary>Refuses a negative weight, and accepts zero.</summary>
    /// <remarks>
    /// Zero is a real answer — a field the budget should not charge for at all — so it is kept.
    /// A negative weight would let a query buy budget back by naming a field, which turns the cap
    /// into something a caller controls.
    /// </remarks>
    private static int? Weight(int? weight)
    {
        if (weight is null)
        {
            return null;
        }

        if (weight < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(weight), weight,
                "A cost weight cannot be negative: a query would buy budget back by naming the field.");
        }

        return weight;
    }

    /// <summary>Refuses an audit that records nothing, or that names a feature that does not exist.</summary>
    /// <remarks>
    /// <see cref="PolicyFeature.None"/> is zero, so an unparsed or defaulted value lands on it —
    /// and an audit of nothing is indistinguishable from no audit at all, which is the reading that
    /// loses records. Refused at the source instead.
    /// </remarks>
    private static PolicyFeature? Audited(PolicyFeature? features)
    {
        if (features is null)
        {
            return null;
        }

        if (features.Value == PolicyFeature.None)
        {
            throw new ArgumentException(
                "An audit must name at least one feature. PolicyFeature.None records nothing, which " +
                "is not distinguishable from no audit at all.", nameof(features));
        }

        if ((features.Value & ~PolicyFeature.All) != 0)
        {
            throw new ArgumentException(
                $"'{features.Value}' names a feature that does not exist.", nameof(features));
        }

        return features;
    }
}
