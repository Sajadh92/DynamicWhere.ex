namespace DynamicWhere.ex;

internal static class ErrorCode
{
    public static string SetsUniqueSort => "Conditions Sets Must Has Unique Sort Value";
    public static string ConditionsUniqueSort => "Conditions Must Has Unique Sort Value";
    public static string SubConditionsGroupsUniqueSort => "Sub Conditions Groups Must Has Unique Sort Value";
    public static string RequiredIntersection => "Conditions Set Of Index 1 To N Must Has Intersection";
    public static string InvalidField => "Condition Must Has Valid Field Name";
    public static string InvalidValue => "Some Condition Values Are Null Or White Space";
    public static string RequiredValues => "Condition With Operator In Or NotIn Must Has One Or More Values";
    public static string NotRequiredValues => "Condition With Operator IsNull Or IsNotNull Must Has No Values";
    public static string RequiredTwoValue => "Condition With Operator Between Or NotBetween Must Has Only Two Values";
    public static string RequiredOneValue(string name) => $"Condition With Operator {name} Must Has Only One Value";
}
