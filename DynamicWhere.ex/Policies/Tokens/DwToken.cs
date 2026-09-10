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
    /// The same shape as a hashed mask on purpose. A column that switches from
    /// <c>MaskStrategy.Hash</c> to <c>MaskStrategy.Tokenize</c> keeps its width and its character
    /// set, so nothing downstream has to be told; and a caller cannot tell from the output which of
    /// the two produced it, which is one fact fewer to reason from.
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
    /// see it: the token is, and the token is random. This hash is an index into a store that is
    /// already secret, so a salt here would protect nothing that reaching the store does not
    /// already give away.
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
}
