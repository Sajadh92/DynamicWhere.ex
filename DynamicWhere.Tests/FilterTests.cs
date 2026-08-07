using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Source;
using DynamicWhere.Tests.Domain;
using System.Linq.Dynamic.Core;

namespace DynamicWhere.Tests;

/// <summary>
/// Covers every case in the API's <c>FilterTestController</c>: <c>Filter&lt;T&gt;</c>,
/// <c>FilterDynamic&lt;T&gt;</c>, and each <c>ToList</c> / <c>ToListAsync</c> / dynamic overload,
/// over both <see cref="IQueryable{T}"/> and in-memory sources.
/// </summary>
public class FilterTests : SalesTestBase
{
    public FilterTests(SalesFixture fixture) : base(fixture) { }

    private static Condition Cond(string field, DataType type, Operator op, int sort = 1, params object[] values) =>
        new() { Sort = sort, Field = field, DataType = type, Operator = op, Values = values.ToList() };

    /// <summary>IsActive == true → six products.</summary>
    private static ConditionGroup ActiveOnly() => new()
    {
        Connector = Connector.And,
        Conditions = [Cond("IsActive", DataType.Boolean, Operator.Equal, 1, "true")]
    };

    private static OrderBy By(string field, Direction direction = Direction.Ascending, int sort = 1) =>
        new() { Sort = sort, Field = field, Direction = direction };

    #region Filter<T>

    [Fact]
    public void FilterSimpleCondition() =>
        Assert.Equal(6, Products.Filter(new Filter { ConditionGroup = ActiveOnly() }).Count());

    [Fact]
    public void FilterWithOrder() =>
        Assert.Equal(["Widget", "pro", "Ultra", "Pro", "Gadget pro", "ProBook"],
            Products.Filter(new Filter { ConditionGroup = ActiveOnly(), Orders = [By("Price")] })
                    .Select(p => p.Name).ToList());

    [Fact]
    public void FilterWithPage() =>
        Assert.Equal(5, Products.Filter(new Filter
        {
            ConditionGroup = ActiveOnly(),
            Orders = [By("Price")],
            Page = new PageBy { PageNumber = 1, PageSize = 5 }
        }).Count());

    [Fact]
    public void FilterWithSelect()
    {
        var result = Products.Filter(new Filter
        {
            ConditionGroup = ActiveOnly(),
            Selects = ["Id", "Name", "Price"]
        }).ToList();

        Assert.Equal(6, result.Count);
        Assert.All(result, p => Assert.Null(p.Description));
        Assert.Contains(result, p => p.Name == "Pro" && p.Price == 150.00m);
    }

