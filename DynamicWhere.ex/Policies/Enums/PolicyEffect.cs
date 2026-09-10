namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// What a fragment does to a feature. Values ascend by authority, so when two fragments tie on
/// level, wildcard specificity, and priority, the greater value wins.
/// </summary>
public enum PolicyEffect
{
    /// <summary>The feature is permitted.</summary>
    Allow = 0,

    /// <summary>The feature is permitted, but the value is transformed on output.</summary>
    Mask = 1,

    /// <summary>The feature is refused.</summary>
    Deny = 2
}
