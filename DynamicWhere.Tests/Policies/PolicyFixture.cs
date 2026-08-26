using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// A person record carrying the full range of policy attributes.
/// </summary>
/// <remarks>
/// Kept out of the sales fixture on purpose. <c>RequirePolicy</c> refuses every unguarded read, so
/// decorating a shared entity with it would fail the several hundred existing tests that query
/// without a context — and quietly weaken this suite into asserting against a model no other test
/// touches.
/// </remarks>
[DwEntity(RequirePolicy = true)]
public class Staff
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    /// <summary>Refused for every feature.</summary>
    [DwDenied]
    public string NationalId { get; set; } = string.Empty;

    /// <summary>Refused for projection, but countable and filterable.</summary>
    [DwNoSelect]
    public decimal Salary { get; set; }

    /// <summary>Refused for sorting and grouping only.</summary>
    [DwDeny(PolicyFeature.Order | PolicyFeature.Group)]
    public string Notes { get; set; } = string.Empty;

    /// <summary>Confirmable but not searchable.</summary>
    [DwOperators(Allow = new[] { Operator.Equal, Operator.In })]
    public string Badge { get; set; } = string.Empty;
}

/// <summary>
/// A record with no policy attributes, so guarded and unguarded reads can be compared and change
/// tracking observed on a whole entity rather than a projection.
/// </summary>
public class Office
{
    public int Id { get; set; }

    public string City { get; set; } = string.Empty;

    public int Capacity { get; set; }
}

/// <summary>The context behind the policy integration tests.</summary>
public class PolicyContext : DbContext
{
    public PolicyContext(DbContextOptions<PolicyContext> options) : base(options)
    {
    }

    public DbSet<Staff> Staff => Set<Staff>();

    public DbSet<Office> Offices => Set<Office>();
}

/// <summary>
/// One seeded SQLite in-memory database, shared across the policy integration tests. The connection
/// stays open for the fixture's life because SQLite drops an in-memory database as soon as its last
/// connection closes.
/// </summary>
public sealed class PolicyFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PolicyContext> _options;

    public PolicyFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<PolicyContext>()
            .UseSqlite(_connection)
            .Options;

        using PolicyContext context = CreateContext();

        context.Database.EnsureCreated();

        context.Staff.AddRange(
            new Staff
            {
                Id = 1, Name = "Ada", Department = "Engineering",
                NationalId = "AAA-111", Salary = 120000m, Notes = "founder", Badge = "E-1"
            },
            new Staff
            {
                Id = 2, Name = "Bo", Department = "Engineering",
                NationalId = "BBB-222", Salary = 95000m, Notes = "joined 2024", Badge = "E-2"
            },
            new Staff
            {
                Id = 3, Name = "Cy", Department = "Sales",
                NationalId = "CCC-333", Salary = 70000m, Notes = "part time", Badge = "S-1"
            });

        context.Offices.AddRange(
            new Office { Id = 1, City = "Baghdad", Capacity = 40 },
            new Office { Id = 2, City = "Amman", Capacity = 25 });

        context.SaveChanges();
    }

    /// <summary>A fresh context per call, so no test sees another's change tracker.</summary>
    public PolicyContext CreateContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}

/// <summary>Shares one seeded database across the policy integration tests.</summary>
[CollectionDefinition(Name)]
public class PolicyCollection : ICollectionFixture<PolicyFixture>
{
    public const string Name = "policies";
}
