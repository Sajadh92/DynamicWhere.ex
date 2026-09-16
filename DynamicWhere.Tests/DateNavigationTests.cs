using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests;

/// <summary>
/// A date member that cannot hold null, reached through a navigation that can.
/// </summary>
/// <remarks>
/// The builder drops the null guard for a member whose type cannot be null, because the parser cannot
/// compare such a member to null at all. Decided from the member alone, that also dropped what the
/// guard did for a path: <c>Approval.ApprovedAt</c> is NULL to a provider for an invoice with no
/// approval. <c>IsNotNull</c> became <c>true</c> — every invoice, including under a forced scope —
/// <c>IsNull</c> became <c>false</c>, and <c>NotEqual</c> picked up the provider's null compensation and
/// returned the invoices with nothing to compare. 3.0.0 answered all three by the approval.
/// </remarks>
public sealed class DateNavigationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public DateNavigationTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private NavigationDb Seeded()
    {
        NavigationDb db = new(_connection);

        db.Database.EnsureCreated();

        if (!db.Invoices.Any())
        {
            db.Invoices.Add(new Billed { Id = 1, Approval = Approved() });
            db.Invoices.Add(new Billed { Id = 2, Approval = null });
            db.SaveChanges();
        }

        return db;
    }

    private static Signoff Approved() => new()
    {
        Id = 1,
        ApprovedAt = new DateTime(2026, 1, 1, 9, 0, 0),
        SignedAt = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero),
        Day = new DateOnly(2026, 1, 1)
    };

    private static Billed[] InMemory() => new[]
    {
        new Billed { Id = 1, Approval = Approved() },
        new Billed { Id = 2, Approval = null }
    };

    private static ConditionGroup On(string field, DataType type, Operator op, params object[] values)
    {
        Condition condition = new() { Field = field, DataType = type, Operator = op };

        condition.Values.AddRange(values);

        return new ConditionGroup { Conditions = { condition } };
    }

    private static int[] Ids(IQueryable<Billed> query, ConditionGroup group) =>
        query.Where(group).Select(invoice => invoice.Id).AsEnumerable().OrderBy(id => id).ToArray();

    [Theory]
    [InlineData("Approval.ApprovedAt", DataType.DateTime)]
    [InlineData("Approval.ApprovedAt", DataType.Date)]
    [InlineData("Approval.SignedAt", DataType.DateTime)]
    [InlineData("Approval.Day", DataType.Date)]
    public void IsNotNull_is_answered_by_the_navigation(string field, DataType type)
    {
        using NavigationDb db = Seeded();

        Assert.Equal(new[] { 1 }, Ids(db.Invoices.AsNoTracking(), On(field, type, Operator.IsNotNull)));
        Assert.Equal(new[] { 1 }, Ids(InMemory().AsQueryable(), On(field, type, Operator.IsNotNull)));
    }

    [Theory]
    [InlineData("Approval.ApprovedAt", DataType.DateTime)]
    [InlineData("Approval.SignedAt", DataType.DateTime)]
    [InlineData("Approval.Day", DataType.Date)]
    public void IsNull_is_answered_by_the_navigation(string field, DataType type)
    {
        using NavigationDb db = Seeded();

        Assert.Equal(new[] { 2 }, Ids(db.Invoices.AsNoTracking(), On(field, type, Operator.IsNull)));
        Assert.Equal(new[] { 2 }, Ids(InMemory().AsQueryable(), On(field, type, Operator.IsNull)));
    }

    [Theory]
    [InlineData(DataType.DateTime)]
    [InlineData(DataType.Date)]
    public void NotEqual_never_returns_the_row_with_nothing_to_compare(DataType type)
    {
        using NavigationDb db = Seeded();

        ConditionGroup notNewYear = On("Approval.ApprovedAt", type, Operator.NotEqual, "2025-06-01");

        Assert.Equal(new[] { 1 }, Ids(db.Invoices.AsNoTracking(), notNewYear));
        Assert.Equal(new[] { 1 }, Ids(InMemory().AsQueryable(), notNewYear));
    }

    [Fact]
    public void A_comparison_through_a_missing_navigation_does_not_throw_in_memory()
    {
        // The guard is on the navigation, so the member is never read through a null reference.
        Assert.Equal(
            new[] { 1 },
            Ids(InMemory().AsQueryable(), On("Approval.SignedAt", DataType.DateTime, Operator.GreaterThan, "2025-06-01")));
    }

    [Fact]
    public void A_member_on_the_entity_itself_still_needs_no_guard()
    {
        // Nothing lies between the entity and the member, so there is nothing to guard and the
        // constants stand.
        Billed[] invoices = { new() { Id = 7, IssuedAt = new DateTime(2026, 1, 1) } };

        Assert.Equal(new[] { 7 }, Ids(invoices.AsQueryable(), On("IssuedAt", DataType.DateTime, Operator.IsNotNull)));
        Assert.Empty(Ids(invoices.AsQueryable(), On("IssuedAt", DataType.DateTime, Operator.IsNull)));
    }

    [Fact]
    public void A_path_through_a_collection_guards_the_navigation_inside_it()
    {
        BilledBatch[] batches =
        {
            new() { Id = 1, Invoices = { new Billed { Id = 10, Approval = Approved() } } },
            new() { Id = 2, Invoices = { new Billed { Id = 20, Approval = null } } }
        };

        int[] ids = batches.AsQueryable()
            .Where(new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = "Invoices.Approval.SignedAt",
                        DataType = DataType.DateTime,
                        Operator = Operator.IsNotNull
                    }
                }
            })
            .Select(batch => batch.Id)
            .ToArray();

        Assert.Equal(new[] { 1 }, ids);
    }
}

public class Billed
{
    public int Id { get; set; }

    public DateTime IssuedAt { get; set; }

    public int? ApprovalId { get; set; }

    public Signoff? Approval { get; set; }
}

public class Signoff
{
    public int Id { get; set; }

    public DateTime ApprovedAt { get; set; }

    public DateTimeOffset SignedAt { get; set; }

    public DateOnly Day { get; set; }
}

public class BilledBatch
{
    public int Id { get; set; }

    public List<Billed> Invoices { get; set; } = new();
}

internal sealed class NavigationDb : DbContext
{
    private readonly SqliteConnection _connection;

    public NavigationDb(SqliteConnection connection) => _connection = connection;

    public DbSet<Billed> Invoices => Set<Billed>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
}
