using System.Reflection;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The store conformance suite from design section 8.4. Every store implementation inherits it by
/// deriving and supplying a factory.
/// </summary>
/// <remarks>
/// Written against the contract rather than any implementation, so Phase 6's Redis and Entity
/// Framework stores are held to exactly what the in-memory one is held to. The failure cases drive
/// a decorator rather than the store itself, because "the transport went away" is the same event
/// whatever the transport is.
/// </remarks>
public abstract class PolicyStoreConformanceTests
{
    /// <summary>The entity the rules in this suite address.</summary>
    protected const string StaffType = "DynamicWhere.Tests.Policies.Staff";

    /// <summary>
    /// Builds an empty store of the implementation under test.
    /// </summary>
    /// <param name="resolveType">
    /// Supplied when the test needs the store to perform the configuration-time sealed-field check.
    /// </param>
    protected abstract IDwPolicyWritableStore CreateStore(Func<string, Type?>? resolveType = null);

    /// <summary>Builds a rule for the fixture entity.</summary>
    protected static PolicyRule Rule(
        DwSubjectKind kind = DwSubjectKind.Global,
        string? key = null,
        string field = "Department",
        PolicyFeature features = PolicyFeature.Select,
        PolicyEffect effect = PolicyEffect.Deny) =>
        new(kind, key, StaffType, field, features, effect);

    /// <summary>Builds a frozen posture.</summary>
    protected static DwPolicyOptions Options(
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

    // ---------------------------------------------------------------- load, version, watch, poll

    [Fact]
    public async Task A_load_returns_what_was_written()
    {
        IDwPolicyWritableStore store = CreateStore();

        await store.UpsertAsync(Rule(), default);

        StoreSnapshot snapshot = await store.LoadAsync(default);

        Assert.Equal(1, snapshot.Count);
        Assert.Equal("Department", snapshot.For(typeof(Staff))[0].FieldPath);
    }

    [Fact]
    public async Task Every_write_moves_the_version_forward()
    {
        IDwPolicyWritableStore store = CreateStore();

        long start = await store.GetVersionAsync(default);

        PolicyRule rule = Rule();

        await store.UpsertAsync(rule, default);
        long afterUpsert = await store.GetVersionAsync(default);

        await store.DeleteAsync(rule.Id, default);
        long afterDelete = await store.GetVersionAsync(default);

        Assert.True(afterUpsert > start);
        Assert.True(afterDelete > afterUpsert);
    }

    [Fact]
    public async Task A_change_is_observable_through_a_watch_or_through_a_poll()
    {
        // Every store must be able to say that it changed. A store with no watch falls back to the
        // version poll, which is why the poll is part of the contract and not an optimisation.
        IDwPolicyWritableStore store = CreateStore();

        long before = await store.GetVersionAsync(default);

        using CancellationTokenSource cancel = new(TimeSpan.FromSeconds(10));

        IAsyncEnumerable<long>? watch = store.WatchAsync(cancel.Token);

        if (watch is null)
        {
            await store.UpsertAsync(Rule(), default);

            Assert.True(await store.GetVersionAsync(default) > before);

            return;
        }

        Task<long> observed = Task.Run(
            async () =>
            {
                await foreach (long version in watch.WithCancellation(cancel.Token))
                {
                    return version;
                }

                return -1L;
            },
            cancel.Token);

        while (!observed.IsCompleted)
        {
            await store.UpsertAsync(Rule(), cancel.Token);

            await Task.Delay(25, cancel.Token);
        }

        Assert.True(await observed > before);

        (store as IDisposable)?.Dispose();
    }

    // ---------------------------------------------------------------- atomic swap

    [Fact]
    public async Task A_context_prepared_on_one_version_finishes_on_it()
    {
        IDwPolicyWritableStore store = CreateStore();

        await store.UpsertAsync(Rule(), default);

        using StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, Options(), autoRefresh: false);

        DwPolicyContext pinned = await provider.PrepareAsync(new DwPolicyContext());

        Assert.Single(provider.GetFragments(typeof(Staff), pinned));

        await store.UpsertAsync(Rule(field: "Notes"), default);
        await provider.RefreshAsync();

        // The provider moved; this context did not. A query already in flight cannot see half of
        // one version and half of another.
        Assert.Single(provider.GetFragments(typeof(Staff), pinned));

        DwPolicyContext fresh = await provider.PrepareAsync(new DwPolicyContext());

        Assert.Equal(2, provider.GetFragments(typeof(Staff), fresh).Count);
    }

    // ---------------------------------------------------------------- zone split

    [Fact]
    public async Task The_broad_and_narrow_zones_are_split()
    {
        IDwPolicyWritableStore store = CreateStore();

        await store.UpsertAsync(Rule(DwSubjectKind.Role, "Manager"), default);
        await store.UpsertAsync(Rule(DwSubjectKind.User, "alice", "Notes"), default);

        StoreSnapshot snapshot = await store.LoadAsync(default);

        // The broad zone must not carry the user rule; loading every user's rules is what the split
        // exists to avoid.
        Assert.Equal(1, snapshot.Count);

        Assert.Equal(1, (await store.LoadNarrowAsync(new[] { "alice" }, default)).Count);
        Assert.Equal(0, (await store.LoadNarrowAsync(new[] { "bob" }, default)).Count);
    }

