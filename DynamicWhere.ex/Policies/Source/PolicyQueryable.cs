using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Audit;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Source;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

namespace DynamicWhere.ex.Policies.Source;

/// <summary>
/// A query with a caller attached. Every method sanitizes what it is given, hands the result to the
/// existing extension method unchanged, and records what was decided.
/// </summary>
/// <typeparam name="T">The entity type being queried.</typeparam>
/// <remarks>
/// The handle exists so that enforcement is visible at the call site. An implicit interceptor would
/// guard more queries with less ceremony, but it would also make the guarded and unguarded paths
/// indistinguishable in a code review, and <c>[DwEntity(RequirePolicy = true)]</c> is what closes
/// the gap for types that must never be read without one.
/// <para>
/// The surface mirrors the unguarded one method for method. Anything missing here is a method a
/// caller has to drop out of the guarded path to reach, which is the same hole
/// <c>RequirePolicy</c> exists to close.
/// </para>
/// </remarks>
public sealed class PolicyQueryable<T> where T : class
{
    private readonly IQueryable<T> _source;
    private readonly DwPolicyContext _context;
    private readonly PolicyResolver _resolver;
    private readonly DwPolicyOptions _options;
    private readonly PolicyTrace? _carried;

    private TypePolicy? _typePolicy;

    /// <summary>
    /// Initializes the handle.
    /// </summary>
    internal PolicyQueryable(
        IQueryable<T> source,
        DwPolicyContext context,
        PolicyResolver resolver,
        DwPolicyOptions options,
        PolicyTrace? carried = null)
    {
        _source = source;
        _context = context;
        _resolver = resolver;
        _options = options;
        _carried = carried;
    }

    /// <summary>
    /// What this type's policy says that no single field can answer, resolved once per handle.
    /// </summary>
    /// <remarks>
    /// A handle is per query, so this is one provider sweep however many methods are chained onto
    /// it. It cannot be cached per type: a runtime rule may set an alias or a transform, so the
    /// answer varies by caller.
    /// </remarks>
    private TypePolicy TypePolicy => _typePolicy ??= _resolver.ResolveType(typeof(T), _context);

    /// <summary>
    /// What the policy did to the most recent call on this handle.
    /// </summary>
    /// <remarks>
    /// The methods returning a result object attach the record to it directly. The composable ones
    /// return an <see cref="IQueryable{T}"/>, which has nowhere to carry it, so this is where a
    /// caller composing a query by hand can read what was dropped.
    /// </remarks>
    public PolicyTrace? LastTrace { get; private set; }

    // ------------------------------------------------------------------ terminal, filter

