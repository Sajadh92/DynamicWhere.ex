using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Masking;

/// <summary>
/// The eight strategies, as pure functions from text to text.
/// </summary>
/// <remarks>
/// No state, no options object, no context: everything a strategy needs arrives in its
/// <see cref="MaskStage"/> or as the salt. That is what lets the whole set be tested exhaustively
/// against every shape of input without a fixture.
/// <para>
/// Every strategy fails closed. A value that does not fit the strategy it was given — an address
/// that is not an address, a partial mask whose kept ends exceed the value's length — is masked in
/// full rather than returned as it came. The alternative is a strategy that silently passes through
/// the value it exists to hide, on exactly the inputs nobody thought to test.
/// </para>
/// </remarks>
internal static class MaskEngine
{
    /// <summary>
    /// How long a mask is when it is not preserving the length of what it hides.
    /// </summary>
    /// <remarks>
    /// Fixed, because the point of not preserving length is that the output says nothing about the
    /// input. Length is itself a disclosure: a preserved-length mask over a national identifier
    /// tells a caller how many digits it has, which is most of the format.
    /// </remarks>
    private const int FixedMaskLength = 8;

    /// <summary>How many characters stand in for each hidden part of an address.</summary>
    private const int ShortMaskLength = 3;

    /// <summary>How long a regular expression may run before it is abandoned.</summary>
    /// <remarks>
    /// A pattern comes from configuration rather than from a caller, but a badly written one can
    /// still backtrack catastrophically over an unusual value and hang the request that happened to
    /// contain it. A timeout turns that into a failed query rather than a stopped thread.
    /// </remarks>
    private static readonly TimeSpan RegexBudget = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Applies one mask to one value.
    /// </summary>
    /// <param name="stage">The mask to apply.</param>
    /// <param name="value">The value, already rendered to text by the stages before this one.</param>
    /// <param name="salt">The salt for <see cref="MaskStrategy.Hash"/>.</param>
    /// <returns>The masked text, or null under <see cref="MaskStrategy.Null"/>.</returns>
    internal static string? Apply(MaskStage stage, string? value, string salt)
    {
        if (stage.Strategy == MaskStrategy.Null)
        {
            return null;
        }

        if (stage.Strategy == MaskStrategy.Fixed)
        {
            return stage.Text;
        }

        // A null value has nothing to disclose, and masking it into a run of stars would invent a
        // value where the database holds none. Every other strategy needs text to work on.
        if (value is null)
        {
            return null;
        }

        return stage.Strategy switch
        {
            MaskStrategy.Full => Run(stage, value.Length),
            MaskStrategy.Partial => Partial(stage, value),
            MaskStrategy.Email => Email(stage, value),
            MaskStrategy.Phone => Phone(stage, value),
            MaskStrategy.Regex => Regex(stage, value),
            MaskStrategy.Hash => Hash(value, salt),

            // Not reachable, and an exception rather than a full mask so it stays that way. A token
            // is not a function of its input — it comes from a vault this type deliberately knows
            // nothing about — so the pipeline applies it before calling here. Masking in full
            // instead would be safe and silent, which is how a mis-wiring survives to production
            // with every tokenized column reading as a run of stars.
            MaskStrategy.Tokenize => throw new InvalidOperationException(
                "A tokenizing mask reached the mask engine, which has no vault to resolve it "
                + "against. TransformPipeline applies MaskStrategy.Tokenize itself; a caller "
                + "reaching this has bypassed it."),

            _ => Run(stage, value.Length)
        };
    }

    /// <summary>Builds a run of mask characters of the appropriate length.</summary>
    private static string Run(MaskStage stage, int valueLength) =>
        new(stage.MaskChar, stage.PreserveLength ? valueLength : FixedMaskLength);

    /// <summary>
    /// Keeps a prefix and a suffix, hiding everything between them.
    /// </summary>
    /// <remarks>
    /// When the kept ends meet or overlap, the whole value is masked. Keeping them anyway would
    /// return the value complete under a configuration that reads as though it hides something —
    /// <c>KeepEnd = 4</c> on a four-character value would disclose all of it.
    /// </remarks>
    private static string Partial(MaskStage stage, string value)
    {
        if (stage.KeepStart + stage.KeepEnd >= value.Length)
        {
            return Run(stage, value.Length);
        }

        string start = value[..stage.KeepStart];
        string end = stage.KeepEnd == 0 ? string.Empty : value[^stage.KeepEnd..];
        int hidden = value.Length - stage.KeepStart - stage.KeepEnd;

        return string.Concat(start, new string(stage.MaskChar, hidden), end);
    }

