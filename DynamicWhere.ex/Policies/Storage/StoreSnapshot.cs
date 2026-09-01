using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Storage;

/// <summary>
/// The broad zone of a policy store, held as one immutable object and swapped atomically.
/// </summary>
/// <remarks>
/// A query that begins on version 41 finishes on version 41. The reference is pinned when a context
/// is prepared and read from there for the life of that context, so a refresh landing mid-request
/// cannot produce a filter gated on one version and a projection gated on another.
/// <para>
/// The broad zone is everything but user-level rules: global, tenant, role and custom. It is
/// bounded by the number of subjects an organisation defines, so it is loaded whole. User rules are
/// unbounded — a million users is a million rows — and live in a <see cref="NarrowZone"/> fetched
/// once per caller.
/// </para>
/// <para>
/// Disabled rules are dropped here rather than at resolution. Enablement changes only through a
/// write, and a write bumps the version, so a snapshot can be trusted to have been built against
/// the enablement in force when it loaded. Validity windows are deliberately <em>not</em> resolved
/// here — see <see cref="PolicyRule.AppliesAt"/>.
/// </para>
/// </remarks>
public sealed class StoreSnapshot
{
    private static readonly IReadOnlyList<PolicyRule> None = Array.Empty<PolicyRule>();

    private readonly Dictionary<string, List<PolicyRule>> _byEntity;

    /// <summary>
    /// Initializes a snapshot from the rules a store loaded.
    /// </summary>
    /// <param name="version">The store's version at the moment of the load.</param>
    /// <param name="loadedAt">When the store read the rules, by the store's own clock.</param>
    /// <param name="rules">The broad-zone rules. Disabled ones are dropped.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rules"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="rules"/> contains a null, or a user-level rule.
    /// </exception>
    public StoreSnapshot(long version, DateTimeOffset loadedAt, IEnumerable<PolicyRule> rules)
    {
        if (rules is null)
        {
            throw new ArgumentNullException(nameof(rules));
        }

        Version = version;
        LoadedAt = loadedAt;
        _byEntity = new Dictionary<string, List<PolicyRule>>(StringComparer.OrdinalIgnoreCase);

        int count = 0;

        foreach (PolicyRule rule in rules)
        {
            if (rule is null)
            {
                throw new ArgumentException(
                    "A snapshot cannot hold a null rule. A rule that is not there enforces " +
                    "nothing, and a field no rule speaks to is allowed.", nameof(rules));
            }

            // Refused rather than filtered out. A store that loads user rules into the broad zone
            // is a store with a bug, and silently dropping them here would turn that bug into a
            // user-level denial that quietly stops applying.
            if (!rule.IsBroad)
            {
                throw new ArgumentException(
                    $"Rule {rule.Id} targets {rule.SubjectKind}, which belongs to the narrow zone. " +
                    "Loading user rules into the snapshot would grow it without bound; return them " +
                    "from LoadNarrowAsync instead.", nameof(rules));
            }

            if (!rule.Enabled)
            {
                continue;
            }

            if (!_byEntity.TryGetValue(rule.EntityType, out List<PolicyRule>? bucket))
            {
                bucket = new List<PolicyRule>();
                _byEntity[rule.EntityType] = bucket;
            }

            bucket.Add(rule);
            count++;
        }

        Count = count;
    }

    /// <summary>The store's version this snapshot was loaded at.</summary>
    public long Version { get; }

    /// <summary>
    /// When the store read these rules, by the store's own clock.
    /// </summary>
    /// <remarks>
    /// Diagnostic. The staleness ceiling is <em>not</em> measured from here, because this timestamp
    /// comes from an implementation the library does not control — a database stamping it with its
    /// own server time, for instance. A store clock running behind would only fail closed, but one
    /// running ahead would make every snapshot look fresher than it is and silently extend the
    /// ceiling. <c>StorePolicyProvider</c> stamps its own load time and measures against that.
    /// </remarks>
    public DateTimeOffset LoadedAt { get; }

    /// <summary>How many enabled rules the snapshot holds.</summary>
    public int Count { get; }

    /// <summary>A snapshot holding nothing, at version zero.</summary>
    /// <remarks>
    /// Stands for "no load has succeeded". An empty store is a legitimate state and loads a real
    /// snapshot at a real version; this one is not that, and must not be mistaken for it.
    /// </remarks>
    public static StoreSnapshot Empty { get; } =
        new(version: 0, DateTimeOffset.MinValue, Array.Empty<PolicyRule>());

