using System.Collections.ObjectModel;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.DTOs;

/// <summary>
/// Everything a policy did to one query, and the posture it did it under.
/// </summary>
/// <remarks>
/// The decisions are exposed through a wrapper rather than as the backing list. The subject of a
/// trace is the same caller whose access it records refusing, and handing back the live list would
/// let them delete the evidence of their own refusal before it reached an audit sink.
/// </remarks>
public sealed class PolicyTrace
{
    private readonly List<PolicyDecision> _decisions = new();

    // The name the caller wrote for a canonical path, where the two differ. Not part of the record:
    // a refusal raised after sanitization — a date the query engine cannot read — reads it so that
    // it names the field as the caller did, rather than the internal path an alias exists to hide.
    private readonly Dictionary<string, string> _spoken = new(StringComparer.Ordinal);
    private readonly ReadOnlyCollection<PolicyDecision> _view;

    /// <summary>
    /// Initializes a trace for one query.
    /// </summary>
    /// <param name="tier">The enforcement tier the query ran under.</param>
    /// <param name="dryRun">True when no decision in this query threw or dropped anything.</param>
    public PolicyTrace(DwTier tier, bool dryRun)
    {
        Tier = tier;
        DryRun = dryRun;

        // Wrapped once here rather than on each read: the property is read by tracing and audit
        // code that may enumerate it repeatedly, and a fresh wrapper per call would allocate on a
        // path that exists to observe, not to cost.
        _view = new ReadOnlyCollection<PolicyDecision>(_decisions);
    }

    /// <summary>The enforcement tier the query ran under.</summary>
    public DwTier Tier { get; }

    /// <summary>
    /// True when no decision in this query threw or dropped anything, though every decision was
    /// still recorded.
    /// </summary>
    public bool DryRun { get; }

    /// <summary>Every decision taken, in the order it was taken.</summary>
    public IReadOnlyList<PolicyDecision> Decisions => _view;

    /// <summary>
    /// Records a decision.
    /// </summary>
    /// <param name="decision">The decision to record.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="decision"/> is null.</exception>
    public void Add(PolicyDecision decision)
    {
        if (decision is null)
        {
            throw new ArgumentNullException(nameof(decision));
        }

        _decisions.Add(decision);
    }

    /// <summary>How many decisions have been recorded.</summary>
    internal int Count => _decisions.Count;

    /// <summary>
    /// Withdraws every decision recorded after the first <paramref name="count"/>: the steps of an action
    /// that was then not taken, such as members left out of a projection that is not built.
    /// </summary>
    internal void Withdraw(int count) => _decisions.RemoveRange(count, _decisions.Count - count);

    /// <summary>Remembers that the caller reached a canonical path by writing another name.</summary>
    internal void RecordSpelling(string canonicalPath, string spoken) => _spoken[canonicalPath] = spoken;

    /// <summary>The name the caller wrote for a path, or the path itself when they wrote that.</summary>
    internal string Spoken(string fieldPath) =>
        _spoken.TryGetValue(fieldPath, out string? spoken) ? spoken : fieldPath;
}
