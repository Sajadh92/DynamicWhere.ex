namespace DynamicWhere.ex.Policies.Tokens;

/// <summary>
/// Where the mapping from a real value to the token standing in for it is kept.
/// </summary>
/// <remarks>
/// The storage behind <c>MaskStrategy.Tokenize</c>. A token is random and carries no relationship
/// to the value it replaces, which is the one property a hash cannot have: a hash is derived from
/// its input, so anyone holding the salt can recompute every digest the deployment ever emitted,
/// and anyone who guesses a short salt can do it offline. A token can only be reversed by reading
/// this store, which is a separate thing to protect and a separate thing to revoke.
/// <para>
/// What tokenization does not close is equality. The same value must map to the same token, or a
/// caller could not group or join by the tokenized column and the strategy would be a slower
/// <c>Fixed</c> mask. So a caller who can write a chosen value and read it back tokenized still
/// learns that value's token and can recognise it elsewhere. That is inherent in any format
/// preserving equality, hashing included, and is documented rather than defended.
/// </para>
/// <para>
/// There is deliberately no method here that turns a token back into a value. The library never
/// needs one, and an interface carrying one would push every implementation to build a reversal
/// path that has to be defended. A deployment that needs to reverse a token can read its own store.
/// </para>
/// <para>
/// <b>This is the one policy store read on the query path.</b> Every other one is read at startup,
/// on refresh, or when a context is prepared, because those answers are known before a row is. A
/// token cannot be: it depends on the value, and the value is not known until the row is
/// materialized. An implementation that talks to a remote store must therefore keep an in-process
/// cache, or a query over ten thousand rows becomes ten thousand round trips. Both vaults this
/// library ships do.
/// </para>
/// </remarks>
public interface IDwTokenVault
{
    /// <summary>
    /// Returns the token standing in for one value, creating and storing one if this is the first
    /// time the value has been seen in this scope.
    /// </summary>
    /// <param name="scope">
    /// What the mapping is namespaced by, so two fields holding the same value do not share a token
    /// unless somebody said they should. Never blank.
    /// </param>
    /// <param name="value">The real value, already rendered to text by the stages before this one.</param>
    /// <returns>The token. Never null, never blank, and stable for this scope and value.</returns>
    /// <remarks>
    /// Called once per value per row, from many threads at once, so an implementation must be
    /// thread-safe and must not be slow on a repeat.
    /// <para>
    /// An implementation that cannot reach its store throws. It must never invent a token it did
    /// not store and must never return the value it was given: the first would hand two callers
    /// different tokens for one value, and the second would emit exactly the value the strategy
    /// exists to hide.
    /// </para>
    /// </remarks>
    string GetOrCreate(string scope, string value);
}
