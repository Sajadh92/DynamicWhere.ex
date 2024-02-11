using System.Collections;

namespace DynamicWhere.ex;

/// <summary>
/// Provides methods for converting Condition and ConditionGroup objects into their C# string representations.
/// </summary>
internal static class Converter
{
    /// <summary>
    /// Converts a condition into its corresponding C# string representation.
    /// </summary>
    /// <typeparam name="T">The type of the entity being queried.</typeparam>
    /// <param name="condition">The condition to convert.</param>
    /// <returns>The C# string representation of the condition.</returns>
    /// <exception cref="LogicException">Thrown if the condition contains invalid data.</exception>
    /// <exception cref="ArgumentNullException">Thrown if the condition field is null.</exception>
    public static string AsString<T>(this Condition condition)
    {
        // Validate the condition.
        condition.Validate<T>();

        // Split the field into individual property names.
        string[] props = condition.Field!.Trim().Replace(" ", "").Split('.');

        Type type = typeof(T), parent = typeof(T);

        // Initialize variables to build the condition string.
        string conditionAsString = ""; int any = 0;

        for (int i = 0; i < props.Length; i++)
        {
            string p = props[i];

            // Check if the parent is a generic list and append 'Any' if true.
            if (parent.IsGenericType && parent.GetInterface(nameof(IEnumerable)) != null)
            {
                conditionAsString += $"Any(i{i} => i{i}.{p}"; any++;
            }
            else
            {
                conditionAsString += p;
            }

            if (i < props.Length - 1)
            {
                // Append a dot for navigation to the next property.
                conditionAsString += ".";

                type = parent = type.GetProperty(p)!.PropertyType;

                // If the property is a generic list, navigate to its element type.
                if (type.IsGenericType && type.GetInterface(nameof(IEnumerable)) != null)
                {
                    type = type.GetGenericArguments()[0];
                }
            }
            else
            {
                // For the last property, build the actual condition string.
                string last = conditionAsString.Split(' ').Last().Trim();

                conditionAsString = conditionAsString.Remove(conditionAsString.Length - last.Length);

                conditionAsString += BuildCondition(condition.DataType, condition.Operator, last, condition.Values);
            }
        }

        // Close any 'Any' lambda expressions.
        for (int i = 0; i < any; i++)
        {
            conditionAsString += ")";
        }

        return $"({conditionAsString})";
    }

