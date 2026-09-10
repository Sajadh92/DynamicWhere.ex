using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.ex.Policies.Resolution;

/// <summary>
/// Supplies the resolver with fragments built from a policy store's rules.
/// </summary>
/// <remarks>
/// The counterpart to <see cref="AttributePolicyProvider"/>, and the resolver cannot tell them
/// apart — it merges fragments by how authoritative they claim to be, and nothing a store holds can
/// claim more than <see cref="PolicyLevel.DynamicUser"/>.
/// <para>
/// Reads no I/O on the query path. The snapshot it consults was pinned to the caller's context when
/// that context was prepared, which is also what fetched the caller's user-level rules. A context
/// that was never prepared is refused rather than served from the broad zone alone: a user-level
/// denial that silently does not appear is indistinguishable from a caller who has no user rules,
/// and one of those is a hole.
/// </para>
/// <para>
/// Deliberately not cached per type. The same store rules mean different things to different
/// callers, which is why <c>ResolveType</c> takes a context at all; the snapshot's own index by
/// entity type is an index over immutable data, not a memoized decision.
/// </para>
/// </remarks>
public sealed class StorePolicyProvider : IDwPolicyProvider, IDwPolicyRefresher, IDisposable
{
    private static readonly IReadOnlyList<PolicyFragment> None = Array.Empty<PolicyFragment>();

    private readonly IDwPolicyStore _store;
    private readonly DwPolicyOptions _options;
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _swap = new();

    private StoreSnapshot _snapshot;
    private DateTimeOffset _loadedAt;
    private bool _degraded;
    private Exception? _lastError;
    private Task? _refreshing;
    private bool _disposed;

    /// <summary>Builds a provider around a snapshot that has already loaded.</summary>
    private StorePolicyProvider(IDwPolicyStore store, DwPolicyOptions options, StoreSnapshot first)
    {
        _store = store;
        _options = options;
        _snapshot = first;
        _loadedAt = Clock();
    }

    /// <summary>
    /// Reads the clock. Injectable so that the staleness ceiling can be tested without waiting for
    /// it.
    /// </summary>
    internal Func<DateTimeOffset> Clock { get; set; } = static () => DateTimeOffset.UtcNow;

    /// <summary>The version the provider is currently serving.</summary>
    public long Version => Current.Version;

    /// <summary>
    /// True when the last attempt to reach the store failed and the mode is compensating.
    /// </summary>
    /// <remarks>
    /// A host should surface this as an unhealthy instance. It says nothing about whether queries
    /// are still being answered — that is what <c>StoreFailure</c> decides.
    /// </remarks>
    public bool IsDegraded
    {
        get
        {
            lock (_swap)
            {
                return _degraded;
            }
        }
    }

    /// <summary>
    /// When this provider last loaded the store successfully, by its own clock.
    /// </summary>
    /// <remarks>
    /// The provider's clock, not the store's. A store clock running ahead would make every snapshot
    /// look fresher than it is and silently extend the staleness ceiling, so what is reported here
    /// is what the ceiling is actually measured against.
    /// </remarks>
    public DateTimeOffset LoadedAt
    {
        get
        {
            lock (_swap)
            {
                return _loadedAt;
            }
        }
    }

    /// <summary>
    /// How long ago that was, which is what a health check compares against the staleness ceiling.
    /// </summary>
    public TimeSpan Age => Clock() - LoadedAt;

    /// <summary>
    /// The failure from the last refresh that did not succeed, or null when the last one did.
    /// </summary>
    /// <remarks>
    /// Kept so a health endpoint can say why an instance is degraded rather than only that it is.
    /// Cleared by a refresh that succeeds, so it never outlives the condition it describes.
    /// </remarks>
    public Exception? LastError
    {
        get
        {
            lock (_swap)
            {
                return _lastError;
            }
        }
    }

    /// <summary>
    /// Loads the store and returns a provider serving it.
    /// </summary>
    /// <param name="store">The store to read.</param>
    /// <param name="options">The posture, for the failure mode and the staleness ceiling.</param>
    /// <param name="autoRefresh">
    /// False to leave the provider without a background refresh, for a host that drives
    /// <see cref="RefreshAsync"/> itself.
    /// </param>
    /// <param name="ct">Cancels the first load.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="store"/> or <paramref name="options"/> is null.
    /// </exception>
    /// <remarks>
    /// <b>The first load is not caught.</b> Booting into an unknown policy state means the process
    /// cannot know whether it is enforcing anything, and refusing to start is the only safe answer.
    /// Building the provider through a factory that performs that load is what makes the guarantee
    /// structural: a failure here means there is no provider to hand to <c>DwPolicy.Configure</c>,
    /// rather than a check somebody has to remember to write.
    /// </remarks>
    public static async ValueTask<StorePolicyProvider> CreateAsync(
        IDwPolicyStore store,
        DwPolicyOptions options,
        bool autoRefresh = true,
        CancellationToken ct = default)
    {
        if (store is null)
        {
            throw new ArgumentNullException(nameof(store));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        StoreSnapshot first = await store.LoadAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Policy store '{store.GetType().FullName}' returned null from LoadAsync. Return " +
                "an empty snapshot instead: a null cannot be told apart from a store with no " +
                "rules, and every field would be allowed.");

        StorePolicyProvider provider = new(store, options, first);

        if (autoRefresh)
        {
            provider._refreshing = Task.Run(() => provider.RefreshLoopAsync(provider._stopping.Token));
        }

        return provider;
    }

