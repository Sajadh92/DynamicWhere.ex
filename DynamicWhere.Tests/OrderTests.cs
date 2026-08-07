using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Source;
using DynamicWhere.Tests.Domain;

namespace DynamicWhere.Tests;

/// <summary>
/// Covers every case in the API's <c>OrderTestController</c>: the single-criterion and
/// multi-criterion <c>Order&lt;T&gt;</c> overloads across entities, plus paths that cross a
/// collection navigation.
/// </summary>
/// <remarks>
/// SQLite's default BINARY collation sorts ordinally, so uppercase names precede "pro".
/// Enums are stored as strings, so ordering by <c>Status</c> or <c>Tier</c> is alphabetical rather
/// than by the underlying enum value.
/// </remarks>
public class OrderTests : SalesTestBase
{
    public OrderTests(SalesFixture fixture) : base(fixture) { }

    private static OrderBy By(string field, Direction direction = Direction.Ascending, int sort = 1) =>
        new() { Sort = sort, Field = field, Direction = direction };

    #region Single criterion

    [Fact]
    public void SingleNameAscending() =>
        Assert.Equal(["Basic", "Gadget pro", "Laptop Pro", "Pro", "ProBook", "Ultra", "Widget", "pro"],
            Products.Order(By("Name")).Select(p => p.Name).ToList());

    [Fact]
    public void SingleNameDescending() =>
        Assert.Equal(["pro", "Widget", "Ultra", "ProBook", "Pro", "Laptop Pro", "Gadget pro", "Basic"],
            Products.Order(By("Name", Direction.Descending)).Select(p => p.Name).ToList());

    [Fact]
    public void SinglePriceAscending() =>
        Assert.Equal(["Widget", "Basic", "pro", "Ultra", "Pro", "Laptop Pro", "Gadget pro", "ProBook"],
            Products.Order(By("Price")).Select(p => p.Name).ToList());

    [Fact]
    public void SinglePriceDescending() =>
        Assert.Equal(["ProBook", "Gadget pro", "Laptop Pro", "Pro", "Ultra", "pro", "Basic", "Widget"],
            Products.Order(By("Price", Direction.Descending)).Select(p => p.Name).ToList());

    [Fact]
    public void SingleDateAscending() =>
        Assert.Equal(["Laptop Pro", "Gadget pro", "pro", "Basic", "ProBook", "Ultra", "Pro", "Widget"],
            Products.Order(By("CreatedAt")).Select(p => p.Name).ToList());

    [Fact]
    public void SingleDateDescending() =>
        Assert.Equal(["Widget", "Pro", "Ultra", "ProBook", "Basic", "pro", "Gadget pro", "Laptop Pro"],
            Products.Order(By("CreatedAt", Direction.Descending)).Select(p => p.Name).ToList());

