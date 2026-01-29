using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;

namespace DynamicWhere.ex.Source;

/// <summary>
/// Builds EF Core–translatable predicate snippets from (<see cref="DataType"/>, <see cref="Operator"/>, field, values).
/// <para>
/// <b>Text comparisons</b>:
/// <list type="bullet">
///   <item><description>Uses C# string operations (<c>==</c>, <c>Contains</c>, <c>StartsWith</c>, <c>EndsWith</c>).</description></item>
///   <item><description><c>I*</c> operators (e.g., <see cref="Operator.IContains"/>) are case-insensitive by normalizing both sides with <c>ToLower()</c>.</description></item>
/// </list>
/// </para>
/// <para>
/// Returns a C# dynamic LINQ expression string (not SQL) intended for libraries like System.Linq.Dynamic.Core.
/// </para>
/// </summary>
internal static class Builder
{
    /// <summary>
    /// Builds a dynamic LINQ predicate snippet for the provided <see cref="DataType"/> and <see cref="Operator"/>.
    /// The returned value is a C# expression string (not SQL) suitable for libraries like System.Linq.Dynamic.Core.
    /// For text comparisons, the expression uses <c>==</c>/<c>!=</c> and string methods (<c>Contains</c>, <c>StartsWith</c>, <c>EndsWith</c>).
    /// Case-insensitive (<c>I*</c>) operators normalize both sides via <c>ToLower()</c>.
    /// </summary>
    /// <param name="dataType">Logical data type of the field.</param>
    /// <param name="_operator">Operator to apply.</param>
    /// <param name="field">Field access expression (e.g., <c>"x.Email"</c>).</param>
    /// <param name="values">Operator values (validated upstream; count depends on <paramref name="_operator"/>).</param>
    /// <returns>Dynamic LINQ predicate snippet.</returns>
    /// <exception cref="LogicException">Thrown if the combination of <paramref name="dataType"/> and <paramref name="_operator"/> is unsupported.</exception>
    public static string BuildCondition(DataType dataType, Operator _operator, string field, List<string> values)
    {
        // Normalize value tokens by trimming whitespace; validation already happened upstream.
        values = values.Select(v => v.Trim()).ToList();

        switch (dataType)
        {
            // ----------------------------------------------------------
            // TEXT
            // ----------------------------------------------------------
            case DataType.Text:
            {
                switch (_operator)
                {
                    case Operator.Equal:
                        return $"{field} != null && {field} == \"{values[0]}\"";

                    case Operator.IEqual:
                        return $"{field} != null && {field}.ToLower() == \"{values[0].ToLower()}\"";

                    case Operator.NotEqual:
                        return $"{field} != null && {field} != \"{values[0]}\"";

                    case Operator.INotEqual:
                        return $"{field} != null && {field}.ToLower() != \"{values[0].ToLower()}\"";

                    case Operator.Contains:
                        return $"{field} != null && {field}.Contains(\"{values[0]}\")";

                    case Operator.IContains:
                        return $"{field} != null && {field}.ToLower().Contains(\"{values[0].ToLower()}\")";

                    case Operator.NotContains:
                        return $"{field} != null && !{field}.Contains(\"{values[0]}\")";

                    case Operator.INotContains:
                        return $"{field} != null && !{field}.ToLower().Contains(\"{values[0].ToLower()}\")";

                    case Operator.StartsWith:
                        return $"{field} != null && {field}.StartsWith(\"{values[0]}\")";

                    case Operator.IStartsWith:
                        return $"{field} != null && {field}.ToLower().StartsWith(\"{values[0].ToLower()}\")";

                    case Operator.EndsWith:
                        return $"{field} != null && {field}.EndsWith(\"{values[0]}\")";

                    case Operator.IEndsWith:
                        return $"{field} != null && {field}.ToLower().EndsWith(\"{values[0].ToLower()}\")";

                    case Operator.NotStartsWith:
                        return $"{field} != null && !{field}.StartsWith(\"{values[0]}\")";

                    case Operator.INotStartsWith:
                        return $"{field} != null && !{field}.ToLower().StartsWith(\"{values[0].ToLower()}\")";

                    case Operator.NotEndsWith:
                        return $"{field} != null && !{field}.EndsWith(\"{values[0]}\")";

                    case Operator.INotEndsWith:
                        return $"{field} != null && !{field}.ToLower().EndsWith(\"{values[0].ToLower()}\")";

                    case Operator.In:
                    {
                        var ors = values.Select(v => $"{field} == \"{v}\"");
                        return $"{field} != null && ({string.Join(" || ", ors)})";
                    }

                    case Operator.IIn:
                    {
                        var ors = values.Select(v => $"{field}.ToLower() == \"{v.ToLower()}\"");
                        return $"{field} != null && ({string.Join(" || ", ors)})";
                    }

                    case Operator.NotIn:
                    {
                        var ands = values.Select(v => $"{field} != \"{v}\"");
                        return $"{field} != null && ({string.Join(" && ", ands)})";
                    }

                    case Operator.INotIn:
                    {
                        var ands = values.Select(v => $"{field}.ToLower() != \"{v.ToLower()}\"");
                        return $"{field} != null && ({string.Join(" && ", ands)})";
                    }

                    case Operator.IsNull: return $"{field} == null";
                    case Operator.IsNotNull: return $"{field} != null";
                }
                break;
            }

            // ----------------------------------------------------------
            // GUID (exact string semantics)
            // ----------------------------------------------------------
            case DataType.Guid:
            {
                switch (_operator)
                {
                    case Operator.Equal:
                        return $"{field} != null && {field} == \"{values[0]}\"";

                    case Operator.NotEqual:
                        return $"{field} != null && {field} != \"{values[0]}\"";

                    case Operator.In:
                    {
                        var ors = values.Select(v => $"{field} == \"{v}\"");
                        return $"{field} != null && ({string.Join(" || ", ors)})";
                    }

                    case Operator.NotIn:
                    {
                        var ands = values.Select(v => $"{field} != \"{v}\"");
                        return $"{field} != null && ({string.Join(" && ", ands)})";
                    }

                    case Operator.IsNull: return $"{field} == null";
                    case Operator.IsNotNull: return $"{field} != null";
                }
                break;
            }

            // ----------------------------------------------------------
            // NUMBER
            // ----------------------------------------------------------
            case DataType.Number:
            {
                switch (_operator)
                {
                    case Operator.Equal: return $"{field} != null && {field} == {values[0]}";
                    case Operator.NotEqual: return $"{field} != null && {field} != {values[0]}";
                    case Operator.GreaterThan: return $"{field} != null && {field} > {values[0]}";
                    case Operator.GreaterThanOrEqual: return $"{field} != null && {field} >= {values[0]}";
                    case Operator.LessThan: return $"{field} != null && {field} < {values[0]}";
                    case Operator.LessThanOrEqual: return $"{field} != null && {field} <= {values[0]}";

                    case Operator.In:
                    {
                        var ors = values.Select(v => $"{field} == {v}");
                        return $"{field} != null && ({string.Join(" || ", ors)})";
                    }

                    case Operator.NotIn:
                    {
                        var ands = values.Select(v => $"{field} != {v}");
                        return $"{field} != null && ({string.Join(" && ", ands)})";
                    }

                    case Operator.Between:
                        return $"{field} != null && {field} >= {values[0]} && {field} <= {values[1]}";

                    case Operator.NotBetween:
                        return $"{field} != null && ({field} < {values[0]} || {field} > {values[1]})";

                    case Operator.IsNull: return $"{field} == null";
                    case Operator.IsNotNull: return $"{field} != null";
                }
            }
            break;

            // ----------------------------------------------------------
            // BOOLEAN
            // ----------------------------------------------------------
            case DataType.Boolean:
            {
                switch (_operator)
                {
                    case Operator.Equal: return $"{field} != null && {field} == {values[0]}";
                    case Operator.NotEqual: return $"{field} != null && {field} != {values[0]}";
                    case Operator.IsNull: return $"{field} == null";
                    case Operator.IsNotNull: return $"{field} != null";
                }
            }
            break;

            // ----------------------------------------------------------
            // DATETIME (expect ISO 8601 strings)
            // ----------------------------------------------------------
            case DataType.DateTime:
            {
                switch (_operator)
                {
                    case Operator.Equal:
                        return $"{field} != null && {field} == DateTime.Parse(\"{values[0]}\")";

                    case Operator.NotEqual:
                        return $"{field} != null && {field} != DateTime.Parse(\"{values[0]}\")";

                    case Operator.GreaterThan:
                        return $"{field} != null && {field} > DateTime.Parse(\"{values[0]}\")";

                    case Operator.GreaterThanOrEqual:
                        return $"{field} != null && {field} >= DateTime.Parse(\"{values[0]}\")";

                    case Operator.LessThan:
                        return $"{field} != null && {field} < DateTime.Parse(\"{values[0]}\")";

                    case Operator.LessThanOrEqual:
                        return $"{field} != null && {field} <= DateTime.Parse(\"{values[0]}\")";

                    case Operator.Between:
                        return $"{field} != null && {field} >= DateTime.Parse(\"{values[0]}\") && {field} <= DateTime.Parse(\"{values[1]}\")";

                    case Operator.NotBetween:
                        return $"{field} != null && ({field} < DateTime.Parse(\"{values[0]}\") || {field} > DateTime.Parse(\"{values[1]}\") )";

                    case Operator.IsNull: return $"{field} == null";
                    case Operator.IsNotNull: return $"{field} != null";
                }
            }
            break;

            // ----------------------------------------------------------
            // DATE (compare by .Date)
            // ----------------------------------------------------------
            case DataType.Date:
            {
                switch (_operator)
                {
                    case Operator.Equal:
                        return $"{field} != null && {field}.Date == DateTime.Parse(\"{values[0]}\").Date";

                    case Operator.NotEqual:
                        return $"{field} != null && {field}.Date != DateTime.Parse(\"{values[0]}\").Date";

                    case Operator.GreaterThan:
                        return $"{field} != null && {field}.Date > DateTime.Parse(\"{values[0]}\").Date";

                    case Operator.GreaterThanOrEqual:
                        return $"{field} != null && {field}.Date >= DateTime.Parse(\"{values[0]}\").Date";

                    case Operator.LessThan:
                        return $"{field} != null && {field}.Date < DateTime.Parse(\"{values[0]}\").Date";

                    case Operator.LessThanOrEqual:
                        return $"{field} != null && {field}.Date <= DateTime.Parse(\"{values[0]}\").Date";

                    case Operator.Between:
                        return $"{field} != null && {field}.Date >= DateTime.Parse(\"{values[0]}\").Date && {field}.Date <= DateTime.Parse(\"{values[1]}\").Date";

                    case Operator.NotBetween:
                        return $"{field} != null && ({field}.Date < DateTime.Parse(\"{values[0]}\").Date || {field}.Date > DateTime.Parse(\"{values[1]}\").Date)";

                    case Operator.IsNull: return $"{field} == null";
                    case Operator.IsNotNull: return $"{field} != null";
                }
            }
            break;

            // ----------------------------------------------------------
            // Enum
            // ----------------------------------------------------------
            case DataType.Enum:
            {
                switch (_operator)
                {
                    case Operator.Equal:
                        return $"{field} != null && {field} == \"{values[0]}\"";

                    case Operator.NotEqual:
                        return $"{field} != null && {field} != \"{values[0]}\"";

                    case Operator.Contains:
                        return $"{field} != null && {field}.Contains(\"{values[0]}\")";

                    case Operator.NotContains:
                        return $"{field} != null && !{field}.Contains(\"{values[0]}\")";

                    case Operator.StartsWith:
                        return $"{field} != null && {field}.StartsWith(\"{values[0]}\")";

                    case Operator.EndsWith:
                        return $"{field} != null && {field}.EndsWith(\"{values[0]}\")";

                    case Operator.NotStartsWith:
                        return $"{field} != null && !{field}.StartsWith(\"{values[0]}\")";

                    case Operator.NotEndsWith:
                        return $"{field} != null && !{field}.EndsWith(\"{values[0]}\")";

                    case Operator.In:
                    {
                        var ors = values.Select(v => $"{field} == \"{v}\"");
                        return $"{field} != null && ({string.Join(" || ", ors)})";
                    }

                    case Operator.NotIn:
                    {
                        var ands = values.Select(v => $"{field} != \"{v}\"");
                        return $"{field} != null && ({string.Join(" && ", ands)})";
                    }

                    case Operator.IsNull: return $"{field} == null";
                    case Operator.IsNotNull: return $"{field} != null";
                }
                break;
            }
        }

        throw new LogicException($"Unsupported combination of DataType '{dataType}' and Operator '{_operator}'.");
    }
}
