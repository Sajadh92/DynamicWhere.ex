namespace DynamicWhere.ex;

/// <summary>
/// Represents a sorting configuration specifying the field name and sorting direction.
/// </summary>
public class OrderBy
{
    /// <summary>
    /// The sort order.
    /// </summary>
    public int Sort { get; set; }

    /// <summary>
    /// The field name to sort by.
    /// </summary>
    public string? Field { get; set; }

    /// <summary>
    /// The sorting direction.
    /// </summary>
    public Direction Direction { get; set; } = Direction.Ascending;
}
