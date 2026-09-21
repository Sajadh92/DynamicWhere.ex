using DynamicWhere.ex.Policies.EntityFrameworkCore;
using DynamicWhere.ex.Policies.Tokens;
using Microsoft.EntityFrameworkCore;

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

/// <summary>
/// The durable suite against a vault that holds a key, plus what the key is for and what it must not
/// break: the tokens an unkeyed vault already issued.
/// </summary>
public sealed class KeyedSqliteTokenVaultConformanceTests : DurableTokenVaultConformanceTests, IDisposable
{
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] Other = Enumerable.Range(101, 32).Select(i => (byte)i).ToArray();

    private readonly SqlitePolicyDatabase _database = new();

    /// <inheritdoc />
    protected override IDwTokenVault CreateVault() => new EfTokenVault(() => _database.Create(), Key);

    /// <summary>Stops the database.</summary>
    public void Dispose() => _database.Dispose();

    private List<string> StoredKeys()
    {
        using DwPolicyDbContext db = _database.Create();

        return db.Set<DwPolicyTokenRecord>().Select(row => row.Key).ToList().OrderBy(key => key, StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void The_table_holds_no_plain_digest_of_the_value()
    {
        CreateVault().GetOrCreate(Scope, "07701234567");

        string stored = Assert.Single(StoredKeys());

        Assert.Equal(DwToken.KeyFor(Scope, "07701234567", Key), stored);
        Assert.StartsWith(DwToken.KeyedPrefix, stored, StringComparison.Ordinal);
        Assert.NotEqual(DwToken.KeyFor(Scope, "07701234567"), stored);
    }

    /// <summary>A deployment that adds a key keeps every token it has handed out.</summary>
    [Fact]
    public void A_value_keeps_the_token_an_unkeyed_vault_gave_it()
    {
        string issued = new EfTokenVault(() => _database.Create()).GetOrCreate(Scope, "AAA-000123");

        Assert.Equal(issued, CreateVault().GetOrCreate(Scope, "AAA-000123"));

        // Both rows, until the vault is told every instance holds the key: one still running without
        // it reads the unkeyed row, and must go on finding the same token there.
        Assert.Equal(
            new[] { DwToken.KeyFor(Scope, "AAA-000123"), DwToken.KeyFor(Scope, "AAA-000123", Key) }.OrderBy(key => key, StringComparer.Ordinal),
            StoredKeys());

        Assert.Equal(issued, new EfTokenVault(() => _database.Create()).GetOrCreate(Scope, "AAA-000123"));
    }

    [Fact]
    public void Retiring_deletes_the_unkeyed_row_and_keeps_the_token()
    {
        string issued = new EfTokenVault(() => _database.Create()).GetOrCreate(Scope, "AAA-000123");

        EfTokenVault retiring = new(() => _database.Create(), Key, retireUnkeyed: true);

        Assert.Equal(issued, retiring.GetOrCreate(Scope, "AAA-000123"));
        Assert.Equal(DwToken.KeyFor(Scope, "AAA-000123", Key), Assert.Single(StoredKeys()));

        // A cold vault finds it under the key alone.
        Assert.Equal(issued, CreateVault().GetOrCreate(Scope, "AAA-000123"));
    }

    /// <summary>
    /// The order a deployment does it in: every instance takes the key first, and only then is the vault
    /// told to retire. By then the active values already have their keyed rows, and those are the
    /// unkeyed rows that most need to go.
    /// </summary>
    [Fact]
    public void Retiring_reaches_a_value_adopted_before_retiring_began()
    {
        string issued = new EfTokenVault(() => _database.Create()).GetOrCreate(Scope, "AAA-000123");

        Assert.Equal(issued, CreateVault().GetOrCreate(Scope, "AAA-000123"));
        Assert.Equal(2, StoredKeys().Count);

        Assert.Equal(issued, new EfTokenVault(() => _database.Create(), Key, retireUnkeyed: true).GetOrCreate(Scope, "AAA-000123"));
        Assert.Equal(DwToken.KeyFor(Scope, "AAA-000123", Key), Assert.Single(StoredKeys()));
    }

    [Fact]
    public void A_value_with_no_unkeyed_row_is_not_given_one()
    {
        new EfTokenVault(() => _database.Create(), Key, retireUnkeyed: true).GetOrCreate(Scope, "BBB-1");
        CreateVault().GetOrCreate(Scope, "BBB-2");

        Assert.All(StoredKeys(), key => Assert.StartsWith(DwToken.KeyedPrefix, key, StringComparison.Ordinal));
    }

    /// <summary>Stated as a test because it is the hazard of changing a key: every value is met for the first time again.</summary>
    [Fact]
    public void Another_key_is_another_vault()
    {
        string first = CreateVault().GetOrCreate(Scope, "AAA-000123");
        string second = new EfTokenVault(() => _database.Create(), Other).GetOrCreate(Scope, "AAA-000123");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_key_that_is_absent_or_short_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => new EfTokenVault(() => _database.Create(), null!));
        Assert.Throws<ArgumentException>(() => new EfTokenVault(() => _database.Create(), new byte[8]));
        Assert.Throws<ArgumentNullException>(() => new EfTokenVault(null!, Key));
    }
}
