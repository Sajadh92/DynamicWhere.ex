namespace DynamicWhere.ex;

/// <summary>
/// Provides methods for converting Condition and ConditionGroup objects into their C# string representations.
/// </summary>
internal static class Converter
{
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
    /// Converts a condition into its corresponding C# string representation.
    /// </summary>
    /// <typeparam name="T">The type to validate the field against.</typeparam>
    /// <param name="condition">The condition to convert.</param>
    /// <returns>The C# string representation of the condition.</returns>
    public static string AsString<T>(this Condition condition)
    {
        // Validate the condition.
        condition.Validate<T>();

        switch (condition.DataType)
        {
            case DataType.Guid:
            {
                switch (condition.Operator)
                {
                    case Operator.Equal:
                    {
                        return $"({condition.Field} != null && {condition.Field} == \"{condition.Values[0]}\")";
                    }

                    case Operator.NotEqual:
                    {
                        return $"({condition.Field} != null && {condition.Field} != \"{condition.Values[0]}\")";
                    }

                    case Operator.In:
                    {
                        var conditions = condition.Values.Select(value => $"{condition.Field} == \"{value}\"");

                        return $"({condition.Field} != null && ({string.Join(" || ", conditions)}))";
                    }

                    case Operator.NotIn:
                    {
                        var conditions = condition.Values.Select(value => $"{condition.Field} != \"{value}\"");

                        return $"({condition.Field} != null && ({string.Join(" && ", conditions)}))";
                    }

                    case Operator.IsNull:
                    {
                        return $"({condition.Field} == null)";
                    }

                    case Operator.IsNotNull:
                    {
                        return $"({condition.Field} != null)";
                    }
                }
            }
            break;

            case DataType.Text:
            {
                switch (condition.Operator)
                {
                    case Operator.Equal:
                    {
                        return $"({condition.Field} != null && {condition.Field} == \"{condition.Values[0]}\")";
                    }

                    case Operator.IEqual:
                    {
                        return $"({condition.Field} != null && {condition.Field}.ToLower() == \"{condition.Values[0].ToLower()}\")";
                    }

                    case Operator.NotEqual:
                    {
                        return $"({condition.Field} != null && {condition.Field} != \"{condition.Values[0]}\")";
                    }

                    case Operator.INotEqual:
                    {
                        return $"({condition.Field} != null && {condition.Field}.ToLower() != \"{condition.Values[0].ToLower()}\")";
                    }

                    case Operator.Contains:
                    {
                        return $"({condition.Field} != null && {condition.Field}.Contains(\"{condition.Values[0]}\"))";
                    }

                    case Operator.IContains:
                    {
                        return $"({condition.Field} != null && {condition.Field}.ToLower().Contains(\"{condition.Values[0].ToLower()}\"))";
                    }

                    case Operator.NotContains:
                    {
                        return $"({condition.Field} != null && !{condition.Field}.Contains(\"{condition.Values[0]}\"))";
                    }

                    case Operator.INotContains:
                    {
                        return $"({condition.Field} != null && !{condition.Field}.ToLower().Contains(\"{condition.Values[0].ToLower()}\"))";
                    }

                    case Operator.StartsWith:
                    {
                        return $"({condition.Field} != null && {condition.Field}.StartsWith(\"{condition.Values[0]}\"))";
                    }

                    case Operator.IStartsWith:
                    {
                        return $"({condition.Field} != null && {condition.Field}.ToLower().StartsWith(\"{condition.Values[0].ToLower()}\"))";
                    }

                    case Operator.NotStartsWith:
                    {
                        return $"({condition.Field} != null && !{condition.Field}.StartsWith(\"{condition.Values[0]}\"))";
                    }

                    case Operator.INotStartsWith:
                    {
                        return $"({condition.Field} != null && !{condition.Field}.ToLower().StartsWith(\"{condition.Values[0].ToLower()}\"))";
                    }

                    case Operator.EndsWith:
                    {
                        return $"({condition.Field} != null && {condition.Field}.EndsWith(\"{condition.Values[0]}\"))";
                    }

                    case Operator.IEndsWith:
                    {
                        return $"({condition.Field} != null && {condition.Field}.ToLower().EndsWith(\"{condition.Values[0].ToLower()}\"))";
                    }

                    case Operator.NotEndsWith:
                    {
                        return $"({condition.Field} != null && !{condition.Field}.EndsWith(\"{condition.Values[0]}\"))";
                    }

                    case Operator.INotEndsWith:
                    {
                        return $"({condition.Field} != null && !{condition.Field}.ToLower().EndsWith(\"{condition.Values[0].ToLower()}\"))";
                    }

                    case Operator.In:
                    {
                        var conditions = condition.Values.Select(value => $"{condition.Field} == \"{value}\"");

                        return $"({condition.Field} != null && ({string.Join(" || ", conditions)}))";
                    }

                    case Operator.IIn:
                    {
                        var conditions = condition.Values.Select(value => $"{condition.Field}.ToLower() == \"{value.ToLower()}\"");

                        return $"({condition.Field} != null && ({string.Join(" || ", conditions)}))";
                    }

                    case Operator.NotIn:
                    {
                        var conditions = condition.Values.Select(value => $"{condition.Field} != \"{value}\"");

                        return $"({condition.Field} != null && ({string.Join(" && ", conditions)}))";
                    }

                    case Operator.INotIn:
                    {
                        var conditions = condition.Values.Select(value => $"{condition.Field}.ToLower() != \"{value.ToLower()}\"");

                        return $"({condition.Field} != null && ({string.Join(" && ", conditions)}))";
                    }

                    case Operator.IsNull:
                    {
                        return $"({condition.Field} == null)";
                    }

                    case Operator.IsNotNull:
                    {
                        return $"({condition.Field} != null)";
                    }
                }
            }
            break;

            case DataType.Number:
            {
                switch (condition.Operator)
                {
                    case Operator.Equal:
                    {
                        return $"({condition.Field} != null && {condition.Field} == {condition.Values[0]})";
                    }

                    case Operator.NotEqual:
                    {
                        return $"({condition.Field} != null && {condition.Field} != {condition.Values[0]})";
                    }

                    case Operator.GreaterThan:
                    {
                        return $"({condition.Field} != null && {condition.Field} > {condition.Values[0]})";
                    }

                    case Operator.GreaterThanOrEqual:
                    {
                        return $"({condition.Field} != null && {condition.Field} >= {condition.Values[0]})";
                    }

                    case Operator.LessThan:
                    {
                        return $"({condition.Field} != null && {condition.Field} < {condition.Values[0]})";
                    }

                    case Operator.LessThanOrEqual:
                    {
                        return $"({condition.Field} != null && {condition.Field} <= {condition.Values[0]})";
                    }

                    case Operator.In:
                    {
                        var conditions = condition.Values.Select(value => $"{condition.Field} == {value}");

                        return $"({condition.Field} != null && ({string.Join(" || ", conditions)}))";
                    }

                    case Operator.NotIn:
                    {
                        var conditions = condition.Values.Select(value => $"{condition.Field} != {value}");

                        return $"({condition.Field} != null && ({string.Join(" && ", conditions)}))";
                    }

                    case Operator.Between:
                    {
                        return $"({condition.Field} != null && {condition.Field} >= {condition.Values[0]} && {condition.Field} <= {condition.Values[1]})";
                    }

                    case Operator.NotBetween:
                    {
                        return $"({condition.Field} != null && ({condition.Field} < {condition.Values[0]} || {condition.Field} > {condition.Values[1]}))";
                    }

                    case Operator.IsNull:
                    {
                        return $"({condition.Field} == null)";
                    }

                    case Operator.IsNotNull:
                    {
                        return $"({condition.Field} != null)";
                    }
                }
            }
            break;

            case DataType.Boolean:
            {
                switch (condition.Operator)
                {
                    case Operator.Equal:
                    {
                        return $"({condition.Field} != null && {condition.Field} == {condition.Values[0]})";
                    }

                    case Operator.NotEqual:
                    {
                        return $"({condition.Field} != null && {condition.Field} != {condition.Values[0]})";
                    }

                    case Operator.IsNull:
                    {
                        return $"({condition.Field} == null)";
                    }

                    case Operator.IsNotNull:
                    {
                        return $"({condition.Field} != null)";
                    }
                }
            }
            break;

            case DataType.DateTime:
            {
                switch (condition.Operator)
                {
                    case Operator.Equal:
                    {
                        return $"({condition.Field} != null && {condition.Field} == \"{condition.Values[0]}\")";
                    }

                    case Operator.NotEqual:
                    {
                        return $"({condition.Field} != null && {condition.Field} != \"{condition.Values[0]}\")";
                    }

                    case Operator.GreaterThan:
                    {
                        return $"({condition.Field} != null && {condition.Field} > \"{condition.Values[0]}\")";
                    }

                    case Operator.GreaterThanOrEqual:
                    {
                        return $"({condition.Field} != null && {condition.Field} >= \"{condition.Values[0]}\")";
                    }

                    case Operator.LessThan:
                    {
                        return $"({condition.Field} != null && {condition.Field} < \"{condition.Values[0]}\")";
                    }

                    case Operator.LessThanOrEqual:
                    {
                        return $"({condition.Field} != null && {condition.Field} <= \"{condition.Values[0]}\")";
                    }

                    case Operator.Between:
                    {
                        return $"({condition.Field} != null && {condition.Field} >= \"{condition.Values[0]}\" && {condition.Field} <= \"{condition.Values[1]}\")";
                    }

                    case Operator.NotBetween:
                    {
                        return $"({condition.Field} != null && ({condition.Field} < \"{condition.Values[0]}\" || {condition.Field} > \"{condition.Values[1]}\"))";
                    }

                    case Operator.IsNull:
                    {
                        return $"({condition.Field} == null)";
                    }

                    case Operator.IsNotNull:
                    {
                        return $"({condition.Field} != null)";
                    }
                }
            }
            break;
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
