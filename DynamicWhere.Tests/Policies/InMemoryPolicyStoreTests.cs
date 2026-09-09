using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers the in-memory store's own behaviour. The contract it shares with every other store is
/// covered by <see cref="PolicyStoreConformanceTests"/>.
/// </summary>
public class InMemoryPolicyStoreTests
{
    private const string StaffType = "DynamicWhere.Tests.Policies.Staff";

    private static PolicyRule Rule(
        DwSubjectKind kind = DwSubjectKind.Role,
        string? key = "Manager",
        string field = "Department",
        PolicyFeature features = PolicyFeature.Select,
        PolicyEffect effect = PolicyEffect.Deny) =>
        new(kind, key, StaffType, field, features, effect);

    [Fact]
    public async Task Seeding_bumps_the_version_once_rather_than_once_per_rule()
    {
        InMemoryPolicyStore store = new();

        store.Seed(Rule(), Rule(field: "Notes"), Rule(field: "Name"));

        Assert.Equal(1, await store.GetVersionAsync(default));
    }

    [Fact]
    public async Task Every_write_bumps_the_version()
    {
        InMemoryPolicyStore store = new();
        PolicyRule rule = Rule();

        await store.UpsertAsync(rule, default);
        long afterUpsert = await store.GetVersionAsync(default);

        await store.DeleteAsync(rule.Id, default);
        long afterDelete = await store.GetVersionAsync(default);

        Assert.Equal(1, afterUpsert);
        Assert.Equal(2, afterDelete);
    }

    [Fact]
    public async Task Deleting_a_rule_that_is_not_there_still_bumps()
    {
        // A caller that deleted a rule someone else had already deleted still expects the version
        // it reads next to reflect the state it asked for.
        InMemoryPolicyStore store = new();

        await store.DeleteAsync(Guid.NewGuid(), default);

        Assert.Equal(1, await store.GetVersionAsync(default));
    }

    [Fact]
    public async Task An_upsert_replaces_the_rule_with_the_same_identifier()
    {
        InMemoryPolicyStore store = new();
        PolicyRule first = Rule(effect: PolicyEffect.Deny);

        await store.UpsertAsync(first, default);

        PolicyRule replacement = new(
            DwSubjectKind.Role, "Manager", StaffType, "Department", PolicyFeature.Select,
            PolicyEffect.Allow, id: first.Id);

        await store.UpsertAsync(replacement, default);

        StoreSnapshot snapshot = await store.LoadAsync(default);

        Assert.Equal(1, snapshot.Count);
        Assert.Equal(PolicyEffect.Allow, snapshot.For(typeof(Staff))[0].Effect);
    }

    [Fact]
    public async Task Load_returns_the_broad_zone_and_leaves_user_rules_to_the_narrow_load()
    {
        InMemoryPolicyStore store = new();

        store.Seed(
            Rule(DwSubjectKind.Global, null),
            Rule(DwSubjectKind.User, "alice", field: "Notes"));

        StoreSnapshot snapshot = await store.LoadAsync(default);
        NarrowZone zone = await store.LoadNarrowAsync(new[] { "alice" }, default);

        Assert.Equal(1, snapshot.Count);
        Assert.Equal(1, zone.Count);
    }

    [Fact]
    public async Task A_narrow_load_matches_a_user_identity_case_insensitively()
    {
        // The identity arrives from a token this library does not control the casing of.
        InMemoryPolicyStore store = new();

        store.Seed(Rule(DwSubjectKind.User, "Alice"));

        Assert.Equal(1, (await store.LoadNarrowAsync(new[] { "ALICE" }, default)).Count);
        Assert.Equal(0, (await store.LoadNarrowAsync(new[] { "bob" }, default)).Count);
    }

    [Fact]
    public async Task A_caller_with_no_user_identities_gets_an_empty_zone_rather_than_every_rule()
    {
        InMemoryPolicyStore store = new();

        store.Seed(Rule(DwSubjectKind.User, "alice"));

        Assert.Equal(0, (await store.LoadNarrowAsync(Array.Empty<string>(), default)).Count);
    }

    // ---------------------------------------------------------------- sealed fields

