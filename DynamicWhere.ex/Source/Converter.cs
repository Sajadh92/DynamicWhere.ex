using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Optimization.Cache.Source;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace DynamicWhere.ex.Source;

/// <summary>
/// Provides utility methods for constructing dynamic LINQ projection and predicate expressions, including support for
/// projecting selected fields and converting filter and sort conditions to dynamic query strings.
/// </summary>
/// <remarks>The methods in this class are intended for internal use in scenarios where dynamic query generation
/// is required, such as building LINQ expressions based on user-specified fields or conditions. These utilities support
/// navigation through nested properties and collections, and handle validation of property paths and condition
/// structures. All members are static and thread-safe.</remarks>
internal static class Converter
{
    /// <summary>
    /// Builds a strongly-typed projection expression that selects only the specified fields from type T,
    /// supporting direct properties, nested reference navigation properties, and collection navigations.
    /// </summary>
    /// <remarks>This method is typically used to construct dynamic projections for LINQ queries, such as when
    /// selecting a subset of properties from an entity. Nested properties can be specified using dot notation (e.g.,
    /// "Category.Name"). Fields not included in the list will not be populated in the resulting object.</remarks>
    /// <typeparam name="T">The type of the object to project. Must be a reference type.</typeparam>
    /// <param name="normalizedFields">A list of field paths, using dot notation for nested properties, that specify which fields to include in the
    /// projection. Each entry must be a non-empty string.</param>
    /// <returns>An expression that, when compiled and invoked, returns a new instance of T with only the specified fields
    /// populated. All other fields are set to their default values.</returns>
    public static Expression<Func<T, T>> BuildTypedSelectExpression<T>(List<string> normalizedFields) where T : class
    {
        // Define the parameter for the lambda expression.
        var param = Expression.Parameter(typeof(T), "e");

        // Gather requested fields into a tree by path segments.
        // Example: ["Id", "Name", "Category.Name", "Category.ParentCategory.Name"]
        // becomes a tree that can be projected recursively.
        var tree = new ProjectionNode(typeof(T));

        // Add each requested field path to the projection tree.
        foreach (var f in normalizedFields)
        {
            if (string.IsNullOrWhiteSpace(f))
            {
                continue;
            }

            tree.AddPath(f);
        }

        // Build the MemberInit expression recursively based on the projection tree.
        var body = BuildTypedMemberInitExpression(typeof(T), param, tree);

        // Return the final lambda expression.
        return Expression.Lambda<Func<T, T>>(body, param);
    }

