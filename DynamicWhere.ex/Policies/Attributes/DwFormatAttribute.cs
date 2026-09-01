namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Renders the decorated member's value through a format string.
/// </summary>
/// <remarks>
/// Runs after generalization and before masking, so a date reduced to its year is rendered and then
/// obscured rather than the other way round. Formatting emits text, so it is valid only where text
/// is assignable — a <c>DateTime</c> member cannot carry it, and a <c>string</c> member holding a
/// rendered date can.
/// <para>
/// Applied with <see cref="System.Globalization.CultureInfo.InvariantCulture"/>. A value whose
/// rendering depends on the server's locale is a value that changes when the server moves.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DwFormat("yyyy-MM-dd")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DwFormatAttribute : DwPolicyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    /// <param name="format">A standard or custom .NET format string.</param>
    public DwFormatAttribute(string format) => Format = format;

    /// <summary>The format string applied to the value.</summary>
    public string Format { get; }
}
