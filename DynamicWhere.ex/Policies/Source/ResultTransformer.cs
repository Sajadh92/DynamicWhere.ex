using System.Collections;
using System.Dynamic;
using System.Reflection;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Masking;

namespace DynamicWhere.ex.Policies.Source;

/// <summary>
/// Step five of the sandwich: what a result looks like on its way out.
/// </summary>
/// <remarks>
/// Entities and projections are walked by path, which the graph walker does. A summary is different
/// enough to need its own routine: its rows are not entities, its columns are named after grouping
/// keys with their dots stripped and after aggregate aliases, and transforming a grouping key can
/// make two rows collide in a way no entity result can.
/// </remarks>
internal static class ResultTransformer
{
    /// <summary>Transforms the rows of an entity or projection result.</summary>
    internal static void Rows(
        IEnumerable rows,
        TypePolicy policy,
        IReadOnlyCollection<string>? projected,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyTrace trace) =>
        GraphWalker.Apply(rows, policy, projected, context, options, trace);

    /// <summary>
    /// Rewrites the column names of generated rows into the caller's own vocabulary.
    /// </summary>
    /// <param name="rows">The materialized rows. Replaced in place when anything is renamed.</param>
    /// <param name="policy">The type's policy, whose alias map is the vocabulary.</param>
    /// <param name="trace">Collects what was renamed.</param>
    /// <param name="drop">
    /// A column to omit from the rebuilt rows, or null to keep every column. The group-size count
    /// the floor adds for itself is removed this way — one re-projection does both jobs rather than
    /// two rebuilding the same rows twice.
    /// </param>
    /// <remarks>
    /// The outbound half of <c>[DwAlias]</c>. Renaming a column is not mutating a value: a generated
    /// projection bakes its property names in when the query is built, so the only way to change one
    /// is to re-project — which is why this runs after materialization and only on the surfaces
    /// whose rows are generated rather than instances of the caller's own type.
    /// <para>
    /// Nothing aliased, nothing rebuilt. When no projected column carries an alias the list is left
    /// exactly as the pipeline produced it, so every caller who does not use the feature sees the
    /// shape they saw before it existed.
    /// </para>
    /// <para>
    /// The alias is used whichever spelling the caller wrote going in. An alias is the public name
    /// of the field, not an echo of the request — two callers asking for the same data would
    /// otherwise get two different shapes from one query.
    /// </para>
    /// </remarks>
    internal static void Rename(
        List<dynamic>? rows, TypePolicy policy, PolicyTrace trace, string? drop = null)
    {
        if (rows is null || rows.Count == 0 || (policy.Aliases.Count == 0 && drop is null))
        {
            return;
        }

        // Inverted from the sanitizer's map, which answers "what could this name mean" because that
        // is the question going in. A name standing for more than one path is refused inbound and is
        // skipped here for the same reason: renaming two columns to one name would lose one of them
        // outright, where the inbound refusal at least says so.
        Dictionary<string, string> byColumn = new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, IReadOnlyList<string>> entry in policy.Aliases)
        {
            if (entry.Value.Count != 1)
            {
                continue;
            }

            // A generated projection names a nested column by its path with the dots removed, which
            // is the spelling the validator accepts for a group-by key for the same reason.
            byColumn[entry.Value[0].Replace(".", string.Empty)] = entry.Key;
        }

        // Not "no aliases, nothing to do" any more. There is a second reason to rebuild a row —
        // removing the group-size count the floor added for itself — and returning here on an empty
        // alias map left that column in the caller's result on every type that happens not to use
        // aliases, which is most of them.
        if (byColumn.Count == 0 && drop is null)
        {
            return;
        }

        List<string> renamed = new();

        for (int i = 0; i < rows.Count; i++)
        {
            object? row = rows[i];

            if (row is null)
            {
                continue;
            }

            ExpandoObject? rebuilt = Rebuild(row, byColumn, renamed, drop);

            if (rebuilt is not null)
            {
                rows[i] = rebuilt;
            }
        }

