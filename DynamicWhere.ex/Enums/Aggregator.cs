namespace DynamicWhere.ex.Enums;

/// <summary>
/// Represents supported aggregation operations used in dynamic queries.
/// </summary>
public enum Aggregator
{
    /// <summary>
    /// Represents an operation that counts the number of elements in a collection.
    /// </summary>
    Count,

    /// <summary>
    /// Represents an operation that counts the number of distinct elements in a collection.
    /// </summary>
    CountDistinct,

    /// <summary>
    /// Represents an operation that calculates the sum of a collection of numeric values.
    /// </summary>
    Sumation,

    /// <summary>
    /// Represents an operation that calculates the average of a collection of numeric values.
    /// </summary>
    Average,

    /// <summary>
    /// Represents an operation that finds the minimum value in a collection.
    /// </summary>
    Minimum,

    /// <summary>
    /// Represents an operation that finds the maximum value in a collection.
    /// </summary>
    Maximum,

    /// <summary>
    /// Represents an operation that retrieves the first element of a collection, or a default value if the collection is empty.
    /// </summary>
    FirstOrDefault,

    /// <summary>
    /// Represents an operation that retrieves the last element of a collection, or a default value if the collection is empty.
    /// </summary>
    LastOrDefault
}
