using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.DTOs;

namespace DynamicWhere.ex.Policies.Source;

/// <summary>
/// The k-anonymity floor of design section 7.2: how small a group may be before it is suppressed,
/// and the column the suppression reads.
/// </summary>
/// <remarks>
/// Shared by the two halves that need it. The sanitizer decides whether to add the count, and the
/// result transformer decides which rows to drop — and both have to agree about the floor, or one
/// adds a column nobody reads or the other reads a column nobody added. One routine, called twice
/// with the same inputs.
/// </remarks>
internal static class GroupFloor
{
    /// <summary>
    /// The alias of the count the library adds for itself.
    /// </summary>
    /// <remarks>
    /// A plain identifier, because the alias is emitted verbatim into the generated projection and
    /// anything else either fails to parse or appends terms of its own. The leading underscores are
    /// what make it improbable rather than impossible for a caller to have chosen — which is why
    /// taking it is refused rather than assumed.
    /// </remarks>
    internal const string SizeAlias = "__dwGroupSize";

    /// <summary>
    /// The smallest group this summary may report.
    /// </summary>
    /// <param name="summary">The summary, after grouping has been canonicalized.</param>
    /// <param name="policy">The type's policy, which carries each field's own floor.</param>
    /// <param name="options">The posture, which carries the global floor.</param>
    /// <returns>The effective floor, or one when nothing sets one.</returns>
    /// <remarks>
    /// The largest in play, never the last. Two fields each naming a floor both mean it, and
    /// honouring the smaller would answer for a group one of them was written to protect.
    /// <para>
    /// A field's floor is read from its transform chain, which is where the attribute that obscures
    /// the value declares how small a group it will tolerate being aggregated over. A field nothing
    /// transforms sets no floor of its own and leaves the global setting to decide.
    /// </para>
    /// </remarks>
    internal static int For(Summary summary, TypePolicy policy, DwPolicyOptions options)
    {
        int floor = options.Caps.MinGroupSize;

        if (summary.GroupBy?.AggregateBy is not { } aggregates)
        {
            return floor;
        }

        foreach (AggregateBy aggregate in aggregates)
        {
            if (string.IsNullOrWhiteSpace(aggregate.Field))
            {
                continue;
            }

            if (policy.Transforms.TryGetValue(aggregate.Field!, out ValueTransform? transform)
                && transform.MinGroupSize > floor)
            {
                floor = transform.MinGroupSize;
            }
        }

        return floor;
    }

    /// <summary>
    /// Adds the count the floor reads, or returns the summary untouched when no floor applies.
    /// </summary>
    /// <param name="summary">The sanitized summary, modified in place.</param>
    /// <param name="floor">The effective floor.</param>
    /// <returns>True when a count was added and must be removed from the result again.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the caller has already used the reserved alias.
    /// </exception>
    /// <remarks>
    /// Added after gating, never before. This is the library counting on its own behalf, the way a
    /// forced predicate is the library filtering on the caller's — running it through the gate would
    /// check the caller's policy against a column the caller did not write, and charging it to the
    /// query budget would bill them for the library's own control.
    /// <para>
    /// <c>Count</c> takes no field, so nothing has to be chosen and nothing can be refused: the
    /// pipeline emits <c>Count()</c> over the group whatever the field says.
    /// </para>
    /// </remarks>
    internal static bool Inject(Summary summary, int floor)
    {
        if (floor <= 1 || summary.GroupBy is null)
        {
            return false;
        }

        // A grouped summary with no aggregates is legal, and the list can genuinely be null despite
        // its property initializer: JSON carrying "aggregateBy": null overwrites the initializer,
        // which is why GroupBy.Clone already handles it and why all six readers in FilterSanitizer
        // null-check it. This routine has to as well, and then needs somewhere to put the size
        // column. Assigning here is safe because Inject only ever sees the sanitizer's deep clone.
        summary.GroupBy.AggregateBy ??= new List<AggregateBy>();

        foreach (AggregateBy existing in summary.GroupBy.AggregateBy)
        {
            if (string.Equals(existing.Alias, SizeAlias, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(SizeAlias);
            }
        }

        summary.GroupBy.AggregateBy.Add(new AggregateBy
        {
            Aggregator = Aggregator.Count,
            Alias = SizeAlias
        });

        return true;
    }
}
