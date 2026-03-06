using DynamicWhere.API.Data;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Source;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace DynamicWhere.API.Controllers;

/// <summary>
/// Controller for testing Order (single OrderBy) and Order (list of OrderBy) extension methods.
/// Covers: single ascending, single descending, order by text/number/date, multiple orders.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class OrderTestController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<OrderTestController> _logger;

    public OrderTestController(AppDbContext context, ILogger<OrderTestController> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Order<T>(OrderBy) — Single Order

    /// <summary>
    /// Order: single field ascending (Name A→Z).
    /// </summary>
    [HttpGet("single/name-asc")]
    public async Task<ActionResult<PerformanceResult>> TestSingleNameAsc()
    {
        return await RunOrderTest("Single Name ASC", _context.Products,
            new OrderBy { Sort = 1, Field = "Name", Direction = Direction.Ascending });
    }

    /// <summary>
    /// Order: single field descending (Name Z→A).
    /// </summary>
    [HttpGet("single/name-desc")]
    public async Task<ActionResult<PerformanceResult>> TestSingleNameDesc()
    {
        return await RunOrderTest("Single Name DESC", _context.Products,
            new OrderBy { Sort = 1, Field = "Name", Direction = Direction.Descending });
    }

    /// <summary>
    /// Order: single numeric field ascending (Price low→high).
    /// </summary>
    [HttpGet("single/price-asc")]
    public async Task<ActionResult<PerformanceResult>> TestSinglePriceAsc()
    {
        return await RunOrderTest("Single Price ASC", _context.Products,
            new OrderBy { Sort = 1, Field = "Price", Direction = Direction.Ascending });
    }

    /// <summary>
    /// Order: single numeric field descending (Price high→low).
    /// </summary>
    [HttpGet("single/price-desc")]
    public async Task<ActionResult<PerformanceResult>> TestSinglePriceDesc()
    {
        return await RunOrderTest("Single Price DESC", _context.Products,
            new OrderBy { Sort = 1, Field = "Price", Direction = Direction.Descending });
    }

    /// <summary>
    /// Order: single date field ascending (CreatedAt oldest→newest).
    /// </summary>
    [HttpGet("single/date-asc")]
    public async Task<ActionResult<PerformanceResult>> TestSingleDateAsc()
    {
        return await RunOrderTest("Single CreatedAt ASC", _context.Products,
            new OrderBy { Sort = 1, Field = "CreatedAt", Direction = Direction.Ascending });
    }

    /// <summary>
    /// Order: single date field descending (CreatedAt newest→oldest).
    /// </summary>
    [HttpGet("single/date-desc")]
    public async Task<ActionResult<PerformanceResult>> TestSingleDateDesc()
    {
        return await RunOrderTest("Single CreatedAt DESC", _context.Products,
            new OrderBy { Sort = 1, Field = "CreatedAt", Direction = Direction.Descending });
    }

    /// <summary>
    /// Order: single boolean field (IsActive).
    /// </summary>
    [HttpGet("single/boolean")]
    public async Task<ActionResult<PerformanceResult>> TestSingleBoolean()
    {
        return await RunOrderTest("Single IsActive ASC", _context.Products,
            new OrderBy { Sort = 1, Field = "IsActive", Direction = Direction.Ascending });
    }

    /// <summary>
    /// Order: single field on Orders entity (OrderDate DESC).
    /// </summary>
    [HttpGet("single/orders-date")]
    public async Task<ActionResult<PerformanceResult>> TestSingleOrdersDate()
    {
        return await RunOrderTest("Single Orders OrderDate DESC", _context.Orders,
            new OrderBy { Sort = 1, Field = "OrderDate", Direction = Direction.Descending });
    }

    /// <summary>
    /// Order: single field on Customers entity (TotalSpent DESC).
    /// </summary>
    [HttpGet("single/customers-spending")]
    public async Task<ActionResult<PerformanceResult>> TestSingleCustomersSpending()
    {
        return await RunOrderTest("Single Customers TotalSpent DESC", _context.Customers,
            new OrderBy { Sort = 1, Field = "TotalSpent", Direction = Direction.Descending });
    }

    #endregion

    #region Order<T>(List<OrderBy>) — Multiple Orders

    /// <summary>
    /// Order: two fields — IsActive DESC, then Name ASC.
    /// </summary>
    [HttpGet("multiple/two-fields")]
    public async Task<ActionResult<PerformanceResult>> TestMultipleTwoFields()
    {
        return await RunMultiOrderTest("Multiple Two Fields - IsActive DESC, Name ASC", _context.Products,
        [
            new OrderBy { Sort = 1, Field = "IsActive", Direction = Direction.Descending },
            new OrderBy { Sort = 2, Field = "Name", Direction = Direction.Ascending }
        ]);
    }

    /// <summary>
    /// Order: three fields — Price DESC, Rating DESC, Name ASC.
    /// </summary>
    [HttpGet("multiple/three-fields")]
    public async Task<ActionResult<PerformanceResult>> TestMultipleThreeFields()
    {
        return await RunMultiOrderTest("Multiple Three Fields - Price DESC, Rating DESC, Name ASC", _context.Products,
        [
            new OrderBy { Sort = 1, Field = "Price", Direction = Direction.Descending },
            new OrderBy { Sort = 2, Field = "Rating", Direction = Direction.Descending },
            new OrderBy { Sort = 3, Field = "Name", Direction = Direction.Ascending }
        ]);
    }

    /// <summary>
    /// Order: four fields — IsActive DESC, CreatedAt DESC, Price ASC, Name ASC.
    /// </summary>
    [HttpGet("multiple/four-fields")]
    public async Task<ActionResult<PerformanceResult>> TestMultipleFourFields()
    {
        return await RunMultiOrderTest("Multiple Four Fields - IsActive DESC, CreatedAt DESC, Price ASC, Name ASC", _context.Products,
        [
            new OrderBy { Sort = 1, Field = "IsActive", Direction = Direction.Descending },
            new OrderBy { Sort = 2, Field = "CreatedAt", Direction = Direction.Descending },
            new OrderBy { Sort = 3, Field = "Price", Direction = Direction.Ascending },
            new OrderBy { Sort = 4, Field = "Name", Direction = Direction.Ascending }
        ]);
    }

    /// <summary>
    /// Order: multiple fields on Orders entity — Status ASC, TotalAmount DESC.
    /// </summary>
    [HttpGet("multiple/orders")]
    public async Task<ActionResult<PerformanceResult>> TestMultipleOrders()
    {
        return await RunMultiOrderTest("Multiple Orders - Status ASC, TotalAmount DESC", _context.Orders,
        [
            new OrderBy { Sort = 1, Field = "Status", Direction = Direction.Ascending },
            new OrderBy { Sort = 2, Field = "TotalAmount", Direction = Direction.Descending }
        ]);
    }

    /// <summary>
    /// Order: multiple fields on Customers entity — Tier DESC, TotalSpent DESC.
    /// </summary>
    [HttpGet("multiple/customers")]
    public async Task<ActionResult<PerformanceResult>> TestMultipleCustomers()
    {
        return await RunMultiOrderTest("Multiple Customers - Tier DESC, TotalSpent DESC", _context.Customers,
        [
            new OrderBy { Sort = 1, Field = "Tier", Direction = Direction.Descending },
            new OrderBy { Sort = 2, Field = "TotalSpent", Direction = Direction.Descending }
        ]);
    }

    #endregion

    #region Run All Tests

    /// <summary>
    /// Run all Order tests and return aggregated results.
    /// </summary>
    [HttpGet("all")]
    public async Task<ActionResult<AllTestsResult>> TestAll()
    {
        var allResults = new List<PerformanceResult>();
        var overallSw = Stopwatch.StartNew();

        var testMethods = new List<(string name, Func<Task<ActionResult<PerformanceResult>>> method)>
        {
            // Single order
            ("Single Name ASC",            TestSingleNameAsc),
            ("Single Name DESC",           TestSingleNameDesc),
            ("Single Price ASC",           TestSinglePriceAsc),
            ("Single Price DESC",          TestSinglePriceDesc),
            ("Single Date ASC",            TestSingleDateAsc),
            ("Single Date DESC",           TestSingleDateDesc),
            ("Single Boolean",             TestSingleBoolean),
            ("Single Orders Date",         TestSingleOrdersDate),
            ("Single Customers Spending",  TestSingleCustomersSpending),
            // Multiple orders
            ("Multiple Two Fields",        TestMultipleTwoFields),
            ("Multiple Three Fields",      TestMultipleThreeFields),
            ("Multiple Four Fields",       TestMultipleFourFields),
            ("Multiple Orders",            TestMultipleOrders),
            ("Multiple Customers",         TestMultipleCustomers)
        };

        foreach (var (name, method) in testMethods)
        {
            try
            {
                var result = await method();
                var performanceResult = result.Result switch
                {
                    OkObjectResult ok => ok.Value as PerformanceResult,
                    _ => result.Value
                };
                if (performanceResult != null) allResults.Add(performanceResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing Order test: {name}", name);
                allResults.Add(new PerformanceResult
                {
                    TestName = name, Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
                });
            }
        }

        overallSw.Stop();

        return Ok(new AllTestsResult
        {
            TotalTests = allResults.Count,
            SuccessfulTests = allResults.Count(r => r.Success),
            FailedTests = allResults.Count(r => !r.Success),
            TotalExecutionTimeMs = overallSw.Elapsed.TotalMilliseconds,
            AverageTranslationTimeMs = allResults.Count == 0 ? 0 : allResults.Average(r => r.Metrics.TranslationTimeMs),
            AverageExecutionTimeMs = allResults.Count == 0 ? 0 : allResults.Average(r => r.Metrics.ExecutionTimeMs),
            AverageTotalTimeMs = allResults.Count == 0 ? 0 : allResults.Average(r => r.Metrics.TotalTimeMs),
            TotalRecordsReturned = allResults.Sum(r => r.Metrics.RecordsReturned),
            Results = allResults
        });
    }

    #endregion

    #region Helpers

    private async Task<ActionResult<PerformanceResult>> RunOrderTest<T>(
        string testName, IQueryable<T> source, OrderBy order) where T : class
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            sw.Restart();
            var query = source.Order(order);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(100).ToListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = $"Order - {testName}",
                Metrics = metrics, Input = order, Output = results, Success = true,
                Message = $"Ordered by {order.Field} {order.Direction} — {results.Count} results"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in {testName}", testName);
            return Ok(new PerformanceResult
            {
                TestName = $"Order - {testName}",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    private async Task<ActionResult<PerformanceResult>> RunMultiOrderTest<T>(
        string testName, IQueryable<T> source, List<OrderBy> orders) where T : class
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            sw.Restart();
            var query = source.Order(orders);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(100).ToListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = $"Order - {testName}",
                Metrics = metrics, Input = orders, Output = results, Success = true,
                Message = $"Ordered by {orders.Count} fields — {results.Count} results"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in {testName}", testName);
            return Ok(new PerformanceResult
            {
                TestName = $"Order - {testName}",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    #endregion
}