    /// <summary>
    /// Builds a string representation of a condition for dynamic LINQ queries based on the specified data type, operator, field, and values.
    /// </summary>
    /// <param name="DataType">The data type of the field.</param>
    /// <param name="Operator">The comparison operator.</param>
    /// <param name="Field">The field to compare.</param>
    /// <param name="Values">The values to compare against.</param>
    /// <returns>A string representation of the condition for use in dynamic LINQ queries.</returns>
    public static string BuildCondition(DataType DataType, Operator Operator, string Field, List<string> Values)
    {
        switch (DataType)
        {
            case DataType.Guid:
            {
                switch (Operator)
                {
                    case Operator.Equal:
                    {
                        return $"{Field} != null && {Field} == \"{Values[0]}\"";
                    }

                    case Operator.NotEqual:
                    {
                        return $"{Field} != null && {Field} != \"{Values[0]}\"";
                    }

                    case Operator.In:
                    {
                        var conditions = Values.Select(value => $"{Field} == \"{value}\"");

                        return $"{Field} != null && ({string.Join(" || ", conditions)})";
                    }

                    case Operator.NotIn:
                    {
                        var conditions = Values.Select(value => $"{Field} != \"{value}\"");

                        return $"{Field} != null && ({string.Join(" && ", conditions)})";
                    }

                    case Operator.IsNull:
                    {
                        return $"{Field} == null";
                    }

                    case Operator.IsNotNull:
                    {
                        return $"{Field} != null";
                    }
                }
            }
            break;

            case DataType.Text:
            {
                switch (Operator)
                {
                    case Operator.Equal:
                    {
                        return $"{Field} != null && {Field} == \"{Values[0]}\"";
                    }

                    case Operator.IEqual:
                    {
                        return $"{Field} != null && {Field}.ToLower() == \"{Values[0].ToLower()}\"";
                    }

                    case Operator.NotEqual:
                    {
                        return $"{Field} != null && {Field} != \"{Values[0]}\"";
                    }

                    case Operator.INotEqual:
                    {
                        return $"{Field} != null && {Field}.ToLower() != \"{Values[0].ToLower()}\"";
                    }

                    case Operator.Contains:
                    {
                        return $"{Field} != null && {Field}.Contains(\"{Values[0]}\")";
                    }

                    case Operator.IContains:
                    {
                        return $"{Field} != null && {Field}.ToLower().Contains(\"{Values[0].ToLower()}\")";
                    }

                    case Operator.NotContains:
                    {
                        return $"{Field} != null && !{Field}.Contains(\"{Values[0]}\")";
                    }

                    case Operator.INotContains:
                    {
                        return $"{Field} != null && !{Field}.ToLower().Contains(\"{Values[0].ToLower()}\")";
                    }

                    case Operator.StartsWith:
                    {
                        return $"{Field} != null && {Field}.StartsWith(\"{Values[0]}\")";
                    }

                    case Operator.IStartsWith:
                    {
                        return $"{Field} != null && {Field}.ToLower().StartsWith(\"{Values[0].ToLower()}\")";
                    }

                    case Operator.NotStartsWith:
                    {
                        return $"{Field} != null && !{Field}.StartsWith(\"{Values[0]}\")";
                    }

                    case Operator.INotStartsWith:
                    {
                        return $"{Field} != null && !{Field}.ToLower().StartsWith(\"{Values[0].ToLower()}\")";
                    }

                    case Operator.EndsWith:
                    {
                        return $"{Field} != null && {Field}.EndsWith(\"{Values[0]}\")";
                    }

                    case Operator.IEndsWith:
                    {
                        return $"{Field} != null && {Field}.ToLower().EndsWith(\"{Values[0].ToLower()}\")";
                    }

                    case Operator.NotEndsWith:
                    {
                        return $"{Field} != null && !{Field}.EndsWith(\"{Values[0]}\")";
                    }

                    case Operator.INotEndsWith:
                    {
                        return $"{Field} != null && !{Field}.ToLower().EndsWith(\"{Values[0].ToLower()}\")";
                    }

                    case Operator.In:
                    {
                        var conditions = Values.Select(value => $"{Field} == \"{value}\"");

                        return $"{Field} != null && ({string.Join(" || ", conditions)})";
                    }

                    case Operator.IIn:
                    {
                        var conditions = Values.Select(value => $"{Field}.ToLower() == \"{value.ToLower()}\"");

                        return $"{Field} != null && ({string.Join(" || ", conditions)})";
                    }

                    case Operator.NotIn:
                    {
                        var conditions = Values.Select(value => $"{Field} != \"{value}\"");

                        return $"{Field} != null && ({string.Join(" && ", conditions)})";
                    }

                    case Operator.INotIn:
                    {
                        var conditions = Values.Select(value => $"{Field}.ToLower() != \"{value.ToLower()}\"");

                        return $"{Field} != null && ({string.Join(" && ", conditions)})";
                    }

                    case Operator.IsNull:
                    {
                        return $"{Field} == null";
                    }

                    case Operator.IsNotNull:
                    {
                        return $"{Field} != null";
                    }
                }
            }
            break;

            case DataType.Number:
            {
                switch (Operator)
                {
                    case Operator.Equal:
                    {
                        return $"{Field} != null && {Field} == {Values[0]}";
                    }

                    case Operator.NotEqual:
                    {
                        return $"{Field} != null && {Field} != {Values[0]}";
                    }

                    case Operator.GreaterThan:
                    {
                        return $"{Field} != null && {Field} > {Values[0]}";
                    }

                    case Operator.GreaterThanOrEqual:
                    {
                        return $"{Field} != null && {Field} >= {Values[0]}";
                    }

                    case Operator.LessThan:
                    {
                        return $"{Field} != null && {Field} < {Values[0]}";
                    }

                    case Operator.LessThanOrEqual:
                    {
                        return $"{Field} != null && {Field} <= {Values[0]}";
                    }

                    case Operator.In:
                    {
                        var conditions = Values.Select(value => $"{Field} == {value}");

                        return $"{Field} != null && ({string.Join(" || ", conditions)})";
                    }

                    case Operator.NotIn:
                    {
                        var conditions = Values.Select(value => $"{Field} != {value}");

                        return $"{Field} != null && ({string.Join(" && ", conditions)})";
                    }

                    case Operator.Between:
                    {
                        return $"{Field} != null && {Field} >= {Values[0]} && {Field} <= {Values[1]}";
                    }

                    case Operator.NotBetween:
                    {
                        return $"{Field} != null && ({Field} < {Values[0]} || {Field} > {Values[1]})";
                    }

                    case Operator.IsNull:
                    {
                        return $"{Field} == null";
                    }

                    case Operator.IsNotNull:
                    {
                        return $"{Field} != null";
                    }
                }
            }
            break;

            case DataType.Boolean:
            {
                switch (Operator)
                {
                    case Operator.Equal:
                    {
                        return $"{Field} != null && {Field} == {Values[0]}";
                    }

                    case Operator.NotEqual:
                    {
                        return $"{Field} != null && {Field} != {Values[0]}";
                    }

                    case Operator.IsNull:
                    {
                        return $"{Field} == null";
                    }

                    case Operator.IsNotNull:
                    {
                        return $"{Field} != null";
                    }
                }
            }
            break;

            case DataType.DateTime:
            {
                switch (Operator)
                {
                    case Operator.Equal:
                    {
                        return $"{Field} != null && {Field} == \"{Values[0]}\"";
                    }

                    case Operator.NotEqual:
                    {
                        return $"{Field} != null && {Field} != \"{Values[0]}\"";
                    }

                    case Operator.GreaterThan:
                    {
                        return $"{Field} != null && {Field} > \"{Values[0]}\"";
                    }

                    case Operator.GreaterThanOrEqual:
                    {
                        return $"{Field} != null && {Field} >= \"{Values[0]}\"";
                    }

                    case Operator.LessThan:
                    {
                        return $"{Field} != null && {Field} < \"{Values[0]}\"";
                    }

                    case Operator.LessThanOrEqual:
                    {
                        return $"{Field} != null && {Field} <= \"{Values[0]}\"";
                    }

                    case Operator.Between:
                    {
                        return $"{Field} != null && {Field} >= \"{Values[0]}\" && {Field} <= \"{Values[1]}\"";
                    }

                    case Operator.NotBetween:
                    {
                        return $"{Field} != null && ({Field} < \"{Values[0]}\" || {Field} > \"{Values[1]}\")";
                    }

                    case Operator.IsNull:
                    {
                        return $"{Field} == null";
                    }

                    case Operator.IsNotNull:
                    {
                        return $"{Field} != null";
                    }
                }
            }
            break;

            case DataType.Date:
            {
                switch (Operator)
                {
                    case Operator.Equal:
                    {
                        return $"{Field} != null && {Field}.Date == \"{DateTime.Parse(Values[0]).Date}\"";
                    }

                    case Operator.NotEqual:
                    {
                        return $"{Field} != null && {Field}.Date != \"{DateTime.Parse(Values[0]).Date}\"";
                    }

                    case Operator.GreaterThan:
                    {
                        return $"{Field} != null && {Field}.Date > \"{DateTime.Parse(Values[0]).Date}\"";
                    }

                    case Operator.GreaterThanOrEqual:
                    {
                        return $"{Field} != null && {Field}.Date >= \"{DateTime.Parse(Values[0]).Date}\"";
                    }

                    case Operator.LessThan:
                    {
                        return $"{Field} != null && {Field}.Date < \"{DateTime.Parse(Values[0]).Date}\"";
                    }

                    case Operator.LessThanOrEqual:
                    {
                        return $"{Field} != null && {Field}.Date <= \"{DateTime.Parse(Values[0]).Date}\"";
                    }

                    case Operator.Between:
                    {
                        return $"{Field} != null && {Field}.Date >= \"{DateTime.Parse(Values[0]).Date}\" && {Field}.Date <= \"{DateTime.Parse(Values[1]).Date}\"";
                    }

                    case Operator.NotBetween:
                    {
                        return $"{Field} != null && ({Field}.Date < \"{DateTime.Parse(Values[0]).Date}\" || {Field}.Date > \"{DateTime.Parse(Values[1]).Date}\")";
                    }

                    case Operator.IsNull:
                    {
                        return $"{Field} == null";
                    }

                    case Operator.IsNotNull:
                    {
                        return $"{Field} != null";
                    }
                }
            }
            break;
        }

        return string.Empty;
    }

