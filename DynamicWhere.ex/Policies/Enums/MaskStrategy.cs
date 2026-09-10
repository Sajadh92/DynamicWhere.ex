namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// How a value is obscured on its way out of the library.
/// </summary>
/// <remarks>
/// Every strategy emits text, which is what decides where each one may be used: a transform is
/// valid only when its output is assignable to the member it decorates, so masking is a text
/// operation and a numeric field reaches for <see cref="GeneralizeMode"/> instead.
/// <para>
/// Read by name wherever a rule is persisted, never by number, so the order of these members is not
/// part of any contract.
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

    /// <summary>A keyed hash, the salt read from options and never from the attribute.</summary>
    /// <remarks>
    /// Derived from the value, so whoever holds the salt can recompute every digest the deployment
    /// has emitted. <see cref="Tokenize"/> is the same shape of output without that property.
    /// </remarks>
    Hash = 6,

    /// <summary>The value removed entirely. The only strategy whose output is not text.</summary>
    Null = 7,

    /// <summary>
    /// A random token standing in for the value, the mapping kept in a vault.
    /// </summary>
    /// <remarks>
    /// The same output shape as <see cref="Hash"/> and none of its derivation. A hash can be
    /// recomputed by anyone holding the salt and guessed at by anyone who can brute-force a weak
    /// one; a token can only be reversed by reading the vault, which is a separate store to protect
    /// and a separate one to revoke.
    /// <para>
    /// Needs <c>DwPolicyOptions.TokenVault</c>. A query masking to a token without one is refused
    /// with <c>MissingTokenVault</c>, and the startup scan reports it, for the same reason a hash
    /// without a salt is refused: the output looks correct either way, so nobody downstream could
    /// tell.
    /// </para>
    /// <para>
    /// Equality survives and is meant to: the same value maps to the same token, so a caller can
    /// group and join by the column without learning what is in it. That also means a caller who
    /// can write a chosen value and read it back learns that one value's token. No format that
    /// preserves equality can avoid this.
    /// </para>
    /// </remarks>
    Tokenize = 8
}
