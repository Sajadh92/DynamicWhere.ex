namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// What a policy decision did to one feature of one field.
/// </summary>
/// <remarks>
/// Every member here is one some code path emits. A member nothing can produce reads as a
/// capability the library does not have, which is why the transformation actions arrived with the
/// engine that emits them rather than with the enum.
/// <para>
/// A transform is a chain, and one decision is recorded for it, naming the most significant stage
/// that ran: a replacement outranks a custom transformer, which outranks a mask, which outranks a
/// generalization. A chain of only <c>Format</c> or <c>Truncate</c> records as
/// <see cref="Masked"/>, because what every one of these members means is that the value handed to
/// the caller is not the value the database holds. The reason names the stages.
/// </para>
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
    Injected = 4,

    /// <summary>The value was passed through a transformer the application supplied.</summary>
    Mutated = 5,

    /// <summary>The value was replaced outright, by a constant or by the type's default.</summary>
    Defaulted = 6,

    /// <summary>The value kept its kind but lost precision.</summary>
    Generalized = 7
}
