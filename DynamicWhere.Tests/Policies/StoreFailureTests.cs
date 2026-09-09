using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers what happens when the store cannot be reached, and how the staleness ceiling bounds every
/// answer to that.
/// </summary>
/// <remarks>
/// Two failures are treated deliberately differently. A startup load failure is fatal under every
/// mode, because a process that cannot read its policy cannot know whether it is enforcing
/// anything. A later failure is <c>StoreFailure</c>'s decision — but only until the last successful
/// load is older than <c>MaxSnapshotAge</c>, after which every mode refuses.
/// </remarks>
public class StoreFailureTests
{
    private const string StaffType = "DynamicWhere.Tests.Policies.Staff";

    private static PolicyRule Rule(string field = "Department") =>
        new(DwSubjectKind.Global, null, StaffType, field, PolicyFeature.Select, PolicyEffect.Deny);

    private static DwPolicyOptions Options(
        StoreFailureMode mode = StoreFailureMode.LastKnownGood, TimeSpan? maxAge = null)
    {
        DwPolicyOptions options = new() { StoreFailure = mode };

        if (maxAge is not null)
        {
            options.MaxSnapshotAge = maxAge.Value;
        }

        options.Freeze();

        return options;
    }

    // ---------------------------------------------------------------- startup

    [Fact]
    public async Task A_startup_load_failure_throws_and_leaves_no_provider()
    {
        // Under every mode, including the two that tolerate a later failure. Booting into an
        // unknown policy state means the process cannot know whether it is enforcing anything.
        foreach (StoreFailureMode mode in Enum.GetValues<StoreFailureMode>())
        {
            UnreachableStore store = new(new InMemoryPolicyStore()) { Broken = true };

            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await StorePolicyProvider.CreateAsync(store, Options(mode)));
        }
    }

    [Fact]
    public async Task A_store_returning_null_from_load_is_refused_rather_than_read_as_empty()
    {
        // A null snapshot cannot be told apart from a store with no rules, and a store with no
        // rules allows every field.
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await StorePolicyProvider.CreateAsync(new NullStore(), Options()));
    }

    // ---------------------------------------------------------------- last known good

    [Fact]
    public async Task A_refresh_failure_keeps_the_last_known_good_snapshot()
    {
        InMemoryPolicyStore inner = new();

        inner.Seed(Rule());

        UnreachableStore store = new(inner);

        using StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, Options(), autoRefresh: false);

        store.Broken = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.RefreshAsync());

        Assert.True(provider.IsDegraded);

        // Still answering, from what loaded before the store went away.
        DwPolicyContext context = await provider.PrepareAsync(new DwPolicyContext());

        Assert.Single(provider.GetFragments(typeof(Staff), context));
    }

    [Fact]
    public async Task A_successful_refresh_clears_the_degraded_flag()
    {
        InMemoryPolicyStore inner = new();

        inner.Seed(Rule());

        UnreachableStore store = new(inner);

        using StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, Options(), autoRefresh: false);

        store.Broken = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.RefreshAsync());

        store.Broken = false;

        await provider.RefreshAsync();

        Assert.False(provider.IsDegraded);
    }

    // ---------------------------------------------------------------- fail closed

    [Fact]
    public async Task Fail_closed_refuses_from_the_next_query_rather_than_waiting_out_the_ceiling()
    {
        InMemoryPolicyStore inner = new();

        inner.Seed(Rule());

        UnreachableStore store = new(inner);

        using StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, Options(StoreFailureMode.FailClosed), autoRefresh: false);

        DwPolicyContext context = await provider.PrepareAsync(new DwPolicyContext());

        Assert.Single(provider.GetFragments(typeof(Staff), context));

        store.Broken = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.RefreshAsync());

        PolicyException error = Assert.Throws<PolicyException>(
            () => provider.GetFragments(typeof(Staff), context));

        Assert.Equal(PolicyErrorCode.StoreUnavailable, error.ErrorCode);
    }

    [Fact]
    public async Task Fail_closed_throws_rather_than_denying_because_a_denial_can_be_outranked()
    {
        // The reason this is an exception. Staff.Badge carries a sealed [DwOperators] allowance on
        // Where, which outranks any dynamic denial — so a blanket Deny-All fragment would have left
        // that field filterable in precisely the state the mode exists to refuse.
        InMemoryPolicyStore inner = new();

        UnreachableStore store = new(inner);

        using StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, Options(StoreFailureMode.FailClosed), autoRefresh: false);

        DwPolicyContext context = await provider.PrepareAsync(new DwPolicyContext());

        store.Broken = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.RefreshAsync());

        PolicyResolver resolver = new(new IDwPolicyProvider[]
        {
            new AttributePolicyProvider(), provider
        });

        Assert.Throws<PolicyException>(
            () => resolver.Resolve(typeof(Staff), "Badge", context));
    }

    // ---------------------------------------------------------------- static only

    [Fact]
    public async Task Static_only_drops_the_store_and_leaves_the_attributes_enforcing()
    {
        InMemoryPolicyStore inner = new();

        inner.Seed(Rule());

        UnreachableStore store = new(inner);

        using StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, Options(StoreFailureMode.StaticOnly), autoRefresh: false);

        DwPolicyContext context = await provider.PrepareAsync(new DwPolicyContext());

        store.Broken = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.RefreshAsync());

        Assert.Empty(provider.GetFragments(typeof(Staff), context));

        // The compile-time policy is untouched by any of this.
        PolicyResolver resolver = new(new IDwPolicyProvider[]
        {
            new AttributePolicyProvider(), provider
        });

        Assert.False(resolver.Resolve(typeof(Staff), "NationalId", context)
            .Allows(PolicyFeature.Select));
    }

    // ---------------------------------------------------------------- the ceiling

    [Theory]
    [InlineData(StoreFailureMode.LastKnownGood)]
    [InlineData(StoreFailureMode.FailClosed)]
    [InlineData(StoreFailureMode.StaticOnly)]
    public async Task The_ceiling_binds_under_every_mode(StoreFailureMode mode)
    {
        // Including StaticOnly, which otherwise answers quietly, and including a provider whose
        // store never failed at all. What the ceiling measures is how long a snapshot has been
        // honoured on trust nobody renewed.
        InMemoryPolicyStore store = new();

        store.Seed(Rule());

        using StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, Options(mode, TimeSpan.FromMinutes(15)), autoRefresh: false);

        DwPolicyContext context = await provider.PrepareAsync(new DwPolicyContext());

        Assert.NotNull(provider.GetFragments(typeof(Staff), context));

        provider.Clock = () => DateTimeOffset.UtcNow.AddMinutes(16);

        PolicyException error = Assert.Throws<PolicyException>(
            () => provider.GetFragments(typeof(Staff), context));

        Assert.Equal(PolicyErrorCode.StoreUnavailable, error.ErrorCode);
    }

    [Fact]
    public async Task A_context_pinned_too_long_ago_is_refused_even_after_the_provider_refreshed()
    {
        // The ceiling is measured on what this caller is actually being served, not on what the
        // provider happens to hold. A long-lived context serving a twenty-minute-old policy is the
        // case the ceiling exists for, and it is why a context is built once per request.
        InMemoryPolicyStore store = new();

        store.Seed(Rule());

        using StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, Options(maxAge: TimeSpan.FromMinutes(15)), autoRefresh: false);

        DwPolicyContext stale = await provider.PrepareAsync(new DwPolicyContext());

        provider.Clock = () => DateTimeOffset.UtcNow.AddMinutes(16);

        await provider.RefreshAsync();

        Assert.Throws<PolicyException>(() => provider.GetFragments(typeof(Staff), stale));

        // A context prepared after the refresh is served normally.
        DwPolicyContext fresh = await provider.PrepareAsync(new DwPolicyContext());

        Assert.Single(provider.GetFragments(typeof(Staff), fresh));
    }

    // ---------------------------------------------------------------- refresh and atomic swap

    [Fact]
    public async Task A_query_that_begins_on_one_version_finishes_on_it()
    {
        // Design section 5.3. The snapshot is pinned to the context, so a refresh landing between
        // two fields of one query cannot gate a filter on version 41 and a projection on 42.
        InMemoryPolicyStore store = new();

        store.Seed(Rule());

        using StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, Options(), autoRefresh: false);

        DwPolicyContext context = await provider.PrepareAsync(new DwPolicyContext());

        Assert.Single(provider.GetFragments(typeof(Staff), context));

        await store.UpsertAsync(Rule("Notes"), default);
        await provider.RefreshAsync();

        // The provider moved on; this context did not.
        Assert.Equal(2, provider.Version);
        Assert.Single(provider.GetFragments(typeof(Staff), context));
        Assert.Equal(2, provider.GetFragments(
            typeof(Staff), await provider.PrepareAsync(new DwPolicyContext())).Count);
    }

    [Fact]
    public async Task A_watch_drives_the_refresh_without_waiting_for_a_poll()
    {
        using InMemoryPolicyStore store = new();

        store.Seed(Rule());

        // A poll interval far longer than this test will wait, so anything observed came from the
        // watch rather than from the timer.
        using StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, Options(maxAge: TimeSpan.FromHours(1)));

        // The default poll interval is thirty seconds and this waits five, so anything observed
        // here arrived through the watch. The write is retried because a watch registers when its
        // enumeration begins, which is a moment after CreateAsync returns.
        await WaitFor(
            () => provider.Version >= 2,
            async () => await store.UpsertAsync(Rule("Notes"), default));

        Assert.True(provider.Version >= 2);
    }

    [Fact]
    public async Task A_store_with_no_watch_is_polled_instead()
    {
        using InMemoryPolicyStore inner = new();

        inner.Seed(Rule());

        PollOnlyStore store = new(inner);

        DwPolicyOptions options = new() { MaxSnapshotAge = TimeSpan.FromHours(1) };

        options.RefreshInterval = TimeSpan.FromMilliseconds(20);
        options.Freeze();

        using StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(store, options);

        await inner.UpsertAsync(Rule("Notes"), default);

        await WaitFor(() => provider.Version >= 2);

        Assert.True(provider.Version >= 2);
    }

    /// <summary>
    /// A disposed provider stops following the store.
    /// </summary>
    /// <remarks>
    /// Asserted by changing the store and watching the provider not notice. The test used to call
    /// Dispose twice and assert nothing, so a refresh loop that polled forever after disposal
    /// passed it — which is the property the name promises.
    /// </remarks>
    [Fact]
    public async Task Disposing_stops_the_refresh()
    {
        using InMemoryPolicyStore store = new();

        DwPolicyOptions options = new() { RefreshInterval = TimeSpan.FromMilliseconds(50) };

        options.Freeze();

        StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(store, options);

        long version = provider.Version;

        provider.Dispose();

        await store.UpsertAsync(Rule("Notes"), default);

        // Comfortably longer than the interval the loop would have woken on.
        await Task.Delay(500);

        Assert.True(store.Version > version, "the store did not change, so nothing was proved");
        Assert.Equal(version, provider.Version);

        // Disposing twice is harmless; a provider held in a container may be disposed by more than
        // one path.
        provider.Dispose();
    }

    /// <summary>Waits for a condition, or gives up after a bounded time.</summary>
    /// <param name="condition">What is being waited for.</param>
    /// <param name="nudge">
    /// Run on each attempt, for a signal that has to be re-sent because the listener registers
    /// asynchronously.
    /// </param>
    private static async Task WaitFor(Func<bool> condition, Func<Task>? nudge = null)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            if (nudge is not null)
            {
                await nudge();
            }

            await Task.Delay(25);
        }
    }

    /// <summary>A store that cannot report changes, so the provider must poll.</summary>
    private sealed class PollOnlyStore : IDwPolicyStore
    {
        private readonly IDwPolicyStore _inner;

        internal PollOnlyStore(IDwPolicyStore inner) => _inner = inner;

        public ValueTask<StoreSnapshot> LoadAsync(CancellationToken ct) => _inner.LoadAsync(ct);

        public ValueTask<NarrowZone> LoadNarrowAsync(
            IReadOnlyList<string> userIdentities, CancellationToken ct) =>
            _inner.LoadNarrowAsync(userIdentities, ct);

        public ValueTask<long> GetVersionAsync(CancellationToken ct) => _inner.GetVersionAsync(ct);

        public IAsyncEnumerable<long>? WatchAsync(CancellationToken ct) => null;
    }

    /// <summary>A store that answers a load with nothing at all.</summary>
    private sealed class NullStore : IDwPolicyStore
    {
        public ValueTask<StoreSnapshot> LoadAsync(CancellationToken ct) => default;

        public ValueTask<NarrowZone> LoadNarrowAsync(
            IReadOnlyList<string> userIdentities, CancellationToken ct) => default;

        public ValueTask<long> GetVersionAsync(CancellationToken ct) => default;

        public IAsyncEnumerable<long>? WatchAsync(CancellationToken ct) => null;
    }
}
