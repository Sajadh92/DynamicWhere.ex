namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// What a policy decision did to one feature of one field.
/// </summary>
/// <remarks>
/// Only the actions the gate can currently take are listed. The transformation actions —
/// mutated, defaulted, generalized — arrive with the mask engine, in the same change that makes
/// something able to emit them. A member no code path can produce reads as a capability the
/// library does not have.
/// </remarks>
public enum PolicyAction
{
    /// <summary>The request proceeded untouched.</summary>
    Allowed = 0,

    /// <summary>The request was refused and the query threw.</summary>
    Denied = 1,

    /// <summary>The request was silently removed from the query.</summary>
    Dropped = 2,

    /// <summary>The request proceeded and the output value will be transformed.</summary>
    Masked = 3,

    /// <summary>A predicate was added to the query that the caller did not send.</summary>
    Injected = 4
}
