using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.DTOs;

namespace DynamicWhere.ex.Policies.Discovery;

/// <summary>
/// What a policy would do to one clause, without running it.
/// </summary>
/// <typeparam name="TClause">The clause simulated — a filter, a summary, or a set operation.</typeparam>
/// <remarks>
/// A refusal is an answer here, not an error. An operator asking what a caller's filter would do
/// wants "it would be refused, and here is why" reported the same way as "it would run, and here is
/// what it would become" — so the exception is caught, kept, and returned alongside the trace that
/// explains it. Letting it escape would lose the trace with it.
/// </remarks>
public sealed class PolicySimulation<TClause> where TClause : class
{
    /// <summary>Initializes a simulation result.</summary>
    /// <param name="clause">The sanitized clause, or null when the policy refused it.</param>
    /// <param name="trace">Everything the policy decided on the way.</param>
    /// <param name="refusal">The refusal, or null when the clause would run.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="trace"/> is null.</exception>
    public PolicySimulation(TClause? clause, PolicyTrace trace, PolicyException? refusal)
    {
        Clause = clause;
        Trace = trace ?? throw new ArgumentNullException(nameof(trace));
        Refusal = refusal;
    }

    /// <summary>
    /// The clause as the pipeline would receive it — fields dropped, predicates injected, names
    /// canonical — or null when the policy refused it outright.
    /// </summary>
    /// <remarks>
    /// A copy. The clause handed in is never modified, so an operator can simulate the same filter
    /// against several callers and compare the answers.
    /// </remarks>
    public TClause? Clause { get; }

    /// <summary>Everything the policy decided, in the order it decided it.</summary>
    public PolicyTrace Trace { get; }

    /// <summary>The refusal, or null when the clause would run.</summary>
    public PolicyException? Refusal { get; }

    /// <summary>True when the clause would run.</summary>
    public bool WouldRun => Refusal is null;
}