    /// <summary>
    /// Returns the broad-zone rules addressing one type, in no particular order.
    /// </summary>
    /// <remarks>
    /// Indexed by <see cref="Type.FullName"/>, case-insensitively, matching how
    /// <see cref="PolicyRule"/> stores it. This is an index on immutable data, not a memoized
    /// decision: what a caller is permitted still resolves per caller, every time.
    /// </remarks>
    /// <param name="entityType">The type being queried.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entityType"/> is null.</exception>
    public IReadOnlyList<PolicyRule> For(Type entityType)
    {
        if (entityType is null)
        {
            throw new ArgumentNullException(nameof(entityType));
        }

        string? name = entityType.FullName;

        return name is not null && _byEntity.TryGetValue(name, out List<PolicyRule>? rules)
            ? rules
            : None;
    }
}

/// <summary>
/// The user-level rules for one caller, fetched once per caller rather than once per field.
/// </summary>
/// <remarks>
/// Held on the prepared context alongside the pinned snapshot. The two travel together because the
/// alternative — resolving the narrow zone lazily on the query path — would need I/O inside
/// <c>GetFragments</c>, which is synchronous and documented not to perform any.
/// <para>
/// An empty zone and an absent one are different, and the difference is the whole reason a context
/// must be prepared: empty means this caller has no user rules, and absent means nobody looked.
/// </para>
/// </remarks>
public sealed class NarrowZone
{
    private static readonly IReadOnlyList<PolicyRule> None = Array.Empty<PolicyRule>();

    private readonly Dictionary<string, List<PolicyRule>> _byEntity;

    /// <summary>
    /// Initializes a narrow zone.
    /// </summary>
    /// <param name="version">The store version the rules were read at.</param>
    /// <param name="rules">The user-level rules for one caller. Disabled ones are dropped.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rules"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="rules"/> contains a null, or a rule that is not user-level.
    /// </exception>
    public NarrowZone(long version, IEnumerable<PolicyRule> rules)
    {
        if (rules is null)
        {
            throw new ArgumentNullException(nameof(rules));
        }

        Version = version;
        _byEntity = new Dictionary<string, List<PolicyRule>>(StringComparer.OrdinalIgnoreCase);

        int count = 0;

        foreach (PolicyRule rule in rules)
        {
            if (rule is null)
            {
                throw new ArgumentException(
                    "A narrow zone cannot hold a null rule.", nameof(rules));
            }

            // The mirror of the snapshot's refusal. A broad rule arriving here would be applied
            // twice, and a store returning broad rules from the narrow load is one whose zone split
            // is not doing the work it exists to do.
            if (rule.SubjectKind != DwSubjectKind.User)
            {
                throw new ArgumentException(
                    $"Rule {rule.Id} targets {rule.SubjectKind}, which belongs to the broad zone " +
                    "and is already in the snapshot.", nameof(rules));
            }

            if (!rule.Enabled)
            {
                continue;
            }

            if (!_byEntity.TryGetValue(rule.EntityType, out List<PolicyRule>? bucket))
            {
                bucket = new List<PolicyRule>();
                _byEntity[rule.EntityType] = bucket;
            }

            bucket.Add(rule);
            count++;
        }

        Count = count;
    }

    /// <summary>The store version these rules were read at.</summary>
    public long Version { get; }

    /// <summary>How many enabled rules the zone holds.</summary>
    public int Count { get; }

    /// <summary>
    /// A zone holding nothing — the correct result for a caller with no user-level rules.
    /// </summary>
    public static NarrowZone Empty { get; } = new(version: 0, Array.Empty<PolicyRule>());

    /// <summary>
    /// Returns this caller's user-level rules addressing one type.
    /// </summary>
    /// <param name="entityType">The type being queried.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entityType"/> is null.</exception>
    public IReadOnlyList<PolicyRule> For(Type entityType)
    {
        if (entityType is null)
        {
            throw new ArgumentNullException(nameof(entityType));
        }

        string? name = entityType.FullName;

        return name is not null && _byEntity.TryGetValue(name, out List<PolicyRule>? rules)
            ? rules
            : None;
    }
}
