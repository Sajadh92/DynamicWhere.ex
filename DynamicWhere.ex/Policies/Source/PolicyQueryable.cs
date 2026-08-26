using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Source;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>
    /// Initializes the handle.
    /// </summary>
    internal PolicyQueryable(
        IQueryable<T> source,
        DwPolicyContext context,
        PolicyResolver resolver,
        DwPolicyOptions options)
    {
        _source = source;
        _context = context;
        _resolver = resolver;
        _options = options;
    }

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
        PolicyTrace trace = NewTrace();
        Filter sanitized = Sanitize(filter, trace);

        using (PolicyScope.Enter(_context))
        {
            FilterResult<T> result = Guarded().ToList(sanitized, getQueryString);

            result.Policy = trace;

            return result;
        }
    }

    /// <summary>Applies a filter asynchronously and returns the matching page.</summary>
    /// <param name="filter">The caller's filter. Never modified.</param>
    /// <param name="getQueryString">When true, includes the generated SQL in the result.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filter"/> is null.</exception>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the filter.</exception>
    public async Task<FilterResult<T>> ToListAsync(Filter filter, bool getQueryString = false)
    {
        PolicyTrace trace = NewTrace();
        Filter sanitized = Sanitize(filter, trace);

        using (PolicyScope.Enter(_context))
        {
            FilterResult<T> result = await Guarded().ToListAsync(sanitized, getQueryString);

            result.Policy = trace;

            return result;
        }
    }

    /// <summary>Applies a filter and returns the matching page as dynamic objects.</summary>
    /// <param name="filter">The caller's filter. Never modified.</param>
    /// <param name="getQueryString">When true, includes the generated SQL in the result.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filter"/> is null.</exception>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the filter.</exception>
    public FilterResult<dynamic> ToListDynamic(Filter filter, bool getQueryString = false)
    {
        PolicyTrace trace = NewTrace();
        Filter sanitized = Sanitize(filter, trace);

        using (PolicyScope.Enter(_context))
        {
            FilterResult<dynamic> result = Guarded().ToListDynamic(sanitized, getQueryString);

            result.Policy = trace;

            return result;
        }
    }

    /// <summary>Applies a filter asynchronously and returns the matching page as dynamic objects.</summary>
    /// <param name="filter">The caller's filter. Never modified.</param>
    /// <param name="getQueryString">When true, includes the generated SQL in the result.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filter"/> is null.</exception>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the filter.</exception>
    public async Task<FilterResult<dynamic>> ToListAsyncDynamic(Filter filter, bool getQueryString = false)
    {
        PolicyTrace trace = NewTrace();
        Filter sanitized = Sanitize(filter, trace);

        using (PolicyScope.Enter(_context))
        {
            FilterResult<dynamic> result = await Guarded().ToListAsyncDynamic(sanitized, getQueryString);

            result.Policy = trace;

            return result;
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
        PolicyTrace trace = NewTrace();
        Summary sanitized = Sanitize(summary, trace);

        using (PolicyScope.Enter(_context))
        {
            SummaryResult result = Guarded().ToList(sanitized, getQueryString);

            result.Policy = trace;

            return result;
        }
    }

    /// <summary>Applies a summary asynchronously and returns the grouped page.</summary>
    /// <param name="summary">The caller's summary. Never modified.</param>
    /// <param name="getQueryString">When true, includes the generated SQL in the result.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="summary"/> is null.</exception>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the summary.</exception>
    public async Task<SummaryResult> ToListAsync(Summary summary, bool getQueryString = false)
    {
        PolicyTrace trace = NewTrace();
        Summary sanitized = Sanitize(summary, trace);

        using (PolicyScope.Enter(_context))
        {
            SummaryResult result = await Guarded().ToListAsync(sanitized, getQueryString);

            result.Policy = trace;

            return result;
        }
    }

    // ----------------------------------------------------------------- terminal, segment

    /// <summary>Applies a set operation asynchronously and returns the combined page.</summary>
    /// <param name="segment">The caller's segment. Never modified.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="segment"/> is null.</exception>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the segment.</exception>
    public async Task<SegmentResult<T>> ToListAsync(Segment segment)
    {
        PolicyTrace trace = NewTrace();

        Segment sanitized = FilterSanitizer.Sanitize<T>(segment, _resolver, _context, _options, trace);

        LastTrace = trace;

        using (PolicyScope.Enter(_context))
        {
            SegmentResult<T> result = await Guarded().ToListAsync(sanitized);

            result.Policy = trace;

            return result;
        }
    }

    // --------------------------------------------------------------------- composable

    /// <summary>Projects the allowed subset of the requested fields.</summary>
    /// <param name="fields">The fields to project.</param>
    /// <exception cref="PolicyException">Thrown when the policy refuses every requested field.</exception>
    public IQueryable<T> Select(List<string> fields)
    {
        Filter sanitized = SanitizeClause(new Filter { Selects = fields });

        using (PolicyScope.Enter(_context))
        {
            return Guarded().Select(sanitized.Selects!);
        }
    }

    /// <summary>Projects the allowed subset of the requested fields as dynamic objects.</summary>
    /// <param name="fields">The fields to project.</param>
    /// <exception cref="PolicyException">Thrown when the policy refuses every requested field.</exception>
    public IQueryable SelectDynamic(List<string> fields)
    {
        Filter sanitized = SanitizeClause(new Filter { Selects = fields });

        using (PolicyScope.Enter(_context))
        {
            return Guarded().SelectDynamic(sanitized.Selects!);
        }
    }

    /// <summary>Applies one condition.</summary>
    /// <param name="condition">The condition to apply.</param>
    /// <exception cref="PolicyException">Thrown when the policy refuses the condition.</exception>
    public IQueryable<T> Where(Condition condition)
    {
        ConditionGroup group = new();

        group.Conditions.Add(condition);

        Filter sanitized = SanitizeClause(new Filter { ConditionGroup = group });

        using (PolicyScope.Enter(_context))
        {
            return Guarded().Where(sanitized.ConditionGroup!.Conditions[0]);
        }
    }

    /// <summary>Applies a group of conditions.</summary>
    /// <param name="group">The conditions to apply.</param>
    /// <exception cref="PolicyException">Thrown when the policy refuses any condition.</exception>
    public IQueryable<T> Where(ConditionGroup group)
    {
        Filter sanitized = SanitizeClause(new Filter { ConditionGroup = group });

        using (PolicyScope.Enter(_context))
        {
            return Guarded().Where(sanitized.ConditionGroup!);
        }
    }

    /// <summary>Sorts by one field, unless the policy refuses it.</summary>
    /// <param name="order">The sort to apply.</param>
    /// <exception cref="PolicyException">Thrown in the strict tier when the policy refuses the field.</exception>
    public IQueryable<T> Order(OrderBy order) => Order(new List<OrderBy> { order });

    /// <summary>Sorts by the allowed subset of the requested fields.</summary>
    /// <param name="orders">The sorts to apply.</param>
    /// <exception cref="PolicyException">Thrown in the strict tier when the policy refuses a field.</exception>
    public IQueryable<T> Order(List<OrderBy> orders)
    {
        Filter sanitized = SanitizeClause(new Filter { Orders = orders });

        using (PolicyScope.Enter(_context))
        {
            return Guarded().Order(sanitized.Orders!);
        }
    }

    /// <summary>Takes one page, unless it exceeds the configured cap.</summary>
    /// <param name="page">The page to take.</param>
    /// <exception cref="PolicyException">Thrown when the page exceeds the cap.</exception>
    public IQueryable<T> Page(PageBy page)
    {
        Filter sanitized = SanitizeClause(new Filter { Page = page });

        using (PolicyScope.Enter(_context))
        {
            return Guarded().Page(sanitized.Page!);
        }
    }

    /// <summary>Groups and aggregates, unless the policy refuses a key or an aggregate.</summary>
    /// <param name="groupBy">The grouping to apply.</param>
    /// <exception cref="PolicyException">Thrown when the policy refuses a key or an aggregated field.</exception>
    public IQueryable Group(GroupBy groupBy)
    {
        Summary sanitized = Sanitize(new Summary { GroupBy = groupBy }, NewTrace());

        using (PolicyScope.Enter(_context))
        {
            return Guarded().Group(sanitized.GroupBy!);
        }
    }

    /// <summary>Applies a whole filter and returns the query, without executing it.</summary>
    /// <param name="filter">The caller's filter. Never modified.</param>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the filter.</exception>
    public IQueryable<T> Filter(Filter filter)
    {
        PolicyTrace trace = NewTrace();
        Filter sanitized = Sanitize(filter, trace);

        using (PolicyScope.Enter(_context))
        {
            return Guarded().Filter(sanitized);
        }
    }

    /// <summary>Applies a whole filter and returns a dynamic query, without executing it.</summary>
    /// <param name="filter">The caller's filter. Never modified.</param>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the filter.</exception>
    public IQueryable FilterDynamic(Filter filter)
    {
        PolicyTrace trace = NewTrace();
        Filter sanitized = Sanitize(filter, trace);

        using (PolicyScope.Enter(_context))
        {
            return Guarded().FilterDynamic(sanitized);
        }
    }

    /// <summary>Applies a summary and returns the query, without executing it.</summary>
    /// <param name="summary">The caller's summary. Never modified.</param>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the summary.</exception>
    public IQueryable Summary(Summary summary)
    {
        Summary sanitized = Sanitize(summary, NewTrace());

        using (PolicyScope.Enter(_context))
        {
            return Guarded().Summary(sanitized);
        }
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

    /// <summary>Sanitizes a whole filter and records the outcome.</summary>
    private Filter Sanitize(Filter filter, PolicyTrace trace)
    {
        Filter sanitized = FilterSanitizer.Sanitize<T>(filter, _resolver, _context, _options, trace);

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
    private Filter SanitizeClause(Filter clause)
    {
        PolicyTrace trace = NewTrace();

        Filter sanitized = FilterSanitizer.Sanitize<T>(
            clause, _resolver, _context, _options, trace, synthesizeProjection: false);

        LastTrace = trace;

        return sanitized;
    }

    /// <summary>
    /// Starts a record for one query.
    /// </summary>
    /// <remarks>
    /// Dry run is the union of the two switches. The global one turns enforcement off everywhere;
    /// the per-context one turns it off for a single caller, which is what lets one canary subject
    /// run unenforced while everyone else stays enforced.
    /// </remarks>
    private PolicyTrace NewTrace() => new(_options.Tier, _options.DryRun || _context.DryRun);
}
