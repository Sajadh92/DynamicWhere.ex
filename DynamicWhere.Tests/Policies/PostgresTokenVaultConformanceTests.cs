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
}
