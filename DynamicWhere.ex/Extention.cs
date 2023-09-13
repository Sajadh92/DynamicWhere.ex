using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

namespace DynamicWhere.ex;

public static class Extention
{
    /// <summary>
    /// Filters an IQueryable based on the specified Segment's conditions.
    /// </summary>
    /// <typeparam name="T">The type of elements in the query.</typeparam>
    /// <param name="query">The source IQueryable.</param>
    /// <param name="segment">The Segment containing conditions for filtering.</param>
    /// <returns>A filtered list of elements of type T.</returns>
    public static async Task<List<T>> ToListAsync<T>(this IQueryable<T> query, Segment segment)
    {
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (segment == null)
        {
            throw new ArgumentNullException(nameof(segment));
        }

        var sets = segment.ValidateAndGetSets();

        if (sets.Count == 0)
        {
            return await query.ToListAsync();
        }

        List<(int sort, Intersection? intersection, List<T> list)> dataSets = new();

        foreach (var set in sets.OrderBy(x => x.Sort))
        {
            var queryable = query.Where(set.ConditionGroup);

            var list = await queryable.ToListAsync();

            dataSets.Add(new(set.Sort, set.Intersection, list));
        }

        var result = dataSets.OrderBy(x => x.sort).First().list;

        foreach (var (sort, intersection, list) in dataSets.OrderBy(x => x.sort).Skip(1))
        {
            switch (intersection)
            {
                case Intersection.Union:
                {
                    result = result.Union(list).ToList();
                }
                break;

                case Intersection.Intersect:
                {
                    result = result.Intersect(list).ToList();
                }
                break;

                case Intersection.Except:
                {
                    result = result.Except(list).ToList();
                }
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Filters an IQueryable based on the specified ConditionGroup.
    /// </summary>
    /// <typeparam name="T">The type of elements in the query.</typeparam>
    /// <param name="query">The source IQueryable.</param>
    /// <param name="group">The ConditionGroup specifying the filter conditions.</param>
    /// <returns>An IQueryable with the applied filter conditions.</returns>
    public static IQueryable<T> Where<T>(this IQueryable<T> query, ConditionGroup group)
    {
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (group == null)
        {
            throw new ArgumentNullException(nameof(group));
        }

        string where = group.AsString<T>();

        if (string.IsNullOrWhiteSpace(where))
        {
            return query;
        }

        return query.Where(where);
    }

    /// <summary>
    /// Filters an IQueryable based on the specified Condition.
    /// </summary>
    /// <typeparam name="T">The type of elements in the query.</typeparam>
    /// <param name="query">The source IQueryable.</param>
    /// <param name="condition">The Condition specifying the filter condition.</param>
    /// <returns>An IQueryable with the applied filter condition.</returns>
    public static IQueryable<T> Where<T>(this IQueryable<T> query, Condition condition)
    {
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (condition == null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        string where = condition.AsString<T>();

        if (string.IsNullOrWhiteSpace(where))
        {
            return query;
        }

        return query.Where(where);
    }

    /// <summary>
    /// Converts a ConditionGroup to its string representation.
    /// </summary>
    /// <typeparam name="T">The type of elements being filtered.</typeparam>
    /// <param name="group">The ConditionGroup to convert to a string.</param>
    /// <returns>A string representation of the ConditionGroup.</returns>
    private static string AsString<T>(this ConditionGroup group)
    {
        group.Validate();

        string connector = group.Connector switch
        {
            Connector.And => " && ",
            Connector.Or => " || ",
            _ => string.Empty
        };

        List<string> conditions = new();

        foreach (Condition condition in group.Conditions.OrderBy(x => x.Sort))
        {
            string conditionAsString = condition.AsString<T>();

            if (!string.IsNullOrWhiteSpace(conditionAsString))
            {
                conditions.Add(conditionAsString);
            }
        }

        foreach (ConditionGroup subGroup in group.SubConditionGroups.OrderBy(x => x.Sort))
        {
            string subGroupConditionsAsString = subGroup.AsString<T>();

            if (!string.IsNullOrWhiteSpace(subGroupConditionsAsString))
            {
                conditions.Add(subGroupConditionsAsString);
            }
        }

        if (conditions.Any())
        {
            return $"({string.Join(connector, conditions)})";
        }

        return string.Empty;
    }

    /// <summary>
    /// Converts a Condition to its string representation.
    /// </summary>
    /// <typeparam name="T">The type of elements being filtered.</typeparam>
    /// <param name="condition">The Condition to convert to a string.</param>
    /// <returns>A string representation of the Condition.</returns>
    private static string AsString<T>(this Condition condition)
    {
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
    /// Validates and retrieves a list of ConditionSets from a Segment.
    /// </summary>
    /// <param name="segment">The Segment containing ConditionSets.</param>
    /// <returns>A list of validated ConditionSets.</returns>
    private static List<ConditionSet> ValidateAndGetSets(this Segment segment)
    {
        if (segment.ConditionSets.Count == 0)
        {
            return new();
        }

        bool hasDublicateSort = segment.ConditionSets.GroupBy(x => x.Sort).Select(x => x.Count()).Any(x => x > 1);

        if (hasDublicateSort)
        {
            throw new LogicException(ErrorCode.SetsUniqueSort);
        }

        bool hasNotIntersection = segment.ConditionSets.OrderBy(x => x.Sort).Skip(1).Any(x => x.Intersection == null);

        if (hasNotIntersection)
        {
            throw new LogicException(ErrorCode.RequiredIntersection);
        }

        List<ConditionSet> sets = segment.ConditionSets.OrderBy(x => x.Sort).ToList();

        sets[0].Intersection = null;

        return sets;
    }

    /// <summary>
    /// Validates a ConditionGroup for duplicate sort values.
    /// </summary>
    /// <param name="group">The ConditionGroup to validate.</param>
    private static void Validate(this ConditionGroup group)
    {
        bool hasDublicateSort = group.Conditions.GroupBy(x => x.Sort).Select(x => x.Count()).Any(x => x > 1);

        if (hasDublicateSort)
        {
            throw new LogicException(ErrorCode.ConditionsUniqueSort);
        }

        hasDublicateSort = group.SubConditionGroups.GroupBy(x => x.Sort).Select(x => x.Count()).Any(x => x > 1);

        if (hasDublicateSort)
        {
            throw new LogicException(ErrorCode.SubConditionsGroupsUniqueSort);
        }
    }

    /// <summary>
    /// Validates a Condition for correctness based on the type and operator.
    /// </summary>
    /// <typeparam name="T">The type of elements being filtered.</typeparam>
    /// <param name="condition">The Condition to validate.</param>
    private static void Validate<T>(this Condition condition)
    {
        if (string.IsNullOrWhiteSpace(condition.Field))
        {
            throw new LogicException(ErrorCode.InvalidField);
        }

        List<string> properties = typeof(T).GetProperties().Select(x => x.Name).ToList();

        if (!properties.Contains(condition.Field ?? "@"))
        {
            throw new LogicException(ErrorCode.InvalidField);
        }

        if (condition.Operator == Operator.Between || condition.Operator == Operator.NotBetween)
        {
            if (condition.Values.Count != 2)
            {
                throw new LogicException(ErrorCode.RequiredTwoValue);
            }

            if (string.IsNullOrWhiteSpace(condition.Values[0]) || string.IsNullOrWhiteSpace(condition.Values[1]))
            {
                throw new LogicException(ErrorCode.InvalidValue);
            }
        }
        else if (condition.Operator == Operator.In || condition.Operator == Operator.NotIn ||
                 condition.Operator == Operator.IIn || condition.Operator == Operator.INotIn)
        {
            if (condition.Values.Count == 0)
            {
                throw new LogicException(ErrorCode.RequiredValues);
            }

            if (condition.Values.Any(x => string.IsNullOrWhiteSpace(x)))
            {
                throw new LogicException(ErrorCode.InvalidValue);
            }
        }
        else if (condition.Operator == Operator.IsNull || condition.Operator == Operator.IsNotNull)
        {
            if (condition.Values.Count != 0)
            {
                throw new LogicException(ErrorCode.NotRequiredValues);
            }
        }
        else
        {
            if (condition.Values.Count != 1)
            {
                throw new LogicException(ErrorCode.RequiredOneValue(condition.Operator.ToString()));
            }

            if (string.IsNullOrWhiteSpace(condition.Values[0]))
            {
                throw new LogicException(ErrorCode.InvalidValue);
            }
        }
    }
}