    [Fact]
    public void SingleBoolean()
    {
        // Ascending puts false first; the two inactive products lead.
        var names = Products.Order(By("IsActive")).Select(p => p.Name).ToList();

        Assert.Equal(["Basic", "Laptop Pro"], names.Take(2).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(8, names.Count);
    }

    [Fact]
    public void SingleNullableDateAscendingPutsNullsFirst()
    {
        var names = Products.Order(By("UpdatedAt")).Select(p => p.Name).ToList();

        // Products 2, 4 and 6 have no UpdatedAt.
        Assert.Equal(["Basic", "Laptop Pro", "pro"], names.Take(3).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void SingleOrdersDateDescending() =>
        Assert.Equal(["ORD-1006", "ORD-1004", "ORD-1003", "ORD-1002", "ORD-1001", "ORD-1005"],
            Orders.Order(By("OrderDate", Direction.Descending)).Select(o => o.OrderNumber).ToList());

    [Fact]
    public void SingleCustomersSpendingDescending() =>
        Assert.Equal(["Jack", "John", "Jane", "Alice"],
            Customers.Order(By("TotalSpent", Direction.Descending)).Select(c => c.FirstName).ToList());

    [Fact]
    public void NestedReferencePath()
    {
        // Category names descending — Smartphones, Laptops, Electronics, Clearance — with Name as a
        // tie-break so products sharing a category have a defined order. "Widget" has no category and
        // sorts last, because SQLite places NULLs last when ordering descending.
        var names = Products.Order([By("Category.Name", Direction.Descending), By("Name", sort: 2)])
                            .Select(p => p.Name)
                            .ToList();

        Assert.Equal(["Pro", "pro", "Laptop Pro", "ProBook", "Gadget pro", "Ultra", "Basic", "Widget"], names);
    }

    #endregion

    #region Multiple criteria

    [Fact]
    public void MultipleTwoFields() =>
        // IsActive descending (active first), then Name ascending.
        Assert.Equal(["Gadget pro", "Pro", "ProBook", "Ultra", "Widget", "pro", "Basic", "Laptop Pro"],
            Products.Order([By("IsActive", Direction.Descending), By("Name", sort: 2)]).Select(p => p.Name).ToList());

    [Fact]
    public void MultipleThreeFields() =>
        // Prices are all distinct, so Price descending decides the whole ordering.
        Assert.Equal(["ProBook", "Gadget pro", "Laptop Pro", "Pro", "Ultra", "pro", "Basic", "Widget"],
            Products.Order(
            [
                By("Price", Direction.Descending),
                By("Rating", Direction.Descending, 2),
                By("Name", sort: 3)
            ]).Select(p => p.Name).ToList());

    [Fact]
    public void MultipleFourFields() =>
        Assert.Equal(["Widget", "Pro", "Ultra", "ProBook", "pro", "Gadget pro", "Basic", "Laptop Pro"],
            Products.Order(
            [
                By("IsActive", Direction.Descending),
                By("CreatedAt", Direction.Descending, 2),
                By("Price", sort: 3),
                By("Name", sort: 4)
            ]).Select(p => p.Name).ToList());

    [Fact]
    public void MultipleOrders() =>
        // Status is stored as text, so ascending is alphabetical: Cancelled, Delivered, Pending, …
        Assert.Equal(["ORD-1006", "ORD-1004", "ORD-1001", "ORD-1005", "ORD-1002", "ORD-1003"],
            Orders.Order([By("Status"), By("TotalAmount", Direction.Descending, 2)]).Select(o => o.OrderNumber).ToList());

    [Fact]
    public void MultipleCustomers() =>
        // Tier descending is alphabetical too: Silver, Gold, Bronze.
        Assert.Equal(["Jane", "Jack", "John", "Alice"],
            Customers.Order([By("Tier", Direction.Descending), By("TotalSpent", Direction.Descending, 2)])
                     .Select(c => c.FirstName).ToList());

    [Fact]
    public void SortValueDecidesPriority()
    {
        // Sort 2 is applied before sort 1's declaration order in the list.
        var names = Products.Order(
        [
            By("Name", Direction.Ascending, sort: 2),
            By("IsActive", Direction.Descending, sort: 1)
        ]).Select(p => p.Name).ToList();

        Assert.Equal(["Gadget pro", "Pro", "ProBook", "Ultra", "Widget", "pro", "Basic", "Laptop Pro"], names);
    }

    [Fact]
    public void EmptyOrderListLeavesQueryUnchanged() =>
        Assert.Equal(8, Products.Order([]).Count());

    #endregion

    #region Paths crossing a collection navigation

    [Fact]
    public void CollectionPathAscendingUsesMinimum() =>
        // Cheapest line item per order; ORD-1005 has none, so its non-nullable decimal defaults to 0.
        Assert.Equal(["ORD-1005", "ORD-1003", "ORD-1006", "ORD-1004", "ORD-1001", "ORD-1002"],
            Orders.Order(By("OrderItems.UnitPrice")).Select(o => o.OrderNumber).ToList());

    [Fact]
    public void CollectionPathDescendingUsesMaximum() =>
        Assert.Equal(["ORD-1002", "ORD-1004", "ORD-1001", "ORD-1006", "ORD-1003", "ORD-1005"],
            Orders.Order(By("OrderItems.UnitPrice", Direction.Descending)).Select(o => o.OrderNumber).ToList());

    [Fact]
    public void CollectionPathIntoReferenceNavigation() =>
        // Alphabetically first product name per order; ORD-1005 has no items, so it sorts as null.
        Assert.Equal(["ORD-1005", "ORD-1006", "ORD-1004", "ORD-1001", "ORD-1002", "ORD-1003"],
            Orders.Order(By("OrderItems.Product.Name")).Select(o => o.OrderNumber).ToList());

    #endregion
}
