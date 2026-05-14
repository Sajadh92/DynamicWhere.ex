using DynamicWhere.ex.Enums;

namespace DynamicWhere.ex.Classes.Core;

/// <summary>
/// Represents a condition used in dynamic queries.
/// </summary>
public class Condition
{
    /// <summary>
    /// The sort order of the condition.
    /// </summary>
    public int Sort { get; set; }

    /// <summary>
    /// The field name associated with the condition.
    /// </summary>
    public string? Field { get; set; }

    /// <summary>
    /// The data type of the condition.
    /// </summary>
    public DataType DataType { get; set; }

    /// <summary>
    /// The logical operator for the condition.
    /// </summary>
    public Operator Operator { get; set; }

    /// <summary>
    /// The list of values associated with the condition.
    /// <para>
    /// Accepts heterogeneous JSON types: raw strings (<c>"abc"</c>), numbers (<c>0</c>, <c>1.569</c>),
    /// booleans (<c>true</c>, <c>false</c>), or quoted strings (<c>"0"</c>, <c>"false"</c>).
    /// Values are normalized to strings internally based on <see cref="DataType"/>, so callers
    /// previously sending <c>List&lt;string&gt;</c> via JSON remain fully backward compatible.
    /// </para>
    /// </summary>
    public List<object> Values { get; set; } = new();
}
