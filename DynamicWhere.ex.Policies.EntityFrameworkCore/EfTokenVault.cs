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
/// Guard this table as you would guard the column it protects, and give the vault a key. Without
/// one a row's key is a plain digest of the value, and a tokenized value is nearly always drawn from
/// a space small enough to hash whole, so reading the table turns every token in every result back
/// into the value behind it. With one it is an HMAC, and the table and the key have to be taken
/// together. Either way the trade tokenization makes against hashing stands: the mapping is a table
/// you can lock, move and revoke, rather than an algorithm anyone with the salt can run.
/// </para>
/// </remarks>
public sealed class EfTokenVault : IDwTokenVault
{
    private readonly Func<DbContext> _contexts;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly byte[]? _key;
    private readonly bool _retireUnkeyed;

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

    /// <summary>
    /// Initializes a vault that stores its mappings under a key, so a copy of the table gives no value back.
    /// </summary>
    /// <param name="contexts">
    /// Creates a context whose model carries <see cref="DwPolicyTokenConfiguration"/>. Called once
    /// per value this vault has not already resolved; the vault disposes what it is given.
    /// </param>
    /// <param name="key">
    /// The vault's secret, at least <see cref="DwToken.MinimumKeyLength"/> bytes, held where the table
    /// is not: configuration or a secret manager. Every instance sharing the table takes the same one.
    /// </param>
    /// <param name="retireUnkeyed">
    /// True to delete a value's unkeyed row the first time this vault meets the value, once its keyed
    /// row is there, whether this vault wrote that row or found it. Leave it false until every
    /// instance sharing the table has the key: an instance still running without one mints a new
    /// token for a value whose unkeyed row is gone.
    /// </param>
    /// <remarks>
    /// A table that already holds unkeyed rows keeps every token it has issued. A value met for the
    /// first time under the key is looked up under its unkeyed key too, and a token found there is the
    /// one written under the keyed key, so yesterday's export still lines up with today's. The unkeyed
    /// row is left in place until <paramref name="retireUnkeyed"/> says otherwise, and one for a value
    /// never met again stays until an operator removes it: delete the rows whose key does not start
    /// with <c>hmac:</c>, knowing that a value whose only row is removed gets a new token the next
    /// time it is met.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contexts"/> or <paramref name="key"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key"/> is shorter than <see cref="DwToken.MinimumKeyLength"/>.</exception>
    public EfTokenVault(Func<DbContext> contexts, byte[] key, bool retireUnkeyed = false)
        : this(contexts)
    {
        _key = DwToken.RequireKey(key);
        _retireUnkeyed = retireUnkeyed;
    }

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

        string key = _key is null ? DwToken.KeyFor(scope, value) : DwToken.KeyFor(scope, value, _key);

        if (_cache.TryGetValue(key, out string? cached))
        {
            return cached;
        }

        using DbContext context = _contexts();

        string? stored = Read(context, key);

        // Under a key, a value that has an unkeyed row keeps the token that row gave it: the keyed row
        // is written with it rather than with a fresh one, so nothing already handed out stops matching.
        // The unkeyed row is looked for when there is no keyed one to answer, and, by a vault that
        // retires, every time a value is first met, so a row adopted before retiring began goes too.
        string? unkeyedKey = _key is null ? null : DwToken.KeyFor(scope, value);

        string? adopted = unkeyedKey is not null && (stored is null || _retireUnkeyed)
            ? Read(context, unkeyedKey)
            : null;

        if (stored is not null)
        {
            Retire(context, adopted is null ? null : unkeyedKey);

            return _cache.GetOrAdd(key, stored);
        }

        string minted = adopted ?? DwToken.New();

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

            Retire(context, adopted is null ? null : unkeyedKey);

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

            Retire(context, adopted is null ? null : unkeyedKey);

            return _cache.GetOrAdd(key, winner);
        }
    }

    /// <summary>
    /// Deletes a value's unkeyed row once its keyed one is there to answer for it, when the vault was
    /// told every instance can read that one.
    /// </summary>
    /// <param name="context">The context the keyed row was just written or read through.</param>
    /// <param name="unkeyedKey">The unkeyed row's key, or null when the value had none.</param>
    /// <remarks>
    /// Another instance retiring the same row at the same moment deletes it first, and this delete then
    /// touches nothing. That is the row gone, which is what was wanted, so it is not an error.
    /// </remarks>
    private void Retire(DbContext context, string? unkeyedKey)
    {
        if (!_retireUnkeyed || unkeyedKey is null)
        {
            return;
        }

        context.ChangeTracker.Clear();
        context.Set<DwPolicyTokenRecord>().Remove(new DwPolicyTokenRecord { Key = unkeyedKey });

        try
        {
            context.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            context.ChangeTracker.Clear();
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
