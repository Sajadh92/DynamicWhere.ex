namespace DynamicWhere.ex;

/// <summary>
/// Specifies the logical connector used to combine conditions in a condition group.
/// </summary>
public enum Connector
{
    /// <summary>Represents the logical "AND" connector, requiring all conditions to be true.</summary>
    And,
    /// <summary>Represents the logical "OR" connector, requiring at least one condition to be true.</summary>
    Or
}
