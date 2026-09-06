using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Storage;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.ex.Policies.EntityFrameworkCore;

/// <summary>
/// A policy store held in a relational database, through Entity Framework Core.
/// </summary>
/// <remarks>
/// No raw SQL and no provider-specific API, so one package serves SQL Server, PostgreSQL and any
/// other EF relational provider — design section 5.6. It is held to the same conformance suite as
/// the in-memory and Redis stores.
/// <para>
/// Written against <see cref="DbContext"/> rather than <see cref="DwPolicyDbContext"/>, and
/// reaching its tables through <c>Set&lt;T&gt;</c>, so it serves the shipped context and a
/// consumer's own equally. That is what makes applying the entity configurations to an existing
/// context a real option rather than a documented one.
/// </para>
/// <para>
/// A context is created and disposed per operation, from the factory supplied. The store outlives
/// any request — the background refresh reads it on a timer — so it cannot hold a scoped context of
/// its own.
/// </para>
/// </remarks>
public sealed class EfPolicyStore : IDwPolicyWritableStore
{
    /// <summary>The subject kind that lives in the narrow zone, as the column spells it.</summary>
    private const string UserKind = nameof(DwSubjectKind.User);

    /// <summary>
    /// How many times a write retries when another writer moved the version underneath it.
    /// </summary>
    /// <remarks>
    /// Bounded rather than unbounded: a contended version row should be retried, and a write that
    /// cannot land after a few attempts is a signal, not something to spin on. Exhausting the
    /// budget rethrows, which surfaces to the caller as a failed administrative write rather than
    /// as a policy silently unchanged.
    /// </remarks>
    private const int WriteAttempts = 4;

    private readonly Func<DbContext> _contexts;
    private readonly Func<string, Type?>? _resolveType;

