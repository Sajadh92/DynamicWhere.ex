using DynamicWhere.ex.Policies.EntityFrameworkCore;
using DynamicWhere.ex.Policies.Tokens;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The durable vault suite run against Entity Framework on a real PostgreSQL server.
/// </summary>
/// <remarks>
/// The SQLite leg proves the vault uses no provider-specific API. This one proves the half SQLite
/// structurally cannot: that two writers minting a token for the same value at the same moment
/// settle on one, through the primary key rather than by retrying. An in-memory SQLite database is
/// a single connection and throws under concurrent use, so a race test there measures the harness.
/// <para>
/// The store conformance suite splits along the same line, for the same reason, and carries its
/// lost-update test here too.
/// </para>
/// </remarks>
public sealed class PostgresTokenVaultConformanceTests
    : DurableTokenVaultConformanceTests, IAsyncLifetime
{
    private readonly PostgreSqlContainer _server =
        new PostgreSqlBuilder("postgres:16-alpine").Build();

    private Func<DbContext>? _contexts;

    /// <summary>Starts the server and creates the schema.</summary>
    public async Task InitializeAsync()
    {
        await _server.StartAsync();

        DbContextOptions<DwPolicyDbContext> options =
            new DbContextOptionsBuilder<DwPolicyDbContext>()
                .UseNpgsql(_server.GetConnectionString())
                .Options;

        _contexts = () => new DwPolicyDbContext(options);

        using DbContext db = _contexts();

        await db.Database.EnsureCreatedAsync();
    }

    /// <summary>Stops the server.</summary>
    public Task DisposeAsync() => _server.DisposeAsync().AsTask();

    /// <inheritdoc />
    protected override IDwTokenVault CreateVault() => new EfTokenVault(_contexts!);

    /// <summary>
    /// Many vaults holding the key, meeting at once a value an unkeyed vault already tokenized, all
    /// hand back that token, and one of them retiring the unkeyed row under the others does not
    /// disturb it.
    /// </summary>
    /// <remarks>
    /// On the leg with a real server behind it, for the reason the unkeyed race gives: SQLite in memory
    /// is one connection and cannot contend.
    /// </remarks>
    [Fact]
    public void Many_keyed_callers_racing_for_a_value_all_get_the_token_it_already_had()
    {
        byte[] key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

        string issued = new EfTokenVault(_contexts!).GetOrCreate(Scope, "RACE-000123");

        EfTokenVault[] vaults = Enumerable.Range(0, 8)
            .Select(i => new EfTokenVault(_contexts!, key, retireUnkeyed: i % 2 == 0)).ToArray();

        string[] tokens = new string[vaults.Length];

        Parallel.For(0, vaults.Length, i => tokens[i] = vaults[i].GetOrCreate(Scope, "RACE-000123"));

        Assert.All(tokens, token => Assert.Equal(issued, token));

        Parallel.For(0, vaults.Length, i => tokens[i] = vaults[i].GetOrCreate(Scope, "RACE-NEW"));

        Assert.Single(tokens.Distinct(StringComparer.Ordinal));

        using DbContext db = _contexts!();

        Assert.DoesNotContain(
            db.Set<DwPolicyTokenRecord>().Select(row => row.Key).ToList(),
            stored => stored == DwToken.KeyFor(Scope, "RACE-000123"));
    }
}
