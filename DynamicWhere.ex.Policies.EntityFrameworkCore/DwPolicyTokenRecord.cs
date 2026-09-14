namespace DynamicWhere.ex.Policies.EntityFrameworkCore;

/// <summary>
/// One row of <c>DwPolicyTokens</c>: the token standing in for one value in one scope.
/// </summary>
/// <remarks>
/// The value itself is not here, and that is the point. The key is the scope followed by a digest
/// of the value, so this table can answer "what token does this value have" without being a
/// readable copy of the column it protects. Someone who steals the table learns which tokens exist
/// and can confirm a value they already hold; they do not learn the values.
/// <para>
/// A row is written once and never updated. Nothing in this library rewrites a token, because a
/// token that changed would break every comparison already handed to a caller — which is why the
/// vault caches rows in process and the rule store does not cache anything.
/// </para>
/// </remarks>
public class DwPolicyTokenRecord
{
    /// <summary>The scope and a digest of the value, which together are the row's key.</summary>
    /// <remarks>Built by <c>DwToken.KeyFor</c>, so every vault agrees on what a row is keyed by.</remarks>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The scope on its own, in the clear.
    /// </summary>
    /// <remarks>
    /// Redundant against <see cref="Key"/>, and worth the column twice over: it is what lets an
    /// operator see how many tokens a field has issued, and what lets a deployment retire one
    /// field's tokens without touching another's. Neither is possible against a key that has to be
    /// parsed to be filtered.
    /// </remarks>
    public string Scope { get; set; } = string.Empty;

    /// <summary>The token handed to callers in place of the value.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>When the token was minted.</summary>
    /// <remarks>
    /// For the operator, not for the library. Nothing expires a token, and nothing may: a reissued
    /// token silently stops matching the ones already in circulation.
    /// </remarks>
    public DateTimeOffset CreatedAt { get; set; }
}