    /// <summary>
    /// Initializes the store.
    /// </summary>
    /// <param name="contexts">
    /// Creates a context whose model carries <see cref="DwPolicyRuleConfiguration"/> and
    /// <see cref="DwPolicyVersionConfiguration"/>. Called once per operation; the store disposes
    /// what it is given.
    /// </param>
    /// <param name="resolveType">
    /// Turns a rule's entity name into a type, so <see cref="UpsertAsync"/> can refuse a rule
    /// targeting a field a sealed attribute already speaks to. Optional.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contexts"/> is null.</exception>
    public EfPolicyStore(Func<DbContext> contexts, Func<string, Type?>? resolveType = null)
    {
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _resolveType = resolveType;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// Thrown when any row cannot be read as written. The load fails rather than skipping it: a
    /// skipped row is a control that is no longer enforced, with the snapshot loaded, the version
    /// advanced and nothing reporting the difference.
    /// </exception>
    public async ValueTask<StoreSnapshot> LoadAsync(CancellationToken ct)
    {
        using DbContext db = _contexts();

        // The version is read *before* the rules, and the order is load-bearing. Read after, a
        // write landing in between would stamp this snapshot with a version newer than the rules it
        // holds — and the poll compares versions, so it would see no change and serve the previous
        // policy indefinitely. Read first, the same race stamps a version older than the rules,
        // which costs one redundant reload and nothing else.
        long version = await ReadVersionAsync(db, ct).ConfigureAwait(false);

        List<DwPolicyRuleRecord> rows = await db.Set<DwPolicyRuleRecord>()
            .AsNoTracking()
            .Where(row => row.SubjectKind != UserKind)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Materialized before the snapshot is built so a row that cannot be read throws here,
        // where it is a load failure, rather than part-way through the snapshot's constructor.
        List<PolicyRule> rules = new(rows.Count);

        foreach (DwPolicyRuleRecord row in rows)
        {
            rules.Add(row.ToRule());
        }

        return new StoreSnapshot(version, DateTimeOffset.UtcNow, rules);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="userIdentities"/> is null.
    /// </exception>
    public async ValueTask<NarrowZone> LoadNarrowAsync(
        IReadOnlyList<string> userIdentities, CancellationToken ct)
    {
        if (userIdentities is null)
        {
            throw new ArgumentNullException(nameof(userIdentities));
        }

        if (userIdentities.Count == 0)
        {
            return NarrowZone.Empty;
        }

        // Matched on the normalized column, never on SubjectKey. A relational '=' honours the
        // column's collation — case-insensitive under SQL Server's default and case-sensitive under
        // PostgreSQL — so comparing the key an operator typed would make the same rule apply on one
        // provider and not the other. PolicyRule.MatchesSubject is OrdinalIgnoreCase, and this is
        // how that survives the trip through a database.
        List<string> wanted = new(userIdentities.Count);

        foreach (string identity in userIdentities)
        {
            string? normalized = DwPolicyRuleRecord.Normalize(identity);

            if (normalized is not null)
            {
                wanted.Add(normalized);
            }
        }

        if (wanted.Count == 0)
        {
            return NarrowZone.Empty;
        }

        using DbContext db = _contexts();

        long version = await ReadVersionAsync(db, ct).ConfigureAwait(false);

        List<DwPolicyRuleRecord> rows = await db.Set<DwPolicyRuleRecord>()
            .AsNoTracking()
            .Where(row => row.SubjectKind == UserKind
                && row.SubjectKeyNormalized != null
                && wanted.Contains(row.SubjectKeyNormalized))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        List<PolicyRule> rules = new(rows.Count);

        foreach (DwPolicyRuleRecord row in rows)
        {
            rules.Add(row.ToRule());
        }

        return new NarrowZone(version, rules);
    }

    /// <inheritdoc />
    public async ValueTask<long> GetVersionAsync(CancellationToken ct)
    {
        using DbContext db = _contexts();

        return await ReadVersionAsync(db, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <returns>
    /// Always null. A database has no notification channel to offer, so the provider falls back to
    /// polling <see cref="GetVersionAsync"/>.
    /// </returns>
    /// <remarks>
    /// Null rather than a stream that never yields, because those are not the same thing to the
    /// caller: an empty stream is indistinguishable from a store that has not changed yet, and the
    /// provider would then watch a channel that can never fire instead of polling. Design section
    /// 5.4 gives the relational case a poll and nothing else, which is why the poll is part of the
    /// contract rather than an optimisation.
    /// </remarks>
    public IAsyncEnumerable<long>? WatchAsync(CancellationToken ct) => null;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the rule targets a sealed field.</exception>
    public async ValueTask<PolicyRule> UpsertAsync(PolicyRule rule, CancellationToken ct)
    {
        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        SealedFields.Refuse(rule, _resolveType, nameof(rule));

        await WriteAsync(
            async (db, token) =>
            {
                DwPolicyRuleRecord fresh = DwPolicyRuleRecord.FromRule(rule);

                DwPolicyRuleRecord? existing = await db.Set<DwPolicyRuleRecord>()
                    .FirstOrDefaultAsync(row => row.Id == rule.Id, token)
                    .ConfigureAwait(false);

                if (existing is null)
                {
                    db.Add(fresh);

                    return;
                }

                db.Entry(existing).CurrentValues.SetValues(fresh);
            },
            ct).ConfigureAwait(false);

        return rule;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The version moves whether or not a row was removed. A caller that deleted a rule someone
    /// else had already deleted still expects the version it reads next to reflect the state it
    /// asked for.
    /// </remarks>
    public ValueTask DeleteAsync(Guid id, CancellationToken ct) =>
        new(WriteAsync(
            async (db, token) =>
            {
                DwPolicyRuleRecord? existing = await db.Set<DwPolicyRuleRecord>()
                    .FirstOrDefaultAsync(row => row.Id == id, token)
                    .ConfigureAwait(false);

                if (existing is not null)
                {
                    db.Remove(existing);
                }
            },
            ct));

    /// <summary>Reads the single-row version, or zero when nothing has been written yet.</summary>
    private static async Task<long> ReadVersionAsync(DbContext db, CancellationToken ct)
    {
        DwPolicyVersionRecord? row = await db.Set<DwPolicyVersionRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == DwPolicyVersionConfiguration.SingleRowId, ct)
            .ConfigureAwait(false);

        // Zero for a database nothing has been written to. That is honest rather than convenient:
        // the first write creates the row at one, so the version still moves forward on the write
        // an instance is waiting to notice.
        return row?.Version ?? 0;
    }

    /// <summary>
    /// Applies a change and moves the version, in one transaction, retrying a lost update.
    /// </summary>
    /// <remarks>
    /// One <c>SaveChanges</c> for both halves, deliberately. Written as two, a process that died
    /// between them would leave a rule stored and the version unmoved — and since the version is
    /// the only thing a database store can notify through, every other instance would keep serving
    /// the previous policy until something unrelated happened to move it. Either both land or
    /// neither does.
    /// <para>
    /// The retry exists because <see cref="DwPolicyVersionRecord.Version"/> is a concurrency token:
    /// two writers that both read 41 do not both get to write 42, and the loser is told rather than
    /// silently overwriting.
    /// </para>
    /// </remarks>
    private async Task<long> WriteAsync(
        Func<DbContext, CancellationToken, Task> change, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            using DbContext db = _contexts();

            await change(db, ct).ConfigureAwait(false);

            DwPolicyVersionRecord? version = await db.Set<DwPolicyVersionRecord>()
                .FirstOrDefaultAsync(v => v.Id == DwPolicyVersionConfiguration.SingleRowId, ct)
                .ConfigureAwait(false);

            if (version is null)
            {
                version = new DwPolicyVersionRecord
                {
                    Id = DwPolicyVersionConfiguration.SingleRowId,
                    Version = 1,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                db.Add(version);
            }
            else
            {
                version.Version++;
                version.UpdatedAt = DateTimeOffset.UtcNow;
            }

            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                return version.Version;
            }
            catch (DbUpdateException) when (attempt < WriteAttempts)
            {
                // Either the token caught a concurrent bump, or two writers raced to create the
                // single row. Both are the same event — somebody else moved the version — and both
                // are resolved by reading it again on a fresh context.
            }
        }
    }
}
