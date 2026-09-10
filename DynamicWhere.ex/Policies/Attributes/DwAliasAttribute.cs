namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Gives the decorated member a public name, which a caller may use anywhere a field path is
/// accepted.
/// </summary>
/// <remarks>
/// An alias adds a spelling; it never removes one. The internal path keeps working, so decorating a
/// field is not a breaking change to a filter already in production, and hiding the internal name is
/// the schema endpoint's job — it advertises the alias and does not advertise the path.
/// <para>
/// A runtime rule may also set an alias, subject to the usual ceiling: a sealed attribute here
/// cannot be replaced, an <see cref="DwPolicyAttribute.Overridable"/> one can. That makes the public
/// field vocabulary vary by caller, which is the point — a caller entitled to rename fields to match
/// their own understanding of the data gets a filter they can read.
/// </para>
/// <para>
/// The consequence is that two spellings can collide: a rule aliasing <c>"Salary"</c> onto a
/// different field, on a type that already has a <c>Salary</c> property, makes one name mean two
/// paths. Neither reading is safe to guess — preferring the property ignores the rule, preferring
/// the alias redirects the filter to another column — so an ambiguous name is refused outright.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DwAlias("customer_name")]
/// public string Name { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DwAliasAttribute : DwPolicyAttribute
{
    /// <summary>
    /// Initializes the attribute.
    /// </summary>
    /// <param name="name">
    /// The public name. Validated where every alias is validated, at fragment construction, so an
    /// alias from an attribute and one from a runtime rule are held to one standard.
    /// </param>
    public DwAliasAttribute(string name) => Name = name;

    /// <summary>The public name this member answers to.</summary>
    public string Name { get; }
}
