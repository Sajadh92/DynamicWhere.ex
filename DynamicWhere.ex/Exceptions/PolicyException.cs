using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Exceptions;

/// <summary>
/// Thrown when a policy refuses part of a query.
/// </summary>
/// <remarks>
/// Derives from <see cref="LogicException"/> so existing catch blocks continue to work unchanged.
/// The structured properties let an API layer turn a refusal into a useful response without
/// parsing the message.
/// </remarks>
public class PolicyException : LogicException
{
    /// <summary>
    /// Initializes the exception.
    /// </summary>
    /// <param name="code">The error code, from <c>ErrorCode</c>.</param>
    /// <param name="fieldPath">The field the policy refused.</param>
    /// <param name="feature">The feature that was refused.</param>
    /// <param name="tier">The enforcement tier in force when the refusal happened.</param>
    public PolicyException(string code, string fieldPath, PolicyFeature feature, DwTier tier)
        : base($"{code}: field '{fieldPath}', feature '{feature}', tier '{tier}'.")
    {
        Code = code;
        FieldPath = fieldPath;
        Feature = feature;
        Tier = tier;
    }

    /// <summary>The error code.</summary>
    public string Code { get; }

    /// <summary>The field the policy refused.</summary>
    public string FieldPath { get; }

    /// <summary>The feature that was refused.</summary>
    public PolicyFeature Feature { get; }

    /// <summary>The enforcement tier in force.</summary>
    public DwTier Tier { get; }

    /// <summary>The identifier of the runtime rule that decided, when one did.</summary>
    public string? RuleId { get; init; }

    /// <summary>The attribute or rule description that decided.</summary>
    public string? SourceOrigin { get; init; }
}