    /// <summary>
    /// Pins the current snapshot to a context and fetches that caller's user-level rules.
    /// </summary>
    /// <param name="context">The caller's context, built once per request.</param>
    /// <param name="ct">Cancels the narrow load.</param>
    /// <returns>The same context, prepared, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    /// <remarks>
    /// Call this before the context's first query. Everything downstream then resolves synchronously
    /// against what was read here, and every query made with this context sees one coherent version.
    /// <para>
    /// A narrow load failure is not caught. There is no safe way to continue: an empty narrow zone
    /// is a real answer meaning "this caller has no user rules", so substituting one for a failed
    /// read would drop a denial written for exactly this caller.
    /// </para>
    /// </remarks>
    public async ValueTask<DwPolicyContext> PrepareAsync(
        DwPolicyContext context, CancellationToken ct = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        List<string> users = context.Identities(DwSubjectKind.User).ToList();

        NarrowZone narrow = users.Count == 0
            ? NarrowZone.Empty
            : await _store.LoadNarrowAsync(users, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Policy store '{_store.GetType().FullName}' returned null from " +
                    "LoadNarrowAsync. Return an empty zone instead: a null cannot be told apart " +
                    "from a caller with no user-level rules.");

        lock (_swap)
        {
            context.Attach(this, new PolicyAttachment(_snapshot, _loadedAt, narrow, users));
        }

        return context;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="entityType"/> or <paramref name="context"/> is null.
    /// </exception>
    /// <exception cref="PolicyException">
    /// Thrown with <see cref="PolicyErrorCode.PolicyContextNotPrepared"/> when the context never
    /// went through <see cref="PrepareAsync"/>, and with
    /// <see cref="PolicyErrorCode.StoreUnavailable"/> when the store's state cannot be trusted.
    /// </exception>
    public IReadOnlyList<PolicyFragment> GetFragments(Type entityType, DwPolicyContext context)
    {
        if (entityType is null)
        {
            throw new ArgumentNullException(nameof(entityType));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        PolicyAttachment attachment = context.AttachmentFor(this) as PolicyAttachment
            ?? throw Refuse(
                PolicyErrorCode.PolicyContextNotPrepared,
                entityType,
                "the context was never prepared against the policy store");

        // A context is mutable, so a user subject can be added after it was prepared — and that
        // user's rules would then never have been read. Serving the query anyway would drop a
        // denial written for exactly the caller now asking, which is preparation's whole subject.
        if (!attachment.Covers(context.Identities(DwSubjectKind.User)))
        {
            throw Refuse(
                PolicyErrorCode.PolicyContextNotPrepared,
                entityType,
                "the caller gained a user subject after the context was prepared, so that user's " +
                "rules were never read");
        }

        DateTimeOffset now = Clock();

        // Checked before anything is read. Under FailClosed a failed refresh refuses immediately
        // rather than waiting out the ceiling, and under StaticOnly the store's rules drop away and
        // the compile-time attributes carry the whole policy.
        if (IsDegraded)
        {
            switch (_options.StoreFailure)
            {
                case StoreFailureMode.FailClosed:
                    throw Refuse(
                        PolicyErrorCode.StoreUnavailable,
                        entityType,
                        "the policy store could not be reached and the failure mode is FailClosed");

                case StoreFailureMode.StaticOnly:
                    return None;
            }
        }

        // The ceiling, and it binds under every mode including a healthy one. A snapshot older than
        // this is being honoured on trust nobody has renewed — which is how "the store died six
        // hours ago" becomes "we have been honouring revoked grants all afternoon".
        //
        // Measured against the load time this provider stamped, not the one the store reported. A
        // store's clock is not this library's to trust: running behind it would only fail closed,
        // but running ahead it would make every snapshot look fresh and extend the ceiling without
        // anybody seeing it happen.
        if (now - attachment.LoadedAt > _options.MaxSnapshotAge)
        {
            throw Refuse(
                PolicyErrorCode.StoreUnavailable,
                entityType,
                $"the policy snapshot loaded at {attachment.LoadedAt:O} is older than the " +
                $"{_options.MaxSnapshotAge} ceiling");
        }

        List<PolicyFragment> fragments = new();

        // The broad zone first, then this caller's own rules. Order does not decide anything — the
        // resolver ranks by level, and a user rule outranks a role rule wherever they meet — but
        // the sweep is deterministic, which keeps a tie attributable.
        Collect(attachment.Snapshot.For(entityType), context, now, fragments);
        Collect(attachment.Narrow.For(entityType), context, now, fragments);

        return fragments;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A failure marks the instance degraded and keeps the last known good snapshot. What that
    /// means for a query is <c>StoreFailure</c>'s decision, bounded by the staleness ceiling.
    /// </remarks>
    public async ValueTask<long> RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            StoreSnapshot loaded = await _store.LoadAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"Policy store '{_store.GetType().FullName}' returned null from LoadAsync.");

            DateTimeOffset stamp = Clock();

            lock (_swap)
            {
                // One reference assignment. A query holding the previous snapshot finishes on it,
                // and a query starting now gets the new one; neither observes a half-applied load.
                _snapshot = loaded;
                _loadedAt = stamp;
                _degraded = false;
                _lastError = null;
            }

            return loaded.Version;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure)
        {
            lock (_swap)
            {
                _degraded = true;
                _lastError = failure;
            }

            throw;
        }
    }

