using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Source;
using DynamicWhere.Tests.Domain;

namespace DynamicWhere.Tests;

/// <summary>
/// Covers every case in the API's <c>SegmentTestController</c>: condition sets combined with
/// <see cref="Intersection.Union"/>, <see cref="Intersection.Intersect"/> and
/// <see cref="Intersection.Except"/>, then ordered, paginated and projected.
/// </summary>
/// <remarks>
/// Set A (IsActive) = Pro, pro, ProBook, Ultra, Widget, Gadget pro.
/// Set B (Price &gt; 100) = Pro, ProBook, Laptop Pro, Gadget pro.
/// Set C (StockQuantity == 0) = pro, Gadget pro.
/// </remarks>
public class SegmentTests : SalesTestBase
{
    public SegmentTests(SalesFixture fixture) : base(fixture) { }

    private static Condition Cond(string field, DataType type, Operator op, params object[] values) =>
        new() { Sort = 1, Field = field, DataType = type, Operator = op, Values = values.ToList() };

    private static ConditionSet Set(int sort, Intersection? intersection, Condition condition) => new()
    {
        Sort = sort,
        Intersection = intersection,
        ConditionGroup = new ConditionGroup { Connector = Connector.And, Conditions = [condition] }
    };

    private static ConditionSet ActiveSet(int sort = 1, Intersection? intersection = null) =>
        Set(sort, intersection, Cond("IsActive", DataType.Boolean, Operator.Equal, "true"));

    private static ConditionSet ExpensiveSet(int sort, Intersection? intersection) =>
        Set(sort, intersection, Cond("Price", DataType.Number, Operator.GreaterThan, "100"));

    private static ConditionSet OutOfStockSet(int sort, Intersection? intersection) =>
        Set(sort, intersection, Cond("StockQuantity", DataType.Number, Operator.Equal, "0"));

    #region Set operations

    [Fact]
    public async Task Union()
    {
        SegmentResult<Product> result = await Products.ToListAsync(new Segment
        {
            ConditionSets = [ActiveSet(), ExpensiveSet(2, Intersection.Union)]
        });

        // Six active plus "Laptop Pro", which is inactive but costs 250.
        Assert.Equal(7, result.TotalCount);
        Assert.Contains(result.Data!, p => p.Name == "Laptop Pro");
        Assert.DoesNotContain(result.Data!, p => p.Name == "Basic");
    }