    [Fact]
    public async Task A_narrow_load_matches_identities_case_insensitively()
    {
        // The identity arrives from a token whose casing this library does not control. A user rule
        // that fails to match its own user is a denial that does nothing.
        IDwPolicyWritableStore store = CreateStore();

        await store.UpsertAsync(Rule(DwSubjectKind.User, "Alice"), default);

        Assert.Equal(1, (await store.LoadNarrowAsync(new[] { "ALICE" }, default)).Count);
    }

    // ---------------------------------------------------------------- failure modes

    [Fact]
    public async Task A_startup_load_failure_throws()
    {
        UnreachableStore store = new(CreateStore()) { Broken = true };

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await StorePolicyProvider.CreateAsync(store, Options()));
    }

    [Fact]
    public async Task A_refresh_failure_falls_back_to_the_last_known_good()
    {
        IDwPolicyWritableStore inner = CreateStore();

        await inner.UpsertAsync(Rule(), default);

        UnreachableStore store = new(inner);

        using StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, Options(), autoRefresh: false);

        store.Broken = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await provider.RefreshAsync());

        DwPolicyContext context = await provider.PrepareAsync(new DwPolicyContext());

        Assert.Single(provider.GetFragments(typeof(Staff), context));
        Assert.True(provider.IsDegraded);
    }

    [Fact]
    public async Task An_exceeded_ceiling_escalates_to_fail_closed()
    {
        IDwPolicyWritableStore store = CreateStore();

        await store.UpsertAsync(Rule(), default);

        using StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, Options(maxAge: TimeSpan.FromMinutes(15)), autoRefresh: false);

        DwPolicyContext context = await provider.PrepareAsync(new DwPolicyContext());

        Assert.Single(provider.GetFragments(typeof(Staff), context));

        provider.Clock = () => DateTimeOffset.UtcNow.AddMinutes(16);

        PolicyException error = Assert.Throws<PolicyException>(
            () => provider.GetFragments(typeof(Staff), context));

        Assert.Equal(PolicyErrorCode.StoreUnavailable, error.ErrorCode);
    }

    // ---------------------------------------------------------------- write surface

    [Fact]
    public async Task A_sealed_field_is_rejected_on_upsert()
    {
        // Staff.NationalId carries [DwDenied], which is sealed for every feature.
        IDwPolicyWritableStore store = CreateStore(_ => typeof(Staff));

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await store.UpsertAsync(
                Rule(field: "NationalId", effect: PolicyEffect.Allow), default));
    }

    [Fact]
    public void A_read_only_store_cannot_write()
    {
        // Structural, not behavioural: a replica registers only IDwPolicyStore, so the write
        // methods must not be reachable through it. If they ever move up to the read interface this
        // goes red, which is the only way that regression would be noticed.
        string[] members = typeof(IDwPolicyStore)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToArray();

        Assert.DoesNotContain(nameof(IDwPolicyWritableStore.UpsertAsync), members);
        Assert.DoesNotContain(nameof(IDwPolicyWritableStore.DeleteAsync), members);

        Assert.True(typeof(IDwPolicyStore).IsAssignableFrom(typeof(IDwPolicyWritableStore)));
    }
}

/// <summary>
/// The conformance suite run against <see cref="InMemoryPolicyStore"/>.
/// </summary>
public sealed class InMemoryStoreConformanceTests : PolicyStoreConformanceTests
{
    /// <inheritdoc />
    protected override IDwPolicyWritableStore CreateStore(Func<string, Type?>? resolveType = null) =>
        new InMemoryPolicyStore(resolveType);
}

/// <summary>
/// Any store, plus a switch that makes its transport go away.
/// </summary>
/// <remarks>
/// A decorator rather than a per-store failure hook, because "the store cannot be reached" is
/// the same event whether the transport is a socket, a database connection, or nothing at all.
/// </remarks>
internal sealed class UnreachableStore : IDwPolicyStore
{
    private readonly IDwPolicyStore _inner;

    /// <summary>Wraps a store.</summary>
    public UnreachableStore(IDwPolicyStore inner) => _inner = inner;

    /// <summary>True to make every call fail.</summary>
    public bool Broken { get; set; }

    /// <inheritdoc />
    public ValueTask<StoreSnapshot> LoadAsync(CancellationToken ct) =>
        Broken ? throw Gone() : _inner.LoadAsync(ct);

    /// <inheritdoc />
    public ValueTask<NarrowZone> LoadNarrowAsync(
        IReadOnlyList<string> userIdentities, CancellationToken ct) =>
        Broken ? throw Gone() : _inner.LoadNarrowAsync(userIdentities, ct);

    /// <inheritdoc />
    public ValueTask<long> GetVersionAsync(CancellationToken ct) =>
        Broken ? throw Gone() : _inner.GetVersionAsync(ct);

    /// <inheritdoc />
    public IAsyncEnumerable<long>? WatchAsync(CancellationToken ct) => null;

    private static InvalidOperationException Gone() => new("The store is unreachable.");
}
