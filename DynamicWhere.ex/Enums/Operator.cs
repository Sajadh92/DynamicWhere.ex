namespace DynamicWhere.ex;

/// <summary>
/// Specifies various logical comparison operators for constructing dynamic queries.
/// </summary>
public enum Operator
{
    /// <summary>
    /// Equality comparison.
    /// </summary>
    Equal,

    /// <summary>
    /// Case-insensitive equality comparison.
    /// </summary>
    IEqual,

    /// <summary>
    /// Inequality comparison.
    /// </summary>
    NotEqual,

    /// <summary>
    /// Case-insensitive inequality comparison.
    /// </summary>
    INotEqual,

    /// <summary>
    /// Greater than comparison.
    /// </summary>
    GreaterThan,

    /// <summary>
    /// Greater than or equal to comparison.
    /// </summary>
    GreaterThanOrEqual,

    /// <summary>
    /// Less than comparison.
    /// </summary>
    LessThan,

    /// <summary>
    /// Less than or equal to comparison.
    /// </summary>
    LessThanOrEqual,

    /// <summary>
    /// Substring containment check.
    /// </summary>
    Contains,

    /// <summary>
    /// Case-insensitive substring containment check.
    /// </summary>
    IContains,

    /// <summary>
    /// Negation of substring containment check.
    /// </summary>
    NotContains,

    /// <summary>
    /// Case-insensitive negation of substring containment check.
    /// </summary>
    INotContains,

    /// <summary>
    /// Prefix check.
    /// </summary>
    StartsWith,

    /// <summary>
    /// Case-insensitive prefix check.
    /// </summary>
    IStartsWith,

    /// <summary>
    /// Negation of prefix check.
    /// </summary>
    NotStartsWith,

    /// <summary>
    /// Case-insensitive negation of prefix check.
    /// </summary>
    INotStartsWith,

    /// <summary>
    /// Suffix check.
    /// </summary>
    EndsWith,

    /// <summary>
    /// Case-insensitive suffix check.
    /// </summary>
    IEndsWith,

    /// <summary>
    /// Negation of suffix check.
    /// </summary>
    NotEndsWith,

    /// <summary>
    /// Case-insensitive negation of suffix check.
    /// </summary>
    INotEndsWith,

    /// <summary>
    /// Membership check.
    /// </summary>
    In,

    /// <summary>
    /// Case-insensitive membership check.
    /// </summary>
    IIn,

    /// <summary>
    /// Negation of membership check.
    /// </summary>
    NotIn,

    /// <summary>
    /// Case-insensitive negation of membership check.
    /// </summary>
    INotIn,

    /// <summary>
    /// Range comparison.
    /// </summary>
    Between,

    /// <summary>
    /// Negation of range comparison.
    /// </summary>
    NotBetween,

    /// <summary>
    /// Null check.
    /// </summary>
    IsNull,

    /// <summary>
    /// Negation of null check.
    /// </summary>
    IsNotNull
}
