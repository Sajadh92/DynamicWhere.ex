using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Redis;
using DynamicWhere.ex.Policies.Storage;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The store conformance suite run against a real Redis server, plus what only Redis can get wrong.
/// </summary>
/// <remarks>
/// A container rather than a fake multiplexer. A fake would prove the fake works, and the property
/// the suite exists for is that three real stores behave alike — pub/sub delivery, byte-for-byte
/// key matching and an atomic counter are exactly the things a fake supplies for free and a server
/// does not.
/// <para>
/// Each call for a store takes a fresh key prefix, which is how the suite gets a clean store
/// without flushing a shared server — and incidentally proves the prefix isolates, which is what
/// two applications sharing one Redis depend on.
/// </para>
/// </remarks>
public sealed class RedisStoreConformanceTests : PolicyStoreConformanceTests, IAsyncLifetime
{
    private readonly RedisContainer _server =
        new RedisBuilder().WithImage("redis:7-alpine").Build();

    private IConnectionMultiplexer? _redis;
    private int _prefixes;

    /// <summary>Starts the server and connects.</summary>
    public async Task InitializeAsync()
    {
        await _server.StartAsync();

        _redis = await ConnectionMultiplexer.ConnectAsync(_server.GetConnectionString());
    }

    /// <summary>Disconnects and stops the server.</summary>
    public async Task DisposeAsync()
    {
        if (_redis is not null)
        {
            await _redis.DisposeAsync();
        }

        await _server.DisposeAsync();
    }

    /// <inheritdoc />
    protected override IDwPolicyWritableStore CreateStore(Func<string, Type?>? resolveType = null) =>
        new RedisPolicyStore(_redis!, NextPrefix(), resolveType);

    // ---------------------------------------------------------------- the backstop behind pub/sub

    [Fact]
    public async Task The_version_is_read_from_the_server_and_not_from_the_writer()
    {
        // The memo's warning made into a test. A store that satisfied WatchAsync and left
        // GetVersionAsync returning something it remembered locally would pass every other test
        // here and silently remove the poll — and the poll is the only thing bounding a dropped
        // pub/sub message, which Redis never guarantees to deliver.
        //
        // Two stores, one prefix: the reader never saw the write, so anything it reports it must
        // have read from Redis.
        string prefix = NextPrefix();

        RedisPolicyStore writer = new(_redis!, prefix);
        RedisPolicyStore reader = new(_redis!, prefix);

        Assert.Equal(0, await reader.GetVersionAsync(default));

        await writer.UpsertAsync(Rule(), default);

        Assert.Equal(1, await reader.GetVersionAsync(default));
        Assert.Equal(1, (await reader.LoadAsync(default)).Count);
    }

    [Fact]
    public async Task A_write_announces_the_version_it_produced()
    {
        // The fast path of design section 5.4: milliseconds rather than a poll interval. The value
        // carried is the version the write landed on, so a subscriber can tell it apart from the
        // one it already holds.
        string prefix = NextPrefix();

        RedisPolicyStore store = new(_redis!, prefix);

        using CancellationTokenSource cancel = new(TimeSpan.FromSeconds(20));

        IAsyncEnumerable<long> watch = store.WatchAsync(cancel.Token)!;

        Task<long> announced = Task.Run(
            async () =>
            {
                await foreach (long version in watch.WithCancellation(cancel.Token))
                {
                    return version;
                }

                return -1L;
            },
            cancel.Token);

        // The subscription is established asynchronously, so the write is repeated until it is
        // observed rather than sent once into a channel nobody is listening on yet.
        while (!announced.IsCompleted)
        {
            await store.UpsertAsync(Rule(), cancel.Token);

            await Task.Delay(25, cancel.Token);
        }

        Assert.True(await announced > 0);
        Assert.Equal(await announced, await store.GetVersionAsync(default));
    }

    // ---------------------------------------------------------------- the key layout

    [Fact]
    public async Task Two_prefixes_do_not_see_each_other()
    {
        RedisPolicyStore mine = new(_redis!, NextPrefix());
        RedisPolicyStore theirs = new(_redis!, NextPrefix());

        await mine.UpsertAsync(Rule(), default);

        Assert.Equal(1, (await mine.LoadAsync(default)).Count);
        Assert.Equal(0, (await theirs.LoadAsync(default)).Count);
        Assert.Equal(0, await theirs.GetVersionAsync(default));
    }

    [Fact]
    public async Task A_user_rule_is_found_whatever_the_caller_spells_its_identity()
    {
        // Redis matches keys byte for byte, so a rule written under ':user:Alice' and fetched under
        // ':user:alice' is simply not there — and a caller with a user-level denial that was never
        // read is indistinguishable from a caller who has none.
        IDwPolicyWritableStore store = CreateStore();

        await store.UpsertAsync(Rule(DwSubjectKind.User, "Alice"), default);

        Assert.Equal(1, (await store.LoadNarrowAsync(new[] { "ALICE" }, default)).Count);
        Assert.Equal(1, (await store.LoadNarrowAsync(new[] { "alice" }, default)).Count);
        Assert.Equal(1, (await store.LoadNarrowAsync(new[] { " Alice " }, default)).Count);
        Assert.Equal(0, (await store.LoadNarrowAsync(new[] { "alicia" }, default)).Count);
    }

    [Fact]
    public async Task A_rule_that_changes_subject_leaves_no_copy_behind()
    {
        // The rule's identifier is stable and its subject is not, so an upsert can move it between
        // the broad hash and a user's. A copy left in the old one goes on applying to the old
        // subject — which for an Allow is a grant nobody wrote, to a caller nobody named.
        IDwPolicyWritableStore store = CreateStore();

        Guid id = Guid.NewGuid();

        await store.UpsertAsync(
            new PolicyRule(
                DwSubjectKind.Role, "Manager", StaffType, "Salary", PolicyFeature.Select,
                PolicyEffect.Deny, id: id),
            default);

        Assert.Equal(1, (await store.LoadAsync(default)).Count);

        await store.UpsertAsync(
            new PolicyRule(
                DwSubjectKind.User, "alice", StaffType, "Salary", PolicyFeature.Select,
                PolicyEffect.Deny, id: id),
            default);

        Assert.Equal(0, (await store.LoadAsync(default)).Count);
        Assert.Equal(1, (await store.LoadNarrowAsync(new[] { "alice" }, default)).Count);
    }

    [Fact]
    public async Task A_rule_is_deleted_wherever_it_lives()
    {
        // A delete is handed an identifier and nothing else, and the rule could be in the broad
        // hash or any user's. The owner index is what makes that one lookup rather than a scan.
        IDwPolicyWritableStore store = CreateStore();

        PolicyRule user = Rule(DwSubjectKind.User, "alice");

        await store.UpsertAsync(user, default);
        await store.DeleteAsync(user.Id, default);

        Assert.Equal(0, (await store.LoadNarrowAsync(new[] { "alice" }, default)).Count);
    }

    // ---------------------------------------------------------------- a document nobody can read

    [Fact]
    public async Task A_stored_document_that_cannot_be_read_fails_the_load()
    {
        string prefix = NextPrefix();

        RedisPolicyStore store = new(_redis!, prefix);

        await store.UpsertAsync(Rule(), default);

        await _redis!.GetDatabase()
            .HashSetAsync($"{prefix}:rules", Guid.NewGuid().ToString(), "{ truncated");

        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await store.LoadAsync(default));
    }

    /// <summary>A prefix no other store in this class is using.</summary>
    private string NextPrefix() => $"test:{Interlocked.Increment(ref _prefixes)}";
}
