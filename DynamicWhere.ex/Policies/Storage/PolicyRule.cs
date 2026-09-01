using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Storage;

/// <summary>
/// One rule as a policy store holds it: who it targets, what it addresses, and what it does.
/// </summary>
/// <remarks>
/// This is the store boundary, and the boundary is the security surface. A rule arriving from a
/// database row or a JSON document has every field <em>defaulted</em> rather than absent, and three
/// of the four enumerations involved default to their most permissive member:
/// <list type="bullet">
/// <item><description><see cref="PolicyEffect.Allow"/> is zero, so an unparsed effect is a grant.</description></item>
/// <item><description><see cref="DwSubjectKind.Global"/> is zero, so an unparsed subject applies to everyone.</description></item>
/// <item><description><see cref="PolicyFeature.None"/> is zero, and covers no feature, so an unparsed feature makes the rule inert.</description></item>
/// <item><description><see cref="PolicyLevel"/> has no member at zero at all, so an unparsed level outranks a sealed attribute.</description></item>
/// </list>
/// A record of all zeroes therefore reads as "grant everyone everything, above sealed". Every one
/// of those is refused here, and the level is never accepted in the first place — it is derived
/// from the subject through a mapping with no default arm, so zero is unreachable by construction
/// rather than by a check that a later edit could drop.
/// <para>
/// Immutable, because a snapshot is shared across every request thread that reads it.
/// </para>
/// </remarks>
public sealed class PolicyRule
{
    /// <summary>
    /// Initializes a rule, refusing anything that could not be enforced as written.
    /// </summary>
    /// <param name="subjectKind">The kind of principal this rule targets.</param>
    /// <param name="subjectKey">
    /// The identity within that kind. Required for every kind but
    /// <see cref="DwSubjectKind.Global"/>, which carries none.
    /// </param>
    /// <param name="entityType">
    /// The full name of the type this rule addresses, namespace included.
    /// </param>
    /// <param name="fieldPath">A field path, or <see cref="PolicyFragment.Wildcard"/>.</param>
    /// <param name="features">The features this rule speaks to. At least one.</param>
    /// <param name="effect">What it does to those features.</param>
    /// <param name="priority">Tiebreak within the level. Higher wins.</param>
    /// <param name="enabled">False to keep the rule without applying it.</param>
    /// <param name="validFrom">When the rule begins to apply, or null for immediately.</param>
    /// <param name="validTo">When the rule stops applying, exclusive, or null for never.</param>
    /// <param name="purpose">
    /// A declared purpose the caller must match, or null to apply unconditionally.
    /// </param>
    /// <param name="transform">One stage of the field's transform chain, or null.</param>
    /// <param name="allowedOperators">The operators this rule permits, or null to say nothing.</param>
    /// <param name="alias">The public name this rule gives the field, or null.</param>
    /// <param name="forced">A predicate to add to every query on the type, or null.</param>
    /// <param name="requiredOperators">
    /// The operators satisfying a filtering requirement, or null to require none.
    /// </param>
    /// <param name="id">The rule's identifier, or null to assign a new one.</param>
    /// <param name="createdBy">Who granted this, for the compliance review that will ask.</param>
    /// <param name="createdAt">When it was granted.</param>
    /// <param name="updatedBy">Who last changed it.</param>
    /// <param name="updatedAt">When it was last changed.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="subjectKind"/> or <paramref name="effect"/> is not a member of
    /// its enumeration.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the entity name is blank or carries no namespace, when the field path is blank
    /// or names no segment, when a non-global subject has no identity, when the features are empty
    /// or carry an unknown bit, when the wildcard is given an alias, a requirement or a transform,
    /// or when the validity window closes before it opens.
    /// </exception>
    public PolicyRule(
        DwSubjectKind subjectKind,
        string? subjectKey,
        string entityType,
        string fieldPath,
        PolicyFeature features,
        PolicyEffect effect,
        int priority = 0,
        bool enabled = true,
        DateTimeOffset? validFrom = null,
        DateTimeOffset? validTo = null,
        string? purpose = null,
        TransformStage? transform = null,
        IReadOnlyList<Operator>? allowedOperators = null,
        string? alias = null,
        ForcedPredicate? forced = null,
        IReadOnlyList<Operator>? requiredOperators = null,
        Guid? id = null,
        string? createdBy = null,
        DateTimeOffset? createdAt = null,
        string? updatedBy = null,
        DateTimeOffset? updatedAt = null)
    {
        // Checked before the mapping below rather than relying on it. Two guards on the axis that
        // decides both authority and audience is deliberate: the mapping's own default arm throws,
        // but a later edit that adds a convenient fallback would silently turn an unknown kind into
        // whichever member that arm names.
        if (!Enum.IsDefined(typeof(DwSubjectKind), subjectKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(subjectKind),
                subjectKind,
                "The subject kind is not one this library knows. It decides both which callers a " +
                "rule applies to and how authoritative it is, and the zero value is Global — so a " +
                "kind read from an unmapped column would apply the rule to everyone.");
        }

        // PolicyEffect.Allow is zero. An effect column that is absent, null-coalesced or unparsed
        // therefore reads as a grant, which is the one direction it is never safe to guess in.
        if (!Enum.IsDefined(typeof(PolicyEffect), effect))
        {
            throw new ArgumentOutOfRangeException(
                nameof(effect),
                effect,
                "The effect is not one this library knows. The zero value is Allow, so an effect " +
                "read from an unmapped column would grant access rather than refuse it.");
        }

        // Flags cannot use Enum.IsDefined — Where | Select is 3 and is a member of nothing — so the
        // check is a mask. A bit outside All means the rule was written against a vocabulary this
        // version does not have, and honouring the bits it does recognise would enforce a fraction
        // of what the operator wrote.
        if ((features & ~PolicyFeature.All) != 0)
        {
            throw new ArgumentException(
                $"The features '{features}' include a flag this library does not define. A rule " +
                "written against a newer vocabulary cannot be partly enforced.",
                nameof(features));
        }

        // None covers no feature, so such a rule loses every election in silence. A denial that
        // does nothing is indistinguishable from a denial nobody wrote.
        if (features == PolicyFeature.None)
        {
            throw new ArgumentException(
                "A rule must speak to at least one feature. PolicyFeature.None covers nothing, so " +
                "the rule would be stored, would apply to no query, and would read as enforced.",
                nameof(features));
        }

        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("A rule requires an entity type.", nameof(entityType));
        }

