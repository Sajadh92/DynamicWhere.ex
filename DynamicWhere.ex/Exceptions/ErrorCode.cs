namespace DynamicWhere.ex.Exceptions;

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

    /// <summary>
    /// Indicates that the fields list must have at least one field.
    /// </summary>
    public static string MustHaveFields => "MustHasFields";

    /// <summary>
    /// Indicates that the field must be a valid date format.
    /// </summary>
    public static string InvalidFormat => "InvalidFormat";

    /// <summary>
    /// Indicates that an aggregation must have a non-empty alias.
    /// </summary>
    public static string InvalidAlias => "AggregationMustHasValidAlias";

    /// <summary>
    /// Indicates that the aggregator is not supported for the specified field type.
    /// </summary>
    /// <param name="aggregator">The aggregator name.</param>
    /// <param name="fieldType">The field type name.</param>
    /// <returns>
    /// A formatted error message indicating the aggregator is not supported for the field type.
    /// </returns>
    public static string UnsupportedAggregatorForType(string aggregator, string fieldType) =>
        $"Aggregator[{aggregator}]IsNotSupportedForFieldType[{fieldType}]";

    /// <summary>
    /// Indicates that aggregation fields must be simple types (primitive, string, DateTime, Guid, etc.).
    /// </summary>
    public static string AggregationFieldMustBeSimpleType => "AggregationFieldMustBeSimpleType";

    /// <summary>
    /// Indicates that aggregation fields cannot be collection types.
    /// </summary>
    public static string AggregationFieldCannotBeCollection => "AggregationFieldCannotBeCollectionType";

    /// <summary>
    /// Indicates that GroupBy fields list must contain at least one field.
    /// </summary>
    public static string GroupByMustHaveFields => "GroupByMustHasAtLeastOneField";

    /// <summary>
    /// Indicates that GroupBy fields list must have unique field names.
    /// </summary>
    public static string GroupByFieldsMustBeUnique => "GroupByFieldsMustBeUnique";

    /// <summary>
    /// Indicates that GroupBy fields cannot be complex types.
    /// </summary>
    public static string GroupByFieldCannotBeComplexType => "GroupByFieldCannotBeComplexType";

    /// <summary>
    /// Indicates that GroupBy fields cannot be collection types.
    /// </summary>
    public static string GroupByFieldCannotBeCollection => "GroupByFieldCannotBeCollectionType";

    /// <summary>
    /// Indicates that aggregation aliases must be unique within the GroupBy.
    /// </summary>
    public static string AggregationAliasesMustBeUnique => "AggregationAliasesMustBeUnique";

    /// <summary>
    /// Indicates that aggregation fields cannot be used in the GroupBy fields list.
    /// </summary>
    /// <param name="fieldName">The field name that conflicts.</param>
    /// <returns>
    /// A formatted error message indicating the field is used in both GroupBy fields and aggregation.
    /// </returns>
    public static string AggregationFieldCannotBeGroupByField(string fieldName) =>
        $"AggregationField[{fieldName}]CannotBeUsedInGroupByFields";

    /// <summary>
    /// Indicating that the specified aggregation alias cannot be used as a group by field.
    /// </summary>
    /// <param name="alias">The aggregation alias to check for invalid usage in group by fields.</param>
    /// <returns>
    /// A formatted error message string that specifies the invalid aggregation alias.
    /// </returns>
    public static string AggregationAliasCannotBeGroupByField(string alias) =>
        $"AggregationAlias[{alias}]CannotBeUsedInGroupByFields";

    /// <summary>
    /// Indicates that a summary order field must exist in the group-by fields or aggregate-by aliases.
    /// </summary>
    /// <param name="fieldName">The order field name that does not match any group-by field or aggregate alias.</param>
    /// <returns>
    /// A formatted error message indicating the invalid order field.
    /// </returns>
    public static string SummaryOrderFieldMustExistInGroupByOrAggregate(string fieldName) =>
        $"SummaryOrderField[{fieldName}]MustExistInGroupByFieldsOrAggregateByAliases";
}
