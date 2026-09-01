namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Replaces the decorated member's value outright, with the type's default or with a constant.
/// </summary>
/// <remarks>
/// This short-circuits the chain: a member carrying it emits the replacement and no other stage
/// runs. Masking a value that has already become <c>"N/A"</c> obscures nothing, and letting the two
/// compose would make the emitted value depend on an ordering rule invisible to anyone reading the
/// entity. Declaring it alongside another transform is a configuration error the startup scan
/// reports.
/// <para>
/// C# forbids <see cref="decimal"/> and <see cref="DateTime"/> as attribute arguments, so a constant
/// is written in string form and converted to the member's type when the policy resolves.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DwDefault]              // 0, null, ""
/// [DwDefault("N/A")]       // a constant
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DwDefaultAttribute : DwPolicyAttribute
{
    /// <summary>Replaces the value with the member type's default.</summary>
    public DwDefaultAttribute()
    {
    }

    /// <summary>Replaces the value with a constant.</summary>
    /// <param name="value">The constant, in string form, converted to the member's type.</param>
    public DwDefaultAttribute(string value)
    {
        Value = value;
        HasValue = true;
    }

    /// <summary>The constant, or null when the type's default is used.</summary>
    public string? Value { get; }

    /// <summary>
    /// True when a constant was supplied.
    /// </summary>
    /// <remarks>
    /// Distinguishes <c>[DwDefault]</c> from <c>[DwDefault(null)]</c>, which <see cref="Value"/>
    /// alone cannot: both leave it null, and only one of them means "the type's default".
    /// </remarks>
    public bool HasValue { get; }
}