        string type = entityType.Trim();

        // Matching is against Type.FullName. A short name matches nothing, and a rule that matches
        // nothing is a denial that does nothing — so it is refused here rather than stored and
        // quietly ignored. Matching on the short name instead was the alternative and is worse: two
        // namespaces holding an Employee would each answer to the other's rules, and over-applying
        // an Allow is a grant nobody wrote.
        if (!type.Contains('.', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The entity type '{type}' is not a full name. Rules are matched against " +
                "Type.FullName, so a name without its namespace would match nothing and the rule " +
                "would never apply.",
                nameof(entityType));
        }

        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            throw new ArgumentException("A rule requires a field path.", nameof(fieldPath));
        }

        // The same normalizer the fragments and the resolver use. A rule stored under one spelling
        // and looked up under another does not match.
        string path = PolicyFragment.NormalizePath(fieldPath);

        if (path.Length == 0)
        {
            throw new ArgumentException(
                "A rule requires a field path with at least one segment.", nameof(fieldPath));
        }

        if (subjectKind != DwSubjectKind.Global && string.IsNullOrWhiteSpace(subjectKey))
        {
            throw new ArgumentException(
                $"A rule targeting '{subjectKind}' requires a subject key.", nameof(subjectKey));
        }

        bool isWildcard = path == PolicyFragment.Wildcard;

        // PolicyFragment refuses all three at construction, and that refusal stays as the backstop.
        // Refusing here as well is what turns a bad rule into a rejected upsert an operator sees,
        // rather than an exception thrown mid-sweep on someone else's query — or, in a store that
        // caught it, a rule silently dropped from the snapshot.
        if (isWildcard && alias is not null)
        {
            throw new ArgumentException(
                "An alias cannot be attached to the wildcard path: one name cannot stand for every " +
                "field.", nameof(alias));
        }

        if (isWildcard && requiredOperators is not null)
        {
            throw new ArgumentException(
                "A filtering requirement cannot be attached to the wildcard path: it would demand " +
                "a filter on every field of the type.", nameof(requiredOperators));
        }

        if (isWildcard && transform is not null)
        {
            throw new ArgumentException(
                "A transform cannot be attached to the wildcard path: most stages emit text, which " +
                "is not assignable to every field of a type.", nameof(transform));
        }

        // Such a rule can never apply at any instant. Stored, it reads to an operator as a grant
        // that was configured and to an auditor as a control that is in force, and it is neither.
        if (validFrom is not null && validTo is not null && validTo <= validFrom)
        {
            throw new ArgumentException(
                $"The validity window closes at {validTo} and opens at {validFrom}, so the rule " +
                "could never apply.", nameof(validTo));
        }

        Id = id ?? Guid.NewGuid();
        SubjectKind = subjectKind;
        SubjectKey = subjectKind == DwSubjectKind.Global ? null : subjectKey!.Trim();
        Level = LevelOf(subjectKind);
        EntityType = type;
        FieldPath = path;
        Features = features;
        Effect = effect;
        Priority = priority;
        Enabled = enabled;
        ValidFrom = validFrom;
        ValidTo = validTo;
        Purpose = string.IsNullOrWhiteSpace(purpose) ? null : purpose.Trim();
        Transform = transform;
        AllowedOperators = allowedOperators;
        Alias = alias;
        Forced = forced;
        RequiredOperators = requiredOperators;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
        UpdatedBy = updatedBy;
        UpdatedAt = updatedAt;
    }

    /// <summary>The rule's identifier.</summary>
    public Guid Id { get; }

    /// <summary>The kind of principal this rule targets.</summary>
    public DwSubjectKind SubjectKind { get; }

    /// <summary>The identity within that kind, or null for a global rule.</summary>
    public string? SubjectKey { get; }

    /// <summary>
    /// How authoritative this rule is, derived from <see cref="SubjectKind"/>.
    /// </summary>
    /// <remarks>
    /// Never supplied by a caller and never read from a store. That is the whole point: a level
    /// column defaulting to zero would outrank <see cref="PolicyLevel.SealedAttribute"/>, and the
    /// only way to make that unreachable rather than merely guarded is not to have the column.
    /// </remarks>
    public PolicyLevel Level { get; }

    /// <summary>The full name of the type this rule addresses.</summary>
    public string EntityType { get; }

    /// <summary>The field path this rule addresses, or the wildcard.</summary>
    public string FieldPath { get; }

    /// <summary>The features this rule speaks to.</summary>
    public PolicyFeature Features { get; }

    /// <summary>What this rule does to those features.</summary>
    public PolicyEffect Effect { get; }

    /// <summary>Tiebreak within the level. Higher wins.</summary>
    public int Priority { get; }

    /// <summary>False to keep the rule on record without applying it.</summary>
    public bool Enabled { get; }

    /// <summary>When the rule begins to apply, or null for immediately.</summary>
    public DateTimeOffset? ValidFrom { get; }

    /// <summary>When the rule stops applying, exclusive, or null for never.</summary>
    public DateTimeOffset? ValidTo { get; }

    /// <summary>A declared purpose the caller must match, or null to apply unconditionally.</summary>
    public string? Purpose { get; }

    /// <summary>One stage of the field's transform chain, or null.</summary>
    public TransformStage? Transform { get; }

    /// <summary>The operators this rule permits, or null when it says nothing about operators.</summary>
    public IReadOnlyList<Operator>? AllowedOperators { get; }

    /// <summary>The public name this rule gives the field, or null.</summary>
    public string? Alias { get; }

    /// <summary>A predicate to add to every query on the type, or null.</summary>
    public ForcedPredicate? Forced { get; }

    /// <summary>The operators satisfying a filtering requirement, or null.</summary>
    public IReadOnlyList<Operator>? RequiredOperators { get; }

    /// <summary>Who granted this.</summary>
    public string? CreatedBy { get; }

    /// <summary>When it was granted.</summary>
    public DateTimeOffset? CreatedAt { get; }

    /// <summary>Who last changed it.</summary>
    public string? UpdatedBy { get; }

    /// <summary>When it was last changed.</summary>
    public DateTimeOffset? UpdatedAt { get; }

    /// <summary>
    /// True when this rule belongs to the broad zone, which is loaded whole into every snapshot.
    /// </summary>
    /// <remarks>
    /// Everything but a user-level rule. The split exists because user rules are unbounded — a
    /// million users means a million rows — while global, tenant, role and custom rules are bounded
    /// by the number of subjects an organisation defines.
    /// </remarks>
    public bool IsBroad => SubjectKind != DwSubjectKind.User;

    /// <summary>
    /// True when the rule's validity window contains the given instant.
    /// </summary>
    /// <remarks>
    /// Evaluated per query rather than baked into a snapshot at load. A window resolved at load
    /// time expires only when the next refresh succeeds, so a grant that ended at noon would still
    /// be honoured for the whole of <c>MaxSnapshotAge</c> afterwards — which is the spec's own
    /// argument about expiry that depends on something being remembered, one level down.
    /// <para>
    /// The interval is half-open. A grant valid to one o'clock is gone at one o'clock.
    /// </para>
    /// </remarks>
    /// <param name="now">The instant to test.</param>
    public bool AppliesAt(DateTimeOffset now) =>
        (ValidFrom is null || ValidFrom <= now) && (ValidTo is null || now < ValidTo);

    /// <summary>
    /// True when the caller holds the subject this rule targets.
    /// </summary>
    /// <remarks>
    /// Case-insensitive, exactly as <see cref="DwSubject"/> compares. Identities arrive from token
    /// claims whose casing this library does not control, and a rule targeting <c>Role:Manager</c>
    /// that fails to match a caller whose token says <c>manager</c> is a denial that does not apply.
    /// </remarks>
    /// <param name="context">The caller.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    public bool MatchesSubject(DwPolicyContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return SubjectKind == DwSubjectKind.Global
            || context.Identities(SubjectKind).Contains(SubjectKey!, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the rule's declared purpose matches the caller's, or when it declares none.
    /// </summary>
    /// <remarks>
    /// A purpose-bound rule applies only on a match, which makes it a way to narrow a grant — "you
    /// may read this, but only for billing". A denial that must hold regardless is written without
    /// a purpose, because a purpose-bound denial does not apply to a caller who declares nothing.
    /// </remarks>
    /// <param name="context">The caller.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    public bool MatchesPurpose(DwPolicyContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        return Purpose is null
            || string.Equals(Purpose, context.Purpose, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Converts the rule into the fragment the resolver merges.
    /// </summary>
    /// <remarks>
    /// One conversion for every store, so that Redis, a database and the in-memory implementation
    /// cannot be held to different standards — the same reason the alias validator is shared
    /// between attributes and rules.
    /// </remarks>
    public PolicyFragment ToFragment() =>
        new(FieldPath,
            Features,
            Effect,
            Level,
            PolicySource.FromRule(Id.ToString(), Describe()),
            Priority,
            payload: null,
            AllowedOperators,
            Alias,
            Forced,
            RequiredOperators,
            Transform);

    /// <summary>The subject in the form <see cref="DwSubject.ToString"/> writes it.</summary>
    public string Describe() =>
        SubjectKind == DwSubjectKind.Global ? "Global" : $"{SubjectKind}:{SubjectKey}";

    /// <inheritdoc />
    public override string ToString() =>
        $"{Describe()} {EntityType}.{FieldPath} {Features} => {Effect}";

    /// <summary>
    /// Maps a subject onto the precedence level it carries.
    /// </summary>
    /// <remarks>
    /// Exhaustive, with a throwing default arm rather than a convenient fallback. Every arm returns
    /// a dynamic level, so nothing a store holds can reach <see cref="PolicyLevel.SealedAttribute"/>
    /// — the single guarantee the level ordering exists to provide.
    /// </remarks>
    private static PolicyLevel LevelOf(DwSubjectKind kind) => kind switch
    {
        DwSubjectKind.Global => PolicyLevel.DynamicGlobal,
        DwSubjectKind.Tenant => PolicyLevel.DynamicTenant,
        DwSubjectKind.Role => PolicyLevel.DynamicRole,
        DwSubjectKind.User => PolicyLevel.DynamicUser,

        // A caller-defined dimension, resolved at the tenant level, as DwSubjectKind documents.
        DwSubjectKind.Custom => PolicyLevel.DynamicTenant,
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind), kind, "No precedence level is mapped for this subject kind.")
    };
}