    /// <summary>
    /// Recursively builds a <see cref="MemberInitExpression"/> that projects the specified scalar properties and
    /// navigation properties of an object based on the provided <see cref="ProjectionNode"/>.
    /// </summary>
    /// <remarks>This method recursively constructs projections for nested navigation properties and
    /// collections, including only the fields specified in the projection node. For collection navigation properties,
    /// only element types with a parameterless constructor are supported for projection. The method includes the
    /// 'Id' property by default for nested entities only when that property exists on the type. Properties that
    /// cannot be written to or do not meet the required criteria are skipped.</remarks>
    /// <param name="type">The type of the object to be projected. Must have a parameterless constructor for nested entity or complex type
    /// projections.</param>
    /// <param name="instance">The expression representing the instance from which property values are read.</param>
    /// <param name="node">The projection node that specifies which scalar and navigation properties to include in the projection.</param>
    /// <returns>A MemberInitExpression that initializes a new instance of the specified type with the selected properties and
    /// navigation properties populated according to the projection node.</returns>
    private static MemberInitExpression BuildTypedMemberInitExpression(Type type, Expression instance, ProjectionNode node)
    {
        var bindings = new List<MemberBinding>();

        // Scalars on this node — skip any that also appear as navigation children
        // to prevent duplicate MemberBinding when both "Category" and "Category.Name" are requested.
        foreach (var scalar in node.Scalars)
        {
            if (node.Children.ContainsKey(scalar))
            {
                continue;
            }

            var prop = CacheReflection.FindProperty(type, scalar);
            if (prop == null || !prop.CanWrite)
            {
                continue;
            }

            bindings.Add(Expression.Bind(prop, Expression.Property(instance, prop)));
        }

        // Children = reference navigations
        foreach (var (navName, childNode) in node.Children)
        {
            var navProp = CacheReflection.FindProperty(type, navName);
            if (navProp == null || !navProp.CanWrite)
            {
                continue;
            }

            var navType = navProp.PropertyType;
            if (navType == typeof(string) || navType.IsValueType)
            {
                continue;
            }

            if (CacheReflection.IsCollectionType(navType))
            {
                var elementType = CacheReflection.GetCollectionElementType(navType);
                if (elementType == null)
                {
                    continue;
                }

                // Scalar collections (e.g., List<string>) can be bound directly.
                if (elementType.IsValueType || elementType == typeof(string))
                {
                    bindings.Add(Expression.Bind(navProp, Expression.Property(instance, navProp)));
                    continue;
                }

                // Entity/complex collections: try to project selected fields onto element type.
                // Build: e.Nav.Select(x => new Element { ... }).ToList()
                if (elementType.GetConstructor(Type.EmptyTypes) is null)
                {
                    continue;
                }

                // Include Id by default for nested entities only when the type actually has one.
                if (CacheReflection.FindProperty(elementType, "Id") != null)
                {
                    childNode.Scalars.Add("Id");
                }

                var collectionAccess = Expression.Property(instance, navProp);
                var asQueryable = Expression.Call(
                    typeof(Queryable),
                    nameof(Queryable.AsQueryable),
                    new[] { elementType },
                    collectionAccess);

                var p = Expression.Parameter(elementType, "c");
                var childInit = BuildTypedMemberInitExpression(elementType, p, childNode);
                var selector = Expression.Lambda(childInit, p);

                var selectCall = Expression.Call(
                    typeof(Queryable),
                    nameof(Queryable.Select),
                    new[] { elementType, elementType },
                    asQueryable,
                    selector);

                var listType = typeof(List<>).MakeGenericType(elementType);
                var toListCall = Expression.Call(
                    typeof(Enumerable),
                    nameof(Enumerable.ToList),
                    new[] { elementType },
                    selectCall);

                // Only bind if the property can accept List<T> (common: ICollection<T>, IEnumerable<T>, List<T>)
                if (navType.IsAssignableFrom(listType))
                {
                    bindings.Add(Expression.Bind(navProp, toListCall));
                }

                continue;
            }

            if (navType.GetConstructor(Type.EmptyTypes) is null)
            {
                continue;
            }

            // Include Id by default for nested entities only when the type actually has one.
            if (CacheReflection.FindProperty(navType, "Id") != null)
            {
                childNode.Scalars.Add("Id");
            }

            var navAccess = Expression.Call(
                typeof(EF),
                nameof(EF.Property),
                new[] { navType },
                instance,
                Expression.Constant(navProp.Name));

            var nested = BuildTypedMemberInitExpression(navType, navAccess, childNode);

            // Safe null handling based on FK if available
            Expression navValue;
            var fkProp = CacheReflection.FindProperty(type, navName + "Id");
            if (fkProp != null)
            {
                var fkAccess = Expression.Property(instance, fkProp);
                var fkDefault = Expression.Default(fkProp.PropertyType);
                navValue = Expression.Condition(
                    Expression.Equal(fkAccess, fkDefault),
                    Expression.New(navType),
                    nested);
            }
            else
            {
                navValue = Expression.Condition(
                    Expression.Equal(navAccess, Expression.Constant(null, navType)),
                    Expression.New(navType),
                    nested);
            }

            bindings.Add(Expression.Bind(navProp, navValue));
        }

        return Expression.MemberInit(Expression.New(type), bindings);
    }

