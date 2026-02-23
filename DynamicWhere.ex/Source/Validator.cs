using DynamicWhere.ex.Classes;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Optimization.Cache.Source;
using Microsoft.VisualBasic.FileIO;

namespace DynamicWhere.ex.Source;

/// <summary>
/// Validation helpers for dynamic query components.
/// Ensures shape, value counts, formats, and field paths are valid before building expressions.
/// Uses caching for improved performance with frequently used types.
/// </summary>
internal static class Validator
{
    /// <summary>
    /// Validates a property path for <typeparamref name="T"/> and returns the normalized path.
    /// Supports nested navigation using dot notation and normalizes each segment to the exact property name.
    /// For collection properties, the next segment is validated against the element type.
    /// Uses caching for improved performance with frequently used types.
    /// </summary>
    /// <typeparam name="T">The root type in which the property path is validated.</typeparam>
    /// <param name="name">The property path to validate (e.g., <c>"Order.Customer.Name"</c>).</param>
    /// <returns>The validated, normalized property path.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null or whiteSpace.</exception>
    /// <exception cref="LogicException">Thrown when the path is empty or any segment is not found.</exception>
    public static string Validate<T>(this string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentNullException(nameof(name));
        }

        // Use the cached validation method for improved performance
        return CacheReflection.ValidatePropertyPath(typeof(T), name);
    }

    /// <summary>
    /// Validates a <see cref="Condition"/> for correctness against the entity type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The root type on which the condition's field path is validated.</typeparam>
    /// <param name="condition">The <see cref="Condition"/> instance to validate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="condition"/> is null.</exception>
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
        if (condition == null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        condition.Values ??= new List<string>();

        // Check if the field name is provided and not empty.
        if (string.IsNullOrWhiteSpace(condition.Field))
        {
            throw new LogicException(ErrorCode.InvalidField);
        }

        // Validate and normalize the field name against the specified type using cache.
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
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="group"/> is null.</exception>
    /// <exception cref="LogicException">
    /// Thrown when duplicate <c>Sort</c> values are detected in <see cref="ConditionGroup.Conditions"/>
    /// or <see cref="ConditionGroup.SubConditionGroups"/>.
    /// </exception>
    public static void Validate(this ConditionGroup group)
    {
        if (group == null)
        {
            throw new ArgumentNullException(nameof(group));
        }

        group.Conditions ??= new List<Condition>();

        group.SubConditionGroups ??= new List<ConditionGroup>();

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
    /// Validates an <see cref="AggregateBy"/> to ensure it has a non-empty, valid field for <typeparamref name="T"/>,
    /// a valid alias, and an aggregator that is supported for the field type.
    /// Also ensures the field is a simple type and not a collection.
    /// </summary>
    /// <typeparam name="T">The root type used to validate the field path.</typeparam>
    /// <param name="aggregate">The <see cref="AggregateBy"/> instance to validate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="aggregate"/> is null.</exception>
    /// <exception cref="LogicException">
    /// Thrown if the field is missing/invalid, alias is missing, field is a complex type,
    /// field is a collection type, or the aggregator is not supported for the field type.
    /// </exception>
    /// <remarks>On success, <see cref="AggregateBy.Field"/> is normalized to the entity's exact property casing/path.</remarks>
    public static void Validate<T>(this AggregateBy aggregate)
    {
        if (aggregate == null)
        {
            throw new ArgumentNullException(nameof(aggregate));
        }

        // Check if the alias is provided and not empty.
        // Check if the alias contains invalid characters (e.g., dot notation is not allowed in aliases).
        if (string.IsNullOrWhiteSpace(aggregate.Alias) || aggregate.Alias.Contains('.'))
        {
            throw new LogicException(ErrorCode.InvalidAlias);
        }

        // Count aggregator can work without a field (just count items in the group)
        if (string.IsNullOrWhiteSpace(aggregate.Field) &&
            // Only Count aggregator is allowed without a field
            aggregate.Aggregator != Aggregator.Count)
        {
            throw new LogicException(ErrorCode.InvalidField);
        }

        // Validate and normalize the field name against the specified type using cache.
        // If the field is not empty, validate it.
        // If it is empty, it will be treated as a count of items in the group.
        if (!string.IsNullOrWhiteSpace(aggregate.Field))
        {
            aggregate.Field = aggregate.Field.Validate<T>();

            // Get the field type using cached reflection to validate the aggregator
            var fieldType = CacheReflection.GetFieldType(typeof(T), aggregate.Field);

            // Check if the field is a collection type
            if (CacheReflection.IsCollectionType(fieldType))
            {
                throw new LogicException(ErrorCode.AggregationFieldCannotBeCollection);
            }

            // Check if the field is a simple type (primitive, string, DateTime, Guid, etc.)
            if (!CacheReflection.IsSimpleType(fieldType))
            {
                throw new LogicException(ErrorCode.AggregationFieldMustBeSimpleType);
            }

            // Validate that the aggregator is supported for the field type
            ValidateAggregatorForType(aggregate.Aggregator, fieldType);
        }
        else
        {
            // If no field is specified, only Count aggregator is valid,
            // and it can be applied to any type.
            ValidateAggregatorForType(aggregate.Aggregator, null);
        }

    }

    /// <summary>
    /// Validates that the aggregator is supported for the given field type.
    /// </summary>
    /// <param name="aggregator">The aggregator to validate.</param>
    /// <param name="fieldType">The field type to validate against.</param>
    /// <exception cref="LogicException">Thrown when the aggregator is not supported for the field type.</exception>
    private static void ValidateAggregatorForType(Aggregator aggregator, Type? fieldType)
    {
        // Unwrap nullable types to get the underlying type
        Type? underlyingType = fieldType == null ? null :
            Nullable.GetUnderlyingType(fieldType) ?? fieldType;

        switch (aggregator)
        {
            case Aggregator.Sumation:
            case Aggregator.Average:
                // Sum and Average are only supported for numeric types
                if (underlyingType != null && 
                    !CacheReflection.IsNumericType(underlyingType))
                {
                    throw new LogicException(ErrorCode.UnsupportedAggregatorForType
                        (aggregator.ToString(), fieldType?.Name ?? string.Empty));
                }
                break;

            case Aggregator.Minimum:
            case Aggregator.Maximum:
                // Min and Max are not supported for boolean types
                if (underlyingType != null && 
                    underlyingType == typeof(bool))
                {
                    throw new LogicException(ErrorCode.UnsupportedAggregatorForType
                        (aggregator.ToString(), fieldType?.Name ?? string.Empty));
                }
                break;

            case Aggregator.Count:
            case Aggregator.CountDistinct:
            case Aggregator.FirstOrDefault:
            case Aggregator.LastOrDefault:
                // These aggregators are supported for all types
                break;

            default:
                throw new LogicException(ErrorCode.UnsupportedAggregatorForType
                   (aggregator.ToString(), fieldType?.Name ?? string.Empty));
        }
    }

    /// <summary>
    /// Validates a <see cref="GroupBy"/> to ensure it has valid grouping fields and aggregations.
    /// </summary>
    /// <typeparam name="T">The root type used to validate field paths.</typeparam>
    /// <param name="groupBy">The <see cref="GroupBy"/> instance to validate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="groupBy"/> is null.</exception>
    /// <exception cref="LogicException">
    /// Thrown when:
    /// - Fields list is empty
    /// - Fields list contains duplicates
    /// - Any field is invalid, complex type, or collection type
    /// - Any aggregation alias matches a GroupBy field
    /// - Aggregation aliases are not unique
    /// - Any aggregation validation fails
    /// </exception>
    /// <remarks>
    /// On success, all field names are normalized to the entity's exact property casing/path.
    /// </remarks>
    public static void Validate<T>(this GroupBy groupBy)
    {
        if (groupBy == null)
        {
            throw new ArgumentNullException(nameof(groupBy));
        }

        groupBy.Fields ??= new List<string>();
        groupBy.AggregateBy ??= new List<AggregateBy>();

        // Check if at least one field is provided
        if (groupBy.Fields.Count == 0)
        {
            throw new LogicException(ErrorCode.GroupByMustHaveFields);
        }

        // Validate each field and normalize them
        var normalizedFields = new List<string>();
        var normalizedFieldsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < groupBy.Fields.Count; i++)
        {
            var field = groupBy.Fields[i];

            // Check if the field name is provided and not empty
            if (string.IsNullOrWhiteSpace(field))
            {
                throw new LogicException(ErrorCode.InvalidField);
            }

            // Validate and normalize the field name against the specified type using cache
            var normalizedField = field.Validate<T>();

            // Check for duplicate fields (case-insensitive)
            if (!normalizedFieldsSet.Add(normalizedField))
            {
                throw new LogicException(ErrorCode.GroupByFieldsMustBeUnique);
            }

            // Get the field type using cached reflection
            var fieldType = CacheReflection.GetFieldType(typeof(T), normalizedField);

            // Check if the field is a collection type
            if (CacheReflection.IsCollectionType(fieldType))
            {
                throw new LogicException(ErrorCode.GroupByFieldCannotBeCollection);
            }

            // Check if the field is a simple type (primitive, string, DateTime, Guid, etc.)
            if (!CacheReflection.IsSimpleType(fieldType))
            {
                throw new LogicException(ErrorCode.GroupByFieldCannotBeComplexType);
            }

            normalizedFields.Add(normalizedField);
        }

        // Update the fields with normalized names
        groupBy.Fields = normalizedFields;

        // Validate aggregations
        if (groupBy.AggregateBy.Count > 0)
        {
            var aliasSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var aggregate in groupBy.AggregateBy)
            {
                // Validate the aggregation using the existing validation method
                aggregate.Validate<T>();

                // Check if the aggregation alias is in the GroupBy fields list
                if (normalizedFieldsSet.Contains(aggregate.Alias!))
                {
                    throw new LogicException(ErrorCode.AggregationAliasCannotBeGroupByField(aggregate.Alias!));
                }

                // Check for duplicate aliases (case-insensitive)
                if (!aliasSet.Add(aggregate.Alias!))
                {
                    throw new LogicException(ErrorCode.AggregationAliasesMustBeUnique);
                }
            }
        }
    }

    /// <summary>
    /// Validates an <see cref="OrderBy"/> to ensure it has a non-empty, valid field for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The root type used to validate the field path.</typeparam>
    /// <param name="order">The <see cref="OrderBy"/> instance to validate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="order"/> is null.</exception>
    /// <exception cref="LogicException">Thrown if the field is missing or invalid for <typeparamref name="T"/>.</exception>
    /// <remarks>On success, <see cref="OrderBy.Field"/> is normalized to the entity's exact property casing/path.</remarks>
    public static void Validate<T>(this OrderBy order)
    {
        if (order == null)
        {
            throw new ArgumentNullException(nameof(order));
        }

        // Check if the field name is provided and not empty.
        if (string.IsNullOrWhiteSpace(order.Field))
        {
            throw new LogicException(ErrorCode.InvalidField);
        }

        // Validate the field name against the specified type using cache.
        order.Field = order.Field.Validate<T>();
    }

    /// <summary>
    /// Validates pagination parameters.
    /// </summary>
    /// <typeparam name="T">Unused type parameter; present for a consistent generic extension signature.</typeparam>
    /// <param name="page">The paging configuration to validate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when page number or page size is invalid.</exception>
    /// <remarks>PageNumber must be greater than 0 and PageSize must be greater than 0.</remarks>
    public static void Validate<T>(this PageBy page)
    {
        if (page == null)
        {
            throw new ArgumentNullException(nameof(page));
        }

        // Check page number is a positive integer (starts from 1).
        if (page.PageNumber <= 0)
        {
            throw new LogicException(ErrorCode.InvalidPageNumber);
        }

        // Check page size is a positive integer.
        if (page.PageSize <= 0)
        {
            throw new LogicException(ErrorCode.InvalidPageSize);
        }
    }

    /// <summary>
    /// Validates a <see cref="SummaryRequest"/> to ensure it has a valid group-by configuration
    /// and that any order fields reference valid group-by fields or aggregate-by aliases.
    /// </summary>
    /// <typeparam name="T">The root type used to validate field paths.</typeparam>
    /// <param name="summaryRequest">The <see cref="SummaryRequest"/> instance to validate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="summaryRequest"/> or its GroupBy is null.</exception>
    /// <exception cref="LogicException">
    /// Thrown when:
    /// - GroupBy validation fails
    /// - Any order field does not exist in the group-by fields or aggregate-by aliases
    /// - Page validation fails
    /// </exception>
    public static void Validate<T>(this SummaryRequest summaryRequest)
    {
        if (summaryRequest == null)
        {
            throw new ArgumentNullException(nameof(summaryRequest));
        }

        // GroupBy is required for SummaryRequest.
        if (summaryRequest.GroupBy == null)
        {
            throw new ArgumentNullException(nameof(summaryRequest.GroupBy));
        }

        // Validate GroupBy (validates fields and aggregations against T).
        summaryRequest.GroupBy.Validate<T>();

        // Validate Orders - each order field must exist in GroupBy fields or AggregateBy aliases.
        if (summaryRequest.Orders != null && summaryRequest.Orders.Count > 0)
        {
            // Build the set of valid order fields from GroupBy fields and AggregateBy aliases.
            var validFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add GroupBy fields in both the original dotted form (e.g. "CreatedAt.Year")
            // and the dot-stripped alias form (e.g. "CreatedAtYear") that the Select projection emits.
            foreach (var field in summaryRequest.GroupBy.Fields)
            {
                validFields.Add(field);
                validFields.Add(field.Replace(".", ""));
            }

            // Add AggregateBy aliases.
            foreach (var aggregate in summaryRequest.GroupBy.AggregateBy)
            {
                if (!string.IsNullOrWhiteSpace(aggregate.Alias))
                {
                    validFields.Add(aggregate.Alias);
                }
            }

            // Validate each order field.
            foreach (var order in summaryRequest.Orders)
            {
                if (order == null)
                {
                    throw new ArgumentNullException(nameof(order));
                }

                if (string.IsNullOrWhiteSpace(order.Field))
                {
                    throw new LogicException(ErrorCode.InvalidField);
                }

                if (!validFields.Contains(order.Field))
                {
                    throw new LogicException(ErrorCode.SummaryOrderFieldMustExistInGroupByOrAggregate(order.Field));
                }
            }
        }

        // Validate Page if provided.
        summaryRequest.Page?.Validate<T>();
    }

    /// <summary>
    /// Validates the condition sets within a <see cref="Segment"/> and returns them ordered by <c>Sort</c>.
    /// Also ensures required intersections are present and clears the first set's intersection.
    /// </summary>
    /// <param name="segment">The <see cref="Segment"/> instance containing the condition sets to validate.</param>
    /// <returns>A list of valid condition sets ordered by <c>Sort</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="segment"/> is null.</exception>
    /// <exception cref="LogicException">
    /// Thrown when duplicate set <c>Sort</c> values exist or when any set after the first is missing
    /// an <see cref="Intersection"/> value.
    /// </exception>
    public static List<ConditionSet> ValidateAndGetSets(this Segment segment)
    {
        if (segment == null)
        {
            throw new ArgumentNullException(nameof(segment));
        }

        segment.ConditionSets ??= new List<ConditionSet>();

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
}
