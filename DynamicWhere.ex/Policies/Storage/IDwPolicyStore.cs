namespace DynamicWhere.ex.Policies.Storage;

/// <summary>
/// Where runtime policy rules are read from.
/// </summary>
/// <remarks>
/// Every method is asynchronous and none of them is called on the query path. A store is read at
/// startup, when a context is prepared, and by the background refresh — never while a filter is
/// being sanitized, because <c>IDwPolicyProvider.GetFragments</c> is synchronous and must not
/// perform I/O.
/// <para>
/// Design section 5.2 describes three members. <see cref="LoadNarrowAsync"/> is a fourth, and it is
/// not optional: section 5.3 splits user-level rules out of the snapshot precisely so they are
/// fetched per caller, and the contract as drawn gave them nowhere to be fetched from.
/// </para>
/// </remarks>
public interface IDwPolicyStore
{
    /// <summary>
    /// Reads the whole broad zone.
    /// </summary>
    /// <param name="ct">Cancels the load.</param>
    /// <returns>A snapshot, never null.</returns>
    /// <remarks>
    /// Called once at startup and again on every refresh. A failure at startup is fatal; a failure
    /// afterwards is governed by <c>DwPolicyOptions.StoreFailure</c>.
    /// </remarks>
    ValueTask<StoreSnapshot> LoadAsync(CancellationToken ct);

    /// <summary>
    /// Reads the user-level rules for one caller.
    /// </summary>
    /// <param name="userIdentities">
    /// The identities the caller holds as <c>DwSubjectKind.User</c>. Compared case-insensitively.
    /// </param>
    /// <param name="ct">Cancels the load.</param>
    /// <returns>A zone, never null. Empty when the caller has no user-level rules.</returns>
    /// <remarks>
    /// Called once per prepared context. Returning an empty zone means "this caller has no user
    /// rules", which is a real answer; there is no way to say "I did not look", because a caller
    /// whose narrow zone was never read is refused rather than served from the broad zone alone.
    /// </remarks>
    ValueTask<NarrowZone> LoadNarrowAsync(IReadOnlyList<string> userIdentities, CancellationToken ct);

    /// <summary>
    /// Reads the store's current version without loading the rules.
    /// </summary>
    /// <param name="ct">Cancels the read.</param>
    /// <remarks>
    /// Polled by the refresh when the store offers no change notification of its own. It must be
    /// cheap enough to run indefinitely — a read of a single value, not a scan.
    /// </remarks>
    ValueTask<long> GetVersionAsync(CancellationToken ct);

    /// <summary>
    /// Yields a version each time the store changes, or null when the store cannot say.
    /// </summary>
    /// <param name="ct">Ends the watch.</param>
    /// <returns>The stream, or null to fall back to polling.</returns>
    /// <remarks>
    /// Null rather than an empty stream: an empty stream is indistinguishable from a store that has
    /// simply not changed yet, and falling back to polling for one of those and not the other is
    /// the difference between a slow update and no update at all.
    /// </remarks>
    IAsyncEnumerable<long>? WatchAsync(CancellationToken ct);
}

/// <summary>
/// A store that can be written to.
/// </summary>
/// <remarks>
/// Separated from <see cref="IDwPolicyStore"/> so a read-only replica registers only the read
/// interface and writes become impossible by construction rather than by convention.
/// </remarks>
public interface IDwPolicyWritableStore : IDwPolicyStore
{
    /// <summary>
    /// Adds a rule or replaces the one with the same identifier, and bumps the version.
    /// </summary>
    /// <param name="rule">The rule to store.</param>
    /// <param name="ct">Cancels the write.</param>
    /// <returns>The stored rule.</returns>
    ValueTask<PolicyRule> UpsertAsync(PolicyRule rule, CancellationToken ct);

    /// <summary>
    /// Removes a rule and bumps the version. Removing one that is not there is not an error.
    /// </summary>
    /// <param name="id">The rule to remove.</param>
    /// <param name="ct">Cancels the write.</param>
    ValueTask DeleteAsync(Guid id, CancellationToken ct);
}

/// <summary>
/// Reloads the policy on demand, for a webhook or an administrative action that should not wait for
/// the next poll.
/// </summary>
public interface IDwPolicyRefresher
{
    /// <summary>
    /// Reloads the broad zone now.
    /// </summary>
    /// <param name="ct">Cancels the reload.</param>
    /// <returns>The version now in force.</returns>
    /// <remarks>
    /// A failure here is a refresh failure, not a startup failure, and is governed by
    /// <c>DwPolicyOptions.StoreFailure</c> like any other.
    /// </remarks>
    ValueTask<long> RefreshAsync(CancellationToken ct);
}
