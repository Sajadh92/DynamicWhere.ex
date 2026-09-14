using DynamicWhere.ex.Enums;

namespace DynamicWhere.ex.Classes.Core;

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

    /// <summary>
    /// Returns a copy. <see cref="Field"/> is rewritten in place by the validator, so a guarded
    /// query sorts a copy rather than the caller's own instance.
    /// </summary>
    internal OrderBy Clone() => new()
    {
        Sort = Sort,
        Field = Field,
        Direction = Direction
    };
}
