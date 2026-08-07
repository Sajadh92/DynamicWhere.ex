using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Source;
using DynamicWhere.Tests.Domain;
using System.Linq.Dynamic.Core;

namespace DynamicWhere.Tests;

/// <summary>
/// Covers every case in the API's <c>SummaryTestController</c> and the <c>having/*</c> cases in the
/// <c>GroupTestController</c>: the where → group → having → order → page pipeline.
/// </summary>
/// <remarks>
/// Grouped by IsActive: active has 6 products summing to 1826.49 (avg 304.415, min 5.00, max 999.99);
/// inactive has 2 summing to 269.99 (avg 134.995).
/// </remarks>
public class SummaryTests : SalesTestBase
{
    public SummaryTests(SalesFixture fixture) : base(fixture) { }

    private static AggregateBy Agg(string alias, Aggregator aggregator, string? field = null) =>
        new() { Alias = alias, Aggregator = aggregator, Field = field };

    private static Condition Cond(string field, DataType type, Operator op, int sort = 1, params object[] values) =>
        new() { Sort = sort, Field = field, DataType = type, Operator = op, Values = values.ToList() };

    private static object? GetValue(object row, string name) =>
        row.GetType().GetProperty(name)!.GetValue(row);

    private static dynamic Row(IEnumerable<dynamic> rows, bool isActive) =>
        rows.Single(r => Equals(GetValue(r, "IsActive"), isActive));

    /// <summary>Group products by IsActive with a count and an average price.</summary>
    private static GroupBy CountAndAverage() => new()
    {
        Fields = ["IsActive"],
        AggregateBy = [Agg("ProductCount", Aggregator.Count), Agg("AvgPrice", Aggregator.Average, "Price")]
    };

    #region Summary<T>

    [Fact]
    public void SummarySimple()
    {
        var rows = Products.Summary(new Summary { GroupBy = CountAndAverage() }).ToDynamicList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(6, (int)GetValue(Row(rows, true), "ProductCount")!);
        Assert.Equal(304.415m, (decimal)GetValue(Row(rows, true), "AvgPrice")!, 2);
        Assert.Equal(134.995m, (decimal)GetValue(Row(rows, false), "AvgPrice")!, 2);
    }