    /// <summary>
    /// Reduces an address to its shape, keeping the first character of the local part and of the
    /// domain.
    /// </summary>
    /// <remarks>
    /// Anything that is not shaped like an address is masked in full. A value that reached an email
    /// mask and is not an address is either a misconfiguration or a surprise in the data, and
    /// returning it intact would disclose whatever it actually is.
    /// </remarks>
    private static string Email(MaskStage stage, string value)
    {
        int at = value.IndexOf('@', StringComparison.Ordinal);

        if (at <= 0 || at == value.Length - 1)
        {
            return Run(stage, value.Length);
        }

        string local = value[..at];
        string domain = value[(at + 1)..];
        int dot = domain.LastIndexOf('.');

        if (dot <= 0 || dot == domain.Length - 1)
        {
            return Run(stage, value.Length);
        }

        // Length is disclosure here as much as anywhere: a preserved-length address mask gives away
        // how long the mailbox and the domain are, which narrows a guess considerably. Preserving is
        // the default only because it is the default everywhere else; a deployment that cares sets
        // PreserveLength false and gets the shorter j***@d***.com shape.
        string hiddenLocal = new(
            stage.MaskChar, stage.PreserveLength ? Math.Max(local.Length - 1, 1) : ShortMaskLength);
        string hiddenDomain = new(
            stage.MaskChar, stage.PreserveLength ? Math.Max(dot - 1, 1) : ShortMaskLength);

        return $"{local[0]}{hiddenLocal}@{domain[0]}{hiddenDomain}{domain[dot..]}";
    }

    /// <summary>
    /// Keeps the last few digits of a number and hides the rest, punctuation included.
    /// </summary>
    /// <remarks>
    /// A number with too few digits to keep is masked in full, for the same reason a partial mask
    /// is: the configuration promises to hide something, and on a short value it otherwise hides
    /// nothing.
    /// </remarks>
    private static string Phone(MaskStage stage, string value)
    {
        int keep = stage.KeepEnd > 0 ? stage.KeepEnd : 4;
        int digits = 0;

        foreach (char c in value)
        {
            if (char.IsDigit(c))
            {
                digits++;
            }
        }

        if (digits <= keep)
        {
            return Run(stage, value.Length);
        }

        StringBuilder masked = new(value.Length);
        int remaining = digits - keep;

        foreach (char c in value)
        {
            if (char.IsDigit(c) && remaining > 0)
            {
                masked.Append(stage.MaskChar);
                remaining--;
            }
            else
            {
                masked.Append(c);
            }
        }

        return masked.ToString();
    }

    /// <summary>Replaces every match of the configured pattern.</summary>
    private static string Regex(MaskStage stage, string value) =>
        System.Text.RegularExpressions.Regex.Replace(
            value, stage.Pattern!, stage.Replacement, RegexOptions.None, RegexBudget);

    /// <summary>
    /// Replaces the value with a keyed hash of it.
    /// </summary>
    /// <remarks>
    /// The point of the salt is that the output cannot be reversed by hashing a dictionary of
    /// candidate values and comparing, which an unsalted hash of a national identifier or a
    /// postcode makes trivial. The salt therefore lives in options, supplied at startup, and never
    /// in the attribute where it would be committed to source control.
    /// <para>
    /// HMAC rather than <c>SHA256(salt || value)</c>. The concatenation is the construction every
    /// guide warns about: it is length-extendable, and it collides whenever a salt-and-value pair
    /// can be re-split — a salt ending in a digit and a value beginning with one produce the same
    /// input as the pair that moved the digit across. HMAC is the primitive built for keying a hash,
    /// and switching to it costs nothing that concatenation was buying.
    /// </para>
    /// <para>
    /// The same value hashes to the same text within a deployment, which is deliberate: a caller can
    /// still group and join by it without ever learning what it is. That property is also the limit
    /// of what this strategy can promise. Anyone who can write a chosen value and read it back
    /// hashed learns the digest of that value and can then recognise it wherever else it appears, no
    /// matter how good the salt is. Closing that needs a token whose output is not derived from the
    /// value at all — see <see cref="MaskStrategy.Tokenize"/>.
    /// </para>
    /// </remarks>
    private static string Hash(string value, string salt) =>
        Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(salt), Encoding.UTF8.GetBytes(value)))
            .ToLower(CultureInfo.InvariantCulture);
}
