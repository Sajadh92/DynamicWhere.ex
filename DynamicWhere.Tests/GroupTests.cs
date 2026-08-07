using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Source;
using DynamicWhere.Tests.Domain;
using System.Linq.Dynamic.Core;

namespace DynamicWhere.Tests;

/// <summary>
/// Covers every case in the API's <c>GroupTestController</c>: <c>Group&lt;T&gt;</c> with each
/// <see cref="Aggregator"/>, over several entities and grouping keys.
/// </summary>
/// <remarks>
/// Products split 6 active / 2 inactive. Active prices: 150.00, 45.50, 999.99, 75.25, 5.00, 550.75.
/// Inactive prices: 250.00, 19.99.
/// </remarks>
public class GroupTests : SalesTestBase
{
    public GroupTests(SalesFixture fixture) : base(fixture) { }

    private static AggregateBy Agg(string alias, Aggregator aggregator, string? field = null) =>
        new() { Alias = alias, Aggregator = aggregator, Field = field };

    private static List<dynamic> Run(IQueryable source, GroupBy groupBy) =>
        ((IQueryable<object>)source).Group(groupBy).ToDynamicList();

    private List<dynamic> GroupProducts(GroupBy groupBy) => Products.Group(groupBy).ToDynamicList();

    private static dynamic Row(List<dynamic> rows, string key, object value) =>
        rows.Single(r => Equals(GetValue(r, key), value));

    private static object? GetValue(object row, string name) =>
        row.GetType().GetProperty(name)!.GetValue(row);

    #region Grouping keys and counts

    [Fact]
    public void GroupSimpleCount()
    {
        var rows = GroupProducts(new GroupBy
        {
            Fields = ["IsActive"],
            AggregateBy = [Agg("TotalCount", Aggregator.Count)]
        });

        Assert.Equal(2, rows.Count);
        Assert.Equal(6, (int)GetValue(Row(rows, "IsActive", true), "TotalCount")!);
        Assert.Equal(2, (int)GetValue(Row(rows, "IsActive", false), "TotalCount")!);
    }

    [Fact]
    public void GroupMultipleAggregations()
    {
        var rows = GroupProducts(new GroupBy
        {
            Fields = ["IsActive"],
            AggregateBy =
            [
                Agg("TotalCount", Aggregator.Count),
                Agg("AvgPrice", Aggregator.Average, "Price"),
                Agg("TotalStock", Aggregator.Sumation, "StockQuantity")
            ]
        });

        dynamic inactive = Row(rows, "IsActive", false);
        Assert.Equal(2, (int)GetValue(inactive, "TotalCount")!);
        Assert.Equal(134.995m, (decimal)GetValue(inactive, "AvgPrice")!, 2);   // (250.00 + 19.99) / 2
        Assert.Equal(300, (int)GetValue(inactive, "TotalStock")!);          // 50 + 250
    }

    [Fact]
    public void GroupAllAggregations()
    {
        var rows = GroupProducts(new GroupBy
        {
            Fields = ["IsActive"],
            AggregateBy =
            [
                Agg("TotalCount", Aggregator.Count),
                Agg("DistinctStockCount", Aggregator.CountDistinct, "StockQuantity"),
                Agg("SumPrice", Aggregator.Sumation, "Price"),
                Agg("AvgPrice", Aggregator.Average, "Price"),
                Agg("MinPrice", Aggregator.Minimum, "Price"),
                Agg("MaxPrice", Aggregator.Maximum, "Price"),
                Agg("FirstName", Aggregator.FirstOrDefault, "Name"),
                Agg("LastName", Aggregator.LastOrDefault, "Name")
            ]
        });

        dynamic active = Row(rows, "IsActive", true);
        Assert.Equal(6, (int)GetValue(active, "TotalCount")!);
        // Active stock quantities are 10, 0, 5, 100, 1, 0 → five distinct values.
        Assert.Equal(5, (int)GetValue(active, "DistinctStockCount")!);
        Assert.Equal(1826.49m, (decimal)GetValue(active, "SumPrice")!, 2);
        Assert.Equal(5.00m, (decimal)GetValue(active, "MinPrice")!);
        Assert.Equal(999.99m, (decimal)GetValue(active, "MaxPrice")!);

        // First/Last order the values, so they return the alphabetical extremes.
        Assert.Equal("Gadget pro", (string)GetValue(active, "FirstName")!);
        Assert.Equal("pro", (string)GetValue(active, "LastName")!);
    }

    [Fact]
    public void GroupCountOnly()
    {
        var rows = GroupProducts(new GroupBy
        {
            Fields = ["IsActive"],
            AggregateBy = [Agg("ItemCount", Aggregator.Count)]
        });

        Assert.Equal(8, rows.Sum(r => (int)GetValue(r, "ItemCount")!));
    }

