using System.Reflection;

namespace DynamicWhere.ex;

/// <summary>
/// Validation helpers for dynamic query components.
/// Ensures shape, value counts, formats, and field paths are valid before building expressions.
/// </summary>
internal static class Validator
{
    /// <summary>
    /// Validates a <see cref="Condition"/> for correctness against the entity type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The root type on which the condition's field path is validated.</typeparam>
    /// <param name="condition">The <see cref="Condition"/> instance to validate.</param>
    /// <exception cref="LogicException">
    /// Thrown when the field is missing/invalid, value counts don't match the <see cref="Operator"/>,
    /// or values are not in a valid format for the specified <see cref="DataType"/>.
    /// </exception>
    /// <remarks>
    /// This method also normalizes <see cref="Condition.Field"/> to the exact property casing/path
    /// found on <typeparamref name="T"/> (e.g., <c>"email"</c> becomes <c>"Email"</c>).
    /// </remarks>
    public static void Validate<T>(this Condition condition)
    {
        // Check if the field name is provided and not empty.
        if (string.IsNullOrWhiteSpace(condition.Field))
        {
            throw new LogicException(ErrorCode.InvalidField);
        }

        // Validate and normalize the field name against the specified type.
        condition.Field = condition.Field.Validate<T>();

        // Handle validation based on the operator type.
        switch (condition.Operator)
        {
            case Operator.Between:
            case Operator.NotBetween:
                // For Between and NotBetween operators, exactly two values are required.
                if (condition.Values.Count != 2)
                {
                    throw new LogicException(ErrorCode.RequiredTwoValue);
                }
                break;

            case Operator.In:
            case Operator.NotIn:
            case Operator.IIn:
            case Operator.INotIn:
                // For In, NotIn, IIn, and INotIn operators, at least one value is required.
                if (condition.Values.Count == 0)
                {
                    throw new LogicException(ErrorCode.RequiredValues);
                }
                break;

            case Operator.IsNull:
            case Operator.IsNotNull:
                // For IsNull and IsNotNull operators, values are not required.
                if (condition.Values.Count != 0)
                {
                    throw new LogicException(ErrorCode.NotRequiredValues);
                }
                break;

            default:
                // For other operators, exactly one value is required.
                if (condition.Values.Count != 1)
                {
                    throw new LogicException(ErrorCode.RequiredOneValue(condition.Operator.ToString()));
                }
                break;
        }

        // Validate the values format based on the declared logical data type.
        switch (condition.DataType)
        {
            case DataType.Guid:
                // For GUID fields, each value must be a valid GUID format.
                if (condition.Values.Any(value => !Guid.TryParse(value, out _)))
                {
                    throw new LogicException(ErrorCode.InvalidFormat);
                }
                break;

            case DataType.Number:
                // For numeric fields, each value must parse into a supported numeric type.
                if (condition.Values.Any(value =>
                    !byte.TryParse(value, out _) &&
                    !short.TryParse(value, out _) &&
                    !int.TryParse(value, out _) &&
                    !long.TryParse(value, out _) &&
                    !float.TryParse(value, out _) &&
                    !double.TryParse(value, out _) &&
                    !decimal.TryParse(value, out _)))
                {
                    throw new LogicException(ErrorCode.InvalidFormat);
                }
                break;

            case DataType.Boolean:
                // For boolean fields, each value must be a valid boolean format.
                if (condition.Values.Any(value => !bool.TryParse(value, out _)))
                {
                    throw new LogicException(ErrorCode.InvalidFormat);
                }
                break;

            case DataType.Date:
            case DataType.DateTime:
                // For date/datetime fields, each value must be a valid date/time format.
                if (condition.Values.Any(value => !DateTime.TryParse(value, out _)))
                {
                    throw new LogicException(ErrorCode.InvalidFormat);
                }
                break;
        }
    }

    /// <summary>
    /// Validates a <see cref="ConditionGroup"/> to ensure it has no duplicate sort orders
    /// among its conditions or its sub-condition groups.
    /// </summary>
    /// <param name="group">The <see cref="ConditionGroup"/> instance to validate.</param>
    /// <exception cref="LogicException">
    /// Thrown when duplicate <c>Sort</c> values are detected in <see cref="ConditionGroup.Conditions"/>
    /// or <see cref="ConditionGroup.SubConditionGroups"/>.
    /// </exception>
    public static void Validate(this ConditionGroup group)
    {
        // Check for duplicate sorting values among conditions within the group.
        bool hasDuplicateSort = group.Conditions.GroupBy(x => x.Sort).Any(x => x.Count() > 1);

        if (hasDuplicateSort)
        {
            throw new LogicException(ErrorCode.ConditionsUniqueSort);
        }

        // Check for duplicate sorting values among sub-condition groups within the group.
        hasDuplicateSort = group.SubConditionGroups.GroupBy(x => x.Sort).Any(x => x.Count() > 1);

        if (hasDuplicateSort)
        {
            throw new LogicException(ErrorCode.SubConditionsGroupsUniqueSort);
        }
    }

