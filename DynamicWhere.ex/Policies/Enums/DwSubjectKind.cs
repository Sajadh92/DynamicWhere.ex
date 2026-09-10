namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// The kind of principal a subject identifies. Maps onto the dynamic precedence levels.
/// </summary>
public enum DwSubjectKind
{
    /// <summary>Applies to every caller. Carries no identity.</summary>
    Global = 0,

    /// <summary>A tenant or organisation.</summary>
    Tenant = 1,

    /// <summary>A role or group.</summary>
    Role = 2,

    /// <summary>A single user.</summary>
    User = 3,

    /// <summary>A caller-defined dimension, resolved at the tenant level.</summary>
    Custom = 4
}