        foreach (string column in renamed)
        {
            trace.Add(new PolicyDecision(
                column, PolicyFeature.Select, PolicyAction.Allowed,
                $"emitted as '{byColumn[column]}'"));
        }
    }

    /// <summary>
    /// Rebuilds one row under the caller's names, or returns null when none of its columns is
    /// aliased.
    /// </summary>
    /// <remarks>
    /// An <see cref="ExpandoObject"/> because it is both a dynamic object and a string-keyed
    /// dictionary: existing dynamic member access keeps working, serializers emit it as a plain
    /// object, and a test can read a column by name without reflection.
    /// </remarks>
    private static ExpandoObject? Rebuild(
        object row, Dictionary<string, string> byColumn, List<string> renamed, string? drop)
    {
        Dictionary<string, PropertyInfo> properties = CacheReflection.GetTypeProperties(row.GetType());

        bool any = false;

        foreach (KeyValuePair<string, PropertyInfo> property in properties)
        {
            if (byColumn.ContainsKey(property.Key)
                || string.Equals(property.Key, drop, StringComparison.OrdinalIgnoreCase))
            {
                any = true;
                break;
            }
        }

        if (!any)
        {
            return null;
        }

        ExpandoObject rebuilt = new();
        IDictionary<string, object?> columns = rebuilt!;

        foreach (KeyValuePair<string, PropertyInfo> property in properties)
        {
            if (property.Value.GetIndexParameters().Length != 0
                || string.Equals(property.Key, drop, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string name = byColumn.TryGetValue(property.Key, out string? alias) ? alias : property.Key;

            if (!ReferenceEquals(name, property.Key) && !renamed.Contains(property.Key))
            {
                renamed.Add(property.Key);
            }

            columns[name] = MutatorCache.Read(property.Value, row);
        }

        return rebuilt;
    }

    /// <summary>
    /// Drops every group smaller than the floor, recording each one.
    /// </summary>
    /// <param name="result">The materialized summary, modified in place.</param>
    /// <param name="floor">The effective floor. One or less suppresses nothing.</param>
    /// <param name="dryRun">True to record what would have been dropped and drop nothing.</param>
    /// <param name="trace">Collects what was suppressed.</param>
    /// <returns>How many groups were dropped.</returns>
    /// <remarks>
    /// Design section 7.2's k-anonymity floor. Aggregation over a group of one returns that row's
    /// exact value under any function, so permitting a transformed field to be aggregated without a
    /// floor hands back precisely what the transform was there to hide.
    /// <para>
    /// Every drop is recorded. A group that is simply absent cannot be told from a group that never
    /// existed, and an operator asking why a department is missing from a report needs an answer
    /// that is not "look at the data".
    /// </para>
    /// <para>
    /// A row whose size column cannot be read is dropped rather than kept. The column is one this
    /// library added for itself, so failing to find it means the floor has no idea how large the
    /// group is — and answering anyway is exactly the disclosure the floor exists to prevent.
    /// </para>
    /// </remarks>
    internal static int Suppress(
        SummaryResult result, int floor, bool dryRun, PolicyTrace trace)
    {
        if (floor <= 1 || result.Data.Count == 0)
        {
            return 0;
        }

        List<dynamic> kept = new(result.Data.Count);
        int dropped = 0;

        foreach (object row in result.Data)
        {
            int? size = SizeOf(row);

            if (size is not null && size >= floor)
            {
                kept.Add(row);

                continue;
            }

            dropped++;

            trace.Add(new PolicyDecision(
                GroupFloor.SizeAlias,
                PolicyFeature.Aggregate,
                PolicyAction.Dropped,
                size is null
                    ? $"a group whose size could not be read, below the group floor of {floor}"
                    : $"a group of {size}, below the group floor of {floor}"));
        }

        // A dry run records what it would have done and does not do it, as every other decision
        // under that posture does. The evidence is the point of the posture.
        if (!dryRun)
        {
            result.Data = kept;
        }

        return dropped;
    }

    /// <summary>Reads the size column this library added, or null when it cannot be read.</summary>
    private static int? SizeOf(object row)
    {
        PropertyInfo? column = CacheReflection.FindProperty(row.GetType(), GroupFloor.SizeAlias);

        if (column is null)
        {
            return null;
        }

        object? value = MutatorCache.Read(column, row);

        try
        {
            return value is null ? null : Convert.ToInt32(value);
        }
        catch (Exception conversion)
            when (conversion is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Transforms the keys and aggregates of a summary, then refuses a result whose keys collide.
    /// </summary>
    /// <remarks>
    /// Grouping runs in SQL against real values, so the groups and their counts are correct before
    /// anything here happens. Only then are the keys transformed, which is what keeps a masked field
    /// groupable at all — masking first would merge distinct values in the database and produce
    /// counts that answer a different question.
    /// <para>
    /// The risk that creates is collision: two real keys can transform to the same output, leaving a
    /// table with duplicate keys whose counts do not add up. That is refused rather than merged.
    /// Merging would invent a number nobody computed, and returning it would hand back a result that
    /// reads as wrong.
    /// </para>
    /// </remarks>
    /// <exception cref="PolicyException">Thrown when two groups collide after transformation.</exception>
    internal static void Summary(
        SummaryResult result,
        Summary summary,
        TypePolicy policy,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyTrace trace)
    {
        if (policy.Transforms.Count == 0 || result.Data.Count == 0)
        {
            return;
        }

        List<Column> keys = KeyColumns(summary, policy);
        List<Column> aggregates = AggregateColumns(summary, policy);

        if (keys.Count == 0 && aggregates.Count == 0)
        {
            return;
        }

        foreach (Column column in keys.Concat(aggregates))
        {
            Apply(result.Data, column, context, options, trace);
        }

        if (keys.Count > 0)
        {
            RefuseCollisions(result.Data, keys, options);
        }
    }

    /// <summary>
    /// The grouping keys that carry a transform, paired with the column each one becomes.
    /// </summary>
    /// <remarks>
    /// The projection emits a grouping key with its dots stripped — <c>Contact.Phone</c> becomes
    /// <c>ContactPhone</c> — which is the third spelling of a field this library accepts and the one
    /// a previous phase missed. The column is found by that name and the policy by the path.
    /// </remarks>
    private static List<Column> KeyColumns(Summary summary, TypePolicy policy)
    {
        List<Column> columns = new();

        if (summary.GroupBy?.Fields is null)
        {
            return columns;
        }

        foreach (string field in summary.GroupBy.Fields)
        {
            if (policy.Transforms.TryGetValue(field, out ValueTransform? chain))
            {
                columns.Add(new Column(field.Replace(".", string.Empty), field, chain));
            }
        }

        return columns;
    }

    /// <summary>
    /// The aggregates whose source field carries a transform, paired with their alias column.
    /// </summary>
    /// <remarks>
    /// An aggregate of a transformed field is transformed too. A salary rounded to the nearest band
    /// stays useful when summed; a masked one becomes a mask, which is the honest rendering of a
    /// number the caller may not see. A count carries no field and so inherits nothing.
    /// </remarks>
    private static List<Column> AggregateColumns(Summary summary, TypePolicy policy)
    {
        List<Column> columns = new();

        if (summary.GroupBy?.AggregateBy is null)
        {
            return columns;
        }

        foreach (AggregateBy aggregate in summary.GroupBy.AggregateBy)
        {
            if (!string.IsNullOrWhiteSpace(aggregate.Field)
                && !string.IsNullOrWhiteSpace(aggregate.Alias)
                && policy.Transforms.TryGetValue(aggregate.Field!, out ValueTransform? chain))
            {
                columns.Add(new Column(aggregate.Alias!, aggregate.Field!, chain));
            }
        }

        return columns;
    }

    /// <summary>Transforms one column across every row.</summary>
    private static void Apply(
        List<dynamic> rows,
        Column column,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyTrace trace)
    {
        bool applied = false;

        foreach (object row in rows)
        {
            PropertyInfo property = CacheReflection.FindProperty(row.GetType(), column.Name)
                ?? throw new InvalidOperationException(
                    $"'{column.Path}' is transformed by policy, but the summary has no column " +
                    $"'{column.Name}'. The value would otherwise be emitted exactly as stored.");

            object? value = MutatorCache.Read(property, row);

            object? replacement = TransformPipeline.Apply(
                column.Chain, value, property.PropertyType,
                new DwTransformContext(row, column.Path, context), options);

            if (!MutatorCache.Write(property, row, replacement))
            {
                throw new InvalidOperationException(
                    $"The summary column '{column.Name}' has no setter, so the transformed value " +
                    "cannot replace the real one.");
            }

            applied = true;
        }

        if (applied)
        {
            trace.Add(new PolicyDecision(
                column.Path, PolicyFeature.Aggregate, column.Chain.Action,
                $"summary column '{column.Name}'"));
        }
    }

    /// <summary>
    /// Refuses a result in which two groups now share a key.
    /// </summary>
    /// <remarks>
    /// Rounding two salaries into one band, or masking two identifiers into one run of stars, leaves
    /// rows that look like duplicates and whose aggregates cannot be added together without
    /// inventing a figure the database never computed.
    /// </remarks>
    private static void RefuseCollisions(List<dynamic> rows, List<Column> keys, DwPolicyOptions options)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (object row in rows)
        {
            List<string> parts = new(keys.Count);

            foreach (Column key in keys)
            {
                PropertyInfo property = CacheReflection.FindProperty(row.GetType(), key.Name)!;

                parts.Add(MutatorCache.Read(property, row)?.ToString() ?? " ");
            }

            string composite = string.Join('', parts);

            if (!seen.Add(composite))
            {
                throw new PolicyException(
                    PolicyErrorCode.AmbiguousGroupKey,
                    string.Join(", ", keys.Select(k => k.Path)),
                    PolicyFeature.Group,
                    options.Tier)
                {
                    SourceOrigin =
                        "two groups share a key once transformed, so their aggregates can no longer " +
                        "be told apart"
                };
            }
        }
    }

    /// <summary>One column of a summary row, and the chain that transforms it.</summary>
    private sealed record Column(string Name, string Path, ValueTransform Chain);
}