    [Fact]
    public async Task An_upsert_targeting_a_sealed_field_is_refused_when_types_can_be_resolved()
    {
        // Staff.NationalId carries [DwDenied], which is sealed for every feature.
        InMemoryPolicyStore store = new(_ => typeof(Staff));

        ArgumentException error = await Assert.ThrowsAsync<ArgumentException>(
            async () => await store.UpsertAsync(
                Rule(field: "NationalId", effect: PolicyEffect.Allow), default));

        Assert.Contains("sealed", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_rule_on_a_field_sealed_for_other_features_only_is_accepted()
    {
        // Staff.Notes is sealed for Order and Group. A rule about Select does not collide with it,
        // and refusing the whole field would make a narrow attribute act like a blanket one.
        InMemoryPolicyStore store = new(_ => typeof(Staff));

        await store.UpsertAsync(Rule(field: "Notes", features: PolicyFeature.Select), default);

        Assert.Equal(1, (await store.LoadAsync(default)).Count);

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await store.UpsertAsync(
                Rule(field: "Notes", features: PolicyFeature.Order), default));
    }

    [Fact]
    public async Task Without_a_type_resolver_the_upsert_is_accepted_and_the_rule_is_inert()
    {
        // The configuration-time check needs a Type and a store holds a string. Resolution is what
        // actually enforces the seal, and it does so whether or not the store could check.
        InMemoryPolicyStore store = new();

        await store.UpsertAsync(Rule(field: "NationalId", effect: PolicyEffect.Allow), default);

        Assert.Equal(1, (await store.LoadAsync(default)).Count);
    }

    // ---------------------------------------------------------------- watching

    [Fact]
    public async Task A_watch_yields_the_version_on_every_write()
    {
        using InMemoryPolicyStore store = new();
        using CancellationTokenSource cancel = new(TimeSpan.FromSeconds(10));

        IAsyncEnumerable<long> watch = store.WatchAsync(cancel.Token)!;

        Assert.NotNull(watch);

        List<long> seen = new();

        Task reader = Task.Run(
            async () =>
            {
                await foreach (long version in watch.WithCancellation(cancel.Token))
                {
                    seen.Add(version);

                    if (seen.Count == 2)
                    {
                        return;
                    }
                }
            },
            cancel.Token);

        // The watch registers as the enumeration starts, so the writes are retried until it is
        // listening rather than raced against it.
        while (seen.Count < 2 && !reader.IsCompleted)
        {
            await store.UpsertAsync(Rule(field: "Notes"), cancel.Token);

            await Task.Delay(10, cancel.Token);
        }

        await reader;

        Assert.Equal(2, seen.Count);
        Assert.True(seen[1] > seen[0]);
    }

    /// <summary>
    /// A watch opened on a disposed store ends at once rather than waiting for a change that can
    /// no longer come.
    /// </summary>
    /// <remarks>
    /// Enumerated rather than null-checked. <c>WatchAsync</c> hands back a compiler-generated
    /// iterator, which is never null whether the store is disposed or not, so asserting on the
    /// handle alone passed with the guard inside the iterator deleted.
    /// </remarks>
    [Fact]
    public async Task A_watch_opened_after_disposal_ends_at_once()
    {
        InMemoryPolicyStore store = new();

        store.Dispose();

        List<long> seen = new();

        Task drain = Drain(store, seen);

        // Bounded, because the failure this guards against is a sequence that never ends. Left
        // unbounded the regression hangs the run instead of reporting, which is worse than the
        // NotNull check it replaced.
        Assert.True(await Ended(drain), "the watch did not end on a disposed store");
        Assert.Empty(seen);
    }

    /// <summary>
    /// And a watch already running ends when the store it is watching goes away, rather than
    /// holding its reader open forever.
    /// </summary>
    [Fact]
    public async Task Disposing_ends_a_watch_that_is_already_running()
    {
        InMemoryPolicyStore store = new();

        Task reader = Drain(store, new List<long>());

        // The watcher registers on its first move, so publish one change to be sure it has.
        await store.UpsertAsync(Rule(field: "Notes"), default);

        store.Dispose();

        Assert.True(await Ended(reader), "the watch outlived the store that opened it");
    }
    /// <summary>Reads a watch to its end, on a thread of its own.</summary>
    private static Task Drain(InMemoryPolicyStore store, List<long> seen) =>
        Task.Run(async () =>
        {
            await foreach (long version in store.WatchAsync(default)!)
            {
                seen.Add(version);
            }
        });

    /// <summary>True when the watch ended, false when it was still running after a fair wait.</summary>
    private static async Task<bool> Ended(Task drain) =>
        ReferenceEquals(drain, await Task.WhenAny(drain, Task.Delay(TimeSpan.FromSeconds(5))));

}
