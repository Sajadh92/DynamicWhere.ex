using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Obscures the decorated member's value on its way out of the library.
/// </summary>
/// <remarks>
/// The value is transformed after materialization, on a detached object, so filtering and sorting
/// still run against the real value in SQL while the caller sees the mask. A support agent can sort
/// by salary band without ever being shown a number.
/// <para>
/// Every strategy but <see cref="MaskStrategy.Null"/> emits text, so a mask is valid only on a
/// member text is assignable to. A numeric member reaches for <see cref="DwGeneralizeAttribute"/>
/// instead; the startup scan refuses the mismatch rather than letting it fail on a query.
/// </para>
/// <para>
/// The salt for <see cref="MaskStrategy.Hash"/> lives in the policy options and never here. A salt
/// committed to source control is not a salt.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DwMask(MaskStrategy.Partial, KeepEnd = 4)]        // ****1234
/// [DwMask(MaskStrategy.Email)]                       // j***@d***.com
/// [DwMask(MaskStrategy.Fixed, Text = "[REDACTED]")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DwMaskAttribute : DwPolicyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    /// <param name="strategy">How the value is obscured.</param>
    public DwMaskAttribute(MaskStrategy strategy) => Strategy = strategy;

    /// <summary>How the value is obscured.</summary>
    public MaskStrategy Strategy { get; }

    /// <summary>Characters kept at the start under <see cref="MaskStrategy.Partial"/>.</summary>
    public int KeepStart { get; set; }

    /// <summary>Characters kept at the end under <see cref="MaskStrategy.Partial"/>.</summary>
    public int KeepEnd { get; set; }

    /// <summary>The character standing in for a hidden one.</summary>
    public char MaskChar { get; set; } = '*';

    /// <summary>
    /// When true the mask is as long as the value it hides; when false it is a fixed short run.
    /// </summary>
    /// <remarks>
    /// Length is itself a disclosure. A preserved-length mask over a national identifier tells a
    /// caller how many digits it has, which is most of what they need to guess the rest of the
    /// format.
    /// </remarks>
    public bool PreserveLength { get; set; } = true;

    /// <summary>The pattern matched under <see cref="MaskStrategy.Regex"/>.</summary>
    public string? Pattern { get; set; }

    /// <summary>What each match becomes under <see cref="MaskStrategy.Regex"/>.</summary>
    public string? Replacement { get; set; }

    /// <summary>The constant the whole value becomes under <see cref="MaskStrategy.Fixed"/>.</summary>
    public string? Text { get; set; }
}
