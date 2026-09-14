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

    /// <summary>
    /// Many callers minting for one value at once still end up with one token.
    /// </summary>
    /// <remarks>
    /// The race the store's own uniqueness has to settle. Both writers would otherwise insert, and
    /// the second would win for whoever read next — leaving one value with two tokens and a column
    /// that no longer groups. The loser reads the winner's row rather than retrying, because
    /// retrying would mint another token and lose the same race again.
    /// <para>
    /// It lives on the legs with a real server behind them. SQLite cannot show it: an in-memory
    /// database is a single connection, and driving it from several threads throws rather than
    /// contending, which is a fact about the test harness and not about the vault.
    /// </para>
    /// </remarks>
    [Fact]
    public void Many_callers_racing_for_one_value_all_get_one_token()
    {
        IDwTokenVault[] vaults = Enumerable.Range(0, 8).Select(_ => CreateVault()).ToArray();

        string[] tokens = new string[vaults.Length];

        Parallel.For(0, vaults.Length, i => tokens[i] = vaults[i].GetOrCreate(Scope, "AAA-000123"));

        Assert.Single(tokens.Distinct(StringComparer.Ordinal));
    }

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
