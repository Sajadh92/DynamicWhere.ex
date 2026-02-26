using DynamicWhere.API.Data;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Source;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Linq.Dynamic.Core;

namespace DynamicWhere.API.Controllers;

/// <summary>
/// Controller for testing all Summary extension methods with multiple scenarios.
/// Covers: Summary (IQueryable), ToList sync (IQueryable), ToList in-memory (IEnumerable), ToListAsync.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SummaryTestController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<SummaryTestController> _logger;

    public SummaryTestController(AppDbContext context, ILogger<SummaryTestController> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Test Summary (IQueryable) Extension

    /// <summary>
    /// Test Summary extension: simple group by single field with count.
    /// </summary>
    [HttpGet("summary/simple")]
    public async Task<ActionResult<PerformanceResult>> TestSummarySimple()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = ["IsActive"],
                    AggregateBy =
                    [
                        new AggregateBy { Alias = "TotalCount", Aggregator = Aggregator.Count }
                    ]
                }
            };

            sw.Restart();
            var query = _context.Products.Summary(summary);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Summary Simple - Group by IsActive with Count",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = "Grouped products by IsActive status"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSummarySimple");
            return Ok(new PerformanceResult
            {
                TestName = "Summary Simple - Group by IsActive with Count",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Summary extension: group with ConditionGroup filter applied before grouping.
    /// </summary>
    [HttpGet("summary/with-filter")]
    public async Task<ActionResult<PerformanceResult>> TestSummaryWithFilter()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition
                        {
                            Field = "IsActive",
                            DataType = DataType.Boolean,
                            Operator = Operator.Equal,
                            Values = ["true"],
                            Sort = 1
                        }
                    ]
                },
                GroupBy = new GroupBy
                {
                    Fields = ["CategoryId"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",    Alias = "ProductCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price", Alias = "TotalRevenue", Aggregator = Aggregator.Sumation }
                    ]
                }
            };

            sw.Restart();
            var query = _context.Products.Summary(summary);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Summary With Filter - Active Products by Category",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = "Filtered to active products, grouped by category with count and revenue"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSummaryWithFilter");
            return Ok(new PerformanceResult
            {
                TestName = "Summary With Filter - Active Products by Category",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Summary extension: group with OrderBy applied on grouped result aliases.
    /// </summary>
    [HttpGet("summary/with-order")]
    public async Task<ActionResult<PerformanceResult>> TestSummaryWithOrder()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = ["CategoryId"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",    Alias = "ProductCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price", Alias = "AveragePrice", Aggregator = Aggregator.Average }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "ProductCount", Direction = Direction.Descending, Sort = 1 },
                    new OrderBy { Field = "AveragePrice", Direction = Direction.Descending, Sort = 2 }
                ]
            };

            sw.Restart();
            var query = _context.Products.Summary(summary);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Summary With Order - Category Summary Sorted by Count then Avg Price",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = "Grouped by category, ordered by product count and average price descending"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSummaryWithOrder");
            return Ok(new PerformanceResult
            {
                TestName = "Summary With Order - Category Summary Sorted by Count then Avg Price",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Summary extension: group with pagination applied on grouped results.
    /// </summary>
    [HttpGet("summary/with-page")]
    public async Task<ActionResult<PerformanceResult>> TestSummaryWithPage()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = ["CategoryId"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id", Alias = "ProductCount", Aggregator = Aggregator.Count }
                    ]
                },
                Page = new PageBy { PageNumber = 1, PageSize = 3 }
            };

            sw.Restart();
            var query = _context.Products.Summary(summary);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Summary With Page - First Page of Grouped Results (Size 3)",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = "Grouped by category with page 1, size 3"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSummaryWithPage");
            return Ok(new PerformanceResult
            {
                TestName = "Summary With Page - First Page of Grouped Results (Size 3)",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Summary extension: group with all supported aggregation types in one request.
    /// </summary>
    [HttpGet("summary/all-aggregations")]
    public async Task<ActionResult<PerformanceResult>> TestSummaryAllAggregations()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = ["CategoryId"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",         Alias = "ProductCount",      Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price",      Alias = "TotalRevenue",      Aggregator = Aggregator.Sumation },
                        new AggregateBy { Field = "Price",      Alias = "AveragePrice",      Aggregator = Aggregator.Average },
                        new AggregateBy { Field = "Price",      Alias = "MinPrice",          Aggregator = Aggregator.Minimum },
                        new AggregateBy { Field = "Price",      Alias = "MaxPrice",          Aggregator = Aggregator.Maximum },
                        new AggregateBy { Field = "Rating",     Alias = "AverageRating",     Aggregator = Aggregator.Average },
                        new AggregateBy { Field = "CategoryId", Alias = "UniqueCategories",  Aggregator = Aggregator.CountDistinct },
                        new AggregateBy { Field = "Name",       Alias = "FirstProduct",      Aggregator = Aggregator.FirstOrDefault },
                        new AggregateBy { Field = "Name",       Alias = "LastProduct",       Aggregator = Aggregator.LastOrDefault }
                    ]
                }
            };

            sw.Restart();
            var query = _context.Products.Summary(summary);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Summary All Aggregations - Count, Sum, Avg, Min, Max, Distinct, First, Last",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = "Tested all aggregation types in a single summary request"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSummaryAllAggregations");
            return Ok(new PerformanceResult
            {
                TestName = "Summary All Aggregations - Count, Sum, Avg, Min, Max, Distinct, First, Last",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Summary extension: group by date sub-fields (Year, Month).
    /// </summary>
    [HttpGet("summary/date-grouping")]
    public async Task<ActionResult<PerformanceResult>> TestSummaryDateGrouping()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = ["CreatedAt.Year", "CreatedAt.Month"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",    Alias = "ProductCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price", Alias = "TotalValue",   Aggregator = Aggregator.Sumation },
                        new AggregateBy { Field = "Price", Alias = "AveragePrice", Aggregator = Aggregator.Average }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "CreatedAt.Year",  Direction = Direction.Descending, Sort = 1 },
                    new OrderBy { Field = "CreatedAt.Month", Direction = Direction.Descending, Sort = 2 }
                ]
            };

            sw.Restart();
            var query = _context.Products.Summary(summary);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Summary Date Grouping - Products by CreatedAt Year and Month",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = "Grouped products by creation year and month, ordered most recent first"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSummaryDateGrouping");
            return Ok(new PerformanceResult
            {
                TestName = "Summary Date Grouping - Products by CreatedAt Year and Month",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Summary extension: group by a nested navigation property (Category.Name).
    /// </summary>
    [HttpGet("summary/nested-property")]
    public async Task<ActionResult<PerformanceResult>> TestSummaryNestedProperty()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = ["Category.Name"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",     Alias = "ProductCount",  Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price",  Alias = "AveragePrice",  Aggregator = Aggregator.Average },
                        new AggregateBy { Field = "Rating", Alias = "AverageRating", Aggregator = Aggregator.Average }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "ProductCount", Direction = Direction.Descending, Sort = 1 }
                ]
            };

            sw.Restart();
            var query = _context.Products.Include(p => p.Category).Summary(summary);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Summary Nested Property - Group by Category.Name",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = "Grouped by nested Category.Name with avg price and rating, ordered by product count"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSummaryNestedProperty");
            return Ok(new PerformanceResult
            {
                TestName = "Summary Nested Property - Group by Category.Name",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Summary extension: full pipeline — filter + multi-field group + order + page.
    /// </summary>
    [HttpGet("summary/full-pipeline")]
    public async Task<ActionResult<PerformanceResult>> TestSummaryFullPipeline()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition
                        {
                            Field = "Price",
                            DataType = DataType.Number,
                            Operator = Operator.GreaterThan,
                            Values = ["10"],
                            Sort = 1
                        }
                    ]
                },
                GroupBy = new GroupBy
                {
                    Fields = ["IsActive", "CategoryId"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",            Alias = "Count",      Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price",         Alias = "TotalValue", Aggregator = Aggregator.Sumation },
                        new AggregateBy { Field = "StockQuantity", Alias = "TotalStock", Aggregator = Aggregator.Sumation },
                        new AggregateBy { Field = "Rating",        Alias = "AvgRating",  Aggregator = Aggregator.Average }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "TotalValue", Direction = Direction.Descending, Sort = 1 }
                ],
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var query = _context.Products.Summary(summary);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Summary Full Pipeline - Filter + Multi-Field Group + Order + Page",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = "Multi-field grouping by IsActive and CategoryId with full pipeline"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSummaryFullPipeline");
            return Ok(new PerformanceResult
            {
                TestName = "Summary Full Pipeline - Filter + Multi-Field Group + Order + Page",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Summary extension: nested ConditionGroup (AND + OR sub-group) before grouping.
    /// </summary>
    [HttpGet("summary/nested-filter")]
    public async Task<ActionResult<PerformanceResult>> TestSummaryNestedFilter()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition
                        {
                            Field = "IsActive",
                            DataType = DataType.Boolean,
                            Operator = Operator.Equal,
                            Values = ["true"],
                            Sort = 1
                        }
                    ],
                    SubConditionGroups =
                    [
                        new ConditionGroup
                        {
                            Sort = 1,
                            Connector = Connector.Or,
                            Conditions =
                            [
                                new Condition
                                {
                                    Field = "Rating",
                                    DataType = DataType.Number,
                                    Operator = Operator.GreaterThanOrEqual,
                                    Values = ["4.0"],
                                    Sort = 1
                                },
                                new Condition
                                {
                                    Field = "Price",
                                    DataType = DataType.Number,
                                    Operator = Operator.LessThan,
                                    Values = ["30"],
                                    Sort = 2
                                }
                            ]
                        }
                    ]
                },
                GroupBy = new GroupBy
                {
                    Fields = ["CategoryId"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",    Alias = "ProductCount",  Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price", Alias = "AveragePrice",  Aggregator = Aggregator.Average },
                        new AggregateBy { Field = "Rating",Alias = "AverageRating", Aggregator = Aggregator.Average }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "ProductCount", Direction = Direction.Descending, Sort = 1 }
                ]
            };

            sw.Restart();
            var query = _context.Products.Summary(summary);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Summary Nested Filter - IsActive AND (Rating >= 4 OR Price < 30)",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = "Filtered with nested AND/OR conditions, then grouped by category"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSummaryNestedFilter");
            return Ok(new PerformanceResult
            {
                TestName = "Summary Nested Filter - IsActive AND (Rating >= 4 OR Price < 30)",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    #endregion

    #region Test ToList Sync (IQueryable) with Summary

    /// <summary>
    /// Test synchronous ToList with Summary returning a SummaryResult with pagination metadata.
    /// </summary>
    [HttpGet("tolist/sync")]
    public ActionResult<PerformanceResult> TestToListSync()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition
                        {
                            Field = "IsActive",
                            DataType = DataType.Boolean,
                            Operator = Operator.Equal,
                            Values = ["true"],
                            Sort = 1
                        }
                    ]
                },
                GroupBy = new GroupBy
                {
                    Fields = ["CategoryId"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",    Alias = "ProductCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price", Alias = "TotalRevenue", Aggregator = Aggregator.Sumation }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "ProductCount", Direction = Direction.Descending, Sort = 1 }
                ],
                Page = new PageBy { PageNumber = 1, PageSize = 5 }
            };

            sw.Restart();
            var results = _context.Products.ToList(summary);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = results.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                { "PageNumber", results.PageNumber },
                { "PageSize",   results.PageSize },
                { "PageCount",  results.PageCount },
                { "TotalCount", results.TotalCount }
            };

            return Ok(new PerformanceResult
            {
                TestName = "ToList Sync - SummaryResult with Pagination",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = $"Page {results.PageNumber} of {results.PageCount} ({results.TotalCount} total groups)"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListSync");
            return Ok(new PerformanceResult
            {
                TestName = "ToList Sync - SummaryResult with Pagination",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test synchronous ToList with Summary and getQueryString=true to capture the generated SQL.
    /// </summary>
    [HttpGet("tolist/sync-querystring")]
    public ActionResult<PerformanceResult> TestToListSyncWithQueryString()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = ["IsActive"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",    Alias = "TotalCount",   Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price", Alias = "TotalRevenue", Aggregator = Aggregator.Sumation }
                    ]
                }
            };

            sw.Restart();
            var results = _context.Products.ToList(summary, getQueryString: true);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = results.Data.Count;
            metrics.QueryGenerated = results.QueryString;

            return Ok(new PerformanceResult
            {
                TestName = "ToList Sync - With QueryString Generation",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = "SQL query string captured from EF Core translation"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListSyncWithQueryString");
            return Ok(new PerformanceResult
            {
                TestName = "ToList Sync - With QueryString Generation",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    #endregion

    #region Test ToList In-Memory (IEnumerable) with Summary

    /// <summary>
    /// Test in-memory ToList (IEnumerable overload) with Summary — no EF Core translation.
    /// </summary>
    [HttpGet("tolist/in-memory")]
    public async Task<ActionResult<PerformanceResult>> TestToListInMemory()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var products = await _context.Products.ToListAsync();

            var summary = new Summary
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition
                        {
                            Field = "Price",
                            DataType = DataType.Number,
                            Operator = Operator.GreaterThan,
                            Values = ["50"],
                            Sort = 1
                        }
                    ]
                },
                GroupBy = new GroupBy
                {
                    Fields = ["IsActive"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",    Alias = "ProductCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price", Alias = "AveragePrice", Aggregator = Aggregator.Average },
                        new AggregateBy { Field = "Price", Alias = "MaxPrice",     Aggregator = Aggregator.Maximum }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "ProductCount", Direction = Direction.Descending, Sort = 1 }
                ]
            };

            sw.Restart();
            var results = products.ToList(summary);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = results.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                { "SourceCount", products.Count },
                { "TotalCount",  results.TotalCount }
            };

            return Ok(new PerformanceResult
            {
                TestName = "ToList In-Memory - IEnumerable Overload with Summary",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = $"In-memory summary over {products.Count} products → {results.TotalCount} groups"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListInMemory");
            return Ok(new PerformanceResult
            {
                TestName = "ToList In-Memory - IEnumerable Overload with Summary",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    #endregion

    #region Test ToListAsync with Summary

    /// <summary>
    /// Test async ToListAsync with a basic Summary (no filter, no page).
    /// </summary>
    [HttpGet("tolist/async")]
    public async Task<ActionResult<PerformanceResult>> TestToListAsync()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = ["CategoryId"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",    Alias = "ProductCount",  Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price", Alias = "AveragePrice",  Aggregator = Aggregator.Average },
                        new AggregateBy { Field = "Price", Alias = "TotalRevenue",  Aggregator = Aggregator.Sumation },
                        new AggregateBy { Field = "Rating",Alias = "AverageRating", Aggregator = Aggregator.Average }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "TotalRevenue", Direction = Direction.Descending, Sort = 1 }
                ]
            };

            sw.Restart();
            var results = await _context.Products.ToListAsync(summary);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = results.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                { "TotalCount", results.TotalCount }
            };

            return Ok(new PerformanceResult
            {
                TestName = "ToListAsync - Products Grouped by Category with Revenue",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = $"Async summary: {results.TotalCount} category groups returned"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListAsync");
            return Ok(new PerformanceResult
            {
                TestName = "ToListAsync - Products Grouped by Category with Revenue",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test async ToListAsync with Summary, getQueryString=true, filter, and pagination.
    /// </summary>
    [HttpGet("tolist/async-querystring")]
    public async Task<ActionResult<PerformanceResult>> TestToListAsyncWithQueryString()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition
                        {
                            Field = "Rating",
                            DataType = DataType.Number,
                            Operator = Operator.GreaterThanOrEqual,
                            Values = ["3.0"],
                            Sort = 1
                        }
                    ]
                },
                GroupBy = new GroupBy
                {
                    Fields = ["IsActive", "CategoryId"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",    Alias = "ProductCount",  Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Rating",Alias = "AverageRating", Aggregator = Aggregator.Average }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "AverageRating", Direction = Direction.Descending, Sort = 1 }
                ],
                Page = new PageBy { PageNumber = 1, PageSize = 8 }
            };

            sw.Restart();
            var results = await _context.Products.ToListAsync(summary, getQueryString: true);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = results.Data.Count;
            metrics.QueryGenerated = results.QueryString;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                { "PageNumber", results.PageNumber },
                { "PageSize",   results.PageSize },
                { "PageCount",  results.PageCount },
                { "TotalCount", results.TotalCount }
            };

            return Ok(new PerformanceResult
            {
                TestName = "ToListAsync - With QueryString + Filter + Page",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = $"Page {results.PageNumber} of {results.PageCount} with SQL query string captured"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListAsyncWithQueryString");
            return Ok(new PerformanceResult
            {
                TestName = "ToListAsync - With QueryString + Filter + Page",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    #endregion

    #region Multi-Entity Scenarios

    /// <summary>
    /// Test Summary on Orders: group by Status with revenue statistics.
    /// </summary>
    [HttpGet("orders/by-status")]
    public async Task<ActionResult<PerformanceResult>> TestOrdersByStatus()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = ["Status"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",          Alias = "OrderCount",   Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "TotalAmount", Alias = "TotalRevenue", Aggregator = Aggregator.Sumation },
                        new AggregateBy { Field = "TotalAmount", Alias = "AverageOrder", Aggregator = Aggregator.Average },
                        new AggregateBy { Field = "TotalAmount", Alias = "MaxOrder",     Aggregator = Aggregator.Maximum },
                        new AggregateBy { Field = "TotalAmount", Alias = "MinOrder",     Aggregator = Aggregator.Minimum }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "TotalRevenue", Direction = Direction.Descending, Sort = 1 }
                ]
            };

            sw.Restart();
            var results = await _context.Orders.ToListAsync(summary, getQueryString: true);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = results.Data.Count;
            metrics.QueryGenerated = results.QueryString;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                { "TotalCount", results.TotalCount }
            };

            return Ok(new PerformanceResult
            {
                TestName = "Orders by Status - Revenue Breakdown per Order Status",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = "Orders grouped by status with full revenue statistics"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestOrdersByStatus");
            return Ok(new PerformanceResult
            {
                TestName = "Orders by Status - Revenue Breakdown per Order Status",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Summary on Orders: group by PaymentMethod filtered to paid orders only.
    /// </summary>
    [HttpGet("orders/by-payment-method")]
    public async Task<ActionResult<PerformanceResult>> TestOrdersByPaymentMethod()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition
                        {
                            Field = "IsPaid",
                            DataType = DataType.Boolean,
                            Operator = Operator.Equal,
                            Values = ["true"],
                            Sort = 1
                        }
                    ]
                },
                GroupBy = new GroupBy
                {
                    Fields = ["PaymentMethod"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",          Alias = "OrderCount",    Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "TotalAmount", Alias = "TotalRevenue",  Aggregator = Aggregator.Sumation },
                        new AggregateBy { Field = "TotalAmount", Alias = "AvgOrderValue", Aggregator = Aggregator.Average }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "TotalRevenue", Direction = Direction.Descending, Sort = 1 }
                ]
            };

            sw.Restart();
            var results = await _context.Orders.ToListAsync(summary);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = results.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                { "TotalCount", results.TotalCount }
            };

            return Ok(new PerformanceResult
            {
                TestName = "Orders by PaymentMethod - Paid Orders Revenue per Payment Method",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = "Paid orders grouped by payment method with revenue statistics"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestOrdersByPaymentMethod");
            return Ok(new PerformanceResult
            {
                TestName = "Orders by PaymentMethod - Paid Orders Revenue per Payment Method",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Summary on Orders: group by order date year and month with totals.
    /// </summary>
    [HttpGet("orders/by-date")]
    public async Task<ActionResult<PerformanceResult>> TestOrdersByDate()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = ["OrderDate.Year", "OrderDate.Month"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",          Alias = "OrderCount",   Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "TotalAmount", Alias = "TotalRevenue", Aggregator = Aggregator.Sumation },
                        new AggregateBy { Field = "TotalAmount", Alias = "AvgRevenue",   Aggregator = Aggregator.Average }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "OrderDate.Year",  Direction = Direction.Descending, Sort = 1 },
                    new OrderBy { Field = "OrderDate.Month", Direction = Direction.Descending, Sort = 2 }
                ]
            };

            sw.Restart();
            var results = await _context.Orders.ToListAsync(summary);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = results.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                { "TotalCount", results.TotalCount }
            };

            return Ok(new PerformanceResult
            {
                TestName = "Orders by Date - Monthly Revenue Trend",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = "Orders grouped by year and month for revenue trend analysis"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestOrdersByDate");
            return Ok(new PerformanceResult
            {
                TestName = "Orders by Date - Monthly Revenue Trend",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Summary on Customers: group by Gender with spending statistics, active only.
    /// </summary>
    [HttpGet("customers/by-gender")]
    public async Task<ActionResult<PerformanceResult>> TestCustomersByGender()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition
                        {
                            Field = "IsActive",
                            DataType = DataType.Boolean,
                            Operator = Operator.Equal,
                            Values = ["true"],
                            Sort = 1
                        }
                    ]
                },
                GroupBy = new GroupBy
                {
                    Fields = ["Gender"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",         Alias = "CustomerCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "TotalSpent", Alias = "TotalSpent",    Aggregator = Aggregator.Sumation },
                        new AggregateBy { Field = "TotalSpent", Alias = "AvgSpent",      Aggregator = Aggregator.Average },
                        new AggregateBy { Field = "TotalSpent", Alias = "MaxSpent",      Aggregator = Aggregator.Maximum }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "TotalSpent", Direction = Direction.Descending, Sort = 1 }
                ]
            };

            sw.Restart();
            var results = await _context.Customers.ToListAsync(summary);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = results.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                { "TotalCount", results.TotalCount }
            };

            return Ok(new PerformanceResult
            {
                TestName = "Customers by Gender - Active Customers Spending Statistics",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = "Active customers grouped by gender with spending statistics"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestCustomersByGender");
            return Ok(new PerformanceResult
            {
                TestName = "Customers by Gender - Active Customers Spending Statistics",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Summary on Customers: group by Tier with spending statistics and pagination.
    /// </summary>
    [HttpGet("customers/by-tier")]
    public async Task<ActionResult<PerformanceResult>> TestCustomersByTier()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = ["Tier"],
                    AggregateBy =
                    [
                        new AggregateBy { Field = "Id",         Alias = "CustomerCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "TotalSpent", Alias = "TotalSpent",    Aggregator = Aggregator.Sumation },
                        new AggregateBy { Field = "TotalSpent", Alias = "AvgSpent",      Aggregator = Aggregator.Average },
                        new AggregateBy { Field = "TotalSpent", Alias = "MinSpent",      Aggregator = Aggregator.Minimum },
                        new AggregateBy { Field = "TotalSpent", Alias = "MaxSpent",      Aggregator = Aggregator.Maximum }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "AvgSpent", Direction = Direction.Descending, Sort = 1 }
                ],
                Page = new PageBy { PageNumber = 1, PageSize = 5 }
            };

            sw.Restart();
            var results = await _context.Customers.ToListAsync(summary, getQueryString: true);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = results.Data.Count;
            metrics.QueryGenerated = results.QueryString;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                { "PageNumber", results.PageNumber },
                { "PageSize",   results.PageSize },
                { "PageCount",  results.PageCount },
                { "TotalCount", results.TotalCount }
            };

            return Ok(new PerformanceResult
            {
                TestName = "Customers by Tier - Spending Analytics per Customer Tier",
                Metrics = metrics,
                Input = summary,
                Output = results,
                Success = true,
                Message = $"Page {results.PageNumber} of {results.PageCount}: customers grouped by tier"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestCustomersByTier");
            return Ok(new PerformanceResult
            {
                TestName = "Customers by Tier - Spending Analytics per Customer Tier",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    #endregion

    #region Run All Tests

    /// <summary>
    /// Run all Summary tests and return aggregated results.
    /// </summary>
    [HttpGet("all")]
    public async Task<ActionResult<AllTestsResult>> TestAll()
    {
        var allResults = new List<PerformanceResult>();
        var overallSw = Stopwatch.StartNew();

        var testMethods = new List<(string name, Func<Task<ActionResult<PerformanceResult>>> method)>
        {
            ("Summary Simple",              TestSummarySimple),
            ("Summary With Filter",         TestSummaryWithFilter),
            ("Summary With Order",          TestSummaryWithOrder),
            ("Summary With Page",           TestSummaryWithPage),
            ("Summary All Aggregations",    TestSummaryAllAggregations),
            ("Summary Date Grouping",       TestSummaryDateGrouping),
            ("Summary Nested Property",     TestSummaryNestedProperty),
            ("Summary Full Pipeline",       TestSummaryFullPipeline),
            ("Summary Nested Filter",       TestSummaryNestedFilter),
            ("ToList Sync",                 () => Task.FromResult(TestToListSync())),
            ("ToList Sync QueryString",     () => Task.FromResult(TestToListSyncWithQueryString())),
            ("ToList In-Memory",            TestToListInMemory),
            ("ToListAsync",                 TestToListAsync),
            ("ToListAsync QueryString",     TestToListAsyncWithQueryString),
            ("Orders by Status",            TestOrdersByStatus),
            ("Orders by Payment Method",    TestOrdersByPaymentMethod),
            ("Orders by Date",              TestOrdersByDate),
            ("Customers by Gender",         TestCustomersByGender),
            ("Customers by Tier",           TestCustomersByTier)
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
                if (performanceResult != null)
                    allResults.Add(performanceResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing Summary test: {name}", name);
                allResults.Add(new PerformanceResult
                {
                    TestName = name,
                    Success = false,
                    Message = ex.Message,
                    Metrics = new PerformanceMetrics()
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
}
