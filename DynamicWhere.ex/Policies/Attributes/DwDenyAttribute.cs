using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Refuses one or more query features for the decorated member. The composable primitive behind
/// the named attributes such as <see cref="DwDeniedAttribute"/> and <see cref="DwNoWhereAttribute"/>.
/// </summary>
/// <example>
/// <code>
/// [DwDeny(PolicyFeature.Select | PolicyFeature.Order)]
/// public string InternalNotes { get; set; }
/// </code>
/// </example>
public class DwDenyAttribute : DwPolicyAttribute
{
    /// <summary>
    /// Initializes the attribute.
    /// </summary>
    /// <param name="features">The features to refuse.</param>
    public DwDenyAttribute(PolicyFeature features) => Features = features;

    /// <summary>The features this attribute refuses.</summary>
    public PolicyFeature Features { get; }
}