    /// <summary>Applies a filter and returns the matching page.</summary>
    /// <param name="filter">The caller's filter. Never modified.</param>
    /// <param name="getQueryString">When true, includes the generated SQL in the result.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filter"/> is null.</exception>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the filter.</exception>
    public FilterResult<T> ToList(Filter filter, bool getQueryString = false)
    {
        try
        {
            PolicyTrace trace = NewTrace();
            GuardQueryString(getQueryString, trace);

            Filter sanitized = Sanitize(filter, trace);

            using (PolicyScope.Enter(_context, LastTrace))
            {
                FilterResult<T> result = Guarded().ToList(sanitized, getQueryString);

                ResultTransformer.Rows(
                    result.Data, TypePolicy, sanitized.Selects, _context, _options, trace);

                result.Policy = _options.TraceInResult ? trace : null;

                return result;
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    /// <summary>Applies a filter asynchronously and returns the matching page.</summary>
    /// <param name="filter">The caller's filter. Never modified.</param>
    /// <param name="getQueryString">When true, includes the generated SQL in the result.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filter"/> is null.</exception>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the filter.</exception>
    public async Task<FilterResult<T>> ToListAsync(Filter filter, bool getQueryString = false)
    {
        try
        {
            PolicyTrace trace = NewTrace();
            GuardQueryString(getQueryString, trace);

            Filter sanitized = Sanitize(filter, trace);

            using (PolicyScope.Enter(_context, LastTrace))
            {
                FilterResult<T> result = await Guarded().ToListAsync(sanitized, getQueryString);

                ResultTransformer.Rows(
                    result.Data, TypePolicy, sanitized.Selects, _context, _options, trace);

                result.Policy = _options.TraceInResult ? trace : null;

                return result;
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    /// <summary>Applies a filter and returns the matching page as dynamic objects.</summary>
    /// <param name="filter">The caller's filter. Never modified.</param>
    /// <param name="getQueryString">When true, includes the generated SQL in the result.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filter"/> is null.</exception>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the filter.</exception>
    public FilterResult<dynamic> ToListDynamic(Filter filter, bool getQueryString = false)
    {
        try
        {
            PolicyTrace trace = NewTrace();
            GuardQueryString(getQueryString, trace);

            Filter sanitized = Sanitize(filter, trace);

            using (PolicyScope.Enter(_context, LastTrace))
            {
                FilterResult<dynamic> result = Guarded().ToListDynamic(sanitized, getQueryString);

                ResultTransformer.Rows(
                    result.Data, TypePolicy, sanitized.Selects, _context, _options, trace);

                // After transformation, not before: the transform pipeline reads the generated columns
                // by the names the projection baked in, and renaming first would leave it looking for
                // columns that no longer exist.
                ResultTransformer.Rename(result.Data, TypePolicy, trace);

                result.Policy = _options.TraceInResult ? trace : null;

                return result;
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    /// <summary>Applies a filter asynchronously and returns the matching page as dynamic objects.</summary>
    /// <param name="filter">The caller's filter. Never modified.</param>
    /// <param name="getQueryString">When true, includes the generated SQL in the result.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filter"/> is null.</exception>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the filter.</exception>
    public async Task<FilterResult<dynamic>> ToListAsyncDynamic(Filter filter, bool getQueryString = false)
    {
        try
        {
            PolicyTrace trace = NewTrace();
            GuardQueryString(getQueryString, trace);

            Filter sanitized = Sanitize(filter, trace);

            using (PolicyScope.Enter(_context, LastTrace))
            {
                FilterResult<dynamic> result = await Guarded().ToListAsyncDynamic(sanitized, getQueryString);

                ResultTransformer.Rows(
                    result.Data, TypePolicy, sanitized.Selects, _context, _options, trace);

                // After transformation, not before: the transform pipeline reads the generated columns
                // by the names the projection baked in, and renaming first would leave it looking for
                // columns that no longer exist.
                ResultTransformer.Rename(result.Data, TypePolicy, trace);

                result.Policy = _options.TraceInResult ? trace : null;

                return result;
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    // ----------------------------------------------------------------- terminal, summary

    /// <summary>Applies a summary and returns the grouped page.</summary>
    /// <param name="summary">The caller's summary. Never modified.</param>
    /// <param name="getQueryString">When true, includes the generated SQL in the result.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="summary"/> is null.</exception>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the summary.</exception>
    public SummaryResult ToList(Summary summary, bool getQueryString = false)
    {
        try
        {
            PolicyTrace trace = NewTrace();
            GuardQueryString(getQueryString, trace);

            Summary sanitized = Sanitize(summary, trace);

            using (PolicyScope.Enter(_context, LastTrace))
            {
                SummaryResult result = Guarded().ToList(sanitized, getQueryString);

                // First of the three, and the order matters. A group below the floor is one the caller
                // may not see at all, so nothing downstream should form an opinion about it: transformed
                // second, two suppressed groups whose keys collided once rounded refused the whole
                // summary, denying a result because of rows that were never going to be returned.
                //
                // It reads a column this library added and nothing has transformed, so running first
                // costs it nothing.
                int floor = GroupFloor.For(sanitized, TypePolicy, _options);

                ResultTransformer.Suppress(
                    result, floor, _options.DryRun || _context.DryRun, trace);

                ResultTransformer.Summary(result, sanitized, TypePolicy, _context, _options, trace);

                // After the collision check, which reads the real column names. A summary key is a
                // generated column like any other, so it follows the same vocabulary the schema
                // advertises — and the same pass drops the group-size count the floor added for itself.
                ResultTransformer.Rename(
                    result.Data, TypePolicy, trace, floor > 1 ? GroupFloor.SizeAlias : null);

                result.Policy = _options.TraceInResult ? trace : null;

                return result;
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    /// <summary>Applies a summary asynchronously and returns the grouped page.</summary>
    /// <param name="summary">The caller's summary. Never modified.</param>
    /// <param name="getQueryString">When true, includes the generated SQL in the result.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="summary"/> is null.</exception>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the summary.</exception>
    public async Task<SummaryResult> ToListAsync(Summary summary, bool getQueryString = false)
    {
        try
        {
            PolicyTrace trace = NewTrace();
            GuardQueryString(getQueryString, trace);

            Summary sanitized = Sanitize(summary, trace);

            using (PolicyScope.Enter(_context, LastTrace))
            {
                SummaryResult result = await Guarded().ToListAsync(sanitized, getQueryString);

                // First of the three, and the order matters. A group below the floor is one the caller
                // may not see at all, so nothing downstream should form an opinion about it: transformed
                // second, two suppressed groups whose keys collided once rounded refused the whole
                // summary, denying a result because of rows that were never going to be returned.
                //
                // It reads a column this library added and nothing has transformed, so running first
                // costs it nothing.
                int floor = GroupFloor.For(sanitized, TypePolicy, _options);

                ResultTransformer.Suppress(
                    result, floor, _options.DryRun || _context.DryRun, trace);

                ResultTransformer.Summary(result, sanitized, TypePolicy, _context, _options, trace);

                // After the collision check, which reads the real column names. A summary key is a
                // generated column like any other, so it follows the same vocabulary the schema
                // advertises — and the same pass drops the group-size count the floor added for itself.
                ResultTransformer.Rename(
                    result.Data, TypePolicy, trace, floor > 1 ? GroupFloor.SizeAlias : null);

                result.Policy = _options.TraceInResult ? trace : null;

                return result;
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    // ----------------------------------------------------------------- terminal, segment

    /// <summary>Applies a set operation asynchronously and returns the combined page.</summary>
    /// <param name="segment">The caller's segment. Never modified.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="segment"/> is null.</exception>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the segment.</exception>
    public async Task<SegmentResult<T>> ToListAsync(Segment segment)
    {
        try
        {
            PolicyTrace trace = NewTrace();

            Segment sanitized = FilterSanitizer.Sanitize<T>(
                segment, _resolver, _context, _options, trace,
                applyDefaultOrder: !DefaultOrder.IsOrdered(_source.Expression));

            LastTrace = trace;

            using (PolicyScope.Enter(_context, LastTrace))
            {
                SegmentResult<T> result = await Guarded().ToListAsync(sanitized);

                ResultTransformer.Rows(
                    result.Data, TypePolicy, sanitized.Selects, _context, _options, trace);

                result.Policy = _options.TraceInResult ? trace : null;

                return result;
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    // --------------------------------------------------------------------- composable

    /// <summary>Projects the allowed subset of the requested fields.</summary>
    /// <param name="fields">The fields to project.</param>
    /// <exception cref="PolicyException">Thrown when the policy refuses every requested field.</exception>
    public PolicyQueryable<T> Select(List<string> fields)
    {
        try
        {
            Filter sanitized = SanitizeClause(new Filter { Selects = fields });

            using (PolicyScope.Enter(_context, LastTrace))
            {
                return Chain(Scoped(sanitized).Select(sanitized.Selects!));
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    /// <summary>Projects the allowed subset of the requested fields as dynamic objects.</summary>
    /// <param name="fields">The fields to project.</param>
    /// <exception cref="PolicyException">Thrown when the policy refuses every requested field.</exception>
    public IQueryable SelectDynamic(List<string> fields)
    {
        try
        {
            RefuseUnmaterialized(nameof(SelectDynamic), nameof(ToListDynamic));

            Filter sanitized = SanitizeClause(new Filter { Selects = fields });

            using (PolicyScope.Enter(_context, LastTrace))
            {
                return Scoped(sanitized).SelectDynamic(sanitized.Selects!);
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    /// <summary>Applies one condition.</summary>
    /// <param name="condition">The condition to apply.</param>
    /// <exception cref="PolicyException">Thrown when the policy refuses the condition.</exception>
    public PolicyQueryable<T> Where(Condition condition)
    {
        try
        {
            ConditionGroup group = new();

            group.Conditions.Add(condition);

            Filter sanitized = SanitizeClause(new Filter { ConditionGroup = group });

            // The whole group, not Conditions[0]. Once a forced predicate is injected, index zero is
            // the library's own term and the caller's condition has moved into a subgroup -- taking the
            // first condition would silently drop what the caller actually asked for.
            using (PolicyScope.Enter(_context, LastTrace))
            {
                return Chain(Scoped(sanitized));
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    /// <summary>Applies a group of conditions.</summary>
    /// <param name="group">The conditions to apply.</param>
    /// <exception cref="PolicyException">Thrown when the policy refuses any condition.</exception>
    public PolicyQueryable<T> Where(ConditionGroup group)
    {
        try
        {
            Filter sanitized = SanitizeClause(new Filter { ConditionGroup = group });

            using (PolicyScope.Enter(_context, LastTrace))
            {
                return Chain(Scoped(sanitized));
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    /// <summary>Sorts by one field, unless the policy refuses it.</summary>
    /// <param name="order">The sort to apply.</param>
    /// <exception cref="PolicyException">Thrown in the strict tier when the policy refuses the field.</exception>
    public PolicyQueryable<T> Order(OrderBy order) => Order(new List<OrderBy> { order });

    /// <summary>Sorts by the allowed subset of the requested fields.</summary>
    /// <param name="orders">The sorts to apply.</param>
    /// <exception cref="PolicyException">Thrown in the strict tier when the policy refuses a field.</exception>
    public PolicyQueryable<T> Order(List<OrderBy> orders)
    {
        try
        {
            Filter sanitized = SanitizeClause(new Filter { Orders = orders });

            using (PolicyScope.Enter(_context, LastTrace))
            {
                return Chain(Scoped(sanitized).Order(sanitized.Orders!));
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    /// <summary>Takes one page, unless it exceeds the configured cap.</summary>
    /// <param name="page">The page to take.</param>
    /// <exception cref="PolicyException">Thrown when the page exceeds the cap.</exception>
    public PolicyQueryable<T> Page(PageBy page)
    {
        try
        {
            // A query nothing has ordered takes the type's default, gated like any other order. One the
            // caller ordered keeps that order: a default applied here would replace it.
            Filter sanitized = SanitizeClause(
                new Filter { Page = page }, applyDefaultOrder: !DefaultOrder.IsOrdered(_source.Expression));

            using (PolicyScope.Enter(_context, LastTrace))
            {
                IQueryable<T> scoped = Scoped(sanitized);

                if (sanitized.Orders is { Count: > 0 })
                {
                    scoped = scoped.Order(sanitized.Orders);
                }

                return Chain(scoped.Page(sanitized.Page!));
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    /// <summary>Groups and aggregates, unless the policy refuses a key or an aggregate.</summary>
    /// <param name="groupBy">The grouping to apply.</param>
    /// <exception cref="PolicyException">Thrown when the policy refuses a key or an aggregated field.</exception>
    /// <remarks>
    /// Runs through the summary pipeline rather than core <c>Group</c>. The sanitizer adds the group
    /// floor's count and the <c>HAVING</c> that enforces it, and core <c>Group</c> takes no
    /// <c>Having</c> — so grouping directly here would hand back exactly the small groups the floor
    /// exists to suppress, with the floor's own count sitting in them as a column.
    /// </remarks>
    public IQueryable Group(GroupBy groupBy)
    {
        try
        {
            RefuseUnmaterialized(nameof(Group), "ToList(Summary)");

            Summary sanitized = Sanitize(new Summary { GroupBy = groupBy }, NewTrace());

            // No page. The sanitizer fills one in from DwCaps.DefaultPageSize for a summary that sent
            // none, but Group takes no page and no order: what came back would be the first groups in no
            // particular order, with nothing a caller could pass to reach the rest. The caller pages
            // what this returns, as it always did, or sends a Summary, which carries a page.
            sanitized.Page = null;

            using (PolicyScope.Enter(_context, LastTrace))
            {
                return WithoutGroupSize(Guarded().Summary(sanitized), sanitized);
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    /// <summary>Applies a whole filter and returns the query, without executing it.</summary>
    /// <param name="filter">The caller's filter. Never modified.</param>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the filter.</exception>
    public PolicyQueryable<T> Filter(Filter filter)
    {
        try
        {
            PolicyTrace trace = NewTrace();
            Filter sanitized = Sanitize(filter, trace);

            using (PolicyScope.Enter(_context, LastTrace))
            {
                return Chain(Guarded().Filter(sanitized));
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    /// <summary>Applies a whole filter and returns a dynamic query, without executing it.</summary>
    /// <param name="filter">The caller's filter. Never modified.</param>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the filter.</exception>
    public IQueryable FilterDynamic(Filter filter)
    {
        try
        {
            RefuseUnmaterialized(nameof(FilterDynamic), nameof(ToListDynamic));

            PolicyTrace trace = NewTrace();
            Filter sanitized = Sanitize(filter, trace);

            using (PolicyScope.Enter(_context, LastTrace))
            {
                return Guarded().FilterDynamic(sanitized);
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    /// <summary>Applies a summary and returns the query, without executing it.</summary>
    /// <param name="summary">The caller's summary. Never modified.</param>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the summary.</exception>
    public IQueryable Summary(Summary summary)
    {
        try
        {
            RefuseUnmaterialized(nameof(Summary), "ToList(Summary)");

            Summary sanitized = Sanitize(summary, NewTrace());

            using (PolicyScope.Enter(_context, LastTrace))
            {
                return WithoutGroupSize(Guarded().Summary(sanitized), sanitized);
            }
        }
        catch (PolicyException refusal) when (Refused(refusal))
        {
            throw;
        }
    }

    /// <summary>
    /// Projects the group floor's own count back out of a composable result.
    /// </summary>
    /// <param name="grouped">The grouped query, as the summary pipeline built it.</param>
    /// <param name="sanitized">The summary that ran, carrying the caller's keys and aliases.</param>
    /// <returns>The query projected onto what the caller actually asked for.</returns>
    /// <remarks>
    /// The terminal methods drop the column while they transform the materialized rows. A composable
    /// call has no such pass, so without this the caller receives a column the library added for
    /// itself — one they could order by, page on, or hand to a client as data.
    /// <para>
    /// Group keys are named by their path with the dots removed, which is what the grouping
    /// projection emits, and the aggregates by their aliases.
    /// </para>
    /// </remarks>
    private static IQueryable WithoutGroupSize(IQueryable grouped, Summary sanitized)
    {
        if (sanitized.GroupBy is not { } groupBy || !groupBy.AggregateBy.Any(IsGroupSize))
        {
            return grouped;
        }

        IEnumerable<string> columns = groupBy.Fields
            .Select(field => field.Replace(".", string.Empty))
            .Concat(groupBy.AggregateBy
                .Where(aggregate => !IsGroupSize(aggregate))
                .Select(aggregate => aggregate.Alias!));

        return grouped.Select(DynamicLinq.Config, $"new ({string.Join(", ", columns)})");

        static bool IsGroupSize(AggregateBy aggregate) =>
            string.Equals(aggregate.Alias, GroupFloor.SizeAlias, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------------ internals

    /// <summary>
    /// Detaches the query from change tracking.
    /// </summary>
    /// <remarks>
    /// Not an optimization. Masking transforms values after materialization, and a tracked entity
    /// carrying a masked value would be written back to the database on the next
    /// <c>SaveChanges</c> — silently replacing real data with the mask. Applied here, at the single
    /// point every guarded query passes through, so no individual method can forget it.
    /// <para>
    /// Harmless on a non-EF source: the EF extension returns the query unchanged when the provider
    /// is not one of its own.
    /// </para>
    /// </remarks>
    private IQueryable<T> Guarded() => _source.AsNoTracking();

    /// <summary>
    /// Returns the query as a plain <see cref="IQueryable{T}"/>, outside the guard.
    /// </summary>
    /// <remarks>
    /// The deliberate way out, for a caller who needs EF composition the handle does not mirror —
    /// an <c>Include</c>, a join, a projection into their own type. What comes back is gated and
    /// scoped exactly as the handle left it, and is <em>not</em> transformed: the rows go from EF to
    /// the caller without passing through here again.
    /// <para>
    /// Named rather than implicit so that it is greppable, which is the whole reason the composable
    /// methods return a handle instead of an <see cref="IQueryable{T}"/>. A reviewer can find every
    /// place masking was stepped around by searching for this one word.
    /// </para>
    /// </remarks>
    public IQueryable<T> AsUnguardedQueryable() => _source;

    /// <summary>Wraps a composed query back into a handle, carrying the trace so far.</summary>
    private PolicyQueryable<T> Chain(IQueryable<T> composed) =>
        new(composed, _context, _resolver, _options, LastTrace);

    /// <summary>
    /// Refuses a method that hands back a query the caller materializes, when this type's values
    /// are transformed on the way out.
    /// </summary>
    /// <remarks>
    /// Transformation happens on materialized objects. A query the caller runs themselves is one the
    /// library never sees, so a mask on it would simply not happen — quietly, and one method call
    /// away from the terminal method that does mask. The generic composable methods avoid this by
    /// returning a handle; these four cannot, because what they return is no longer a sequence of
    /// <typeparamref name="T"/>.
    /// </remarks>
    private void RefuseUnmaterialized(string method, string instead)
    {
        if (TypePolicy.Transforms.Count == 0)
        {
            return;
        }

        throw new PolicyException(
            PolicyErrorCode.TransformRequiresMaterialization,
            string.Join(", ", TypePolicy.Transforms.Keys),
            PolicyFeature.Select,
            _options.Tier)
        {
            SourceOrigin =
                $"{method} returns a query for the caller to run, and a transformed value only " +
                $"exists once the library has materialized it. Use {instead}, or " +
                "AsUnguardedQueryable() to leave the guarded path deliberately."
        };
    }

    /// <summary>
    /// Refuses, in the strict tier, a request to return the generated SQL.
    /// </summary>
    /// <remarks>
    /// Design section 7.5. The query text names the columns of denied fields and spells out every
    /// injected predicate, so handing it back discloses both the schema and the shape of the scope
    /// confining the caller. Injection made this sharper rather than milder: before it there was no
    /// tenant predicate in the text to read.
    /// <para>
    /// The convenience tier still returns it, documented. Its caller is the project's own front end,
    /// for which the query text is a debugging aid rather than a disclosure.
    /// </para>
    /// <para>
    /// Checked before the sanitizer runs, so a refused request does no work, and recorded either
    /// way. Dry run overrides it as it overrides every other refusal: an operator running a canary
    /// needs to know the request would have been refused.
    /// </para>
    /// </remarks>
    /// <param name="getQueryString">What the caller asked for.</param>
    /// <param name="trace">The record for this query.</param>
    /// <exception cref="PolicyException">Thrown in the strict tier when the SQL was asked for.</exception>
    private void GuardQueryString(bool getQueryString, PolicyTrace trace)
    {
        if (!getQueryString || _options.Tier != DwTier.Strict)
        {
            return;
        }

        const string origin =
            "the generated SQL names the columns of denied fields and every injected predicate";

        trace.Add(new PolicyDecision(
            PolicyFragment.Wildcard, PolicyFeature.None, PolicyAction.Denied, origin));

        LastTrace = trace;

        if (_options.DryRun || _context.DryRun)
        {
            return;
        }

        throw new PolicyException(
            PolicyErrorCode.QueryStringDenied, PolicyFragment.Wildcard, PolicyFeature.None,
            _options.Tier)
        {
            SourceOrigin = origin
        };
    }

    /// <summary>
    /// The detached query with any forced predicate already applied.
    /// </summary>
    /// <remarks>
    /// The composable methods each return an <see cref="IQueryable{T}"/> that the caller goes on to
    /// materialize, so a scope applied only to the terminal methods is bypassed by composing rather
    /// than terminating. The sanitizer has already put the forced predicate into the clause's
    /// condition group -- including for a clause that carried none, which is exactly the caller a
    /// scope exists for -- so applying that group here covers every one of them.
    /// <para>
    /// When nothing is forced the group is whatever the caller sent, and for the clauses that carry
    /// no conditions at all it is null, so this is the query unchanged.
    /// </para>
    /// </remarks>
    private IQueryable<T> Scoped(Filter sanitized) => Scoped(sanitized.ConditionGroup);

    /// <summary>The detached query filtered by a condition group, or unfiltered when it is null.</summary>
    private IQueryable<T> Scoped(ConditionGroup? group) =>
        group is null ? Guarded() : Guarded().Where(group);

    /// <summary>Sanitizes a whole filter and records the outcome.</summary>
    private Filter Sanitize(Filter filter, PolicyTrace trace)
    {
        // A source the caller ordered before guarding it keeps that order, as it does unguarded.
        Filter sanitized = FilterSanitizer.Sanitize<T>(
            filter, _resolver, _context, _options, trace,
            applyDefaultOrder: !DefaultOrder.IsOrdered(_source.Expression));

        LastTrace = trace;

        return sanitized;
    }

    /// <summary>Sanitizes a summary and records the outcome.</summary>
    private Summary Sanitize(Summary summary, PolicyTrace trace)
    {
        Summary sanitized = FilterSanitizer.Sanitize<T>(summary, _resolver, _context, _options, trace);

        LastTrace = trace;

        return sanitized;
    }

    /// <summary>
    /// Sanitizes a filter standing in for one composed clause.
    /// </summary>
    /// <remarks>
    /// Projection synthesis is suppressed. The caller supplied one clause, not a whole filter, so
    /// there is no projection to complete — and synthesizing one anyway would let a
    /// <c>Where</c> call fail with "every projection field denied" on a type whose fields are all
    /// refused for select.
    /// </remarks>
    private Filter SanitizeClause(Filter clause, bool applyDefaultOrder = false)
    {
        PolicyTrace trace = NewTrace();

        Filter sanitized = FilterSanitizer.Sanitize<T>(
            clause, _resolver, _context, _options, trace, synthesizeProjection: false, applyDefaultOrder);

        LastTrace = trace;

        return sanitized;
    }

    /// <summary>
    /// Writes a refusal to the caller's audit buffer when the posture audits refusals, and lets it go on
    /// its way.
    /// </summary>
    /// <returns>
    /// Always false, so the exception filter that calls it never catches anything: the refusal leaves
    /// with its own stack, and a nested call that sees it again does not record it twice.
    /// </returns>
    private bool Refused(PolicyException refusal)
    {
        RefusalAudit.Record(_context, _options, typeof(T), refusal);

        return false;
    }

    /// <summary>
    /// Starts a record for one query.
    /// </summary>
    /// <remarks>
    /// Dry run is the union of the two switches. The global one turns enforcement off everywhere;
    /// the per-context one turns it off for a single caller, which is what lets one canary subject
    /// run unenforced while everyone else stays enforced.
    /// </remarks>
    private PolicyTrace NewTrace()
    {
        PolicyTrace trace = new(_options.Tier, _options.DryRun || _context.DryRun);

        // A chained handle starts from what the previous links decided, so the trace on the terminal
        // result describes the whole chain rather than only its last step.
        if (_carried is not null)
        {
            foreach (PolicyDecision decision in _carried.Decisions)
            {
                trace.Add(decision);
            }
        }

        return trace;
    }
}
