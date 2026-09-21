using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace DynamicWhere.ex.Policies.Tokens;

/// <summary>
/// A token vault that lives and dies with the process.
/// </summary>
/// <remarks>
/// The default, and the right one for a test, a single-process tool, or a deployment that only
/// needs a tokenized column to be consistent within one answer. It is the wrong one for anything
/// that joins a tokenized column across time: every restart issues fresh tokens, so yesterday's
/// export and today's cannot be lined up. A deployment that needs that reaches for
/// <c>RedisTokenVault</c> or <c>EfTokenVault</c>, which keep the mapping outside the process.
/// <para>
/// Being wrong for that case is not the same as being unsafe for it. New tokens after a restart
/// disclose nothing; they only stop a comparison working. The failure is visible rather than
/// silent, which is why this is a usable default at all.
/// </para>
/// <para>
/// Unbounded on purpose. A cap would have to evict, an evicted mapping would be reissued under a
/// new token, and a column that quietly changes token halfway through a result is worse than one
/// that holds a large dictionary. A deployment tokenizing enough distinct values for that to matter
/// wants a durable vault regardless.
/// </para>
/// </remarks>
public sealed class InMemoryTokenVault : IDwTokenVault
{
    private readonly ConcurrentDictionary<string, string> _tokens = new(StringComparer.Ordinal);

    /// <summary>The key this vault's mappings are stored under, drawn once and never written down.</summary>
    /// <remarks>
    /// The mappings die with the process, so the key can too, and nothing has to be configured for
    /// it. What it buys is that a memory dump holds keyed digests rather than plain ones of values
    /// that are short enough to be found by trying them all.
    /// </remarks>
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    /// <summary>How many distinct values this vault currently holds a token for.</summary>
    /// <remarks>
    /// Reported so a deployment can see the dictionary growing before it becomes a problem, and so
    /// a test can prove that a repeated value was not re-tokenized.
    /// </remarks>
    public int Count => _tokens.Count;

    /// <summary>The keys the mappings are held under, for the test that none is a plain digest of a value.</summary>
    internal IEnumerable<string> Keys => _tokens.Keys;

    /// <inheritdoc/>
    public string GetOrCreate(string scope, string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        string key = DwToken.KeyFor(scope, value, _key);

        // TryGetValue before GetOrAdd, so the common path — a value already seen — never builds a
        // token it then throws away. GetOrAdd with a factory would call it on every miss of the
        // race, and each of those calls draws from the cryptographic source.
        if (_tokens.TryGetValue(key, out string? existing))
        {
            return existing;
        }

        return _tokens.GetOrAdd(key, DwToken.New());
    }

    /// <summary>Forgets every mapping.</summary>
    /// <remarks>
    /// For a test that needs two runs not to share tokens. Calling it against a live deployment
    /// reissues every token, which breaks any comparison already handed to a caller.
    /// </remarks>
    public void Clear() => _tokens.Clear();
}
