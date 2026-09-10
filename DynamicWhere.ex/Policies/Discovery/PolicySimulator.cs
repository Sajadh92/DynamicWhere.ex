using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;

namespace DynamicWhere.ex.Policies.Discovery;

/// <summary>
/// Runs a clause through the policy without running it against any data.
/// </summary>
/// <remarks>
/// Design section 5.7's <c>/simulate</c>: filter in, sanitized filter and trace out, no execution.
/// It exists in the core package for the same reason the schema does — the sanitizer that decides
/// what a filter becomes is an implementation detail of <c>ApplyPolicy</c>, and publishing it to let
/// another assembly call it would commit it as API permanently.
/// <para>
/// Nothing here touches a database. The sanitizer produces a sanitized copy and stops; the query it
/// would have been handed to is never built.
/// </para>
/// </remarks>
public static class PolicySimulator
{
    private static readonly ConcurrentDictionary<(Type Entity, Type Clause), MethodInfo> Dispatch = new();

    /// <summary>
    /// Simulates a filter.
    /// </summary>
    /// <typeparam name="T">The entity type the filter is written against.</typeparam>
    /// <param name="filter">The caller's filter. Never modified.</param>
    /// <param name="context">The caller. Never has audit events recorded against it.</param>
    /// <param name="options">The posture to simulate under.</param>
    /// <param name="resolver">Resolves the policy for one field.</param>
    /// <returns>What would happen, refusal included.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public static PolicySimulation<Filter> Simulate<T>(
        Filter filter, DwPolicyContext context, DwPolicyOptions options, PolicyResolver resolver)
        where T : class =>
        Run<Filter>(
            context, options,
            (probe, trace) => FilterSanitizer.Sanitize<T>(filter, resolver, probe, options, trace));

    /// <summary>
    /// Simulates a summary.
    /// </summary>
    /// <typeparam name="T">The entity type the summary is written against.</typeparam>
    /// <param name="summary">The caller's summary. Never modified.</param>
    /// <param name="context">The caller. Never has audit events recorded against it.</param>
    /// <param name="options">The posture to simulate under.</param>
    /// <param name="resolver">Resolves the policy for one field.</param>
    /// <returns>What would happen, refusal included.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public static PolicySimulation<Summary> Simulate<T>(
        Summary summary, DwPolicyContext context, DwPolicyOptions options, PolicyResolver resolver)
        where T : class =>
        Run<Summary>(
            context, options,
            (probe, trace) => FilterSanitizer.Sanitize<T>(summary, resolver, probe, options, trace));

    /// <summary>
    /// Simulates a set operation.
    /// </summary>
    /// <typeparam name="T">The entity type the segment is written against.</typeparam>
    /// <param name="segment">The caller's segment. Never modified.</param>
    /// <param name="context">The caller. Never has audit events recorded against it.</param>
    /// <param name="options">The posture to simulate under.</param>
    /// <param name="resolver">Resolves the policy for one field.</param>
    /// <returns>What would happen, refusal included.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public static PolicySimulation<Segment> Simulate<T>(
        Segment segment, DwPolicyContext context, DwPolicyOptions options, PolicyResolver resolver)
        where T : class =>
        Run<Segment>(
            context, options,
            (probe, trace) => FilterSanitizer.Sanitize<T>(segment, resolver, probe, options, trace));

    /// <summary>
    /// Simulates a clause against an entity type known only at runtime.
    /// </summary>
    /// <typeparam name="TClause">A <c>Filter</c>, <c>Summary</c> or <c>Segment</c>.</typeparam>
    /// <param name="entityType">The entity type the clause is written against.</param>
    /// <param name="clause">The caller's clause. Never modified.</param>
    /// <param name="context">The caller. Never has audit events recorded against it.</param>
    /// <param name="options">The posture to simulate under.</param>
    /// <param name="resolver">Resolves the policy for one field.</param>
    /// <returns>What would happen, refusal included.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="entityType"/> is not a reference type, or
    /// <typeparamref name="TClause"/> is not a clause this library simulates.
    /// </exception>
    /// <remarks>
    /// An administrative surface holds a <c>Type</c> resolved from a name, not a type parameter, so
    /// the generic overloads above are unreachable from it. The dispatch is cached per pair — this
    /// runs once per request on a screen an operator is reading, not on the query path.
    /// </remarks>
    public static PolicySimulation<TClause> Simulate<TClause>(
        Type entityType,
        TClause clause,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyResolver resolver)
        where TClause : class
    {
        if (entityType is null)
        {
            throw new ArgumentNullException(nameof(entityType));
        }

        if (entityType.IsValueType)
        {
            throw new ArgumentException(
                $"'{entityType.FullName}' is a value type. The guarded surface is defined over "
                + "reference types, so there is no query shape to simulate.",
                nameof(entityType));
        }

        MethodInfo bound = Dispatch.GetOrAdd(
            (entityType, typeof(TClause)),
            static key => Bind(key.Entity, key.Clause));

        try
        {
            return (PolicySimulation<TClause>)bound.Invoke(
                null, new object?[] { clause, context, options, resolver })!;
        }
        catch (TargetInvocationException wrapped) when (wrapped.InnerException is not null)
        {
            // Rethrown as itself, with its stack. Invoke wraps whatever the target threw, and a
            // caller catching a specific type — a host turning a malformed clause into a bad request
            // rather than a five-hundred — would never match against the wrapper. A dispatch
            // mechanism must not change the exception a caller sees.
            ExceptionDispatchInfo.Capture(wrapped.InnerException).Throw();

            throw;
        }
    }

    /// <summary>Finds the generic overload for one clause type and closes it over the entity.</summary>
    private static MethodInfo Bind(Type entityType, Type clauseType)
    {
        foreach (MethodInfo candidate in typeof(PolicySimulator).GetMethods(
            BindingFlags.Public | BindingFlags.Static))
        {
            if (candidate.Name != nameof(Simulate)
                || candidate.GetGenericArguments().Length != 1)
            {
                continue;
            }

            ParameterInfo[] parameters = candidate.GetParameters();

            if (parameters.Length == 4 && parameters[0].ParameterType == clauseType)
            {
                return candidate.MakeGenericMethod(entityType);
            }
        }

        throw new ArgumentException(
            $"'{clauseType.Name}' is not a clause this library simulates. Pass a Filter, a Summary, "
            + "or a Segment.");
    }

    /// <summary>
    /// Sanitizes on a copy of the caller's context, turning a refusal into an answer.
    /// </summary>
    /// <remarks>
    /// The copy is what keeps a simulation out of the audit log. Sanitizing consults the policy
    /// exactly as a real query does and would otherwise record the caller as having touched every
    /// audited field the clause names — an access that did not happen, written into the log kept
    /// precisely to establish which ones did.
    /// </remarks>
    private static PolicySimulation<TClause> Run<TClause>(
        DwPolicyContext context,
        DwPolicyOptions options,
        Func<DwPolicyContext, PolicyTrace, TClause> sanitize)
        where TClause : class
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        DwPolicyContext probe = context.ForSimulation();
        PolicyTrace trace = new(options.Tier, options.DryRun || context.DryRun);

        try
        {
            return new PolicySimulation<TClause>(sanitize(probe, trace), trace, refusal: null);
        }
        catch (PolicyException refused)
        {
            return new PolicySimulation<TClause>(clause: null, trace, refused);
        }
    }
}