    [Fact]
    public void SummaryWithFilter()
    {
        var rows = Products.Summary(new Summary
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("Price", DataType.Number, Operator.GreaterThan, 1, "100")]
            },
            GroupBy = new GroupBy
            {
                Fields = ["IsActive"],
                AggregateBy = [Agg("ProductCount", Aggregator.Count), Agg("TotalRevenue", Aggregator.Sumation, "Price")]
            }
        }).ToDynamicList();

        // Over 100: Pro, ProBook, Gadget pro (active) and Laptop Pro (inactive).
        Assert.Equal(3, (int)GetValue(Row(rows, true), "ProductCount")!);
        Assert.Equal(1, (int)GetValue(Row(rows, false), "ProductCount")!);
        Assert.Equal(250.00m, (decimal)GetValue(Row(rows, false), "TotalRevenue")!, 2);
    }

    [Fact]
    public void SummaryWithOrder()
    {
        var rows = Products.Summary(new Summary
        {
            GroupBy = CountAndAverage(),
            Orders = [new OrderBy { Sort = 1, Field = "ProductCount", Direction = Direction.Ascending }]
        }).ToDynamicList();

        // Ascending by count puts the two-product inactive group first.
        Assert.Equal(2, (int)GetValue(rows[0], "ProductCount")!);
        Assert.Equal(6, (int)GetValue(rows[1], "ProductCount")!);
    }

    [Fact]
    public void SummaryWithPage()
    {
        var rows = Products.Summary(new Summary
        {
            GroupBy = new GroupBy { Fields = ["IsActive"], AggregateBy = [Agg("ProductCount", Aggregator.Count)] },
            Orders = [new OrderBy { Sort = 1, Field = "ProductCount", Direction = Direction.Descending }],
            Page = new PageBy { PageNumber = 1, PageSize = 1 }
        }).ToDynamicList();

        Assert.Single(rows);
        Assert.Equal(6, (int)GetValue(rows[0], "ProductCount")!);
    }

    [Fact]
    public void SummaryAllAggregations()
    {
        var rows = Products.Summary(new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = ["IsActive"],
                AggregateBy =
                [
                    Agg("TotalCount", Aggregator.Count),
                    Agg("UniqueStockCount", Aggregator.CountDistinct, "StockQuantity"),
                    Agg("SumPrice", Aggregator.Sumation, "Price"),
                    Agg("AvgPrice", Aggregator.Average, "Price"),
                    Agg("MinPrice", Aggregator.Minimum, "Price"),
                    Agg("MaxPrice", Aggregator.Maximum, "Price"),
                    Agg("FirstProduct", Aggregator.FirstOrDefault, "Name"),
                    Agg("LastProduct", Aggregator.LastOrDefault, "Name")
                ]
            }
        }).ToDynamicList();

        dynamic active = Row(rows, true);
        Assert.Equal(6, (int)GetValue(active, "TotalCount")!);
        Assert.Equal(5, (int)GetValue(active, "UniqueStockCount")!);
        Assert.Equal(1826.49m, (decimal)GetValue(active, "SumPrice")!, 2);
        Assert.Equal(5.00m, (decimal)GetValue(active, "MinPrice")!, 2);
        Assert.Equal(999.99m, (decimal)GetValue(active, "MaxPrice")!, 2);
        Assert.Equal("Gadget pro", (string)GetValue(active, "FirstProduct")!);
        Assert.Equal("pro", (string)GetValue(active, "LastProduct")!);
    }

    [Fact]
    public void SummaryNestedFilter()
    {
        var rows = Products.Summary(new Summary
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("StockQuantity", DataType.Number, Operator.GreaterThan, 1, "0")],
                SubConditionGroups =
                [
                    new ConditionGroup
                    {
                        Sort = 1,
                        Connector = Connector.Or,
                        Conditions =
                        [
                            Cond("Rating", DataType.Number, Operator.GreaterThanOrEqual, 1, "4.0"),
                            Cond("Price", DataType.Number, Operator.LessThan, 2, "20")
                        ]
                    }
                ]
            },
            GroupBy = CountAndAverage()
        }).ToDynamicList();

        // Stock > 0: Pro(10), ProBook(5), Laptop Pro(50), Ultra(100), Basic(250), Widget(1).
        // Of those, rating >= 4.0 or price < 20 keeps Pro (4.5), ProBook (4.8), Ultra (4.0) and
        // Widget (5.00) among the active ones, and Basic (19.99) among the inactive ones.
        // Laptop Pro is dropped: rating 2.5 and price 250.
        Assert.Equal(4, (int)GetValue(Row(rows, true), "ProductCount")!);
        Assert.Equal(1, (int)GetValue(Row(rows, false), "ProductCount")!);
    }

    [Fact]
    public void SummaryFullPipeline()
    {
        var rows = Products.Summary(new Summary
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("Price", DataType.Number, Operator.GreaterThan, 1, "10")]
            },
            GroupBy = new GroupBy
            {
                Fields = ["IsActive"],
                AggregateBy =
                [
                    Agg("ProductCount", Aggregator.Count),
                    Agg("AvgPrice", Aggregator.Average, "Price"),
                    Agg("TotalValue", Aggregator.Sumation, "Price")
                ]
            },
            Having = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("ProductCount", DataType.Number, Operator.GreaterThan, 1, "2")]
            },
            Orders = [new OrderBy { Sort = 1, Field = "TotalValue", Direction = Direction.Descending }],
            Page = new PageBy { PageNumber = 1, PageSize = 5 }
        }).ToDynamicList();

        // Price > 10 drops Widget (5.00) from the active group, leaving 5 active and 2 inactive.
        // Having keeps only groups with more than 2 rows.
        Assert.Single(rows);
        Assert.Equal(5, (int)GetValue(rows[0], "ProductCount")!);
    }

    #endregion

    #region Having

    [Fact]
    public void HavingSimple()
    {
        var rows = Products.Summary(new Summary
        {
            GroupBy = CountAndAverage(),
            Having = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("ProductCount", DataType.Number, Operator.GreaterThan, 1, "3")]
            }
        }).ToDynamicList();

        Assert.Single(rows);
        Assert.True((bool)GetValue(rows[0], "IsActive")!);
    }

    [Fact]
    public void HavingWithFilter()
    {
        var rows = Products.Summary(new Summary
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("Price", DataType.Number, Operator.LessThan, 1, "300")]
            },
            GroupBy = CountAndAverage(),
            Having = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("ProductCount", DataType.Number, Operator.GreaterThanOrEqual, 1, "2")]
            }
        }).ToDynamicList();

        // Under 300: active Pro, pro, Ultra, Widget (4); inactive Laptop Pro, Basic (2). Both qualify.
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void HavingAnd()
    {
        var rows = Products.Summary(new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = ["IsActive"],
                AggregateBy =
                [
                    Agg("ProductCount", Aggregator.Count),
                    Agg("AvgPrice", Aggregator.Average, "Price"),
                    Agg("MaxPrice", Aggregator.Maximum, "Price")
                ]
            },
            Having = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions =
                [
                    Cond("ProductCount", DataType.Number, Operator.GreaterThan, 1, "1"),
                    Cond("AvgPrice", DataType.Number, Operator.GreaterThan, 2, "200")
                ]
            }
        }).ToDynamicList();

        // Active averages 304.415 and qualifies; inactive averages 134.995 and does not.
        Assert.Single(rows);
        Assert.True((bool)GetValue(rows[0], "IsActive")!);
    }

    [Fact]
    public void HavingNestedGroup()
    {
        var rows = Products.Summary(new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = ["IsActive"],
                AggregateBy =
                [
                    Agg("ProductCount", Aggregator.Count),
                    Agg("AvgPrice", Aggregator.Average, "Price"),
                    Agg("MinPrice", Aggregator.Minimum, "Price")
                ]
            },
            Having = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("ProductCount", DataType.Number, Operator.GreaterThanOrEqual, 1, "2")],
                SubConditionGroups =
                [
                    new ConditionGroup
                    {
                        Sort = 1,
                        Connector = Connector.Or,
                        Conditions =
                        [
                            Cond("MinPrice", DataType.Number, Operator.LessThan, 1, "10"),
                            Cond("AvgPrice", DataType.Number, Operator.GreaterThan, 2, "1000")
                        ]
                    }
                ]
            }
        }).ToDynamicList();

        // Only the active group has a minimum price under 10 (Widget at 5.00).
        Assert.Single(rows);
        Assert.True((bool)GetValue(rows[0], "IsActive")!);
    }

    [Fact]
    public void HavingOnOrders()
    {
        var rows = Orders.Summary(new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = ["Status"],
                AggregateBy = [Agg("OrderCount", Aggregator.Count), Agg("TotalRevenue", Aggregator.Sumation, "TotalAmount")]
            },
            Having = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("OrderCount", DataType.Number, Operator.GreaterThan, 1, "1")]
            }
        }).ToDynamicList();

        // Only Pending has more than one order.
        Assert.Single(rows);
        Assert.Equal(OrderStatus.Pending, GetValue(rows[0], "Status"));
    }

    [Fact]
    public void HavingOnCustomers()
    {
        var rows = Customers.Summary(new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = ["Tier"],
                AggregateBy = [Agg("CustomerCount", Aggregator.Count), Agg("AvgSpending", Aggregator.Average, "TotalSpent")]
            },
            Having = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("AvgSpending", DataType.Number, Operator.GreaterThan, 1, "500")]
            }
        }).ToDynamicList();

        // Gold averages 2000 and Silver 750.50; Bronze at 120 is filtered out.
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void HavingRejectsFieldThatIsNotAnAlias()
    {
        var ex = Assert.Throws<LogicException>(() => Products.Summary(new Summary
        {
            GroupBy = CountAndAverage(),
            Having = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("Price", DataType.Number, Operator.GreaterThan, 1, "10")]
            }
        }).ToDynamicList());

        Assert.Contains("MustExistInAggregateByAliases", ex.Message);
    }

    #endregion

    #region ToList / ToListAsync over Summary

    [Fact]
    public void SummaryToListSync()
    {
        SummaryResult result = Products.ToList(new Summary
        {
            GroupBy = CountAndAverage(),
            Orders = [new OrderBy { Sort = 1, Field = "ProductCount", Direction = Direction.Descending }],
            Page = new PageBy { PageNumber = 1, PageSize = 1 }
        });

        Assert.Equal(2, result.TotalCount);      // groups before pagination
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(1, result.PageSize);
        Assert.Equal(2, result.PageCount);
        Assert.Single(result.Data!);
        Assert.Null(result.QueryString);
    }

    [Fact]
    public void SummaryToListSyncWithQueryString()
    {
        SummaryResult result = Products.ToList(new Summary
        {
            GroupBy = new GroupBy { Fields = ["IsActive"], AggregateBy = [Agg("ProductCount", Aggregator.Count)] }
        }, getQueryString: true);

        Assert.Contains("SELECT", result.QueryString);
    }

    [Fact]
    public void SummaryToListOverInMemorySource()
    {
        SummaryResult result = SalesSeed.Products().ToList(new Summary { GroupBy = CountAndAverage() });

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Data!.Count);
    }

    [Fact]
    public async Task SummaryToListAsync()
    {
        SummaryResult result = await Products.ToListAsync(new Summary
        {
            GroupBy = CountAndAverage(),
            Orders = [new OrderBy { Sort = 1, Field = "ProductCount", Direction = Direction.Descending }]
        });

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(6, (int)GetValue(result.Data![0], "ProductCount")!);
    }

    [Fact]
    public async Task SummaryToListAsyncWithQueryString()
    {
        SummaryResult result = await Products.ToListAsync(new Summary
        {
            GroupBy = new GroupBy { Fields = ["IsActive"], AggregateBy = [Agg("ProductCount", Aggregator.Count)] }
        }, getQueryString: true);

        Assert.Contains("SELECT", result.QueryString);
    }

    #endregion

    #region Other entities

    [Fact]
    public void SummaryOrdersByStatus()
    {
        var rows = Orders.Summary(new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = ["Status"],
                AggregateBy =
                [
                    Agg("OrderCount", Aggregator.Count),
                    Agg("TotalRevenue", Aggregator.Sumation, "TotalAmount"),
                    Agg("AvgOrderValue", Aggregator.Average, "TotalAmount")
                ]
            }
        }).ToDynamicList();

        Assert.Equal(5, rows.Count);
        Assert.Equal(1901.00m, rows.Sum(r => (decimal)GetValue(r, "TotalRevenue")!), 2);
    }

    [Fact]
    public void SummaryOrdersByPaymentMethod()
    {
        var rows = Orders.Summary(new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = ["PaymentMethod"],
                AggregateBy = [Agg("OrderCount", Aggregator.Count), Agg("TotalRevenue", Aggregator.Sumation, "TotalAmount")]
            }
        }).ToDynamicList();

        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public void SummaryOrdersByPaidFlag()
    {
        var rows = Orders.Summary(new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = ["IsPaid"],
                AggregateBy = [Agg("OrderCount", Aggregator.Count), Agg("AvgAmount", Aggregator.Average, "TotalAmount")]
            }
        }).ToDynamicList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(550.00m, (decimal)GetValue(rows.Single(r => (bool)GetValue(r, "IsPaid")!), "AvgAmount")!, 2);
    }

    [Fact]
    public void SummaryCustomersByGender()
    {
        var rows = Customers.Summary(new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = ["Gender"],
                AggregateBy = [Agg("CustomerCount", Aggregator.Count), Agg("AvgSpending", Aggregator.Average, "TotalSpent")]
            }
        }).ToDynamicList();

        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public void SummaryCustomersByTier()
    {
        var rows = Customers.Summary(new Summary
        {
            GroupBy = new GroupBy
            {
                Fields = ["Tier"],
                AggregateBy =
                [
                    Agg("CustomerCount", Aggregator.Count),
                    Agg("TotalSpending", Aggregator.Sumation, "TotalSpent"),
                    Agg("MaxSpent", Aggregator.Maximum, "TotalSpent")
                ]
            }
        }).ToDynamicList();

        Assert.Equal(3, rows.Count);
        Assert.Equal(4870.50m, rows.Sum(r => (decimal)GetValue(r, "TotalSpending")!), 2);
    }

    #endregion

    #region Summary validation

    [Fact]
    public void SummaryRejectsOrderFieldThatIsNeitherGroupFieldNorAlias()
    {
        var ex = Assert.Throws<LogicException>(() => Products.Summary(new Summary
        {
            GroupBy = CountAndAverage(),
            Orders = [new OrderBy { Sort = 1, Field = "Price" }]
        }).ToDynamicList());

        Assert.Contains("MustExistInGroupByFieldsOrAggregateByAliases", ex.Message);
    }

    [Fact]
    public void SummaryRequiresGroupBy() =>
        Assert.Throws<ArgumentNullException>(() => Products.Summary(new Summary()).ToDynamicList());

    [Fact]
    public void SummaryAllowsOrderingByAGroupField()
    {
        var rows = Products.Summary(new Summary
        {
            GroupBy = CountAndAverage(),
            Orders = [new OrderBy { Sort = 1, Field = "IsActive", Direction = Direction.Descending }]
        }).ToDynamicList();

        Assert.True((bool)GetValue(rows[0], "IsActive")!);
    }

    #endregion
}