    [Fact]
    public async Task Intersect()
    {
        SegmentResult<Product> result = await Products.ToListAsync(new Segment
        {
            ConditionSets = [ActiveSet(), ExpensiveSet(2, Intersection.Intersect)]
        });

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(["Gadget pro", "Pro", "ProBook"],
            result.Data!.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Except()
    {
        SegmentResult<Product> result = await Products.ToListAsync(new Segment
        {
            ConditionSets = [ActiveSet(), ExpensiveSet(2, Intersection.Except)]
        });

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(["Ultra", "Widget", "pro"],
            result.Data!.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public async Task UnionThenExcept()
    {
        SegmentResult<Product> result = await Products.ToListAsync(new Segment
        {
            ConditionSets =
            [
                ActiveSet(),
                ExpensiveSet(2, Intersection.Union),
                OutOfStockSet(3, Intersection.Except)
            ]
        });

        // (A ∪ B) is seven products; removing the two with zero stock leaves five.
        Assert.Equal(5, result.TotalCount);
        Assert.DoesNotContain(result.Data!, p => p.Name is "pro" or "Gadget pro");
    }

    [Fact]
    public async Task SetsAreAppliedInSortOrderNotListOrder()
    {
        SegmentResult<Product> result = await Products.ToListAsync(new Segment
        {
            // Declared out of order; Sort decides that Active is the base set.
            ConditionSets = [ExpensiveSet(2, Intersection.Except), ActiveSet()]
        });

        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task SingleSetNeedsNoIntersection()
    {
        SegmentResult<Product> result = await Products.ToListAsync(new Segment { ConditionSets = [ActiveSet()] });

        Assert.Equal(6, result.TotalCount);
    }

    [Fact]
    public async Task NoConditionSetsFallsBackToAPlainFilter()
    {
        SegmentResult<Product> result = await Products.ToListAsync(new Segment
        {
            ConditionSets = [],
            Orders = [new OrderBy { Sort = 1, Field = "Price", Direction = Direction.Ascending }],
            Page = new PageBy { PageNumber = 1, PageSize = 3 }
        });

        Assert.Equal(8, result.TotalCount);
        Assert.Equal(["Widget", "Basic", "pro"], result.Data!.Select(p => p.Name));
    }

    #endregion

    #region Ordering, pagination and projection

    [Fact]
    public async Task FullPipeline()
    {
        SegmentResult<Product> result = await Products.ToListAsync(new Segment
        {
            ConditionSets = [ActiveSet(), ExpensiveSet(2, Intersection.Union)],
            Orders = [new OrderBy { Sort = 1, Field = "Price", Direction = Direction.Descending }],
            Page = new PageBy { PageNumber = 1, PageSize = 3 }
        });

        Assert.Equal(7, result.TotalCount);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(3, result.PageSize);
        Assert.Equal(3, result.PageCount);          // ceil(7 / 3)
        Assert.Equal(["ProBook", "Gadget pro", "Laptop Pro"], result.Data!.Select(p => p.Name));
    }

    [Fact]
    public async Task OrderingAcrossACollectionPath()
    {
        SegmentResult<Order> result = await Orders.ToListAsync(new Segment
        {
            ConditionSets =
            [
                Set(1, null, Cond("IsPaid", DataType.Boolean, Operator.Equal, "true")),
                Set(2, Intersection.Union, Cond("Status", DataType.Enum, Operator.Equal, "Cancelled"))
            ],
            Orders = [new OrderBy { Sort = 1, Field = "TotalAmount", Direction = Direction.Descending }],
            Page = new PageBy { PageNumber = 1, PageSize = 10 }
        });

        // Paid: 1001, 1002, 1004. Cancelled: 1006.
        Assert.Equal(4, result.TotalCount);
        Assert.Equal(["ORD-1004", "ORD-1002", "ORD-1001", "ORD-1006"], result.Data!.Select(o => o.OrderNumber));
    }

    [Fact]
    public async Task WithSelectsProjectsBeforeTheSetOperation()
    {
        // Set operations compare entity instances. Without Selects the sets share tracked instances
        // and Intersect matches by identity; projecting first creates fresh objects per set, so the
        // default reference equality no longer finds any overlap. Project after the segment, or give
        // the entity value equality, when set operations and Selects are combined.
        SegmentResult<Product> result = await Products.ToListAsync(new Segment
        {
            ConditionSets = [ActiveSet(), ExpensiveSet(2, Intersection.Intersect)],
            Selects = ["Id", "Name", "Price"]
        });

        Assert.Empty(result.Data!);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task WithSelectsIsSafeForUnion()
    {
        // Union keeps everything from both sides, so projection only affects de-duplication:
        // the three products in both sets are counted twice.
        SegmentResult<Product> result = await Products.ToListAsync(new Segment
        {
            ConditionSets = [ActiveSet(), ExpensiveSet(2, Intersection.Union)],
            Selects = ["Id", "Name", "Price"]
        });

        Assert.Equal(10, result.TotalCount);
        Assert.All(result.Data!, p => Assert.Equal(0, p.StockQuantity));
    }

    #endregion

    #region Validation

    [Fact]
    public async Task RejectsDuplicateSortValues()
    {
        var ex = await Assert.ThrowsAsync<LogicException>(() => Products.ToListAsync(new Segment
        {
            ConditionSets = [ActiveSet(1), ExpensiveSet(1, Intersection.Union)]
        }));

        Assert.Contains("MustHasUniqueSortValue", ex.Message);
    }

    [Fact]
    public async Task RejectsMissingIntersectionAfterTheFirstSet()
    {
        var ex = await Assert.ThrowsAsync<LogicException>(() => Products.ToListAsync(new Segment
        {
            ConditionSets = [ActiveSet(), ExpensiveSet(2, null)]
        }));

        Assert.Contains("MustHasIntersection", ex.Message);
    }

    #endregion
}