    /// <summary>
    /// Validates an <see cref="OrderBy"/> to ensure it has a non-empty, valid field for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The root type used to validate the field path.</typeparam>
    /// <param name="order">The <see cref="OrderBy"/> instance to validate.</param>
    /// <exception cref="LogicException">Thrown if the field is missing or invalid for <typeparamref name="T"/>.</exception>
    /// <remarks>On success, <see cref="OrderBy.Field"/> is normalized to the entity's exact property casing/path.</remarks>
    public static void Validate<T>(this OrderBy order)
    {
        // Check if the field name is provided and not empty.
        if (string.IsNullOrWhiteSpace(order.Field))
        {
            throw new LogicException(ErrorCode.InvalidField);
        }

        // Validate the field name against the specified type.
        order.Field = order.Field.Validate<T>();
    }

    /// <summary>
    /// Validates pagination parameters.
    /// </summary>
    /// <typeparam name="T">Unused type parameter; present for a consistent generic extension signature.</typeparam>
    /// <param name="page">The paging configuration to validate.</param>
    /// <exception cref="LogicException">Thrown when page number or page size is negative.</exception>
    /// <remarks>PageNumber and PageSize must be non-negative integers.</remarks>
    public static void Validate<T>(this PageBy page)
    {
        // Check page number is a non-negative integer.
        if (page.PageNumber < 0)
        {
            throw new LogicException(ErrorCode.InvalidPageNumber);
        }

        // Check page size is a non-negative integer.
        if (page.PageSize < 0)
        {
            throw new LogicException(ErrorCode.InvalidPageSize);
        }
    }

    /// <summary>
    /// Validates the condition sets within a <see cref="Segment"/> and returns them ordered by <c>Sort</c>.
    /// Also ensures required intersections are present and clears the first set's intersection.
    /// </summary>
    /// <param name="segment">The <see cref="Segment"/> instance containing the condition sets to validate.</param>
    /// <returns>A list of valid condition sets ordered by <c>Sort</c>.</returns>
    /// <exception cref="LogicException">
    /// Thrown when duplicate set <c>Sort</c> values exist or when any set after the first is missing
    /// an <see cref="Intersection"/> value.
    /// </exception>
    public static List<ConditionSet> ValidateAndGetSets(this Segment segment)
    {
        if (segment.ConditionSets.Count == 0)
        {
            return new List<ConditionSet>();
        }

        // Check for duplicate sorting values among condition sets.
        bool hasDuplicateSort = segment.ConditionSets.GroupBy(x => x.Sort).Any(x => x.Count() > 1);

        if (hasDuplicateSort)
        {
            throw new LogicException(ErrorCode.SetsUniqueSort);
        }

        // Check if any condition set except the first one has a null intersection.
        bool hasNullIntersection = segment.ConditionSets.OrderBy(x => x.Sort).Skip(1).Any(x => x.Intersection == null);

        if (hasNullIntersection)
        {
            throw new LogicException(ErrorCode.RequiredIntersection);
        }

        // Sort the condition sets by their sorting values.
        List<ConditionSet> sets = segment.ConditionSets.OrderBy(x => x.Sort).ToList();

        // Set the intersection of the first condition set to null to represent the absence of intersection.
        sets[0].Intersection = null;

        return sets;
    }

    /// <summary>
    /// Validates a property path for <typeparamref name="T"/> and returns the normalized path.
    /// Supports nested navigation using dot notation and normalizes each segment to the exact property name.
    /// For a List&lt;T&gt; property, the next segment is validated against the element type.
    /// </summary>
    /// <typeparam name="T">The root type in which the property path is validated.</typeparam>
    /// <param name="name">The property path to validate (e.g., <c>"Order.Customer.Name"</c>).</param>
    /// <returns>The validated, normalized property path.</returns>
    /// <exception cref="LogicException">Thrown when the path is empty or any segment is not found.</exception>
    public static string Validate<T>(this string name)
    {
        // Split the property name by dots if it contains any, handling leading/trailing whitespaces and empty entries.
        List<string> names = name.Contains('.')
            ? name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : new List<string> { name };

        // Ensure that at least one valid property name is provided.
        if (names.Count == 0)
        {
            throw new LogicException(ErrorCode.InvalidField);
        }

        Type type = typeof(T);

        List<string> validatedNames = new();

        // Iterate through nested property names and validate each level.
        foreach (string propertyName in names)
        {
            // Attempt to retrieve the PropertyInfo for the current property name
            // from the current type with case-insensitive comparison.
            PropertyInfo? prop = type.GetProperties()
                .FirstOrDefault(x => x.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));

            // If the property is not found, throw an exception indicating an invalid field.
            if (prop == null)
            {
                throw new LogicException(ErrorCode.InvalidField);
            }

            validatedNames.Add(prop.Name);

            // Update the current type to the property's type, considering generic List<> types.
            type = prop.PropertyType;

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                type = type.GetGenericArguments()[0];
            }
        }

        // Return the validated property name as a single string.
        return string.Join('.', validatedNames);
    }
}