    [Fact]
    public void GroupCountDistinct()
    {
        var rows = GroupProducts(new GroupBy
        {
            Fields = ["IsActive"],
            AggregateBy = [Agg("UniqueNames", Aggregator.CountDistinct, "Name")]
        });

        Assert.Equal(6, (int)GetValue(Row(rows, "IsActive", true), "UniqueNames")!);
        Assert.Equal(2, (int)GetValue(Row(rows, "IsActive", false), "UniqueNames")!);
    }

    [Fact]
    public void GroupFirstAndLast()
    {
        var rows = GroupProducts(new GroupBy
        {
            Fields = ["IsActive"],
            AggregateBy =
            [
                Agg("FirstProduct", Aggregator.FirstOrDefault, "Name"),
                Agg("LastProduct", Aggregator.LastOrDefault, "Name")
            ]
        });

        dynamic inactive = Row(rows, "IsActive", false);
        Assert.Equal("Basic", (string)GetValue(inactive, "FirstProduct")!);
        Assert.Equal("Laptop Pro", (string)GetValue(inactive, "LastProduct")!);
    }

    [Fact]
    public void GroupRatingAnalysis()
    {
        var rows = GroupProducts(new GroupBy
        {
            Fields = ["IsActive"],
            AggregateBy =
            [
                Agg("MinRating", Aggregator.Minimum, "Rating"),
                Agg("MaxRating", Aggregator.Maximum, "Rating"),
                Agg("AvgRating", Aggregator.Average, "Rating"),
                Agg("ProductCount", Aggregator.Count)
            ]
        });

        dynamic active = Row(rows, "IsActive", true);
        Assert.Equal(3.5, (double)GetValue(active, "MinRating")!);
        Assert.Equal(5.0, (double)GetValue(active, "MaxRating")!);
        Assert.Equal(6, (int)GetValue(active, "ProductCount")!);
    }

    [Fact]
    public void GroupMultipleFields()
    {
        var rows = GroupProducts(new GroupBy
        {
            Fields = ["IsActive", "CategoryId"],
            AggregateBy = [Agg("TotalCount", Aggregator.Count)]
        });

        // Active: Smartphones×2, Laptops×1, Electronics×2, (null)×1. Inactive: Laptops×1, Clearance×1.
        Assert.Equal(6, rows.Count);
        Assert.Equal(8, rows.Sum(r => (int)GetValue(r, "TotalCount")!));
    }

    #endregion

    #region Other entities

    [Fact]
    public void GroupOrdersByStatus()
    {
        var rows = Orders.Group(new GroupBy
        {
            Fields = ["Status"],
            AggregateBy =
            [
                Agg("OrderCount", Aggregator.Count),
                Agg("TotalRevenue", Aggregator.Sumation, "TotalAmount"),
                Agg("AvgOrderValue", Aggregator.Average, "TotalAmount")
            ]
        }).ToDynamicList();

        Assert.Equal(5, rows.Count);

        dynamic pending = Row(rows, "Status", OrderStatus.Pending);
        Assert.Equal(2, (int)GetValue(pending, "OrderCount")!);
        Assert.Equal(300.00m, (decimal)GetValue(pending, "TotalRevenue")!);   // 250.00 + 50.00
        Assert.Equal(150.00m, (decimal)GetValue(pending, "AvgOrderValue")!);
    }

    [Fact]
    public void GroupOrdersByPaymentMethod()
    {
        var rows = Orders.Group(new GroupBy
        {
            Fields = ["PaymentMethod"],
            AggregateBy = [Agg("OrderCount", Aggregator.Count), Agg("TotalRevenue", Aggregator.Sumation, "TotalAmount")]
        }).ToDynamicList();

        Assert.Equal(3, rows.Count);
        Assert.Equal(3, (int)GetValue(Row(rows, "PaymentMethod", PaymentMethod.CreditCard), "OrderCount")!);
        Assert.Equal(2, (int)GetValue(Row(rows, "PaymentMethod", PaymentMethod.PayPal), "OrderCount")!);
        Assert.Equal(1, (int)GetValue(Row(rows, "PaymentMethod", PaymentMethod.BankTransfer), "OrderCount")!);
    }

    [Fact]
    public void GroupOrdersByPaidFlag()
    {
        var rows = Orders.Group(new GroupBy
        {
            Fields = ["IsPaid"],
            AggregateBy = [Agg("OrderCount", Aggregator.Count), Agg("AvgAmount", Aggregator.Average, "TotalAmount")]
        }).ToDynamicList();

        Assert.Equal(3, (int)GetValue(Row(rows, "IsPaid", true), "OrderCount")!);
        Assert.Equal(3, (int)GetValue(Row(rows, "IsPaid", false), "OrderCount")!);
    }

