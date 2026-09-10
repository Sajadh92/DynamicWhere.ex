using System.Collections.Concurrent;
using DynamicWhere.ex.Policies.Tokens;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.ex.Policies.EntityFrameworkCore;

/// <summary>
/// A token vault held in a relational database, through Entity Framework Core.
/// </summary>
/// <remarks>
/// The durable vault for a deployment that already has a database and would rather not add Redis
/// for this. Tokens survive a restart and are shared by every instance pointed at the same table,
/// which is what makes a tokenized column comparable across time and across processes.
/// <para>
/// No raw SQL and no provider-specific API, so it serves SQL Server, PostgreSQL and any other EF
/// relational provider — the same claim <c>EfPolicyStore</c> makes, and the same reason it can be
/// made.
/// </para>
/// <para>
/// Every mapping this vault resolves is kept in process, because a mapping is immutable: a token is
/// written once and nothing in this library ever rewrites it. So the first query over a column
/// costs one round trip per distinct value and every query after it costs none. Without that cache
/// this would be a database call per value per row, which is the one thing a transform on the query
/// path cannot be.
/// </para>
/// <para>
/// Guard this table as you would guard the column it protects. Reading it turns every token in
/// every result back into the value behind it — that is the trade tokenization makes against
/// hashing, where the secret is a salt in configuration rather than a table you can lock, move and
/// revoke.
/// </para>
/// </remarks>
public sealed class EfTokenVault : IDwTokenVault
{
    private readonly Func<DbContext> _contexts;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes the vault.
    /// </summary>
    /// <param name="contexts">
    /// Creates a context whose model carries <see cref="DwPolicyTokenConfiguration"/>. Called once
    /// per value this vault has not already resolved; the vault disposes what it is given.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contexts"/> is null.</exception>
    public EfTokenVault(Func<DbContext> contexts) =>
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));

    /// <summary>How many mappings this instance currently holds in process.</summary>
    /// <remarks>
    /// The size of the cache, never the size of the table. A fresh instance reports zero while the
    /// database holds millions.
    /// </remarks>
    public int CachedCount => _cache.Count;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a row is written and then cannot be read back, which means something is deleting
    /// from the vault while queries run.
    /// </exception>
    public string GetOrCreate(string scope, string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        string key = DwToken.KeyFor(scope, value);

        if (_cache.TryGetValue(key, out string? cached))
        {
            return cached;
        }

        using DbContext context = _contexts();

        string? stored = Read(context, key);

        if (stored is not null)
        {
            return _cache.GetOrAdd(key, stored);
        }

        string minted = DwToken.New();

        context.Set<DwPolicyTokenRecord>().Add(new DwPolicyTokenRecord
        {
            Key = key,
            Scope = scope,
            Token = minted,
            CreatedAt = DateTimeOffset.UtcNow
        });

        try
        {
            context.SaveChanges();

            return _cache.GetOrAdd(key, minted);
        }
        catch (DbUpdateException)
        {
            // Another writer minted a token for this value between the read and the write, and the
            // primary key refused the second one. That is the key doing its job. The row that
            // landed is the answer for everybody, so it is read back rather than retried — retrying
            // would mint another token and lose the same race again.
            context.ChangeTracker.Clear();

            string? winner = Read(context, key);

            if (winner is null)
            {
                throw new InvalidOperationException(
                    $"A token for a value in scope '{scope}' could not be written and could not be "
                    + $"read back from '{DwPolicyTokenConfiguration.Table}'. Either the table is "
                    + "missing from this context's model, or something is deleting from it while "
                    + "queries are running; a token that is reissued is a column whose values stop "
                    + "matching the ones already handed out.");
            }

            return _cache.GetOrAdd(key, winner);
        }
    }

    /// <summary>Forgets every mapping this instance has cached, without touching the table.</summary>
    /// <remarks>
    /// For a test that wants to prove the vault reads what the database holds rather than what it
    /// remembers. Calling it costs the next query its round trips and changes no token.
    /// </remarks>
    public void ClearCache() => _cache.Clear();

    /// <summary>Reads one token, or null when the value has never been seen in this scope.</summary>
    /// <remarks>
    /// No tracking. The vault never updates a row it has read, and a tracked entity would keep the
    /// whole record alive in a context that exists for one lookup.
    /// </remarks>
    private static string? Read(DbContext context, string key) =>
        context.Set<DwPolicyTokenRecord>()
            .AsNoTracking()
            .Where(row => row.Key == key)
            .Select(row => row.Token)
            .FirstOrDefault();
}
