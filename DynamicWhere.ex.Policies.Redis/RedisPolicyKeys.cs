using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.ex.Policies.Redis;

/// <summary>
/// Where this store puts things in Redis.
/// </summary>
/// <remarks>
/// One place, because every one of these is a naming axis and a key that does not match is a rule
/// that is not found — which for a denial is access granted. The per-user key is the sharpest of
/// them: Redis matches keys byte for byte, so a rule written under <c>…:user:Alice</c> and fetched
/// under <c>…:user:alice</c> simply is not there, and the caller looks like someone with no user
/// rules at all.
/// <para>
/// The zone split of design section 5.3 is the layout rather than a filter applied afterwards. Broad
/// rules share one hash, bounded by the subjects an organisation defines; each caller's user rules
/// have a hash of their own, so a million users cost one <c>HGETALL</c> of the broad hash at startup
/// and nothing else.
/// </para>
/// </remarks>
internal sealed class RedisPolicyKeys
{
    /// <summary>The prefix every key and the channel share when none is supplied.</summary>
    public const string DefaultPrefix = "dw:policy";

    /// <summary>Builds the layout under a prefix.</summary>
    /// <param name="prefix">The prefix, or null for <see cref="DefaultPrefix"/>.</param>
    public RedisPolicyKeys(string? prefix)
    {
        string root = string.IsNullOrWhiteSpace(prefix) ? DefaultPrefix : prefix!.Trim();

        Prefix = root;
        Broad = $"{root}:rules";
        Owner = $"{root}:owner";
        Version = $"{root}:version";
        Tokens = $"{root}:tokens";

        // The channel design section 5.4 names. A channel and a key of the same text do not
        // collide: Redis keeps the keyspace and the pub/sub namespace apart.
        Channel = $"{root}:version";
    }

    /// <summary>The prefix in force.</summary>
    public string Prefix { get; }

    /// <summary>The hash holding every broad-zone rule, keyed by rule identifier.</summary>
    public string Broad { get; }

    /// <summary>
    /// The hash mapping a rule identifier to the key that holds it.
    /// </summary>
    /// <remarks>
    /// A delete is given an identifier and nothing else, and the rule could be in the broad hash or
    /// in any one of the per-user hashes. Scanning for it would be a keyspace scan on an
    /// administrative write; this index is one field, written in the same transaction as the rule,
    /// so the two cannot disagree about where a rule lives.
    /// </remarks>
    public string Owner { get; }

    /// <summary>The counter every write advances.</summary>
    public string Version { get; }

    /// <summary>The channel a write announces itself on.</summary>
    public string Channel { get; }

    /// <summary>
    /// The hash mapping a scoped value to the token standing in for it.
    /// </summary>
    /// <remarks>
    /// One hash rather than a key per mapping, so the whole vault is one thing to back up, one
    /// thing to move and one thing to delete. It also rules out a per-mapping expiry: a token that
    /// aged out would be reissued under a new one, and a column that quietly changes token is worse
    /// than a hash that never grows.
    /// <para>
    /// Under the same prefix as the rules, so two applications sharing one Redis keep their tokens
    /// apart for exactly the reason they keep their rules apart.
    /// </para>
    /// </remarks>
    public string Tokens { get; }

    /// <summary>
    /// The hash holding one caller's user-level rules.
    /// </summary>
    /// <param name="identity">The identity, in any casing.</param>
    /// <returns>The key, or null when the identity is blank.</returns>
    /// <remarks>
    /// Built from <see cref="PolicyRule.NormalizeSubjectKey"/>, the same normalizer the relational
    /// store's matching column goes through — so a rule written by one store and read by the other
    /// belongs to the same caller.
    /// </remarks>
    public string? User(string? identity)
    {
        string? normalized = PolicyRule.NormalizeSubjectKey(identity);

        return normalized is null ? null : $"{Prefix}:user:{normalized}";
    }
}
