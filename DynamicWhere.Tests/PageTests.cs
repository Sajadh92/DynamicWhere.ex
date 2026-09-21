using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Source;
using DynamicWhere.Tests.Domain;

namespace DynamicWhere.Tests;

/// <summary>
/// Covers every case in the API's <c>PageTestController</c>: <c>Page&lt;T&gt;</c> across page sizes,
/// page numbers, entities, combined with ordering, and its validation rules.
/// </summary>
public class PageTests : SalesTestBase
{
    public PageTests(SalesFixture fixture) : base(fixture) { }

    private static PageBy Page(int number, int size) => new() { PageNumber = number, PageSize = size };

    [Fact]
    public void FirstPageReturnsEverythingWhenSizeExceedsRowCount() =>
        Assert.Equal(8, Products.Page(Page(1, 10)).Count());

    [Fact]
    public void SecondPageIsEmptyWhenFirstPageCoveredEverything() =>
        Assert.Empty(Products.Page(Page(2, 10)).ToList());

    [Fact]
    public void SmallPageSize() =>
        Assert.Equal(3, Products.Page(Page(1, 3)).Count());

    [Fact]
    public void LargePageSize() =>
        Assert.Equal(8, Products.Page(Page(1, 100)).Count());

    [Fact]
    public void PageWithOrderTakesTheCheapestThree() =>
        Assert.Equal(["Widget", "Basic", "pro"],
            Products.Order(new OrderBy { Sort = 1, Field = "Price", Direction = Direction.Ascending })
                    .Page(Page(1, 3))
                    .Select(p => p.Name)
                    .ToList());

    [Fact]
    public void SecondPageWithOrderContinuesWhereTheFirstStopped() =>
        Assert.Equal(["Ultra", "Pro", "Laptop Pro"],
            Products.Order(new OrderBy { Sort = 1, Field = "Price", Direction = Direction.Ascending })
                    .Page(Page(2, 3))
                    .Select(p => p.Name)
                    .ToList());

    [Fact]
    public void PageOrders() =>
        Assert.Equal(5, Orders.Page(Page(1, 5)).Count());

    [Fact]
    public void PageCustomersBeyondTheLastRow() =>
        // Only four customers exist, so the second page of five is empty.
        Assert.Empty(Customers.Page(Page(2, 5)).ToList());

    [Fact]
    public void HighPageNumberReturnsNothing() =>
        Assert.Empty(Products.Page(Page(999, 10)).ToList());

    /// <summary>
    /// The offset is a product, and in 32 bits it wraps: a negative offset was the first page again
    /// on SQLite and in memory, and an error on SQL Server and PostgreSQL. A page past the last row is
    /// an empty page however far past it is.
    /// </summary>
    [Theory]
    [InlineData(int.MaxValue, 1000)]
    [InlineData(int.MaxValue, 2)]
    [InlineData(4_294_968, 1000)]
    public void PageNumberWhoseOffsetPassesInt32IsAnEmptyPage(int number, int size)
    {
        Assert.Empty(Products.Page(Page(number, size)).ToList());
        Assert.Empty(SalesSeed.Products().AsQueryable().Page(Page(number, size)).ToList());
    }

    [Fact]
    public void RejectsPageNumberBelowOne()
    {
        var ex = Assert.Throws<LogicException>(() => Products.Page(Page(0, 10)).ToList());

        Assert.Contains("PageNumberMustBeGreaterThanZero", ex.Message);
    }

    [Fact]
    public void RejectsPageSizeBelowOne()
    {
        var ex = Assert.Throws<LogicException>(() => Products.Page(Page(1, 0)).ToList());

        Assert.Contains("PageSizeMustBeGreaterThanZero", ex.Message);
    }
}
