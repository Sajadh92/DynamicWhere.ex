using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Reduces the precision of a numeric or temporal member, keeping it the kind of thing it was.
/// </summary>
/// <remarks>
/// The counterpart to masking for the half of the type space masking cannot reach. A
/// <see cref="decimal"/> cannot be star-masked and stay a number, so a salary is rounded to the
/// nearest band and a birth date is reduced to its year.
/// <para>
/// <see cref="GeneralizeMode.Bucket"/> is the exception that emits text — a band label such as
/// <c>25-34</c> — so it is valid only on a member text is assignable to. The other three stay
/// numeric or temporal and are valid where the member is.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DwGeneralize(GeneralizeMode.Round, Step = 10000)]
/// [DwGeneralize(GeneralizeMode.DatePart, Part = DatePart.Year)]
/// [DwGeneralize(GeneralizeMode.Truncate, Decimals = 2)]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DwGeneralizeAttribute : DwPolicyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    /// <param name="mode">How precision is reduced.</param>
    public DwGeneralizeAttribute(GeneralizeMode mode) => Mode = mode;

    /// <summary>How precision is reduced.</summary>
    public GeneralizeMode Mode { get; }

    /// <summary>
    /// The width of a rounding or bucketing step. Must be positive for those two modes.
    /// </summary>
    public int Step { get; set; }

    /// <summary>The component a date is reduced to under <see cref="GeneralizeMode.DatePart"/>.</summary>
    public DatePart Part { get; set; }

    /// <summary>Digits kept after the point under <see cref="GeneralizeMode.Truncate"/>.</summary>
    public int Decimals { get; set; }
}