    /// <summary>
    /// Builds a complete Dynamic LINQ <c>new(...)</c> selector string for the specified fields,
    /// supporting direct scalar properties, whole navigation objects/collections, dotted reference
    /// navigation paths, and dotted paths through collection navigations via <c>Select</c> lambdas.
    /// </summary>
    /// <remarks>
    /// Examples of generated output:
    /// <list type="bullet">
    ///   <item><description><c>"Id"</c> → <c>new(Id)</c></description></item>
    ///   <item><description><c>"Category"</c> → <c>new(Category)</c></description></item>
    ///   <item><description><c>"Category.Name"</c> → <c>new(new(Category.Name as Name) as Category)</c></description></item>
    ///   <item><description><c>"Category.Vendors.Id"</c> → <c>new(new(Category.Vendors.Select(v0 =&gt; new(v0.Id as Id)) as Vendors) as Category)</c></description></item>
    /// </list>
    /// </remarks>
    /// <param name="normalizedFields">Validated, normalized field paths to include in the projection.</param>
    /// <param name="rootType">The CLR type of the root entity.</param>
    /// <returns>A complete <c>new(...)</c> Dynamic LINQ selector string ready for <c>IQueryable.Select(string)</c>.</returns>
    public static string BuildDynamicSelectString(List<string> normalizedFields, Type rootType)
    {
        var pairs = normalizedFields.Select(f => (fullPath: f, remaining: f)).ToList();
        string body = BuildDynamicSelectStringCore(pairs, rootType, isRoot: true, lambdaPrefix: null, lambdaDepth: 0);
        return $"new({body})";
    }

