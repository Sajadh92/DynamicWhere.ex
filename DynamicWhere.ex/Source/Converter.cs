using System.Collections;

namespace DynamicWhere.ex;

/// <summary>
/// Provides conversion helpers that translate <see cref="Condition"/>, <see cref="ConditionGroup"/>,
/// and <see cref="OrderBy"/> instances into dynamic LINQ expression strings.
/// These strings are intended to be consumed by System.Linq.Dynamic.Core (e.g., IQueryable.Where(string)).
/// </summary>
internal static class Converter
{
    /// <summary>
    /// Converts a <see cref="Condition"/> into a dynamic LINQ predicate string.
    /// Supports navigation through nested properties and collections, automatically
    /// inserting Any(...) for collection navigations.
    /// </summary>
    /// <typeparam name="T">The root entity type being queried.</typeparam>
    /// <param name="condition">The condition to convert.</param>
    /// <param name="provider">Database provider hint used to optimize case-insensitive comparison generation.</param>
    /// <returns>
    /// A dynamic LINQ predicate string enclosed in parentheses, suitable for <c>Where(string)</c>.
    /// </returns>
    /// <exception cref="LogicException">Thrown if the condition contains invalid data.</exception>
    public static string AsString<T>(this Condition condition, Provider? provider = Provider.Others)
    {
        // Validate the condition.
        condition.Validate<T>();

        // Split the field into individual property names.
        string[] props = condition.Field!.Trim().Replace(" ", "").Split('.')
;
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

                conditionAsString = conditionAsString[..^last.Length];

                conditionAsString += Builder.BuildCondition(condition.DataType, condition.Operator, last, condition.Values, provider);
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
    /// Converts a <see cref="ConditionGroup"/> into its dynamic LINQ predicate string representation.
    /// Child conditions and subgroups are combined using the group's <see cref="Connector"/>.
    /// </summary>
    /// <typeparam name="T">The root entity type to validate the conditions against.</typeparam>
    /// <param name="group">The <see cref="ConditionGroup"/> to convert.</param>
    /// <param name="provider">Database provider hint used to optimize case-insensitive comparison generation.</param>
    /// <returns>
    /// A dynamic LINQ predicate string enclosed in parentheses, or an empty string when the group has no conditions.
    /// </returns>
    /// <exception cref="LogicException">Thrown when the group or its members are invalid (e.g., duplicate Sort values).</exception>
    public static string AsString<T>(this ConditionGroup group, Provider? provider = Provider.Others)
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
            string conditionAsString = condition.AsString<T>(provider);

            // Add non-empty condition strings to the list.
            if (!string.IsNullOrWhiteSpace(conditionAsString))
            {
                conditions.Add(conditionAsString);
            }
        }

        // Iterate through and convert each sub ConditionGroup in the group.
        foreach (ConditionGroup subGroup in group.SubConditionGroups.OrderBy(x => x.Sort))
        {
            string subGroupConditionsAsString = subGroup.AsString<T>(provider);

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
    /// Converts an <see cref="OrderBy"/> instance into a dynamic LINQ order-by expression string.
    /// </summary>
    /// <typeparam name="T">The type used to validate the field name.</typeparam>
    /// <param name="order">The <see cref="OrderBy"/> instance to convert.</param>
    /// <returns>
    /// A dynamic LINQ order-by expression such as <c>"Name asc"</c> or <c>"Age desc"</c>.
    /// </returns>
    /// <exception cref="LogicException">Thrown if the field name is invalid or empty.</exception>
    public static string AsString<T>(this OrderBy order)
    {
        order.Validate<T>();

        return $"{order.Field} {(order.Direction == Direction.Ascending ? "asc" : "desc")}";
    }
}
