using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.DTOs;

/// <summary>
/// One thing a policy did to one feature of one field, recorded as it happened.
/// </summary>
/// <remarks>
/// A dropped field leaves no trace in the result — the row simply lacks a column that was never
/// requested in the generated SQL. Without a record of the drop, a caller debugging a missing value
/// has nothing to go on and no way to distinguish "the policy removed it" from "the data is null".
/// These decisions are that record.
/// </remarks>
public sealed class PolicyDecision
{
    /// <summary>
    /// Initializes a decision.
    /// </summary>
    /// <param name="fieldPath">The field the decision concerns, in canonical form.</param>
    /// <param name="feature">The feature the decision concerns.</param>
    /// <param name="action">What was done.</param>
    /// <param name="reason">Why, in terms a caller reading a log can act on.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="fieldPath"/> is blank.</exception>
    public PolicyDecision(string fieldPath, PolicyFeature feature, PolicyAction action, string? reason)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            throw new ArgumentException("A decision requires a field path.", nameof(fieldPath));
        }

        FieldPath = fieldPath;
        Feature = feature;
        Action = action;
        Reason = reason;
    }

    /// <summary>The field the decision concerns.</summary>
    public string FieldPath { get; }

    /// <summary>The feature the decision concerns.</summary>
    public PolicyFeature Feature { get; }

    /// <summary>What was done.</summary>
    public PolicyAction Action { get; }

    /// <summary>Why it was done. Null when nothing named a reason.</summary>
    public string? Reason { get; }
}
