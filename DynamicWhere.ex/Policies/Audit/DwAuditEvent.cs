using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Audit;

/// <summary>
/// One use of one audited field, by one caller, recorded as it happened.
/// </summary>
/// <remarks>
/// Recorded whether or not the access succeeded. A log holding only refusals answers "who was
/// stopped" and cannot answer "who read this" — and the second question is the one an audit of a
/// sensitive field exists to answer.
/// <para>
/// Distinct from <see cref="DTOs.PolicyDecision"/>, which travels with the query result for the
/// caller's own benefit and is discarded with it. An audit event outlives the request: it goes to a
/// sink the caller cannot reach, which is what makes it evidence rather than diagnostics.
/// </para>
/// </remarks>
public sealed class DwAuditEvent
{
    /// <summary>
    /// Initializes an event.
    /// </summary>
    /// <param name="occurredAt">When the access happened.</param>
    /// <param name="entityType">The full name of the type being queried.</param>
    /// <param name="fieldPath">The field, in canonical form.</param>
    /// <param name="feature">What the caller was doing with it.</param>
    /// <param name="effect">What the policy decided for that feature.</param>
    /// <param name="subjects">Who was asking.</param>
    /// <param name="purpose">The declared purpose of the query, or null.</param>
    /// <param name="tier">The enforcement tier in force.</param>
    /// <param name="dryRun">True when nothing this query decided was actually enforced.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="entityType"/> or <paramref name="fieldPath"/> is blank.
    /// </exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="subjects"/> is null.</exception>
    public DwAuditEvent(
        DateTimeOffset occurredAt,
        string entityType,
        string fieldPath,
        PolicyFeature feature,
        PolicyEffect effect,
        IReadOnlyList<DwSubject> subjects,
        string? purpose,
        DwTier tier,
        bool dryRun)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("An audit event requires an entity type.", nameof(entityType));
        }

        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            throw new ArgumentException("An audit event requires a field path.", nameof(fieldPath));
        }

        OccurredAt = occurredAt;
        EntityType = entityType;
        FieldPath = fieldPath;
        Feature = feature;
        Effect = effect;
        Subjects = subjects ?? throw new ArgumentNullException(nameof(subjects));
        Purpose = purpose;
        Tier = tier;
        DryRun = dryRun;
    }

    /// <summary>When the access happened.</summary>
    public DateTimeOffset OccurredAt { get; }

    /// <summary>The full name of the type being queried.</summary>
    public string EntityType { get; }

    /// <summary>
    /// The field, in canonical form rather than the spelling the caller used.
    /// </summary>
    /// <remarks>
    /// Canonical deliberately. An alias varies by caller, so a log keyed on what was typed cannot be
    /// searched for every access to one column — which is the only search anyone runs against it.
    /// </remarks>
    public string FieldPath { get; }

    /// <summary>What the caller was doing with the field.</summary>
    public PolicyFeature Feature { get; }

    /// <summary>
    /// What the policy decided for that feature.
    /// </summary>
    /// <remarks>
    /// The effect that governed the access, not whether the query ultimately ran. A query refused
    /// afterwards on an operator restriction or a budget still touched this field, and the trace
    /// carries that refusal.
    /// </remarks>
    public PolicyEffect Effect { get; }

    /// <summary>Who was asking.</summary>
    public IReadOnlyList<DwSubject> Subjects { get; }

    /// <summary>The declared purpose of the query, or null when none was declared.</summary>
    public string? Purpose { get; }

    /// <summary>The enforcement tier in force.</summary>
    public DwTier Tier { get; }

    /// <summary>
    /// True when nothing this query decided was actually enforced.
    /// </summary>
    /// <remarks>
    /// Recorded rather than suppressed. A canary rollout with no evidence of what it was about to
    /// refuse is a rollout nobody can evaluate.
    /// </remarks>
    public bool DryRun { get; }

    /// <inheritdoc />
    public override string ToString() =>
        $"{OccurredAt:O} {EntityType}.{FieldPath} {Feature} {Effect}"
        + (Subjects.Count == 0 ? string.Empty : $" [{string.Join(", ", Subjects)}]");
}
