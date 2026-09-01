namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// How a value is obscured on its way out of the library.
/// </summary>
/// <remarks>
/// Every strategy emits text, which is what decides where each one may be used: a transform is
/// valid only when its output is assignable to the member it decorates, so masking is a text
/// operation and a numeric field reaches for <see cref="GeneralizeMode"/> instead.
/// <para>
/// <c>Tokenize</c> is deliberately absent. It is deferred to v3.1, and a member no code path can
/// produce reads as a capability the library does not have.
/// </para>
/// </remarks>
public enum MaskStrategy
{
    /// <summary>Every character replaced.</summary>
    Full = 0,

    /// <summary>Characters replaced except a kept prefix and suffix.</summary>
    Partial = 1,

    /// <summary>An address reduced to its shape, such as <c>j***@d***.com</c>.</summary>
    Email = 2,

    /// <summary>A number reduced to its last digits.</summary>
    Phone = 3,

    /// <summary>Every match of a pattern replaced.</summary>
    Regex = 4,

    /// <summary>The whole value replaced by a constant, such as <c>[REDACTED]</c>.</summary>
    Fixed = 5,

    /// <summary>A salted hash, the salt read from options and never from the attribute.</summary>
    Hash = 6,

    /// <summary>The value removed entirely. The only strategy whose output is not text.</summary>
    Null = 7
}
