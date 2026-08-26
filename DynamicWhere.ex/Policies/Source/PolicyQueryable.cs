using DynamicWhere.ex.Classes.Complex;
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
/// existing extension method unchanged, and attaches the record of what was decided.
/// </summary>
/// <typeparam name="T">The entity type being queried.</typeparam>
/// <remarks>
/// The handle exists so that enforcement is visible at the call site. An implicit interceptor would
/// guard more queries with less ceremony, but it would also make the guarded and unguarded paths
/// indistinguishable in a code review, and <c>[DwEntity(RequirePolicy = true)]</c> is what closes
/// the gap for types that must never be read without one.
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
    /// Applies a filter and returns the matching page, with the policy decisions attached.
    /// </summary>
    /// <param name="filter">The caller's filter. Never modified.</param>
    /// <param name="getQueryString">When true, includes the generated SQL in the result.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="filter"/> is null.</exception>
    /// <exception cref="PolicyException">Thrown when the policy refuses part of the filter.</exception>
    public FilterResult<T> ToList(Filter filter, bool getQueryString = false)
    {
        PolicyTrace trace = NewTrace();

        Filter sanitized = Sanitize(filter, trace);

        // Entered after sanitizing, not before: sanitization is where a refusal is decided, and it
        // needs no scope of its own.
        using (PolicyScope.Enter(_context))
        {
            FilterResult<T> result = Guarded().ToList(sanitized, getQueryString);

            result.Policy = trace;

            return result;
        }
    }

    /// <summary>
    /// Applies a filter asynchronously and returns the matching page, with the policy decisions
    /// attached.
    /// </summary>
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

    /// <summary>Sanitizes a filter against this caller's policy.</summary>
    private Filter Sanitize(Filter filter, PolicyTrace trace) =>
        FilterSanitizer.Sanitize<T>(filter, _resolver, _context, _options, trace);

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
