namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Names the values a caller may usefully filter the decorated member for, so a schema endpoint can
/// offer a list rather than a free-text box.
/// </summary>
/// <remarks>
/// Advisory, and deliberately not enforcement. A filter naming something outside this list is not
/// refused: the list describes what is worth offering, and a list that drifts from the data would
/// otherwise start rejecting queries that are perfectly valid. Restricting how a field may be
/// queried is <c>[DwOperators]</c>'s job; refusing it outright is <c>[DwDeny]</c>'s.
/// </remarks>
/// <example>
/// <code>
/// [DwAllowedValues("Active", "Suspended", "Closed")]
/// public string Status { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DwAllowedValuesAttribute : DwPolicyAttribute
{
    /// <summary>
    /// Initializes the attribute.
    /// </summary>
    /// <param name="values">
    /// The values worth offering. Validated where every value list is validated, at fragment
    /// construction, so a list from an attribute and one from a runtime rule are held to one
    /// standard.
    /// </param>
    public DwAllowedValuesAttribute(params string[] values) => Values = values;

    /// <summary>The values worth offering.</summary>
    public string[] Values { get; }
}
