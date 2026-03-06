using DynamicWhere.API.Data;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Source;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace DynamicWhere.API.Controllers;

/// <summary>
/// Controller for testing Page (PageBy) extension method.
/// Covers: first page, second page, small page size, large page size, page with ordering.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PageTestController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<PageTestController> _logger;

    public PageTestController(AppDbContext context, ILogger<PageTestController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Page: first page (page 1, size 10).
    /// </summary>
    [HttpGet("first")]
    public async Task<ActionResult<PerformanceResult>> TestFirstPage()
    {
        return await RunPageTest("First Page - Page 1, Size 10", _context.Products,
            new PageBy { PageNumber = 1, PageSize = 10 });
    }

    /// <summary>
    /// Page: second page (page 2, size 10).
    /// </summary>
    [HttpGet("second")]
    public async Task<ActionResult<PerformanceResult>> TestSecondPage()
    {
        return await RunPageTest("Second Page - Page 2, Size 10", _context.Products,
            new PageBy { PageNumber = 2, PageSize = 10 });
    }

    /// <summary>
    /// Page: small page size (page 1, size 3).
    /// </summary>
    [HttpGet("small")]
    public async Task<ActionResult<PerformanceResult>> TestSmallPageSize()
    {
        return await RunPageTest("Small Page Size - Page 1, Size 3", _context.Products,
            new PageBy { PageNumber = 1, PageSize = 3 });
    }

    /// <summary>
    /// Page: large page size (page 1, size 100).
    /// </summary>
    [HttpGet("large")]
    public async Task<ActionResult<PerformanceResult>> TestLargePageSize()
    {
        return await RunPageTest("Large Page Size - Page 1, Size 100", _context.Products,
            new PageBy { PageNumber = 1, PageSize = 100 });
    }

    /// <summary>
    /// Page: with ordering applied before pagination.
    /// </summary>
    [HttpGet("with-order")]
    public async Task<ActionResult<PerformanceResult>> TestPageWithOrder()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var order = new OrderBy { Sort = 1, Field = "Price", Direction = Direction.Descending };
            var page = new PageBy { PageNumber = 1, PageSize = 10 };

            sw.Restart();
            var query = _context.Products.Order(order).Page(page);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.ToListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Page With Order - Price DESC, Page 1, Size 10",
                Metrics = metrics,
                Input = new { Order = order, Page = page },
                Output = results, Success = true,
                Message = "Paging after ordering by Price DESC"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestPageWithOrder");
            return Ok(new PerformanceResult
            {
                TestName = "Page With Order - Price DESC, Page 1, Size 10",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Page: Orders entity (page 1, size 5).
    /// </summary>
    [HttpGet("orders")]
    public async Task<ActionResult<PerformanceResult>> TestPageOrders()
    {
        return await RunPageTest("Orders - Page 1, Size 5", _context.Orders,
            new PageBy { PageNumber = 1, PageSize = 5 });
    }

    /// <summary>
    /// Page: Customers entity (page 2, size 5).
    /// </summary>
    [HttpGet("customers")]
    public async Task<ActionResult<PerformanceResult>> TestPageCustomers()
    {
        return await RunPageTest("Customers - Page 2, Size 5", _context.Customers,
            new PageBy { PageNumber = 2, PageSize = 5 });
    }

    /// <summary>
    /// Page: high page number (potentially empty result).
    /// </summary>
    [HttpGet("high-page")]
    public async Task<ActionResult<PerformanceResult>> TestHighPageNumber()
    {
        return await RunPageTest("High Page Number - Page 999, Size 10 (likely empty)", _context.Products,
            new PageBy { PageNumber = 999, PageSize = 10 });
    }

    #region Run All Tests

    /// <summary>
    /// Run all Page tests and return aggregated results.
    /// </summary>
    [HttpGet("all")]
    public async Task<ActionResult<AllTestsResult>> TestAll()
    {
        var allResults = new List<PerformanceResult>();
        var overallSw = Stopwatch.StartNew();

        var testMethods = new List<(string name, Func<Task<ActionResult<PerformanceResult>>> method)>
        {
            ("First Page",       TestFirstPage),
            ("Second Page",      TestSecondPage),
            ("Small Page Size",  TestSmallPageSize),
            ("Large Page Size",  TestLargePageSize),
            ("Page With Order",  TestPageWithOrder),
            ("Page Orders",      TestPageOrders),
            ("Page Customers",   TestPageCustomers),
            ("High Page Number", TestHighPageNumber)
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
                _logger.LogError(ex, "Error executing Page test: {name}", name);
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

    #region Helper

    private async Task<ActionResult<PerformanceResult>> RunPageTest<T>(
        string testName, IQueryable<T> source, PageBy page) where T : class
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            sw.Restart();
            var query = source.Page(page);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.ToListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = $"Page - {testName}",
                Metrics = metrics, Input = page, Output = results, Success = true,
                Message = $"Page {page.PageNumber}, Size {page.PageSize} — returned {results.Count} records"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in {testName}", testName);
            return Ok(new PerformanceResult
            {
                TestName = $"Page - {testName}",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    #endregion
}
