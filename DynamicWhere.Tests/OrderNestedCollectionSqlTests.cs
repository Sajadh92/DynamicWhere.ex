using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests;

/// <summary>
/// Ordering by a field path that traverses a collection navigation, executed against a real
/// relational provider — proves the generated expression is translated to SQL rather than
/// evaluated on the client.
/// </summary>
public class OrderNestedCollectionSqlTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TestContext _context;

    public OrderNestedCollectionSqlTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _context = new TestContext(new DbContextOptionsBuilder<TestContext>()
            .UseSqlite(_connection)
            .Options);

        _context.Database.EnsureCreated();

        _context.Authors.AddRange(TestData.Authors());

        foreach (Blog blog in TestData.Blogs())
        {
            // Navigation instances are re-created per call; attach ids only and let EF wire the graph.
            foreach (Post post in blog.Posts)
            {
                foreach (Tag tag in post.Tags)
                {
                    tag.Author = null;
                }
            }

            blog.Owner = null;
            _context.Blogs.Add(blog);
        }

        _context.SaveChanges();
        _context.ChangeTracker.Clear();
    }

    private static OrderBy By(string field, Direction direction = Direction.Ascending) =>
        new() { Sort = 1, Field = field, Direction = direction };

    [Fact]
    public void OrderByCollectionElementValue_Ascending_TranslatesToMinSubquery()
    {
        IQueryable<Post> query = _context.Posts.Order(By("Tags.Value"));

        string sql = query.ToQueryString();

        Assert.Contains("MIN(", sql);
        Assert.Equal(new[] { 30, 20, 10 }, query.Select(p => p.Id).ToList());
    }

    [Fact]
    public void OrderByCollectionElementValue_Descending_TranslatesToMaxSubquery()
    {
        IQueryable<Post> query = _context.Posts.Order(By("Tags.Value", Direction.Descending));

        string sql = query.ToQueryString();

        Assert.Contains("MAX(", sql);
        Assert.Equal(new[] { 10, 20, 30 }, query.Select(p => p.Id).ToList());
    }

    [Fact]
    public void OrderByCollectionElementValueType_TranslatesAndHandlesEmptyCollections()
    {
        IQueryable<Post> query = _context.Posts.Order(By("Tags.Score"));

        string sql = query.ToQueryString();

        Assert.Contains("MIN(", sql);
        Assert.Equal(new[] { 30, 10, 20 }, query.Select(p => p.Id).ToList());
    }

    [Fact]
    public void OrderByPathThroughCollectionIntoReferenceNavigation_Translates()
    {
        IQueryable<Post> query = _context.Posts.Order(By("Tags.Author.Name"));

        Assert.Contains("MIN(", query.ToQueryString());
        Assert.Equal(new[] { 30, 10, 20 }, query.Select(p => p.Id).ToList());
    }

    [Fact]
    public void OrderByPathCrossingTwoCollectionLevels_Translates()
    {
        IQueryable<Blog> query = _context.Blogs.Order(By("Posts.Tags.Value"));

        Assert.Contains("MIN(", query.ToQueryString());
        Assert.Equal(new[] { 3, 2, 1 }, query.Select(b => b.Id).ToList());
    }

    [Fact]
    public async Task ToListAsyncWithFilter_OrdersByCollectionElementValue()
    {
        var result = await _context.Posts.ToListAsync(new Filter
        {
            Orders = [By("Tags.Value")],
            Page = new PageBy { PageNumber = 1, PageSize = 2 }
        });

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(new[] { 30, 20 }, result.Data!.Select(p => p.Id));
    }

    [Fact]
    public async Task SegmentOrdersByCollectionElementValue()
    {
        // Segment materialises each condition set, then orders the resulting list in memory —
        // the aggregate has to work under LINQ to Objects as well as in SQL. Tags are eagerly
        // loaded because the in-memory sort can only see navigations that were fetched.
        var result = await _context.Posts.Include(p => p.Tags).ToListAsync(new Segment
        {
            ConditionSets =
            [
                new ConditionSet
                {
                    Sort = 1,
                    ConditionGroup = new ConditionGroup
                    {
                        Connector = Connector.And,
                        Conditions =
                        [
                            new Condition
                            {
                                Sort = 1, Field = "Id", DataType = DataType.Number,
                                Operator = Operator.GreaterThanOrEqual, Values = [0]
                            }
                        ]
                    }
                }
            ],
            Orders = [By("Tags.Score")]
        });

        Assert.Equal(new[] { 30, 10, 20 }, result.Data!.Select(p => p.Id));
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
