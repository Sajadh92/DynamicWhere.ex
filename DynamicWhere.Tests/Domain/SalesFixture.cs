using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests.Domain;

/// <summary>
/// Creates one SQLite in-memory database, seeded once, shared by every test class in the
/// <see cref="SalesCollection"/>. The connection is held open for the fixture's lifetime because
/// SQLite drops an in-memory database as soon as its last connection closes.
/// </summary>
public sealed class SalesFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SalesContext> _options;

    public SalesFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<SalesContext>()
            .UseSqlite(_connection)
            .Options;

        using SalesContext context = CreateContext();

        context.Database.EnsureCreated();

        context.Categories.AddRange(SalesSeed.Categories());
        context.Products.AddRange(SalesSeed.Products());
        context.Customers.AddRange(SalesSeed.Customers());
        context.Orders.AddRange(SalesSeed.Orders());
        context.OrderItems.AddRange(SalesSeed.OrderItems());
        context.Reviews.AddRange(SalesSeed.Reviews());

        context.SaveChanges();
    }

    /// <summary>
    /// A fresh context per call so no test sees another test's change tracker or cached entities.
    /// </summary>
    public SalesContext CreateContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}

/// <summary>
/// Shares one seeded database across all sales test classes instead of rebuilding it per class.
/// </summary>
[CollectionDefinition(Name)]
public class SalesCollection : ICollectionFixture<SalesFixture>
{
    public const string Name = "sales";
}

/// <summary>
/// Base class wiring the shared fixture and exposing the query roots the tests operate on.
/// Each property returns a query over a fresh context.
/// </summary>
[Collection(SalesCollection.Name)]
public abstract class SalesTestBase : IDisposable
{
    private readonly SalesContext _context;

    protected SalesTestBase(SalesFixture fixture) => _context = fixture.CreateContext();

    protected SalesContext Db => _context;

    protected IQueryable<Product> Products => _context.Products;

    protected IQueryable<Category> Categories => _context.Categories;

    protected IQueryable<Customer> Customers => _context.Customers;

    protected IQueryable<Order> Orders => _context.Orders;

    protected IQueryable<OrderItem> OrderItems => _context.OrderItems;

    protected IQueryable<Review> Reviews => _context.Reviews;

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
