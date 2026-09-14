using System.Collections.ObjectModel;

namespace DynamicWhere.ex.Policies.Validation;

/// <summary>
/// What a startup scan found wrong, and what it found questionable.
/// </summary>
/// <remarks>
/// Two lists rather than one, because the two demand different answers. An error is a model that
/// cannot work — a mask whose output the member cannot hold, a transformer that is not one — and
/// starting anyway only defers the failure to a caller. A warning is a model that works and may not
/// be what its author meant, and the engine does not get to decide that.
/// </remarks>
public sealed class PolicyModelReport
{
    /// <summary>Initializes a report.</summary>
    /// <param name="errors">Problems that will fail a query.</param>
    /// <param name="warnings">Problems that will not, but are worth reading.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    public PolicyModelReport(IList<string> errors, IList<string> warnings)
    {
        Errors = new ReadOnlyCollection<string>(
            errors ?? throw new ArgumentNullException(nameof(errors)));
        Warnings = new ReadOnlyCollection<string>(
            warnings ?? throw new ArgumentNullException(nameof(warnings)));
    }

    /// <summary>Problems that will fail a query if the model is used as it stands.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Problems that will not fail anything, but are worth reading.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>True when nothing will fail a query.</summary>
    public bool IsValid => Errors.Count == 0;
}
