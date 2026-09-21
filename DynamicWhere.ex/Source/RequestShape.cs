using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Exceptions;

namespace DynamicWhere.ex.Source;

/// <summary>
/// Refuses a request whose lists hold an entry that is not there.
/// </summary>
/// <remarks>
/// Every list of a request shape is declared to hold objects, and a request body can still say
/// <c>"conditions": [null]</c>, <c>"orders": [null]</c> or <c>"selects": [null]</c>. Nothing read a
/// list expecting that, so the null surfaced wherever it was first touched: a
/// <see cref="NullReferenceException"/> from a sort-order check, from the ordering, or from the copy the
/// policy layer takes before it reads anything, and an <see cref="ArgumentNullException"/> from a name
/// lookup. A host maps those to a server error, for a request that was simply malformed.
/// <para>
/// One walk, run by every method that takes a shape before anything else reads its lists, with or
/// without a policy, so both paths refuse the same request the same way: a
/// <see cref="LogicException"/> naming the list. A name that is null or blank is refused as the
/// grouping fields always refused one, with <see cref="ErrorCode.InvalidField"/>.
/// </para>
/// <para>
/// A list that is itself null is not this class's business. Each reader already says what an absent
/// list means, and most read it as empty.
/// </para>
/// </remarks>
internal static class RequestShape
{
    /// <summary>Refuses a filter with a null entry in any of its lists.</summary>
    /// <param name="filter">The filter, which may be null: a null argument is its method's to refuse.</param>
    /// <exception cref="LogicException">Thrown when a list holds a null entry, or a name is null or blank.</exception>
    internal static void Refuse(Filter? filter)
    {
        if (filter is null)
        {
            return;
        }

        Refuse(filter.ConditionGroup);
        Names(filter.Selects);
        Refuse(filter.Orders);
    }

    /// <summary>Refuses a segment with a null entry in any of its lists.</summary>
    /// <param name="segment">The segment, which may be null.</param>
    /// <exception cref="LogicException">Thrown when a list holds a null entry, or a name is null or blank.</exception>
    internal static void Refuse(Segment? segment)
    {
        if (segment is null)
        {
            return;
        }

        if (segment.ConditionSets is not null)
        {
            foreach (ConditionSet? set in segment.ConditionSets)
            {
                if (set is null)
                {
                    throw new LogicException(ErrorCode.NullEntry(nameof(Segment.ConditionSets)));
                }

                Refuse(set.ConditionGroup);
            }
        }

        Names(segment.Selects);
        Refuse(segment.Orders);
    }

    /// <summary>Refuses a summary with a null entry in any of its lists.</summary>
    /// <param name="summary">The summary, which may be null.</param>
    /// <exception cref="LogicException">Thrown when a list holds a null entry.</exception>
    internal static void Refuse(Summary? summary)
    {
        if (summary is null)
        {
            return;
        }

        Refuse(summary.ConditionGroup);
        Refuse(summary.GroupBy);
        Refuse(summary.Having);
        Refuse(summary.Orders);
    }

    /// <summary>Refuses a condition group with a null condition or a null sub-group, at any depth.</summary>
    /// <param name="group">The group, which may be null.</param>
    /// <exception cref="LogicException">Thrown when either list holds a null entry.</exception>
    internal static void Refuse(ConditionGroup? group)
    {
        if (group is null)
        {
            return;
        }

        if (group.Conditions is not null)
        {
            foreach (Condition? condition in group.Conditions)
            {
                if (condition is null)
                {
                    throw new LogicException(ErrorCode.NullEntry(nameof(ConditionGroup.Conditions)));
                }
            }
        }

        if (group.SubConditionGroups is not null)
        {
            foreach (ConditionGroup? sub in group.SubConditionGroups)
            {
                if (sub is null)
                {
                    throw new LogicException(ErrorCode.NullEntry(nameof(ConditionGroup.SubConditionGroups)));
                }

                Refuse(sub);
            }
        }
    }

    /// <summary>Refuses a grouping with a null aggregate.</summary>
    /// <param name="groupBy">The grouping, which may be null.</param>
    /// <remarks>
    /// Its fields are left to validation, which has always refused a null or blank one with
    /// <see cref="ErrorCode.InvalidField"/>.
    /// </remarks>
    /// <exception cref="LogicException">Thrown when the aggregates hold a null entry.</exception>
    internal static void Refuse(GroupBy? groupBy)
    {
        if (groupBy?.AggregateBy is null)
        {
            return;
        }

        foreach (AggregateBy? aggregate in groupBy.AggregateBy)
        {
            if (aggregate is null)
            {
                throw new LogicException(ErrorCode.NullEntry(nameof(GroupBy.AggregateBy)));
            }
        }
    }

    /// <summary>Refuses an ordering with a null entry.</summary>
    /// <param name="orders">The ordering, which may be null.</param>
    /// <exception cref="LogicException">Thrown when the list holds a null entry.</exception>
    internal static void Refuse(List<OrderBy>? orders)
    {
        if (orders is null)
        {
            return;
        }

        foreach (OrderBy? order in orders)
        {
            if (order is null)
            {
                throw new LogicException(ErrorCode.NullEntry(nameof(Filter.Orders)));
            }
        }
    }

    /// <summary>Refuses a projection with a name that is null or blank.</summary>
    /// <param name="names">The projection, which may be null.</param>
    /// <remarks>
    /// The same refusal a null or blank grouping field has always had. It used to be an
    /// <see cref="ArgumentNullException"/> from the name lookup, which reads a name as its argument.
    /// </remarks>
    /// <exception cref="LogicException">Thrown when a name is null or blank.</exception>
    internal static void Names(List<string>? names)
    {
        if (names is null)
        {
            return;
        }

        foreach (string? name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new LogicException(ErrorCode.InvalidField);
            }
        }
    }
}
