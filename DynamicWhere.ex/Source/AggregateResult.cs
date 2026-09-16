using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Optimization.Cache.Source;

namespace DynamicWhere.ex.Source;

/// <summary>
/// Works out what type a <c>HAVING</c> condition is actually comparing.
/// </summary>
/// <remarks>
/// A <c>HAVING</c> condition names an aggregate alias, not a member, so on its own it carries no type —
/// and a date predicate cannot be written correctly without one. The alias stands for an aggregate
/// whose result type follows from the aggregator and the field it reads, so the type is recovered
/// from there rather than guessed.
/// </remarks>
internal static class AggregateResult
{
    /// <summary>
    /// The CLR type an aggregate produces, where that type can carry a date.
    /// </summary>
    /// <typeparam name="T">The entity being grouped.</typeparam>
    /// <param name="aggregate">A validated aggregate.</param>
    /// <returns>
    /// The field's own type for <c>Minimum</c>, <c>Maximum</c>, <c>FirstOrDefault</c> and
    /// <c>LastOrDefault</c>, which return one of the values they read — a nullable member stays
    /// nullable. Null for the counts, the sum and the average: none of them can be a date, and null
    /// keeps the predicate shape those aliases have always had.
    /// </returns>
    internal static Type? ResultType<T>(this AggregateBy aggregate)
    {
        if (string.IsNullOrWhiteSpace(aggregate.Field))
        {
            return null;
        }

        return aggregate.Aggregator switch
        {
            Aggregator.Minimum or Aggregator.Maximum
                or Aggregator.FirstOrDefault or Aggregator.LastOrDefault
                => CacheReflection.GetFieldType(typeof(T), aggregate.Field),
            _ => null
        };
    }

    /// <summary>
    /// Maps each alias a <c>HAVING</c> clause may name to the type it stands for.
    /// </summary>
    /// <typeparam name="T">The entity being grouped.</typeparam>
    /// <param name="groupBy">A validated grouping.</param>
    /// <returns>
    /// Every alias, case-insensitively — the same comparison <c>HAVING</c> validation uses to decide an
    /// alias exists — with its result type, or null where the type does not bear on a predicate.
    /// </returns>
    internal static Dictionary<string, Type?> AliasTypes<T>(this GroupBy groupBy)
    {
        Dictionary<string, Type?> types = new(StringComparer.OrdinalIgnoreCase);

        foreach (AggregateBy aggregate in groupBy.AggregateBy)
        {
            if (!string.IsNullOrWhiteSpace(aggregate.Alias))
            {
                types[aggregate.Alias] = aggregate.ResultType<T>();
            }
        }

        return types;
    }
}
