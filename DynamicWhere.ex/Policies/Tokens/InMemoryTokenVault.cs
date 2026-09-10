using System.Collections.Concurrent;

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

    /// <summary>How many distinct values this vault currently holds a token for.</summary>
    /// <remarks>
    /// Reported so a deployment can see the dictionary growing before it becomes a problem, and so
    /// a test can prove that a repeated value was not re-tokenized.
    /// </remarks>
    public int Count => _tokens.Count;

    /// <inheritdoc/>
    public string GetOrCreate(string scope, string value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        string key = DwToken.KeyFor(scope, value);

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
