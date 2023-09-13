namespace DynamicWhere.ex;

internal static class ErrorCode
{
    public static string SetsUniqueSort => "ListOfConditionsSetsMustHasUniqueSortValue";
    public static string ConditionsUniqueSort => "AnyListOfConditionsMustHasUniqueSortValue";
    public static string SubConditionsGroupsUniqueSort => "AnyListOfSubConditionsGroupsMustHasUniqueSortValue";
    public static string RequiredIntersection => "ConditionsSetOfIndex[1-N]MustHasIntersection";
    public static string InvalidField => "ConditionMustHasValidFieldName";
    public static string InvalidValue => "ConditionValuesAreNullOrWhiteSpace";
    public static string RequiredValues => "ConditionWithOperator[In-IIn-NotIn-INotIn]MustHasOneOrMoreValues";
    public static string NotRequiredValues => "ConditionWithOperator[IsNull-IsNotNull]MustHasNoValues";
    public static string RequiredTwoValue => "ConditionWithOperator[Between-NotBetween]MustHasOnlyTwoValues";
    public static string RequiredOneValue(string name) => $"ConditionWithOperator[{name}]MustHasOnlyOneValue";
}
