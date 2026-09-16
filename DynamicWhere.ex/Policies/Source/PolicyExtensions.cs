using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.ex.Policies.Source;

/// <summary>
/// The entry point to a guarded query.
/// </summary>
public static class PolicyExtensions
{
    /// <summary>
    /// Attaches a caller to a query, so that everything asked of it afterwards is checked against
    /// that caller's policy.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="query">The source query.</param>
    /// <param name="context">Who is asking.</param>
    /// <returns>A handle mirroring the ordinary query methods, with enforcement in front of them.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="query"/> or <paramref name="context"/> is null.
    /// </exception>
    /// <remarks>
    /// The posture and the policy sources come from <see cref="DwPolicy"/> rather than from
    /// arguments here, because a posture that has to be passed at every call site is one that will
    /// eventually be forgotten at one of them.
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = context.Employees.ApplyPolicy(ctx).ToList(filter);
    /// </code>
    /// </example>
    public static PolicyQueryable<T> ApplyPolicy<T>(this IQueryable<T> query, DwPolicyContext context)
        where T : class =>
        ApplyPolicy(query, RequirePrepared(context), DwPolicy.Options, DwPolicy.Resolver);

    /// <summary>
    /// Attaches a caller to an in-memory sequence.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="query">The source sequence.</param>
    /// <param name="context">Who is asking.</param>
    /// <returns>A handle mirroring the ordinary query methods, with enforcement in front of them.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="query"/> or <paramref name="context"/> is null.
    /// </exception>
    /// <remarks>
    /// The unguarded surface carries the same overloads, and leaving them off here would mean a
    /// caller holding an <see cref="IEnumerable{T}"/> had to step outside the guarded path to use
    /// the library at all.
    /// </remarks>
    public static PolicyQueryable<T> ApplyPolicy<T>(this IEnumerable<T> query, DwPolicyContext context)
        where T : class =>
        ApplyPolicy(
            query?.AsQueryable() ?? throw new ArgumentNullException(nameof(query)),
            RequirePrepared(context),
            DwPolicy.Options,
            DwPolicy.Resolver);

    /// <summary>
    /// Attaches a caller to a query using an explicit posture and resolver, rather than the
    /// application-wide configuration.
    /// </summary>
    /// <typeparam name="T">The entity type being queried.</typeparam>
    /// <param name="query">The source query.</param>
    /// <param name="context">Who is asking.</param>
    /// <param name="options">The posture to enforce.</param>
    /// <param name="resolver">The resolver to consult.</param>
    /// <returns>A handle mirroring the ordinary query methods, with enforcement in front of them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <remarks>
    /// For a host that composes its own configuration, and for tests that must not depend on
    /// process-wide state. Ordinary application code should use the overload that reads
    /// <see cref="DwPolicy"/>.
    /// </remarks>
    public static PolicyQueryable<T> ApplyPolicy<T>(
        this IQueryable<T> query,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyResolver resolver)
        where T : class
    {
        if (query is null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (resolver is null)
        {
            throw new ArgumentNullException(nameof(resolver));
        }

        return new PolicyQueryable<T>(query, context, resolver, options);
    }

    /// <summary>
    /// Refuses a context that never went through <c>DwPolicy.PrepareAsync</c>.
    /// </summary>
    /// <remarks>
    /// A store provider already refuses one, because it has no pinned snapshot to answer from. With
    /// attributes alone nothing refused it, so the same missing call was a failure in one deployment
    /// and silence in another — and the deployment where it was silent is the one that later adds a
    /// store and starts refusing in production. Preparation is the ceremony that says which caller
    /// this is; a query that skipped it is refused wherever it runs.
    /// <para>
    /// Only on the overloads that read <see cref="DwPolicy"/>, because that is where
    /// <c>PrepareAsync</c> is the documented ceremony. A host composing its own options and resolver
    /// owns preparation itself, and a store it hands in still refuses an unprepared context.
    /// </para>
    /// </remarks>
    /// <exception cref="PolicyException">Thrown with <c>PolicyContextNotPrepared</c>.</exception>
    private static DwPolicyContext RequirePrepared(DwPolicyContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (!context.IsPrepared)
        {
            throw new PolicyException(
                PolicyErrorCode.PolicyContextNotPrepared,
                "*",
                PolicyFeature.None,
                DwPolicy.Options.Tier);
        }

        return context;
    }
}
