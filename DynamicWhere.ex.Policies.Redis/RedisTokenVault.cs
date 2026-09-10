using System.Collections.Concurrent;
using DynamicWhere.ex.Policies.Tokens;
using StackExchange.Redis;

namespace DynamicWhere.ex.Policies.Redis;

/// <summary>
/// A token vault held in Redis, so tokens survive a restart and are shared across instances.
/// </summary>
/// <remarks>
/// The mapping lives in one hash under the same prefix as the policy rules. A token is written once
/// and never rewritten, which is what makes the whole thing cacheable: this vault keeps every
/// mapping it has resolved in process, so a query over ten thousand rows of a hundred distinct
/// values costs a hundred round trips on the first query and none on the next.
/// <para>
/// That cache is safe precisely because a mapping is immutable. Nothing in this library ever
/// changes the token for a value, so a cached answer cannot go stale — unlike a policy rule, which
/// is why the rule store is refreshed and this is not.
/// </para>
/// <para>
/// The store does not own the multiplexer and does not dispose it: a connection to Redis is an
/// application-wide resource, and this is one of its users.
/// </para>
/// <para>
/// Guard this hash as you would guard the column it protects. Reading it turns every token in every
/// result back into the value behind it, which is the trade tokenization makes against hashing: the
/// secret is a store you can lock, move and revoke, rather than a salt sitting in configuration.
/// </para>
/// </remarks>
public sealed class RedisTokenVault : IDwTokenVault
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisPolicyKeys _keys;
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes the vault.
    /// </summary>
    /// <param name="redis">A connected multiplexer. Not owned, and not disposed.</param>
    /// <param name="prefix">
    /// The prefix the hash shares with this application's policy keys, or null for
    /// <c>dw:policy</c>. Two applications sharing one Redis want two prefixes, or a value
    /// tokenized by one is recognisable in the other.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="redis"/> is null.</exception>
    public RedisTokenVault(IConnectionMultiplexer redis, string? prefix = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _keys = new RedisPolicyKeys(prefix);
    }

    /// <summary>How many mappings this instance currently holds in process.</summary>
    /// <remarks>
    /// The size of the cache, never the size of the vault. A fresh instance reports zero while
    /// Redis holds millions.
    /// </remarks>
    public int CachedCount => _cache.Count;

    /// <inheritdoc/>
    /// <exception cref="RedisException">
    /// Thrown when Redis cannot be reached. Deliberately not caught: a vault that answered with a
    /// token it had not stored would hand two callers different stand-ins for one value, and one
    /// that answered with the value would emit exactly what the mask exists to hide.
    /// </exception>
    public string GetOrCreate(string scope, string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        string field = DwToken.KeyFor(scope, value);

        if (_cache.TryGetValue(field, out string? cached))
        {
            return cached;
        }

        IDatabase db = _redis.GetDatabase();

        // Written before it is read, with NotExists, so two instances minting a token for the same
        // value at the same moment cannot both win. The loser's HashSet returns false and it reads
        // back whatever the winner stored, so both callers see one token. Reading first and writing
        // after would leave that race open for exactly as long as the round trip takes.
        string minted = DwToken.New();

        bool won = db.HashSet(_keys.Tokens, field, minted, When.NotExists);

        if (won)
        {
            return _cache.GetOrAdd(field, minted);
        }

        RedisValue existing = db.HashGet(_keys.Tokens, field);

        // Present a moment ago and gone now means somebody emptied the hash underneath this query.
        // Minting a replacement would silently re-token a column mid-result, so the query fails
        // instead and says what happened.
        if (existing.IsNullOrEmpty)
        {
            throw new InvalidOperationException(
                $"The token for a value in scope '{scope}' was written and then could not be read "
                + $"back from '{_keys.Tokens}'. Something is deleting from the token hash while "
                + "queries are running; a token that is reissued is a column whose values stop "
                + "matching the ones already handed out.");
        }

        return _cache.GetOrAdd(field, existing!);
    }

    /// <summary>Forgets every mapping this instance has cached, without touching Redis.</summary>
    /// <remarks>
    /// For a test that wants to prove the vault reads what Redis holds rather than what it
    /// remembers. Calling it costs the next query its round trips and changes no token.
    /// </remarks>
    public void ClearCache() => _cache.Clear();
}
