using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.ex.Policies.EntityFrameworkCore;

/// <summary>
/// One row of <c>DwPolicyRules</c>: a <see cref="PolicyRule"/> as a relational schema holds it.
/// </summary>
/// <remarks>
/// <b>Deliberately not a <see cref="PolicyRule"/>.</b> Entity Framework materializes an entity by
/// constructing it and setting properties, which would walk straight past every refusal in the
/// rule's constructor — the guards that refuse an undefined effect, an undefined subject kind, an
/// unknown feature bit, an entity name with no namespace, and a validity window that closes before
/// it opens. Those were mutation-checked one at a time in Phase 5 precisely because a rule arriving
/// from a database is what they exist for, so this phase must not be the one that routes around
/// them. <see cref="ToRule"/> runs that constructor on every row of every load.
/// <para>
/// Every enumeration is a string column holding a member's name. An integer column would hand the
/// all-zero row to anything that can write a zero — a migration adding a column with
/// <c>NOT NULL DEFAULT 0</c> is enough — and <see cref="PolicyEffect.Allow"/>,
/// <see cref="DwSubjectKind.Global"/> and <see cref="PolicyFeature.None"/> are all zero, so that row
/// reads as "grant everyone everything, above sealed".
/// </para>
/// </remarks>
public class DwPolicyRuleRecord
{
    /// <summary>The rule's identifier, and the row's key.</summary>
    public Guid Id { get; set; }

    /// <summary>The subject kind, by name.</summary>
    public string SubjectKind { get; set; } = string.Empty;

    /// <summary>
    /// The identity within that kind, exactly as an operator typed it, or null for a global rule.
    /// </summary>
    /// <remarks>
    /// Stored unchanged because it is what Phase 7's <c>/explain</c> shows back to the operator who
    /// wrote it. Matching happens on <see cref="SubjectKeyNormalized"/> instead.
    /// </remarks>
    public string? SubjectKey { get; set; }

    /// <summary>
    /// The identity lowercased, which is the column the narrow load actually filters on.
    /// </summary>
    /// <remarks>
    /// <see cref="PolicyRule.MatchesSubject"/> compares identities with
    /// <see cref="StringComparison.OrdinalIgnoreCase"/>, because they arrive from token claims whose
    /// casing this library does not control. A relational <c>=</c> honours the column's collation
    /// instead — case-insensitive under SQL Server's default, case-sensitive under PostgreSQL — so
    /// the identical rule and the identical query would apply on a developer's SQL Server and not on
    /// the production Postgres. Normalizing on write puts the comparison back under this library's
    /// control, and a user-level denial that fails to match its own user is a denial that does
    /// nothing.
    /// </remarks>
    public string? SubjectKeyNormalized { get; set; }

    /// <summary>The full name of the type the rule addresses.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>The field path the rule addresses, or the wildcard.</summary>
    public string FieldPath { get; set; } = string.Empty;

    /// <summary>The features the rule speaks to, by name — for instance <c>"Where, Select"</c>.</summary>
    public string Features { get; set; } = string.Empty;

    /// <summary>What the rule does to those features, by name.</summary>
    public string Effect { get; set; } = string.Empty;

    /// <summary>Tiebreak within the level. Higher wins.</summary>
    public int Priority { get; set; }

    /// <summary>False to keep the rule on record without applying it.</summary>
    public bool Enabled { get; set; }

    /// <summary>When the rule begins to apply, or null for immediately.</summary>
    public DateTimeOffset? ValidFrom { get; set; }

    /// <summary>When the rule stops applying, exclusive, or null for never.</summary>
    public DateTimeOffset? ValidTo { get; set; }

    /// <summary>A declared purpose the caller must match, or null to apply unconditionally.</summary>
    public string? Purpose { get; set; }

    /// <summary>
    /// The carriers a relational schema has no column for, as JSON, or null when there are none.
    /// </summary>
    /// <remarks>
    /// Design section 5.1 gives a rule one payload column and describes it as a mask spec, a default
    /// value or a bucket step. A rule also carries a forced predicate, an operator restriction, a
    /// filtering requirement and an alias, and three of those four fail <em>open</em> when they are
    /// dropped. They live here, written and read by <see cref="PolicyRuleDocument"/> — the same
    /// routine the Redis store uses, so the two cannot describe a carrier differently.
    /// <para>
    /// Nothing queries by them, which is why they are one column rather than five: section 5.6
    /// indexes a rule by subject and by field path, and neither index would touch these.
    /// </para>
    /// </remarks>
    public string? Detail { get; set; }

    /// <summary>Who granted this, for the compliance review that will ask.</summary>
    public string? CreatedBy { get; set; }

    /// <summary>When it was granted.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Who last changed it.</summary>
    public string? UpdatedBy { get; set; }

