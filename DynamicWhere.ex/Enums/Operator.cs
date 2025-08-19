namespace DynamicWhere.ex;

/// <summary>
/// Supported operators across data types.
/// </summary>
public enum Operator
{
    // Equality
    /// <summary>Equality (case-sensitive for text).</summary>
    Equal,
    /// <summary>Equality (case-insensitive for text).</summary>
    IEqual,
    /// <summary>Inequality (case-sensitive for text).</summary>
    NotEqual,
    /// <summary>Inequality (case-insensitive for text).</summary>
    INotEqual,

    // Text search
    /// <summary>Contains (case-sensitive for text).</summary>
    Contains,
    /// <summary>Contains (case-insensitive for text).</summary>
    IContains,
    /// <summary>Not contains (case-sensitive for text).</summary>
    NotContains,
    /// <summary>Not contains (case-insensitive for text).</summary>
    INotContains,
    /// <summary>Starts with (case-sensitive for text).</summary>
    StartsWith,
    /// <summary>Starts with (case-insensitive for text).</summary>
    IStartsWith,
    /// <summary>Not starts with (case-sensitive for text).</summary>
    NotStartsWith,
    /// <summary>Not starts with (case-insensitive for text).</summary>
    INotStartsWith,
    /// <summary>Ends with (case-sensitive for text).</summary>
    EndsWith,
    /// <summary>Ends with (case-insensitive for text).</summary>
    IEndsWith,
    /// <summary>Not ends with (case-sensitive for text).</summary>
    NotEndsWith,
    /// <summary>Not ends with (case-insensitive for text).</summary>
    INotEndsWith,

    // Set membership
    /// <summary>In (case-sensitive for text).</summary>
    In,
    /// <summary>In (case-insensitive for text).</summary>
    IIn,
    /// <summary>Not in (case-sensitive for text).</summary>
    NotIn,
    /// <summary>Not in (case-insensitive for text).</summary>
    INotIn,

    // Ranges / ordering
    /// <summary>Greater than.</summary>
    GreaterThan,
    /// <summary>Greater than or equal.</summary>
    GreaterThanOrEqual,
    /// <summary>Less than.</summary>
    LessThan,
    /// <summary>Less than or equal.</summary>
    LessThanOrEqual,
    /// <summary>Between (inclusive).</summary>
    Between,
    /// <summary>Not between.</summary>
    NotBetween,

    // Null checks
    /// <summary>Is NULL.</summary>
    IsNull,
    /// <summary>Is NOT NULL.</summary>
    IsNotNull
}
