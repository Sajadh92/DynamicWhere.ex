namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Describes the decorated member for a schema endpoint, so a front end can build a filter UI from
/// the entity rather than from a hand-maintained copy of it.
/// </summary>
/// <remarks>
/// Pure description: nothing here decides whether a caller may filter, project, or sort. It travels
/// the same fragment path the decisions travel because a schema is answered per caller — a runtime
/// rule may relabel a field for one role, and this attribute is sealed by default, so it cannot
/// unless its author writes <see cref="DwPolicyAttribute.Overridable"/>.
/// <para>
/// Each property elects on its own. Setting only <see cref="Label"/> in a rule leaves an
/// attribute's <see cref="Description"/> and <see cref="Group"/> in place rather than erasing them.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DwDescribe(Label = "Salary", Description = "Monthly gross",
///             Group = "Compensation", Order = 10)]
/// public decimal Salary { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DwDescribeAttribute : DwPolicyAttribute
{
    /// <summary>A short human name for the field.</summary>
    public string? Label { get; set; }

    /// <summary>A longer explanation of what the field holds.</summary>
    public string? Description { get; set; }

    /// <summary>The section a schema endpoint lists the field under.</summary>
    public string? Group { get; set; }

    /// <summary>
    /// Where the field sorts within its group. Unset leaves the order to the schema endpoint.
    /// </summary>
    /// <remarks>
    /// Backed by a nullable so that "unordered" and "sorts first" stay different answers, while the
    /// property itself stays an <c>int</c>: C# does not accept a nullable as a named attribute
    /// argument. A plain <c>int</c> defaulting to zero would make every undecorated field claim the
    /// front of its group.
    /// </remarks>
    public int Order
    {
        get => _order ?? 0;
        set => _order = value;
    }

    /// <summary>The order as declared, or null when the attribute did not set one.</summary>
    internal int? DeclaredOrder => _order;

    private int? _order;
}