    /// <summary>When it was last changed.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Lowercases an identity the way the narrow load will look it up.
    /// </summary>
    /// <param name="key">The identity as it was written, or null.</param>
    /// <returns>The identity in the form the column holds, or null.</returns>
    /// <remarks>
    /// Delegates to <see cref="PolicyRule.NormalizeSubjectKey"/>, which the Redis store uses to
    /// build its per-user key. Two stores normalizing an identity two ways would disagree about
    /// whose rule a row is, and the disagreement would look like a caller who simply has no user
    /// rules.
    /// </remarks>
    public static string? Normalize(string? key) => PolicyRule.NormalizeSubjectKey(key);

    /// <summary>
    /// Builds the row that holds a rule.
    /// </summary>
    /// <param name="rule">The rule to persist.</param>
    /// <returns>The row.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the rule carries a transform a store may not hold.
    /// </exception>
    public static DwPolicyRuleRecord FromRule(PolicyRule rule)
    {
        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        return new DwPolicyRuleRecord
        {
            Id = rule.Id,
            SubjectKind = rule.SubjectKind.ToString(),
            SubjectKey = rule.SubjectKey,
            SubjectKeyNormalized = Normalize(rule.SubjectKey),
            EntityType = rule.EntityType,
            FieldPath = rule.FieldPath,
            Features = rule.Features.ToString(),
            Effect = rule.Effect.ToString(),
            Priority = rule.Priority,
            Enabled = rule.Enabled,
            ValidFrom = rule.ValidFrom,
            ValidTo = rule.ValidTo,
            Purpose = rule.Purpose,
            Detail = PolicyRuleDocument.DetailToJson(rule),
            CreatedBy = rule.CreatedBy,
            CreatedAt = rule.CreatedAt,
            UpdatedBy = rule.UpdatedBy,
            UpdatedAt = rule.UpdatedAt
        };
    }

    /// <summary>
    /// Reads the row back into the rule it holds.
    /// </summary>
    /// <returns>The rule.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when any column cannot be read as written, or when the rule the row describes is one
    /// the boundary refuses.
    /// </exception>
    /// <remarks>
    /// <b>Throws rather than returning null.</b> A row that cannot be read is a control that is no
    /// longer enforced, and skipping it would leave the snapshot loaded, the version advanced and
    /// the instance healthy with one denial quietly gone. Failing the load routes into machinery
    /// that already exists and is already tested: fatal at startup, and afterwards a refresh failure
    /// governed by <c>StoreFailure</c> and bounded by the staleness ceiling.
    /// </remarks>
    public PolicyRule ToRule()
    {
        RuleDetail detail = PolicyRuleDocument.ReadDetail(Detail);

        return new PolicyRule(
            PolicyRuleDocument.ToEnum<DwSubjectKind>(SubjectKind, nameof(SubjectKind)),
            SubjectKey,
            EntityType,
            FieldPath,
            PolicyRuleDocument.ToFeatures(Features),
            PolicyRuleDocument.ToEnum<PolicyEffect>(Effect, nameof(Effect)),
            Priority,
            Enabled,
            ValidFrom,
            ValidTo,
            Purpose,
            detail.Transform,
            detail.AllowedOperators,
            detail.Alias,
            detail.Forced,
            detail.RequiredOperators,
            Id,
            CreatedBy,
            CreatedAt,
            UpdatedBy,
            UpdatedAt);
    }
}

/// <summary>
/// The single row of <c>DwPolicyVersion</c>, bumped on every write.
/// </summary>
/// <remarks>
/// Polled by <c>StorePolicyProvider</c> to notice a change, which is the whole invalidation
/// mechanism for a database store: design section 5.4 gives the relational case no notification
/// channel, so the poll is not an optimisation but the only way an instance learns that policy
/// moved.
/// </remarks>
public class DwPolicyVersionRecord
{
    /// <summary>The row's key. There is one row, and its key is one.</summary>
    public int Id { get; set; }

    /// <summary>
    /// The version, and the concurrency token.
    /// </summary>
    /// <remarks>
    /// A token rather than a plain column because two writers that both read 41 and both write 42
    /// leave a version that did not move for one of them. An instance polling for a change would
    /// see none and keep serving the rules that write withdrew, until something else happened to
    /// move it. As a token the lost update is a <c>DbUpdateConcurrencyException</c> the store
    /// retries.
    /// </remarks>
    public long Version { get; set; }

    /// <summary>When the version last moved.</summary>
    /// <remarks>
    /// Diagnostic only. The staleness ceiling is measured on the provider's own clock, deliberately,
    /// so that a database stamping a row with its own server time cannot extend it.
    /// </remarks>
    public DateTimeOffset UpdatedAt { get; set; }
}
