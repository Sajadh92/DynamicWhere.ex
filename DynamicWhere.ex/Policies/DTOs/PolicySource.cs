namespace DynamicWhere.ex.Policies.DTOs;

/// <summary>
/// Where a fragment came from. Carried through resolution into the trace and the explain endpoint,
/// so an operator can see not only what was decided but which attribute or rule decided it.
/// </summary>
public sealed class PolicySource
{
    private PolicySource(string origin, string? ruleId, string? subject, bool isSealed)
    {
        Origin = origin;
        RuleId = ruleId;
        Subject = subject;
        IsSealed = isSealed;
    }

    /// <summary>The attribute type name, or a description of the rule's subject.</summary>
    public string Origin { get; }

    /// <summary>The identifier of the runtime rule, or null when the source is an attribute.</summary>
    public string? RuleId { get; }

    /// <summary>The subject the rule targeted, or null when the source is an attribute.</summary>
    public string? Subject { get; }

    /// <summary>True when the source is a compile-time attribute that runtime rules cannot override.</summary>
    public bool IsSealed { get; }

    /// <summary>Creates a source describing a compile-time attribute.</summary>
    /// <exception cref="ArgumentException">Thrown when <paramref name="attributeName"/> is blank.</exception>
    public static PolicySource FromAttribute(string attributeName, bool isSealed)
    {
        if (string.IsNullOrWhiteSpace(attributeName))
        {
            throw new ArgumentException(
                "An attribute source requires an attribute name.", nameof(attributeName));
        }

        return new PolicySource(attributeName, ruleId: null, subject: null, isSealed);
    }

    /// <summary>Creates a source describing a runtime rule.</summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="ruleId"/> or <paramref name="subject"/> is blank.
    /// </exception>
    public static PolicySource FromRule(string ruleId, string subject)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
        {
            throw new ArgumentException("A rule source requires a rule identifier.", nameof(ruleId));
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("A rule source requires a subject.", nameof(subject));
        }

        return new PolicySource($"Rule {ruleId}", ruleId, subject, isSealed: false);
    }

    /// <inheritdoc />
    public override string ToString() =>
        RuleId is null ? $"{Origin}{(IsSealed ? " (sealed)" : string.Empty)}" : $"{Origin} [{Subject}]";
}
