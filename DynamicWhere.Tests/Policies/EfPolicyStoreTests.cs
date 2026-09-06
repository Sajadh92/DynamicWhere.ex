using DynamicWhere.ex.Policies.EntityFrameworkCore;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// What the relational store does that the conformance suite does not already pin.
/// </summary>
/// <remarks>
/// The shared behaviour is covered by <see cref="SqliteStoreConformanceTests"/> and
/// <see cref="PostgresStoreConformanceTests"/>. What is here is specific to holding a policy in a
/// database: the order the version and the rules are read in, the absence of a notification
/// channel, and what a row nobody can read does to a load.
/// </remarks>
public class EfPolicyStoreTests
{
    private const string StaffType = "DynamicWhere.Tests.Policies.Staff";

    [Fact]
    public async Task An_untouched_database_is_at_version_zero()
    {
        // Honest rather than convenient. The first write creates the row at one, so the version
        // still moves forward on the write an instance is waiting to notice.
        using SqlitePolicyDatabase database = new();

        Assert.Equal(0, await database.Store().GetVersionAsync(default));
    }

    [Fact]
    public async Task A_database_offers_no_watch_so_the_provider_polls()
    {
        // Null, not an empty stream. An empty stream is indistinguishable from a store that has
        // not changed yet, so the provider would sit on a channel that can never fire instead of
        // polling — and the poll is the only invalidation a database store has.
        using SqlitePolicyDatabase database = new();

        Assert.Null(database.Store().WatchAsync(default));
    }

    [Fact]
    public async Task A_snapshot_never_claims_a_version_newer_than_the_rules_it_holds()
    {
        // The direction matters. A snapshot stamped newer than its rules is one the poll compares
        // equal and never reloads, so the instance serves the previous policy indefinitely. Stamped
        // older, the same race costs one redundant reload.
        using SqlitePolicyDatabase database = new();

        EfPolicyStore store = database.Store();

        await store.UpsertAsync(Rule(), default);

        StoreSnapshot snapshot = await store.LoadAsync(default);

        Assert.True(snapshot.Version <= await store.GetVersionAsync(default));
        Assert.Equal(1, snapshot.Count);
    }

    [Fact]
    public async Task Every_write_moves_the_version_by_exactly_one()
    {
        using SqlitePolicyDatabase database = new();

        EfPolicyStore store = database.Store();

        for (int expected = 1; expected <= 5; expected++)
        {
            await store.UpsertAsync(Rule(), default);

            Assert.Equal(expected, await store.GetVersionAsync(default));
        }
    }

    [Fact]
    public async Task Upserting_the_same_identifier_replaces_the_row_rather_than_adding_one()
    {
        using SqlitePolicyDatabase database = new();

        EfPolicyStore store = database.Store();

        Guid id = Guid.NewGuid();

        await store.UpsertAsync(Rule(field: "Department", id: id), default);
        await store.UpsertAsync(Rule(field: "Notes", id: id), default);

        StoreSnapshot snapshot = await store.LoadAsync(default);

        Assert.Equal(1, snapshot.Count);
        Assert.Equal("Notes", snapshot.For(typeof(Staff))[0].FieldPath);
    }

    // ---------------------------------------------------------------- a row nobody can read

    [Fact]
    public async Task A_row_whose_enumeration_column_was_defaulted_fails_the_load()
    {
        // The all-zero row: Allow is zero, Global is zero. A store that read integers would load
        // this as "grant everyone everything, above sealed". It is stored as a name, so a column
        // holding a number is a row that cannot be read — and the load fails rather than skipping
        // it, because a skipped row is a control that quietly stopped being enforced.
        using SqlitePolicyDatabase database = new();

        await database.Store().UpsertAsync(Rule(), default);

        using (DwPolicyDbContext db = database.Create())
        {
            DwPolicyRuleRecord row = await db.PolicyRules.FirstAsync();

            row.Effect = "0";

            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await database.Store().LoadAsync(default));
    }

    [Fact]
    public async Task A_row_whose_detail_cannot_be_read_fails_the_load()
    {
        using SqlitePolicyDatabase database = new();

        await database.Store().UpsertAsync(Rule(), default);

        using (DwPolicyDbContext db = database.Create())
        {
            DwPolicyRuleRecord row = await db.PolicyRules.FirstAsync();

            row.Detail = "{ truncated";

            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAnyAsync<ArgumentException>(
            async () => await database.Store().LoadAsync(default));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A rule for the fixture entity.</summary>
    private static PolicyRule Rule(string field = "Department", Guid? id = null) =>
        new(DwSubjectKind.Global, null, StaffType, field, PolicyFeature.Select, PolicyEffect.Deny,
            id: id);
}

/// <summary>
/// A throwaway SQLite database carrying the two policy tables.
/// </summary>
/// <remarks>
/// SQLite proves the claim design section 5.6 actually makes — that the store uses no raw SQL and
/// no provider-specific API, so it runs on any EF relational provider. It cannot prove the
/// concurrency behaviour, which is what the PostgreSQL container leg is for.
/// <para>
/// The connection is held open for the fixture's lifetime because an in-memory SQLite database
/// exists only while a connection to it does.
/// </para>
/// </remarks>
public sealed class SqlitePolicyDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>Opens the database and creates the schema.</summary>
    public SqlitePolicyDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using DwPolicyDbContext db = Create();

        db.Database.EnsureCreated();
    }

    /// <summary>Opens a context over the database.</summary>
    public DwPolicyDbContext Create() =>
        new(new DbContextOptionsBuilder<DwPolicyDbContext>().UseSqlite(_connection).Options);

    /// <summary>Builds a store over the database.</summary>
    /// <param name="resolveType">Supplied when the sealed-field check should be performed.</param>
    public EfPolicyStore Store(Func<string, Type?>? resolveType = null) =>
        new(() => Create(), resolveType);

    /// <summary>Closes the database, which is what destroys it.</summary>
    public void Dispose() => _connection.Dispose();
}
