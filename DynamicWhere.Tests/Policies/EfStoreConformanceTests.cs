using DynamicWhere.ex.Policies.EntityFrameworkCore;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Storage;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The store conformance suite run against Entity Framework on SQLite.
/// </summary>
/// <remarks>
/// SQLite is here to prove the claim design section 5.6 actually makes: the store uses no raw SQL
/// and no provider-specific API, so it runs on any EF relational provider. Two providers passing
/// the identical suite is the only evidence for that which does not depend on reading the code.
/// <para>
/// It cannot prove the concurrency behaviour — SQLite serializes writers — which is why the
/// PostgreSQL leg exists and carries the lost-update test.
/// </para>
/// </remarks>
public sealed class SqliteStoreConformanceTests : PolicyStoreConformanceTests, IDisposable
{
    private readonly SqlitePolicyDatabase _database = new();

    /// <inheritdoc />
    protected override IDwPolicyWritableStore CreateStore(Func<string, Type?>? resolveType = null)
    {
        _database.Reset();

        return _database.Store(resolveType);
    }

    /// <summary>Closes the database, which is what destroys it.</summary>
    public void Dispose() => _database.Dispose();
}

/// <summary>
/// The store conformance suite run against Entity Framework on a real PostgreSQL server.
/// </summary>
/// <remarks>
/// A container rather than a fake, because the properties worth checking here are the ones a fake
/// would supply for free: that a case-sensitive collation does not lose a user rule, that a
/// <c>timestamptz</c> accepts what this library writes into it, and that two writers racing for the
/// version row do not both win.
/// <para>
/// The suite therefore needs a Docker daemon, and <c>publish.yml</c> runs the tests before it packs
/// — so a daemon is now required to cut a release, not merely to build. It fails loudly when one is
/// absent rather than skipping: a conformance leg that quietly does not run is the shape this
/// branch calls a test that passes either way.
/// </para>
/// </remarks>
public sealed class PostgresStoreConformanceTests : PolicyStoreConformanceTests, IAsyncLifetime
{
    private readonly PostgreSqlContainer _server =
        new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

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
    protected override IDwPolicyWritableStore CreateStore(Func<string, Type?>? resolveType = null)
    {
        using (DbContext db = _contexts!())
        {
            db.Set<DwPolicyRuleRecord>().RemoveRange(db.Set<DwPolicyRuleRecord>().ToList());
            db.Set<DwPolicyVersionRecord>().RemoveRange(db.Set<DwPolicyVersionRecord>().ToList());
            db.SaveChanges();
        }

        return new EfPolicyStore(_contexts!, resolveType);
    }

    // ---------------------------------------------------------------- what only a server can show

    [Fact]
    public async Task Writers_racing_for_the_version_row_do_not_lose_a_bump()
    {
        // The reason DwPolicyVersionRecord.Version is a concurrency token. Two writers that both
        // read 41 and both write 42 leave a version that moved once for two writes — and since the
        // version is the only thing a database store can notify through, an instance polling for a
        // change sees none and keeps serving the rules one of those writes withdrew.
        //
        // SQLite cannot show this, because it serializes writers. A real server can.
        IDwPolicyWritableStore store = CreateStore();

        const int writers = 12;

        await Task.WhenAll(
            Enumerable.Range(0, writers).Select(
                i => Task.Run(async () => await store.UpsertAsync(Rule(field: $"Field{i}"), default))));

        Assert.Equal(writers, await store.GetVersionAsync(default));
        Assert.Equal(writers, (await store.LoadAsync(default)).Count);
    }

    [Fact]
    public async Task A_user_rule_survives_a_case_sensitive_collation()
    {
        // PostgreSQL's '=' is case-sensitive, so a narrow load comparing the key an operator typed
        // would miss its own user here and match on SQL Server. The store filters on the normalized
        // column instead, which is what puts the comparison back under this library's control.
        IDwPolicyWritableStore store = CreateStore();

        await store.UpsertAsync(Rule(DwSubjectKind.User, "Alice"), default);

        Assert.Equal(1, (await store.LoadNarrowAsync(new[] { "ALICE" }, default)).Count);
        Assert.Equal(1, (await store.LoadNarrowAsync(new[] { "alice" }, default)).Count);
        Assert.Equal(0, (await store.LoadNarrowAsync(new[] { "alicia" }, default)).Count);
    }

    [Fact]
    public async Task A_validity_window_with_an_offset_is_accepted()
    {
        // Npgsql refuses a DateTimeOffset whose offset is not zero when writing timestamptz, so a
        // rule valid from midnight in Baghdad would save on SQL Server and throw here. The model
        // converts to UTC on the way in; the instant is what this library compares.
        IDwPolicyWritableStore store = CreateStore();

        DateTimeOffset baghdad = new(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(3));

        PolicyRule rule = new(
            DwSubjectKind.Global,
            null,
            StaffType,
            "Department",
            PolicyFeature.Select,
            PolicyEffect.Deny,
            validFrom: baghdad,
            validTo: baghdad.AddMonths(1));

        await store.UpsertAsync(rule, default);

        PolicyRule back = (await store.LoadAsync(default)).For(typeof(Staff))[0];

        Assert.Equal(baghdad, back.ValidFrom);
        Assert.Equal(baghdad.AddMonths(1), back.ValidTo);
    }
}
