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
        new RedisBuilder("redis:7-alpine").Build();

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

    // ------------------------------------------------------------------ under a key

    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] Other = Enumerable.Range(101, 32).Select(i => (byte)i).ToArray();

    private string[] Fields(string prefix) => _redis!.GetDatabase()
        .HashKeys(new RedisPolicyKeys(prefix).Tokens)
        .Select(field => (string)field!)
        .OrderBy(field => field, StringComparer.Ordinal)
        .ToArray();

    [Fact]
    public void Under_a_key_the_hash_holds_no_plain_digest_of_the_value()
    {
        string prefix = $"{_prefix}:keyed";

        new RedisTokenVault(_redis!, Key, prefix).GetOrCreate(Scope, "07701234567");

        string field = Assert.Single(Fields(prefix));

        Assert.Equal(DwToken.KeyFor(Scope, "07701234567", Key), field);
        Assert.NotEqual(DwToken.KeyFor(Scope, "07701234567"), field);
    }

    /// <summary>A deployment that adds a key keeps every token it has handed out.</summary>
    [Fact]
    public void Under_a_key_a_value_keeps_the_token_an_unkeyed_vault_gave_it()
    {
        string prefix = $"{_prefix}:adopt";
        string issued = new RedisTokenVault(_redis!, prefix).GetOrCreate(Scope, "AAA-000123");

        Assert.Equal(issued, new RedisTokenVault(_redis!, Key, prefix).GetOrCreate(Scope, "AAA-000123"));

        // Both fields, until the vault is told every instance holds the key.
        Assert.Equal(
            new[] { DwToken.KeyFor(Scope, "AAA-000123"), DwToken.KeyFor(Scope, "AAA-000123", Key) }
                .OrderBy(field => field, StringComparer.Ordinal),
            Fields(prefix));

        Assert.Equal(issued, new RedisTokenVault(_redis!, prefix).GetOrCreate(Scope, "AAA-000123"));
        Assert.Equal(issued, new RedisTokenVault(_redis!, Key, prefix).GetOrCreate(Scope, "AAA-000123"));
    }

    [Fact]
    public void Retiring_deletes_the_unkeyed_field_and_keeps_the_token()
    {
        string prefix = $"{_prefix}:retire";
        string issued = new RedisTokenVault(_redis!, prefix).GetOrCreate(Scope, "AAA-000123");

        Assert.Equal(issued, new RedisTokenVault(_redis!, Key, prefix, retireUnkeyed: true).GetOrCreate(Scope, "AAA-000123"));
        Assert.Equal(DwToken.KeyFor(Scope, "AAA-000123", Key), Assert.Single(Fields(prefix)));
        Assert.Equal(issued, new RedisTokenVault(_redis!, Key, prefix).GetOrCreate(Scope, "AAA-000123"));
    }

    /// <summary>Every instance takes the key first and retiring begins afterwards, so the values that matter are already adopted.</summary>
    [Fact]
    public void Retiring_reaches_a_value_adopted_before_retiring_began()
    {
        string prefix = $"{_prefix}:late";
        string issued = new RedisTokenVault(_redis!, prefix).GetOrCreate(Scope, "AAA-000123");

        Assert.Equal(issued, new RedisTokenVault(_redis!, Key, prefix).GetOrCreate(Scope, "AAA-000123"));
        Assert.Equal(2, Fields(prefix).Length);

        Assert.Equal(issued, new RedisTokenVault(_redis!, Key, prefix, retireUnkeyed: true).GetOrCreate(Scope, "AAA-000123"));
        Assert.Equal(DwToken.KeyFor(Scope, "AAA-000123", Key), Assert.Single(Fields(prefix)));
    }

    [Fact]
    public void Many_keyed_callers_racing_for_a_value_all_get_the_token_it_already_had()
    {
        string prefix = $"{_prefix}:race";
        string issued = new RedisTokenVault(_redis!, prefix).GetOrCreate(Scope, "AAA-000123");

        RedisTokenVault[] vaults = Enumerable.Range(0, 8)
            .Select(i => new RedisTokenVault(_redis!, Key, prefix, retireUnkeyed: i % 2 == 0)).ToArray();

        string[] tokens = new string[vaults.Length];

        Parallel.For(0, vaults.Length, i => tokens[i] = vaults[i].GetOrCreate(Scope, "AAA-000123"));

        Assert.All(tokens, token => Assert.Equal(issued, token));

        // And for a value nobody has met, one token between them.
        Parallel.For(0, vaults.Length, i => tokens[i] = vaults[i].GetOrCreate(Scope, "NEW-1"));

        Assert.Single(tokens.Distinct(StringComparer.Ordinal));
    }

    /// <summary>Stated as a test because it is the hazard of changing a key: every value is met for the first time again.</summary>
    [Fact]
    public void Another_key_is_another_vault()
    {
        string prefix = $"{_prefix}:rotate";

        Assert.NotEqual(
            new RedisTokenVault(_redis!, Key, prefix).GetOrCreate(Scope, "AAA-000123"),
            new RedisTokenVault(_redis!, Other, prefix).GetOrCreate(Scope, "AAA-000123"));
    }

    [Fact]
    public void A_key_that_is_absent_or_short_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new RedisTokenVault(_redis!, (byte[])null!, _prefix));
        Assert.Throws<ArgumentException>(() => new RedisTokenVault(_redis!, new byte[8], _prefix));
        Assert.Throws<ArgumentNullException>(() => new RedisTokenVault(null!, Key, _prefix));
    }
}