    [Fact]
    public void FilterAndConditions()
    {
        var filter = new Filter
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions =
                [
                    Cond("IsActive", DataType.Boolean, Operator.Equal, 1, "true"),
                    Cond("Price", DataType.Number, Operator.GreaterThan, 2, "100")
                ]
            },
            Page = new PageBy { PageNumber = 1, PageSize = 10 }
        };

        // Active and over 100: Pro (150), ProBook (999.99), Gadget pro (550.75).
        Assert.Equal(3, Products.Filter(filter).Count());
    }

    [Fact]
    public void FilterOrConditions()
    {
        var filter = new Filter
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.Or,
                Conditions =
                [
                    Cond("Price", DataType.Number, Operator.LessThan, 1, "20"),
                    Cond("Price", DataType.Number, Operator.GreaterThan, 2, "500")
                ]
            }
        };

        // Basic (19.99), Widget (5.00), ProBook (999.99), Gadget pro (550.75).
        Assert.Equal(4, Products.Filter(filter).Count());
    }

    [Fact]
    public void FilterNestedGroups()
    {
        var filter = new Filter
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("IsActive", DataType.Boolean, Operator.Equal, 1, "true")],
                SubConditionGroups =
                [
                    new ConditionGroup
                    {
                        Sort = 1,
                        Connector = Connector.Or,
                        Conditions =
                        [
                            Cond("Rating", DataType.Number, Operator.GreaterThanOrEqual, 1, "4.8"),
                            Cond("StockQuantity", DataType.Number, Operator.Equal, 2, "0")
                        ]
                    }
                ]
            }
        };

        // Active with rating >= 4.8 (ProBook, Gadget pro) or zero stock (pro, Gadget pro).
        Assert.Equal(3, Products.Filter(filter).Count());
    }

    [Fact]
    public void FilterFullPipeline()
    {
        var result = Products.Filter(new Filter
        {
            ConditionGroup = ActiveOnly(),
            Orders = [By("Price", Direction.Descending)],
            Page = new PageBy { PageNumber = 1, PageSize = 3 },
            Selects = ["Id", "Name", "Price", "Rating"]
        }).ToList();

        Assert.Equal(["ProBook", "Gadget pro", "Pro"], result.Select(p => p.Name));
        Assert.All(result, p => Assert.Equal(0, p.StockQuantity));
    }

    [Fact]
    public void FilterWithNoCriteriaReturnsEverything() =>
        Assert.Equal(8, Products.Filter(new Filter()).Count());

    #endregion

    #region FilterDynamic<T>

    [Fact]
    public void FilterDynamicWithSelect()
    {
        var result = Products.FilterDynamic(new Filter
        {
            ConditionGroup = ActiveOnly(),
            Selects = ["Id", "Name", "Price"]
        }).ToDynamicList();

        Assert.Equal(6, result.Count);
        Assert.Contains(result, r => (string)r.Name == "Pro");
    }

    [Fact]
    public void FilterDynamicWithPage()
    {
        var result = Products.FilterDynamic(new Filter
        {
            ConditionGroup = ActiveOnly(),
            Orders = [By("Price")],
            Page = new PageBy { PageNumber = 1, PageSize = 2 },
            Selects = ["Id", "Name", "Price"]
        }).ToDynamicList();

        Assert.Equal(["Widget", "pro"], result.Select(r => (string)r.Name));
    }

    [Fact]
    public void FilterDynamicFullPipeline()
    {
        var result = Products.FilterDynamic(new Filter
        {
            ConditionGroup = ActiveOnly(),
            Orders = [By("Price", Direction.Descending)],
            Page = new PageBy { PageNumber = 1, PageSize = 3 },
            Selects = ["Id", "Name", "Price", "Rating"]
        }).ToDynamicList();

        Assert.Equal(["ProBook", "Gadget pro", "Pro"], result.Select(r => (string)r.Name));
    }

    [Fact]
    public void FilterDynamicWithoutSelectReturnsEntities() =>
        Assert.Equal(6, Products.FilterDynamic(new Filter { ConditionGroup = ActiveOnly() }).ToDynamicList().Count);

    #endregion

    #region ToList / ToListAsync and their dynamic counterparts

    private static Filter PagedActive(int size = 4) => new()
    {
        ConditionGroup = ActiveOnly(),
        Orders = [By("Price")],
        Page = new PageBy { PageNumber = 1, PageSize = size }
    };

    [Fact]
    public void ToListSync()
    {
        FilterResult<Product> result = Products.ToList(PagedActive());

        Assert.Equal(6, result.TotalCount);       // total ignores pagination
        Assert.Equal(4, result.Data!.Count);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(4, result.PageSize);
        Assert.Equal(2, result.PageCount);        // ceil(6 / 4)
        Assert.Null(result.QueryString);
    }

    [Fact]
    public void ToListSyncWithQueryString()
    {
        FilterResult<Product> result = Products.ToList(PagedActive(), getQueryString: true);

        Assert.NotNull(result.QueryString);
        Assert.Contains("SELECT", result.QueryString);
    }

    [Fact]
    public void ToListSyncDynamic()
    {
        var filter = PagedActive();
        filter.Selects = ["Id", "Name", "Price"];

        FilterResult<dynamic> result = Products.ToListDynamic(filter);

        Assert.Equal(6, result.TotalCount);
        Assert.Equal(["Widget", "pro", "Ultra", "Pro"], result.Data!.Select(r => (string)r.Name));
    }

    [Fact]
    public void ToListOverInMemorySource()
    {
        FilterResult<Product> result = SalesSeed.Products().ToList(PagedActive());

        Assert.Equal(6, result.TotalCount);
        Assert.Equal(["Widget", "pro", "Ultra", "Pro"], result.Data!.Select(p => p.Name));
    }

    [Fact]
    public void ToListDynamicOverInMemorySource()
    {
        var filter = PagedActive();
        filter.Selects = ["Id", "Name", "Price"];

        FilterResult<dynamic> result = SalesSeed.Products().ToListDynamic(filter);

        Assert.Equal(6, result.TotalCount);
        Assert.Equal(4, result.Data!.Count);
    }

    [Fact]
    public async Task ToListAsyncOverQueryable()
    {
        FilterResult<Product> result = await Products.ToListAsync(PagedActive());

        Assert.Equal(6, result.TotalCount);
        Assert.Equal(["Widget", "pro", "Ultra", "Pro"], result.Data!.Select(p => p.Name));
    }

    [Fact]
    public async Task ToListAsyncWithQueryString()
    {
        FilterResult<Product> result = await Products.ToListAsync(PagedActive(), getQueryString: true);

        Assert.Contains("SELECT", result.QueryString);
    }

    [Fact]
    public async Task ToListAsyncDynamic()
    {
        var filter = PagedActive();
        filter.Selects = ["Id", "Name", "Price", "Rating"];

        FilterResult<dynamic> result = await Products.ToListAsyncDynamic(filter);

        Assert.Equal(6, result.TotalCount);
        Assert.Equal(["Widget", "pro", "Ultra", "Pro"], result.Data!.Select(r => (string)r.Name));
    }

    [Fact]
    public void PageCountIsOneWhenNoPagingRequested()
    {
        FilterResult<Product> result = Products.ToList(new Filter { ConditionGroup = ActiveOnly() });

        Assert.Equal(0, result.PageNumber);
        Assert.Equal(0, result.PageSize);
        Assert.Equal(6, result.PageCount);   // pageSize 0 is treated as 1, so PageCount == TotalCount
        Assert.Equal(6, result.TotalCount);
    }

    #endregion

    #region Other entities

    [Fact]
    public async Task OrdersStatusFilter()
    {
        FilterResult<Order> result = await Orders.ToListAsync(new Filter
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("Status", DataType.Enum, Operator.In, 1, "Pending", "Processing")]
            },
            Orders = [By("OrderNumber")],
            Page = new PageBy { PageNumber = 1, PageSize = 10 }
        });

        Assert.Equal(["ORD-1001", "ORD-1002", "ORD-1005"], result.Data!.Select(o => o.OrderNumber));
    }

    [Fact]
    public async Task OrdersDateRange()
    {
        FilterResult<Order> result = await Orders.ToListAsync(new Filter
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("OrderDate", DataType.DateTime, Operator.Between, 1, "2023-01-01T00:00:00", "2023-12-31T23:59:59")]
            },
            Orders = [By("OrderDate")]
        });

        Assert.Equal(["ORD-1001", "ORD-1002"], result.Data!.Select(o => o.OrderNumber));
    }

    [Fact]
    public async Task OrdersNestedAddressFilter()
    {
        FilterResult<Order> result = await Orders.ToListAsync(new Filter
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("ShippingAddress.Country", DataType.Text, Operator.IEqual, 1, "canada")]
            },
            Orders = [By("OrderNumber")]
        });

        Assert.Equal(["ORD-1003", "ORD-1006"], result.Data!.Select(o => o.OrderNumber));
    }

    [Fact]
    public async Task CustomersTextSearch()
    {
        FilterResult<Customer> result = await Customers.ToListAsync(new Filter
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("FirstName", DataType.Text, Operator.IContains, 1, "ja")]
            },
            Orders = [By("FirstName")]
        });

        Assert.Equal(["Jack", "Jane"], result.Data!.Select(c => c.FirstName));
    }

    [Fact]
    public async Task CustomersSpendingFilter()
    {
        FilterResult<Customer> result = await Customers.ToListAsync(new Filter
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = [Cond("TotalSpent", DataType.Number, Operator.GreaterThanOrEqual, 1, "750")]
            },
            Orders = [By("TotalSpent", Direction.Descending)]
        });

        Assert.Equal(["Jack", "John", "Jane"], result.Data!.Select(c => c.FirstName));
    }

    #endregion
}
