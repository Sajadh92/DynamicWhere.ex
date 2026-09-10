using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Exceptions;

/// <summary>
/// Thrown when a policy refuses part of a query.
/// </summary>
/// <remarks>
/// Derives from <see cref="LogicException"/> so existing catch blocks continue to work unchanged.
/// The structured properties let an API layer turn a refusal into a useful response without
/// parsing the message.
/// <para>
/// The reason is carried twice on purpose. <see cref="ErrorCode"/> is the one to branch on — it is
/// a closed set, so a <c>switch</c> over it is exhaustive and the compiler reports the gap when a
/// later release adds a member. <see cref="Code"/> is its name, for log lines and serialized
/// payloads that cannot hold an enum.
/// </para>
/// </remarks>
public class PolicyException : LogicException
{
    /// <summary>
    /// Initializes the exception.
    /// </summary>
    /// <param name="errorCode">The reason the policy refused.</param>
    /// <param name="fieldPath">The field the policy refused.</param>
    /// <param name="feature">The feature that was refused.</param>
    /// <param name="tier">The enforcement tier in force when the refusal happened.</param>
    public PolicyException(PolicyErrorCode errorCode, string fieldPath, PolicyFeature feature, DwTier tier)
        : base($"{errorCode}: field '{fieldPath}', feature '{feature}', tier '{tier}'.")
    {
        ErrorCode = errorCode;
        FieldPath = fieldPath;
        Feature = feature;
        Tier = tier;
    }

    /// <summary>
    /// The reason the policy refused. Branch on this rather than on <see cref="Code"/>.
    /// </summary>
    public PolicyErrorCode ErrorCode { get; }

    /// <summary>
    /// The name of <see cref="ErrorCode"/>, for logging and serialization.
    /// </summary>
    public string Code => ErrorCode.ToString();

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