    /// <summary>Stops the background refresh.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _stopping.Cancel();

        try
        {
            _refreshing?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // The loop ends by cancellation, which surfaces here as an aggregate. Nothing to do
            // with it: the provider is being torn down.
        }

        _stopping.Dispose();
    }

    /// <summary>The snapshot currently being served, read under the swap lock.</summary>
    private StoreSnapshot Current
    {
        get
        {
            lock (_swap)
            {
                return _snapshot;
            }
        }
    }

    /// <summary>
    /// Adds a fragment for every rule that applies to this caller at this instant.
    /// </summary>
    /// <remarks>
    /// Validity is evaluated here rather than resolved into the snapshot at load. A window baked in
    /// at load expires only when the next refresh succeeds, so a grant that ended at noon would be
    /// honoured for the whole of the staleness ceiling afterwards.
    /// </remarks>
    private static void Collect(
        IReadOnlyList<PolicyRule> rules,
        DwPolicyContext context,
        DateTimeOffset now,
        List<PolicyFragment> fragments)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            PolicyRule rule = rules[i];

            if (rule.AppliesAt(now) && rule.MatchesSubject(context) && rule.MatchesPurpose(context))
            {
                fragments.Add(rule.ToFragment());
            }
        }
    }

    /// <summary>
    /// Watches when the store can say, and polls either way.
    /// </summary>
    /// <remarks>
    /// Both, not one or the other. A watch gives the latency — milliseconds rather than a poll
    /// interval — but no notification channel is guaranteed to deliver: Redis pub/sub is
    /// fire-and-forget, and an instance that misses one message would otherwise serve the previous
    /// policy until something else happened to change. The poll bounds that at one interval instead
    /// of forever, and it costs a read of a single version value.
    /// </remarks>
    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        IAsyncEnumerable<long>? watch = null;

        try
        {
            watch = _store.WatchAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            // A store that cannot open a watch is polled alone. Falling through is what keeps a
            // broken notification channel from silently becoming no updates at all.
        }

        Task polling = PollLoopAsync(ct);

        if (watch is not null)
        {
            await Task.WhenAll(WatchLoopAsync(watch, ct), polling).ConfigureAwait(false);

            return;
        }

        await polling.ConfigureAwait(false);
    }

    /// <summary>Reloads whenever the store reports a new version.</summary>
    private async Task WatchLoopAsync(IAsyncEnumerable<long> watch, CancellationToken ct)
    {
        try
        {
            await foreach (long version in watch.WithCancellation(ct).ConfigureAwait(false))
            {
                // Not "greater than". A notification is a wake-up, not the state — RefreshAsync
                // loads whatever the store currently holds. A store restored from a backup moves
                // its version backwards, and skipping that would keep serving rules the restore
                // withdrew until a poll happened to notice.
                if (version == Current.Version)
                {
                    continue;
                }

                await Attempt(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <summary>Reloads when a poll shows the version has moved.</summary>
    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.RefreshInterval, ct).ConfigureAwait(false);

                long version = await _store.GetVersionAsync(ct).ConfigureAwait(false);

                if (version != Current.Version)
                {
                    await Attempt(ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // A failed poll is a failed refresh. RefreshAsync has already recorded it, and a
                // read of the version that threw is recorded here for the same reason: an instance
                // that cannot see the version cannot know it is current.
                lock (_swap)
                {
                    _degraded = true;
                }
            }
        }
    }

    /// <summary>Refreshes, swallowing the failure the loop has already recorded.</summary>
    private async Task Attempt(CancellationToken ct)
    {
        try
        {
            await RefreshAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Already marked degraded by RefreshAsync. The loop keeps running so that a store which
            // comes back is picked up without a restart.
        }
    }

    /// <summary>
    /// Builds the refusal, naming the type rather than a field.
    /// </summary>
    /// <remarks>
    /// Neither of these refusals is about one field: they say the policy for the whole type cannot
    /// be established. The tier is reported as <see cref="DwTier.Strict"/> because the refusal does
    /// not soften in the convenience tier — a policy that cannot be read is not a policy that can
    /// be partly applied.
    /// </remarks>
    private PolicyException Refuse(PolicyErrorCode code, Type entityType, string because) =>
        new(code, entityType.Name, PolicyFeature.All, DwTier.Strict)
        {
            SourceOrigin = $"{_store.GetType().Name}: {because}"
        };
}
