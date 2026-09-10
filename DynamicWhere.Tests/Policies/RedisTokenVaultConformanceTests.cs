using DynamicWhere.ex.Policies.Redis;
using DynamicWhere.ex.Policies.Tokens;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The durable vault suite run against a real Redis server, plus what only Redis can get wrong.
/// </summary>
/// <remarks>
/// A container rather than a fake multiplexer, for the reason the store suite gives: the properties
/// worth checking here are the ones a fake supplies for free. Whether <c>HSETNX</c> is genuinely
/// atomic across connections, and whether a prefix really isolates two applications sharing one
/// server, are facts about a server.
/// <para>
/// Each instance takes a fresh key prefix, which is how a test gets a clean vault without flushing
/// a shared server.
/// </para>
/// </remarks>
public sealed class RedisTokenVaultConformanceTests : DurableTokenVaultConformanceTests, IAsyncLifetime
{
    private readonly RedisContainer _server =
        new RedisBuilder().WithImage("redis:7-alpine").Build();

    private readonly string _prefix = $"dw:tokens:{Guid.NewGuid():N}";

    private IConnectionMultiplexer? _redis;

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
    protected override IDwTokenVault CreateVault() => new RedisTokenVault(_redis!, _prefix);

    [Fact]
    public void Two_prefixes_are_two_vaults()
    {
        // What two applications sharing one Redis are relying on. Without it, a value tokenized in
        // one application is recognisable in the other, which is the cross-scope disclosure a scope
        // exists to prevent — one level up.
        RedisTokenVault mine = new(_redis!, _prefix);
        RedisTokenVault theirs = new(_redis!, $"{_prefix}:other");

        Assert.NotEqual(
            mine.GetOrCreate(Scope, "AAA-000123"),
            theirs.GetOrCreate(Scope, "AAA-000123"));
    }

    [Fact]
    public void A_cold_cache_reads_the_server_rather_than_reissuing()
    {
        // The cache is an optimisation and must never be the source of truth. A vault that minted a
        // fresh token whenever its own dictionary missed would pass every single-instance test and
        // hand a second instance a different answer for the same value.
        RedisTokenVault vault = new(_redis!, _prefix);

        string first = vault.GetOrCreate(Scope, "AAA-000123");

        Assert.Equal(1, vault.CachedCount);

        vault.ClearCache();

        Assert.Equal(0, vault.CachedCount);
        Assert.Equal(first, vault.GetOrCreate(Scope, "AAA-000123"));
    }
}
