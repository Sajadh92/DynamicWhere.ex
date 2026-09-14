using DynamicWhere.ex.Policies.EntityFrameworkCore;
using DynamicWhere.ex.Policies.Tokens;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The durable suite against Entity Framework on SQLite.
/// </summary>
/// <remarks>
/// SQLite because it proves the claim the package makes: no raw SQL and no provider-specific API,
/// so the vault runs on any EF relational provider. It also means this leg needs no Docker daemon,
/// unlike the Redis one.
/// </remarks>
public sealed class SqliteTokenVaultConformanceTests : DurableTokenVaultConformanceTests, IDisposable
{
    private readonly SqlitePolicyDatabase _database = new();

    /// <inheritdoc />
    protected override IDwTokenVault CreateVault() => new EfTokenVault(() => _database.Create());

    /// <summary>Closes the database, which is what destroys it.</summary>
    public void Dispose() => _database.Dispose();
}
