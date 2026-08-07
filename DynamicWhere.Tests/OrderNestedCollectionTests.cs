using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Source;

namespace DynamicWhere.Tests;

/// <summary>
/// Ordering by a field path that traverses a collection navigation, executed in memory
/// (LINQ to Objects) — the mode used by <c>IEnumerable&lt;T&gt;</c> overloads and by
/// <c>Segment</c> once its condition sets have been materialised.
/// </summary>
public class OrderNestedCollectionTests
{
    private static IQueryable<Post> Posts() => TestData.Posts().AsQueryable();

    private static IQueryable<Blog> Blogs() => TestData.Blogs().AsQueryable();

    private static OrderBy By(string field, Direction direction = Direction.Ascending) =>
        new() { Sort = 1, Field = field, Direction = direction };

    #region Collection element value — the reported bug

    [Fact]
    public void OrderByCollectionElementValue_Ascending_SortsByLowestElementValue()
    {
        // Post 30 has no tags (null), Post 20 min "apple", Post 10 min "mango".
        var result = Posts().Order(By("Tags.Value")).ToList();

        Assert.Equal(new[] { 30, 20, 10 }, result.Select(p => p.Id));
    }

    [Fact]
    public void OrderByCollectionElementValue_Descending_SortsByHighestElementValue()
    {
        // Post 10 max "zebra", Post 20 max "apple", Post 30 has no tags (null).
        var result = Posts().Order(By("Tags.Value", Direction.Descending)).ToList();

        Assert.Equal(new[] { 10, 20, 30 }, result.Select(p => p.Id));
    }

    [Fact]
    public void OrderByCollectionElementValue_IsCaseInsensitiveOnFieldName()
    {
        var result = Posts().Order(By("tags.value")).ToList();

        Assert.Equal(new[] { 30, 20, 10 }, result.Select(p => p.Id));
    }

    #endregion

    #region Non-nullable value types over empty collections

    [Fact]
    public void OrderByCollectionElementValueType_Ascending_DoesNotThrowOnEmptyCollection()
    {
        // Post 30 has no tags, so Min() over an empty sequence of int must not throw.
        var result = Posts().Order(By("Tags.Score")).ToList();

        Assert.Equal(new[] { 30, 10, 20 }, result.Select(p => p.Id));
    }

    [Fact]
    public void OrderByCollectionElementValueType_Descending_DoesNotThrowOnEmptyCollection()
    {
        var result = Posts().Order(By("Tags.Score", Direction.Descending)).ToList();

        Assert.Equal(new[] { 20, 10, 30 }, result.Select(p => p.Id));
    }

    #endregion

    #region Deeper paths

    [Fact]
    public void OrderByPathThroughCollectionIntoReferenceNavigation()
    {
        // Post 10 authors ann/bob → min "ann"; Post 20 → "carl"; Post 30 → none.
        var result = Posts().Order(By("Tags.Author.Name")).ToList();

        Assert.Equal(new[] { 30, 10, 20 }, result.Select(p => p.Id));
    }

    [Fact]
    public void OrderByPathCrossingTwoCollectionLevels()
    {
        // Blog 1 → Post 10 → min "mango"; Blog 2 → Post 20 → min "apple"; Blog 3 → Post 30 → none.
        var result = Blogs().Order(By("Posts.Tags.Value")).ToList();

        Assert.Equal(new[] { 3, 2, 1 }, result.Select(b => b.Id));
    }

    [Fact]
    public void OrderByPathCrossingTwoCollectionLevels_ValueType()
    {
        // Blog 1 → min score 3; Blog 2 → 9; Blog 3 → empty → 0.
        var result = Blogs().Order(By("Posts.Tags.Score")).ToList();

        Assert.Equal(new[] { 3, 1, 2 }, result.Select(b => b.Id));
    }

    [Fact]
    public void OrderByCollectionOfSimpleValues()
    {
        // Labels: Blog 1 ["q","a"] → "a"; Blog 2 ["m"] → "m"; Blog 3 [] → null.
        var result = Blogs().Order(By("Labels")).ToList();

        Assert.Equal(new[] { 3, 1, 2 }, result.Select(b => b.Id));
    }

    #endregion

    #region Paths without collections keep their existing behaviour

    [Fact]
    public void OrderByScalar_IsUnchanged()
    {
        var result = Posts().Order(By("Name", Direction.Descending)).ToList();

        Assert.Equal(new[] { 20, 30, 10 }, result.Select(p => p.Id));
    }

    [Fact]
    public void OrderByReferenceNavigationPath_IsUnchanged()
    {
        // Owners: Blog 1 ann, Blog 2 carl, Blog 3 bob.
        var result = Blogs().Order(By("Owner.Name")).ToList();

        Assert.Equal(new[] { 1, 3, 2 }, result.Select(b => b.Id));
    }

    #endregion

    #region Multiple order criteria

    [Fact]
    public void OrderByMultipleCriteria_MixesCollectionAndScalarPaths()
    {
        // Posts 10 and 40 both have a minimum tag value of "mango" and tie-break on Id descending.
        List<Post> posts =
        [
            .. TestData.Posts(),
            new Post
            {
                Id = 40, Name = "p-forty", BlogId = 3,
                Tags = [new Tag { Id = 4, Value = "mango", Score = 1, PostId = 40 }]
            }
        ];

        var result = posts.AsQueryable().Order(
        [
            By("Tags.Value"),
            new OrderBy { Sort = 2, Field = "Id", Direction = Direction.Descending }
        ]).ToList();

        Assert.Equal(new[] { 30, 20, 40, 10 }, result.Select(p => p.Id));
    }

    #endregion

    #region Invalid paths

    [Fact]
    public void OrderByCollectionOfEntities_Throws()
    {
        var ex = Assert.Throws<LogicException>(() => Posts().Order(By("Tags")).ToList());

        Assert.Contains("CannotEndOnCollectionOfComplexElements", ex.Message);
    }

    [Fact]
    public void OrderByPathEndingOnNestedCollectionOfEntities_Throws()
    {
        var ex = Assert.Throws<LogicException>(() => Blogs().Order(By("Posts.Tags")).ToList());

        Assert.Contains("CannotEndOnCollectionOfComplexElements", ex.Message);
    }

    [Fact]
    public void OrderByUnknownFieldUnderCollection_Throws()
    {
        Assert.Throws<LogicException>(() => Posts().Order(By("Tags.Missing")).ToList());
    }

    #endregion

    #region Filter integration

    [Fact]
    public void FilterOrdersByCollectionElementValue()
    {
        var result = TestData.Posts().ToList(new Filter
        {
            Orders = [By("Tags.Value", Direction.Descending)],
            Page = new PageBy { PageNumber = 1, PageSize = 2 }
        });

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(new[] { 10, 20 }, result.Data!.Select(p => p.Id));
    }

    #endregion
}
