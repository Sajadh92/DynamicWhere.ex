namespace DynamicWhere.ex;

/// <summary>
/// Supported logical data types for dynamic condition building.
/// </summary>
public enum DataType
{
    /// <summary>Textual data (string).</summary>
    Text,
    /// <summary>GUID represented as string.</summary>
    Guid,
    /// <summary>Numeric value types.</summary>
    Number,
    /// <summary>Boolean values.</summary>
    Boolean,
    /// <summary>Full timestamp (date and time).</summary>
    DateTime,
    /// <summary>Date-only value.</summary>
    Date
}