    /// <summary>
    /// Converts a ConditionGroup into its corresponding C# string representation.
    /// </summary>
    /// <typeparam name="T">The type to validate the conditions against.</typeparam>
    /// <param name="group">The ConditionGroup to convert.</param>
    /// <returns>The C# string representation of the ConditionGroup.</returns>
    public static string AsString<T>(this ConditionGroup group)
    {
        // Validate the ConditionGroup.
        group.Validate();

        // Define the connector based on the Connector enum.
        string connector = group.Connector switch
        {
            Connector.And => " && ",
            Connector.Or => " || ",
            _ => string.Empty
        };

        List<string> conditions = new();

        // Iterate through and convert each Condition in the group.
        foreach (Condition condition in group.Conditions.OrderBy(x => x.Sort))
        {
            string conditionAsString = condition.AsString<T>();

            // Add non-empty condition strings to the list.
            if (!string.IsNullOrWhiteSpace(conditionAsString))
            {
                conditions.Add(conditionAsString);
            }
        }

        // Iterate through and convert each sub ConditionGroup in the group.
        foreach (ConditionGroup subGroup in group.SubConditionGroups.OrderBy(x => x.Sort))
        {
            string subGroupConditionsAsString = subGroup.AsString<T>();

            // Add non-empty sub-group condition strings to the list.
            if (!string.IsNullOrWhiteSpace(subGroupConditionsAsString))
            {
                conditions.Add(subGroupConditionsAsString);
            }
        }

        // Combine conditions using the specified connector.
        if (conditions.Any())
        {
            return $"({string.Join(connector, conditions)})";
        }

        return string.Empty;
    }

    /// <summary>
    /// Converts an <see cref="OrderBy"/> instance into a string representation for SQL sorting.
    /// </summary>
    /// <typeparam name="T">The type to validate the field name against.</typeparam>
    /// <param name="order">The <see cref="OrderBy"/> instance to convert.</param>
    /// <returns>A string representation of the order-by clause for SQL sorting.</returns>
    /// <exception cref="LogicException">Thrown if the field name is invalid or empty.</exception>
    public static string AsString<T>(this OrderBy order)
    {
        order.Validate<T>();

        return $"{order.Field} {(order.Direction == Direction.Ascending ? "asc" : "desc")}";
    }
}