    [Fact]
    public void GroupCustomersByGender()
    {
        var rows = Customers.Group(new GroupBy
        {
            Fields = ["Gender"],
            AggregateBy =
            [
                Agg("CustomerCount", Aggregator.Count),
                Agg("AvgSpending", Aggregator.Average, "TotalSpent"),
                Agg("TotalSpending", Aggregator.Sumation, "TotalSpent")
            ]
        }).ToDynamicList();

        Assert.Equal(2, rows.Count);

        dynamic male = Row(rows, "Gender", Gender.Male);
        Assert.Equal(2, (int)GetValue(male, "CustomerCount")!);
        Assert.Equal(4000.00m, (decimal)GetValue(male, "TotalSpending")!);   // 1500.00 + 2500.00
        Assert.Equal(2000.00m, (decimal)GetValue(male, "AvgSpending")!);
    }

    [Fact]
    public void GroupCustomersByTier()
    {
        var rows = Customers.Group(new GroupBy
        {
            Fields = ["Tier"],
            AggregateBy =
            [
                Agg("CustomerCount", Aggregator.Count),
                Agg("MaxSpent", Aggregator.Maximum, "TotalSpent"),
                Agg("MinSpent", Aggregator.Minimum, "TotalSpent")
            ]
        }).ToDynamicList();

        Assert.Equal(3, rows.Count);

        dynamic gold = Row(rows, "Tier", CustomerTier.Gold);
        Assert.Equal(2, (int)GetValue(gold, "CustomerCount")!);
        Assert.Equal(2500.00m, (decimal)GetValue(gold, "MaxSpent")!);
        Assert.Equal(1500.00m, (decimal)GetValue(gold, "MinSpent")!);
    }

    #endregion

    #region GroupBy validation

    [Fact]
    public void GroupRejectsEmptyFields()
    {
        var ex = Assert.Throws<LogicException>(() =>
            GroupProducts(new GroupBy { Fields = [], AggregateBy = [Agg("C", Aggregator.Count)] }));

        Assert.Contains("GroupByMustHasAtLeastOneField", ex.Message);
    }

    [Fact]
    public void GroupRejectsDuplicateFields()
    {
        var ex = Assert.Throws<LogicException>(() =>
            GroupProducts(new GroupBy { Fields = ["IsActive", "isactive"], AggregateBy = [Agg("C", Aggregator.Count)] }));

        Assert.Contains("GroupByFieldsMustBeUnique", ex.Message);
    }

    [Fact]
    public void GroupRejectsCollectionField()
    {
        var ex = Assert.Throws<LogicException>(() =>
            GroupProducts(new GroupBy { Fields = ["Reviews"], AggregateBy = [Agg("C", Aggregator.Count)] }));

        // Reported as a complex type, not a collection: the field-type resolver unwraps a collection
        // to its element type before the checks run, so GroupByFieldCannotBeCollection never fires for
        // a collection navigation. Either way the field is rejected.
        Assert.Contains("GroupByFieldCannotBeComplexType", ex.Message);
    }

    [Fact]
    public void GroupRejectsComplexField()
    {
        var ex = Assert.Throws<LogicException>(() =>
            GroupProducts(new GroupBy { Fields = ["Category"], AggregateBy = [Agg("C", Aggregator.Count)] }));

        Assert.Contains("GroupByFieldCannotBeComplexType", ex.Message);
    }

    [Fact]
    public void GroupRejectsDuplicateAliases()
    {
        var ex = Assert.Throws<LogicException>(() => GroupProducts(new GroupBy
        {
            Fields = ["IsActive"],
            AggregateBy = [Agg("Same", Aggregator.Count), Agg("Same", Aggregator.Average, "Price")]
        }));

        Assert.Contains("AggregationAliasesMustBeUnique", ex.Message);
    }

    [Fact]
    public void GroupRejectsAliasClashingWithGroupField()
    {
        var ex = Assert.Throws<LogicException>(() => GroupProducts(new GroupBy
        {
            Fields = ["IsActive"],
            AggregateBy = [Agg("IsActive", Aggregator.Count)]
        }));

        Assert.Contains("CannotBeUsedInGroupByFields", ex.Message);
    }

    [Fact]
    public void GroupRejectsSumOnNonNumericField()
    {
        var ex = Assert.Throws<LogicException>(() => GroupProducts(new GroupBy
        {
            Fields = ["IsActive"],
            AggregateBy = [Agg("Bad", Aggregator.Sumation, "Name")]
        }));

        Assert.Contains("IsNotSupportedForFieldType", ex.Message);
    }

    [Fact]
    public void GroupRejectsAggregationOverCollection()
    {
        var ex = Assert.Throws<LogicException>(() => GroupProducts(new GroupBy
        {
            Fields = ["IsActive"],
            AggregateBy = [Agg("Bad", Aggregator.Maximum, "Reviews")]
        }));

        // Same unwrapping as above — surfaced as "must be a simple type".
        Assert.Contains("AggregationFieldMustBeSimpleType", ex.Message);
    }

    [Fact]
    public void GroupRejectsMissingAliasField()
    {
        var ex = Assert.Throws<LogicException>(() => GroupProducts(new GroupBy
        {
            Fields = ["IsActive"],
            AggregateBy = [Agg("", Aggregator.Count)]
        }));

        Assert.Contains("AggregationMustHasValidAlias", ex.Message);
    }

    #endregion
}
