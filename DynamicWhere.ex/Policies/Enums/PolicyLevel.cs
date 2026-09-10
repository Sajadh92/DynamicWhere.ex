namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// Precedence levels, ordered most authoritative first. The lowest-numbered level that supplies
/// any fragment for a given field and feature decides that feature; levels below it are ignored
/// entirely rather than merged.
/// </summary>
public enum PolicyLevel
{
    /// <summary>
    /// A compile-time attribute with <c>Overridable = false</c>. Absolute — no runtime rule can
    /// loosen or replace it.
    /// </summary>
    SealedAttribute = 1,

    /// <summary>A runtime rule targeting one user.</summary>
    DynamicUser = 2,

    /// <summary>A runtime rule targeting a role.</summary>
    DynamicRole = 3,

    /// <summary>A runtime rule targeting a tenant.</summary>
    DynamicTenant = 4,

    /// <summary>A runtime rule with no subject, applying to everyone.</summary>
    DynamicGlobal = 5,

    /// <summary>
    /// A compile-time attribute with <c>Overridable = true</c>. Acts as a default that any
    /// runtime rule may replace.
    /// </summary>
    OverridableAttribute = 6
}
