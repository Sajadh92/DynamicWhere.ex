namespace DynamicWhere.ex;

/// <summary>
/// Provides error codes used for indicating specific validation or logic errors in the application.
/// </summary>
internal static class ErrorCode
{
    /// <summary>
    /// Indicates that a list of condition sets must have unique sort values.
    /// </summary>
    public static string SetsUniqueSort => "ListOfConditionsSetsMustHasUniqueSortValue";

    /// <summary>
    /// Indicates that any list of conditions must have unique sort values.
    /// </summary>
    public static string ConditionsUniqueSort => "AnyListOfConditionsMustHasUniqueSortValue";

    /// <summary>
    /// Indicates that any list of sub-condition groups must have unique sort values.
    /// </summary>
    public static string SubConditionsGroupsUniqueSort => "AnyListOfSubConditionsGroupsMustHasUniqueSortValue";

    /// <summary>
    /// Indicates that condition sets of index [1-N] must have an intersection specified.
    /// </summary>
    public static string RequiredIntersection => "ConditionsSetOfIndex[1-N]MustHasIntersection";

    /// <summary>
    /// Indicates that a condition must have a valid field name.
    /// </summary>
    public static string InvalidField => "ConditionMustHasValidFieldName";

    /// <summary>
    /// Indicates that condition values are null or whitespace.
    /// </summary>
    public static string InvalidValue => "ConditionValuesAreNullOrWhiteSpace";

    /// <summary>
    /// Indicates that a condition with operators [In, IIn, NotIn, INotIn] must have one or more values.
    /// </summary>
    public static string RequiredValues => "ConditionWithOperator[In-IIn-NotIn-INotIn]MustHasOneOrMoreValues";

    /// <summary>
    /// Indicates that a condition with operators [IsNull, IsNotNull] must have no values.
    /// </summary>
    public static string NotRequiredValues => "ConditionWithOperator[IsNull-IsNotNull]MustHasNoValues";

    /// <summary>
    /// Indicates that a condition with operators [Between, NotBetween] must have exactly two values.
    /// </summary>
    public static string RequiredTwoValue => "ConditionWithOperator[Between-NotBetween]MustHasOnlyTwoValues";

    /// <summary>
    /// Indicates that a condition with the specified operator must have exactly one value.
    /// </summary>
    /// <param name="name">The operator name.</param>
    /// <returns>
    /// A formatted error message indicating the operator-specific validation requirement.
    /// </returns>
    public static string RequiredOneValue(string name) => $"ConditionWithOperator[{name}]MustHasOnlyOneValue";

    /// <summary>
    /// Indicates that a page number must be greater than zero.
    /// </summary>
    public static string InvalidPageNumber => "PageNumberMustBeGreaterThanZero";

    /// <summary>
    /// Indicates that a page size must be greater than zero.
    /// </summary>
    public static string InvalidPageSize => "PageSizeMustBeGreaterThanZero";
}
