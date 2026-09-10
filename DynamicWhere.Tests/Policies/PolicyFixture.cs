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

/// <summary>
/// A tenant-scoped record. The library filters it whether the caller asked or not, which is the
/// whole point of a forced predicate.
/// </summary>
public class Invoice
{
    public int Id { get; set; }

    /// <summary>Scoped from the caller's ambient values.</summary>
    [DwForceWhere(Operator.Equal, ContextValue = "TenantId")]
    public int TenantId { get; set; }

    /// <summary>Scoped by a constant: the soft-delete case.</summary>
    [DwForceWhere(Operator.Equal, Value = "false")]
    public bool IsVoid { get; set; }

    /// <summary>Answers to a public name as well as its own.</summary>
    [DwAlias("reference")]
    public string Number { get; set; } = string.Empty;

    public decimal Amount { get; set; }
}

/// <summary>
/// A record that demands the caller supply the scope rather than supplying it for them.
/// </summary>
public class Journal
{
    public int Id { get; set; }

    [DwRequireWhere]
    public int TenantId { get; set; }

    public decimal Amount { get; set; }
}

/// <summary>
/// A person whose values are changed on the way out: the entity the transformation phase exists
/// for. Each transformed member is also refused for ordering, because sorting runs against the real
/// value and paging through a masked column ranks the true order.
/// </summary>
public class Person
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    /// <summary>Partly hidden, keeping the last four characters.</summary>
    [DwMask(MaskStrategy.Partial, KeepEnd = 4)]
    [DwNoOrder]
    public string NationalId { get; set; } = string.Empty;

    /// <summary>Reduced to its shape.</summary>
    [DwMask(MaskStrategy.Email)]
    [DwNoOrder]
    public string Email { get; set; } = string.Empty;

    /// <summary>Rounded to a band, staying a number so the member can hold it.</summary>
    [DwGeneralize(GeneralizeMode.Round, Step = 10000)]
    [DwNoOrder]
    public decimal Salary { get; set; }

    /// <summary>
    /// Rounded to the nearest five. Integer because SQLite refuses to aggregate a decimal, and an
    /// aggregate of a transformed field is what the summary tests need to exercise.
    /// </summary>
    /// <remarks>
    /// Deliberately opted in to aggregation. Phase 8 denies it by default — MAX over a generalized
    /// column reads the real values, which is design section 7.2's attack — and this field exists to
    /// prove the other half: that the transform runs *after* the grouping, so the aggregate picks
    /// the right row and is rounded on the way out. The un-opted case is
    /// <c>PolicyInferenceTests</c>'s.
    /// </remarks>
    [DwGeneralize(GeneralizeMode.Round, Step = 5, AllowAggregate = true)]
    [DwNoOrder]
    public int Age { get; set; }

    /// <summary>Replaced outright.</summary>
    [DwDefault("N/A")]
    public string Notes { get; set; } = string.Empty;

    public int? BadgeId { get; set; }

    /// <summary>A reference navigation carrying a transform of its own.</summary>
    public Badge? Badge { get; set; }
}

/// <summary>The nested type behind <see cref="Person.Badge"/>.</summary>
public class Badge
{
    public int Id { get; set; }

    [DwMask(MaskStrategy.Full)]
    [DwNoOrder]
    public string Serial { get; set; } = string.Empty;
}

/// <summary>The context behind the policy integration tests.</summary>
public class PolicyContext : DbContext
{
    public PolicyContext(DbContextOptions<PolicyContext> options) : base(options)
    {
    }

    public DbSet<Staff> Staff => Set<Staff>();

    public DbSet<Office> Offices => Set<Office>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<Journal> Journals => Set<Journal>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<Badge> Badges => Set<Badge>();
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

        context.Invoices.AddRange(
            new Invoice { Id = 1, TenantId = 5, IsVoid = false, Number = "INV-1", Amount = 100m },
            new Invoice { Id = 2, TenantId = 5, IsVoid = true, Number = "INV-2", Amount = 200m },
            new Invoice { Id = 3, TenantId = 9, IsVoid = false, Number = "INV-3", Amount = 300m },
            new Invoice { Id = 4, TenantId = 9, IsVoid = false, Number = "INV-1", Amount = 400m });

        context.Journals.AddRange(
            new Journal { Id = 1, TenantId = 5, Amount = 10m },
            new Journal { Id = 2, TenantId = 9, Amount = 20m });

        context.Badges.AddRange(
            new Badge { Id = 1, Serial = "SER-0001" },
            new Badge { Id = 2, Serial = "SER-0002" });

        // Two salaries that round into the same band, so a summary grouped on them collides.
        context.People.AddRange(
            new Person
            {
                Id = 1, Name = "Ada", Department = "Engineering", NationalId = "AAA-111-2345",
                Email = "ada@example.com", Salary = 118000m, Age = 41, Notes = "founder", BadgeId = 1
            },
            new Person
            {
                Id = 2, Name = "Bo", Department = "Engineering", NationalId = "BBB-222-6789",
                Email = "bo@example.com", Salary = 121000m, Age = 32, Notes = "joined 2024", BadgeId = 2
            },
            new Person
            {
                Id = 3, Name = "Cy", Department = "Sales", NationalId = "CCC-333-1111",
                Email = "cy@other.org", Salary = 70000m, Age = 27, Notes = "part time"
            });

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