    /// <summary>
    /// Recursively builds the inner body of a Dynamic LINQ <c>new(...)</c> expression, handling
    /// reference navigation (nested objects), collection navigation (via <c>Select</c> lambdas),
    /// and scalar/terminal properties.
    /// </summary>
    /// <param name="fields">Pairs of (fullPath, remaining) where fullPath is the absolute root-relative path
    /// and remaining is the path suffix relative to the current nesting level.</param>
    /// <param name="currentType">The CLR type being projected at the current recursion level.</param>
    /// <param name="isRoot"><see langword="true"/> for the top-level call; terminal fields are emitted without an alias.
    /// <see langword="false"/> for nested calls; terminal fields are aliased with their segment name.</param>
    /// <param name="lambdaPrefix">The lambda parameter expression prefix (e.g., <c>"v0"</c>, <c>"v0.Product"</c>)
    /// when inside a collection <c>Select</c> lambda; <see langword="null"/> when at the root level or navigating
    /// through reference properties from the root.</param>
    /// <param name="lambdaDepth">Depth counter used to generate unique lambda parameter names (<c>v0</c>, <c>v1</c>, …)
    /// for nested collection traversals.</param>
    private static string BuildDynamicSelectStringCore(
        List<(string fullPath, string remaining)> fields,
        Type currentType,
        bool isRoot,
        string? lambdaPrefix,
        int lambdaDepth)
    {
        var terminals = new List<(string fullPath, string remaining)>();
        var groups = new Dictionary<string, List<(string fullPath, string remaining)>>(StringComparer.Ordinal);

        foreach (var (fullPath, remaining) in fields)
        {
            int dotIndex = remaining.IndexOf('.');

            if (dotIndex == -1)
            {
                terminals.Add((fullPath, remaining));
            }
            else
            {
                string segment = remaining[..dotIndex];
                string newRemaining = remaining[(dotIndex + 1)..];

                if (!groups.TryGetValue(segment, out var list))
                {
                    list = new List<(string, string)>();
                    groups[segment] = list;
                }

                list.Add((fullPath, newRemaining));
            }
        }

        var parts = new List<string>();

        // Emit terminal (leaf) fields, skipping any whose name is also a navigation group key
        // to avoid duplicate property names when both "Category" and "Category.Name" are requested.
        foreach (var (fullPath, remaining) in terminals)
        {
            if (groups.ContainsKey(remaining))
            {
                continue;
            }

            string expr = lambdaPrefix != null ? $"{lambdaPrefix}.{remaining}" : fullPath;
            parts.Add(isRoot ? expr : $"{expr} as {remaining}");
        }

        // Emit navigation groups as nested new(...) or collection Select(...) projections.
        foreach (var (segment, subFields) in groups)
        {
            var segmentProp = CacheReflection.FindProperty(currentType, segment);
            var segmentType = segmentProp?.PropertyType ?? currentType;
            var elementType = CacheReflection.GetCollectionElementType(segmentType);

            if (elementType != null)
            {
                // Collection navigation: generate CollectionPath.Select(vN => new(inner)) as Segment.
                // When inside a lambda, the collection is accessed via the lambda prefix.
                // When not inside a lambda, the absolute collection path is extracted from fullPath.
                string collectionExpr = lambdaPrefix != null
                    ? $"{lambdaPrefix}.{segment}"
                    : subFields[0].fullPath[..(subFields[0].fullPath.Length - subFields[0].remaining.Length - 1)];

                string lambdaParam = $"v{lambdaDepth}";

                // Inside the collection lambda, paths are relative to the element (lambda parameter).
                var elementSubFields = subFields
                    .Select(f => (fullPath: f.remaining, f.remaining))
                    .ToList();

                string innerBody = BuildDynamicSelectStringCore(
                    elementSubFields, elementType, isRoot: false, lambdaPrefix: lambdaParam, lambdaDepth: lambdaDepth + 1);

                parts.Add($"{collectionExpr}.Select({lambdaParam} => new({innerBody})) as {segment}");
            }
            else
            {
                // Reference navigation: generate new(nested fields) as Segment.
                // Extend lambdaPrefix into this segment when inside a lambda.
                string? nestedPrefix = lambdaPrefix != null ? $"{lambdaPrefix}.{segment}" : null;

                string nested = BuildDynamicSelectStringCore(
                    subFields, segmentType, isRoot: false, lambdaPrefix: nestedPrefix, lambdaDepth: lambdaDepth);

                parts.Add($"new({nested}) as {segment}");
            }
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Converts a <see cref="Condition"/> into a dynamic LINQ predicate string.
    /// Supports navigation through nested properties and collections, automatically
    /// inserting Any(...) for collection navigations.
    /// </summary>
    /// <typeparam name="T">The root entity type being queried.</typeparam>
    /// <param name="condition">The condition to convert.</param>
    /// <returns>
    /// A dynamic LINQ predicate string enclosed in parentheses, suitable for <c>Where(string)</c>.
    /// </returns>
    /// <exception cref="LogicException">Thrown if the condition contains invalid data.</exception>
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

            // Check if the parent is a collection type using cached lookup.
            if (CacheReflection.IsCollectionType(parent))
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

                // Since condition.Validate<T>() already validated the property path,
                // We know the property exists. Use cached lookup for type information only.
                type = parent = CacheReflection.FindProperty(type, p)!.PropertyType;


                // Check if the current type is a collection and get its element type.
                var elementType = CacheReflection.GetCollectionElementType(type);
                if (elementType != null)
                {
                    type = elementType;
                }
            }
            else
            {
                // For the last property, build the actual condition string.
                string last = conditionAsString.Split(' ').Last().Trim();

                conditionAsString = conditionAsString[..^last.Length];

                conditionAsString += Builder.BuildCondition(condition.DataType, condition.Operator, last, condition.Values);
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
    /// <returns>
    /// A dynamic LINQ predicate string enclosed in parentheses, or an empty string when the group has no conditions.
    /// </returns>
    /// <exception cref="LogicException">Thrown when the group or its members are invalid (e.g., duplicate Sort values).</exception>
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
    /// Converts a <see cref="GroupBy"/> instance into a dynamic LINQ GroupBy and Select expression string.
    /// </summary>
    /// <typeparam name="T">The type used to validate field names and aggregations.</typeparam>
    /// <param name="groupBy">The <see cref="GroupBy"/> instance to convert.</param>
    /// <returns>
    /// A tuple containing:
    /// - GroupByString: The dynamic LINQ GroupBy key selector (e.g., "new { Field1, Field2 }")
    /// - SelectString: The dynamic LINQ Select projection including grouping fields and aggregations
    /// </returns>
    /// <exception cref="LogicException">Thrown if validation fails or fields/aggregations are invalid.</exception>
    public static (string GroupByString, string SelectString) AsString<T>(this GroupBy groupBy)
    {
        // Validate the GroupBy
        groupBy.Validate<T>();

        // Build the GroupBy key selector
        string groupByString = groupBy.Fields.Count == 1
            ? groupBy.Fields[0]
            : $"new ({string.Join(", ", groupBy.Fields)})";

        // Build the Select projection
        var selectParts = new List<string>();

        // Add grouping fields to select
        // For dotted paths (e.g., "CreatedAt.Year"), Dynamic LINQ names the key property after
        // the last segment only ("Year"), and aliases must be valid identifiers (no dots).
        if (groupBy.Fields.Count == 1)
        {
            // Single field: use Key directly; sanitize alias in case the field is a dotted path
            selectParts.Add($"Key as {groupBy.Fields[0].Replace(".", "")}");
        }
        else
        {
            // Multiple fields: access Key by last segment; sanitize alias for dotted paths
            foreach (var field in groupBy.Fields)
            {
                string keyProp = field.Contains('.') ? field.Split('.').Last() : field;
                string alias = field.Replace(".", "");
                selectParts.Add($"Key.{keyProp} as {alias}");
            }
        }

        // Add aggregations to select
        foreach (var aggregate in groupBy.AggregateBy)
        {
            string aggregationExpression = aggregate.Aggregator switch
            {
                Aggregator.Count => "Count()",
                Aggregator.CountDistinct => $"Select({aggregate.Field}).Distinct().Count()",
                Aggregator.Sumation => $"Sum({aggregate.Field})",
                Aggregator.Average => $"Average({aggregate.Field})",
                Aggregator.Minimum => $"Min({aggregate.Field})",
                Aggregator.Maximum => $"Max({aggregate.Field})",
                Aggregator.FirstOrDefault => $"Select({aggregate.Field}).OrderBy(it).FirstOrDefault()",
                Aggregator.LastOrDefault => $"Select({aggregate.Field}).OrderByDescending(it).FirstOrDefault()",
                _ => throw new LogicException(ErrorCode.UnsupportedAggregatorForType(
                    aggregate.Aggregator.ToString(),
                    "Unknown"))
            };

            selectParts.Add($"{aggregationExpression} as {aggregate.Alias}");
        }

        string selectString = $"new ({string.Join(", ", selectParts)})";

        return (groupByString, selectString);
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

    /// <summary>
    /// Converts a <see cref="Condition"/> inside a HAVING clause into a dynamic LINQ predicate string.
    /// The condition's field is treated as an aggregate-by alias (already validated by <see cref="Validator"/>).
    /// </summary>
    /// <param name="condition">The <see cref="Condition"/> to convert.</param>
    /// <returns>A dynamic LINQ predicate snippet enclosed in parentheses.</returns>
    public static string AsHavingString(this Condition condition)
    {
        condition.Values ??= new List<string>();

        return $"({Builder.BuildCondition(condition.DataType, condition.Operator, condition.Field!, condition.Values)})";
    }

    /// <summary>
    /// Converts a <see cref="ConditionGroup"/> used as a HAVING clause into its dynamic LINQ predicate string.
    /// Condition fields reference aggregate-by aliases rather than entity properties.
    /// </summary>
    /// <param name="group">The <see cref="ConditionGroup"/> to convert.</param>
    /// <returns>
    /// A dynamic LINQ predicate string enclosed in parentheses, or an empty string when the group has no conditions.
    /// </returns>
    public static string AsHavingString(this ConditionGroup group)
    {
        // Validate structure (duplicate sort values, etc.).
        group.Validate();

        string connector = group.Connector switch
        {
            Connector.And => " && ",
            Connector.Or => " || ",
            _ => string.Empty
        };

        var conditions = new List<string>();

        foreach (Condition condition in group.Conditions.OrderBy(x => x.Sort))
        {
            string conditionAsString = condition.AsHavingString();

            if (!string.IsNullOrWhiteSpace(conditionAsString))
            {
                conditions.Add(conditionAsString);
            }
        }

        foreach (ConditionGroup subGroup in group.SubConditionGroups.OrderBy(x => x.Sort))
        {
            string subGroupAsString = subGroup.AsHavingString();

            if (!string.IsNullOrWhiteSpace(subGroupAsString))
            {
                conditions.Add(subGroupAsString);
            }
        }

        if (conditions.Any())
        {
            return $"({string.Join(connector, conditions)})";
        }

        return string.Empty;
    }
}

/// <summary>
/// Represents a node in a projection tree, describing which scalar properties and child projections are included for a
/// given type.
/// </summary>
/// <remarks>A ProjectionNode is used to build and navigate a hierarchical structure that specifies which
/// properties of an object graph should be included in a projection. Each node corresponds to a specific type and
/// tracks both scalar property names and nested child projections. This is typically used in scenarios such as
/// selective data shaping or serialization, where only certain fields of an object and its children are
/// required.</remarks>
internal sealed class ProjectionNode
{
    /// <summary>
    /// Gets the runtime type of the current instance.
    /// </summary>
    public Type Type { get; }

    /// <summary>
    /// Gets the collection of scalar value names used by this instance.
    /// </summary>
    /// <remarks>The collection is case-sensitive and does not allow duplicate entries. Modifications to the
    /// returned set affect the scalars recognized by this instance.</remarks>
    public HashSet<string> Scalars { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the collection of child projection nodes associated with this node.
    /// </summary>
    /// <remarks>The collection uses case-sensitive string keys based on ordinal comparison. The returned
    /// dictionary is read-only; to modify the set of children, use the appropriate methods on the parent node, if
    /// available.</remarks>
    public Dictionary<string, ProjectionNode> Children { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the ProjectionNode class for the specified type.
    /// </summary>
    /// <param name="type">The type to associate with this projection node. Cannot be null.</param>
    public ProjectionNode(Type type) => Type = type;

    /// <summary>
    /// Adds a hierarchical path to the current structure using a dot-delimited string.
    /// </summary>
    /// <param name="path">A string representing the path to add, with segments separated by periods ('.'). Leading, trailing, and
    /// consecutive periods are ignored.</param>
    public void AddPath(string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Add(parts, 0);
    }

    /// <summary>
    /// Adds a scalar or child node to the projection tree based on the specified path segments.
    /// </summary>
    /// <remarks>If the specified path does not correspond to a valid property on the current node's type, the
    /// method does not add a node for that segment. This method is intended for internal use when building or extending
    /// the projection tree structure.</remarks>
    /// <param name="parts">An array of strings representing the path segments to add to the projection tree. Cannot be null.</param>
    /// <param name="index">The zero-based index in the parts array indicating the current segment to process. Must be greater than or equal
    /// to 0 and less than or equal to the length of parts.</param>
    private void Add(string[] parts, int index)
    {
        if (index >= parts.Length)
        {
            return;
        }

        var head = parts[index];
        if (index == parts.Length - 1)
        {
            Scalars.Add(head);
            return;
        }

        if (!Children.TryGetValue(head, out var child))
        {
            // Infer child type from property.
            var prop = CacheReflection.FindProperty(Type, head);
            if (prop == null)
            {
                return;
            }

            var nodeType = prop.PropertyType;
            var elementType = CacheReflection.GetCollectionElementType(nodeType);
            if (elementType != null)
            {
                nodeType = elementType;
            }
            child = new ProjectionNode(nodeType);
            Children[head] = child;
        }

        child.Add(parts, index + 1);
    }
}
