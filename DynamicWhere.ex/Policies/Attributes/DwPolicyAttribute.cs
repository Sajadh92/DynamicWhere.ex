namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Base for every field-level policy attribute.
/// </summary>
/// <remarks>
/// Attributes are sealed by default: a runtime rule may make a field's policy stricter but never
/// looser, unless the attribute author opts in with <see cref="Overridable"/>. That keeps a
/// compromised or misconfigured policy store from granting access the source code denies.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true, Inherited = true)]
public abstract class DwPolicyAttribute : Attribute
{
    /// <summary>
    /// When true, a runtime rule may replace this attribute's decision. Defaults to false, which
    /// makes the decision absolute.
    /// </summary>
    public bool Overridable { get; set; }
}
