namespace DynamicWhere.ex;

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
    /// </summary>
    public List<string> Values { get; set; } = new();
}
