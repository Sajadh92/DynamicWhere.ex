using System.Globalization;
using System.Security.Cryptography;

namespace DynamicWhere.ex.Policies.Tokens;

/// <summary>
/// Makes the tokens every vault hands out.
/// </summary>
/// <remarks>
/// One place, so the three vaults this library ships cannot disagree about what a token is, and so
/// a vault somebody else writes has something to call rather than a format to guess at.
/// </remarks>
public static class DwToken
{
    /// <summary>How many random bytes stand behind one token.</summary>
    /// <remarks>
    /// Sixteen, which is 128 bits. Enough that a deployment will not collide by accident at any
    /// scale it could reach, and enough that a token cannot be guessed by anyone trying.
    /// </remarks>
    private const int Bytes = 16;

    /// <summary>
    /// Builds a fresh token, unrelated to any value.
    /// </summary>
    /// <returns>Thirty-two lowercase hexadecimal characters.</returns>
    /// <remarks>
    /// From a cryptographic source, never <c>Random</c>. A token drawn from a predictable sequence
    /// can be reproduced by anyone who learns the seed, which would put the mapping back within
    /// reach of someone who never read the vault.
    /// <para>
    /// Lowercase hexadecimal, the same character set a hashed mask emits, so a column that switches
    /// from <c>MaskStrategy.Hash</c> to <c>MaskStrategy.Tokenize</c> stays hexadecimal and nothing
    /// downstream has to parse it differently. The width does change: a hash is HMAC-SHA256 and
    /// therefore 64 characters, a token is 16 random bytes and therefore 32, so a column sized for
    /// one is not automatically wide enough for the other.
    /// </para>
    /// </remarks>
    public static string New()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(Bytes);

        return Convert.ToHexString(bytes).ToLower(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Builds the key a vault stores one mapping under.
    /// </summary>
    /// <param name="scope">What the mapping is namespaced by.</param>
    /// <param name="value">The real value.</param>
    /// <returns>An opaque key, safe to use as a dictionary key, a Redis field or a column.</returns>
    /// <remarks>
    /// The scope in the clear and the value hashed, which is what lets an operator see which field
    /// a row belongs to while looking at a vault that does not hold the values themselves. A vault
    /// storing the raw value as its key would be a searchable copy of the column it protects, and
    /// the whole point of moving the mapping out of the algorithm was to have one thing to guard
    /// rather than two.
    /// <para>
    /// Unkeyed, unlike the hash mask. It is not standing in for the value anywhere a caller can
    /// see it: the token is, and the token is random. It is an index into a store that has to be
    /// kept secret, and that is all that protects it: a digest of a value drawn from a small space,
    /// a phone number or a national identifier, is found by hashing every value there is, so
    /// whoever reads a vault keyed this way reads the column it protects. A vault given a key uses
    /// <see cref="KeyFor(string, string, byte[])"/> instead, and reading the store alone then
    /// gives nothing back.
    /// </para>
    /// </remarks>
    public static string KeyFor(string scope, string value)
    {
        // Whitespace as well as empty, matching what MaskStage already refuses. A scope of spaces
        // is one namespace shared by everything that happens to be misconfigured the same way, and
        // it reads in a store as though somebody meant it.
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("A token scope cannot be blank.", nameof(scope));
        }

        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        byte[] digest = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));

        return string.Concat(scope, ":", Convert.ToHexString(digest).ToLower(CultureInfo.InvariantCulture));
    }

    /// <summary>The fewest bytes a vault's key may have.</summary>
    /// <remarks>
    /// Sixteen, the floor the hash mask sets for its salt. A key is what stands between a copy of the
    /// store and the values behind it, and a short one is found the way the values would have been.
    /// </remarks>
    public const int MinimumKeyLength = 16;

    /// <summary>What a keyed mapping's key starts with.</summary>
    /// <remarks>
    /// So the two kinds of key can be told apart in a store that holds both while a deployment moves
    /// from one to the other, by an operator and by a query: <c>WHERE [Key] LIKE 'hmac:%'</c>, or
    /// <c>HSCAN ... MATCH hmac:*</c>.
    /// </remarks>
    public const string KeyedPrefix = "hmac:";

    /// <summary>
    /// Builds the key a vault that holds a secret stores one mapping under.
    /// </summary>
    /// <param name="scope">What the mapping is namespaced by.</param>
    /// <param name="value">The real value.</param>
    /// <param name="key">The vault's secret, at least <see cref="MinimumKeyLength"/> bytes.</param>
    /// <returns>
    /// <see cref="KeyedPrefix"/>, the scope in the clear, and an HMAC-SHA256 of the scope and the
    /// value under the key, in lowercase hexadecimal.
    /// </returns>
    /// <remarks>
    /// The unkeyed key is a plain digest of the value, and a tokenized column is nearly always one
    /// whose values come from a small space. Every phone number there is can be hashed in an
    /// afternoon, so a copy of a vault keyed that way, a backup or a replica or a dump, gives back
    /// every value in it and with them the value behind every token ever handed out. Under a key held
    /// outside the store, in configuration or a secret manager, the copy gives back nothing: the
    /// store and the key have to be taken together.
    /// <para>
    /// The scope is inside the digest as well as in front of it, so one value tokenized in two scopes
    /// is two unrelated keys, and the store does not show that two fields hold the same value.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="scope"/> is blank, or <paramref name="key"/> is shorter than
    /// <see cref="MinimumKeyLength"/>.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="value"/> or <paramref name="key"/> is null.
    /// </exception>
    public static string KeyFor(string scope, string value, byte[] key)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("A token scope cannot be blank.", nameof(scope));
        }

        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        RequireKey(key);

        byte[] scoped = System.Text.Encoding.UTF8.GetBytes(scope);
        byte[] valued = System.Text.Encoding.UTF8.GetBytes(value);

        // A zero byte between the two, which neither holds a meaning for, so "ab" + "c" and
        // "a" + "bc" are two inputs rather than one.
        byte[] input = new byte[scoped.Length + 1 + valued.Length];

        scoped.CopyTo(input, 0);
        valued.CopyTo(input, scoped.Length + 1);

        byte[] digest = HMACSHA256.HashData(key, input);

        return string.Concat(
            KeyedPrefix, scope, ":", Convert.ToHexString(digest).ToLower(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Refuses a vault key that is null or too short to be one.
    /// </summary>
    /// <param name="key">The key a vault was given.</param>
    /// <returns>A copy, so a caller clearing or reusing its array does not re-key a running vault.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="key"/> is shorter than <see cref="MinimumKeyLength"/>.
    /// </exception>
    public static byte[] RequireKey(byte[] key)
    {
        if (key is null)
        {
            throw new ArgumentNullException(nameof(key));
        }

        if (key.Length < MinimumKeyLength)
        {
            throw new ArgumentException(
                $"A token vault key must be at least {MinimumKeyLength} bytes. It is what stands between a " +
                "copy of the vault and the values behind every token, and a short one is found the way " +
                "the values would have been.",
                nameof(key));
        }

        return (byte[])key.Clone();
    }
}
