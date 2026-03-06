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
/// Controller for testing all Summary extension methods:
/// Summary (IQueryable), ToList(Summary), ToList(IEnumerable,Summary), ToListAsync(Summary).
/// Covers: simple grouping, with-filter, with-order, with-page, all aggregations,
/// date grouping, nested property, full pipeline, nested filter, having, multi-entity.
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

    #region Summary IQueryable

    /// <summary>
    /// Summary: simple — group only, no filter.
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
                        new AggregateBy { Alias = "ProductCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price", Alias = "AvgPrice", Aggregator = Aggregator.Average }
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
                TestName = "Summary Simple - Group by IsActive + Count/AvgPrice",
                Metrics = metrics, Input = summary, Output = results, Success = true,
                Message = $"Summary grouped into {results.Count} groups"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSummarySimple");
            return Ok(new PerformanceResult { TestName = "Summary Simple", Success = false, Message = ex.Message, Metrics = new PerformanceMetrics() });
        }
    }

    /// <summary>
    /// Summary: with pre-filter (WHERE before GROUP).
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
                        new Condition { Sort = 1, Field = "Price", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["10"] }
                    ]
                },
                GroupBy = new GroupBy
                {
                    Fields = ["IsActive"],
                    AggregateBy =
                    [
                        new AggregateBy { Alias = "ProductCount", Aggregator = Aggregator.Count },
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
                TestName = "Summary With Filter - WHERE Price > 10, GROUP BY IsActive",
                Metrics = metrics, Input = summary, Output = results, Success = true,
                Message = "Pre-filter WHERE before grouping"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSummaryWithFilter");
            return Ok(new PerformanceResult { TestName = "Summary With Filter", Success = false, Message = ex.Message, Metrics = new PerformanceMetrics() });
        }
    }

    /// <summary>
    /// Summary: with ordering on grouped result.
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
                    Fields = ["IsActive"],
                    AggregateBy =
                    [
                        new AggregateBy { Alias = "ProductCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price", Alias = "AvgPrice", Aggregator = Aggregator.Average }
                    ]
                },
                Orders =
                [
                    new OrderBy { Sort = 1, Field = "AvgPrice", Direction = Direction.Descending }
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
                TestName = "Summary With Order - ORDER BY AvgPrice DESC",
                Metrics = metrics, Input = summary, Output = results, Success = true,
                Message = "Summary with ordering on aggregate alias"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSummaryWithOrder");
            return Ok(new PerformanceResult { TestName = "Summary With Order", Success = false, Message = ex.Message, Metrics = new PerformanceMetrics() });
        }
    }

    /// <summary>
    /// Summary: with pagination on grouped result.
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
                    Fields = ["IsActive"],
                    AggregateBy =
                    [
                        new AggregateBy { Alias = "ProductCount", Aggregator = Aggregator.Count }
                    ]
                },
                Page = new PageBy { PageNumber = 1, PageSize = 5 }
            };

            sw.Restart();
            var result = await _context.Products.ToListAsync(summary);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                ["PageNumber"] = result.PageNumber, ["PageSize"] = result.PageSize,
                ["PageCount"] = result.PageCount, ["TotalCount"] = result.TotalCount
            };

            return Ok(new PerformanceResult
            {
                TestName = "Summary With Page - Page 1, Size 5",
                Metrics = metrics, Input = summary, Output = result, Success = true,
                Message = $"Summary paged: {result.Data.Count} of {result.TotalCount}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSummaryWithPage");
            return Ok(new PerformanceResult { TestName = "Summary With Page", Success = false, Message = ex.Message, Metrics = new PerformanceMetrics() });
        }
    }

    /// <summary>
    /// Summary: all aggregation functions.
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
                    Fields = ["IsActive"],
                    AggregateBy =
                    [
                        new AggregateBy { Alias = "TotalCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "StockQuantity", Alias = "UniqueStockCount", Aggregator = Aggregator.CountDistinct },
                        new AggregateBy { Field = "Price", Alias = "SumPrice", Aggregator = Aggregator.Sumation },
                        new AggregateBy { Field = "Price", Alias = "AvgPrice", Aggregator = Aggregator.Average },
                        new AggregateBy { Field = "Price", Alias = "MinPrice", Aggregator = Aggregator.Minimum },
                        new AggregateBy { Field = "Price", Alias = "MaxPrice", Aggregator = Aggregator.Maximum },
                        new AggregateBy { Field = "Name", Alias = "FirstProduct", Aggregator = Aggregator.FirstOrDefault },
                        new AggregateBy { Field = "Name", Alias = "LastProduct", Aggregator = Aggregator.LastOrDefault }
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
                TestName = "Summary All Aggregations - All 8 aggregation types",
                Metrics = metrics, Input = summary, Output = results, Success = true,
                Message = "All aggregation functions: Count, CountDistinct, Sum, Avg, Min, Max, First, Last"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSummaryAllAggregations");
            return Ok(new PerformanceResult { TestName = "Summary All Aggregations", Success = false, Message = ex.Message, Metrics = new PerformanceMetrics() });
        }
    }

    /// <summary>
    /// Summary: nested filter (complex WHERE before GROUP).
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
                        new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] }
                    ],
                    SubConditionGroups =
                    [
                        new ConditionGroup
                        {
                            Sort = 1,
                            Connector = Connector.Or,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "Price", DataType = DataType.Number, Operator = Operator.LessThan, Values = ["50"] },
                                new Condition { Sort = 2, Field = "Rating", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = ["4.5"] }
                            ]
                        }
                    ]
                },
                GroupBy = new GroupBy
                {
                    Fields = ["IsActive"],
                    AggregateBy =
                    [
                        new AggregateBy { Alias = "ProductCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price", Alias = "AvgPrice", Aggregator = Aggregator.Average }
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
                TestName = "Summary Nested Filter - Active AND (Budget OR TopRated)",
                Metrics = metrics, Input = summary, Output = results, Success = true,
                Message = "Nested WHERE condition before grouping"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSummaryNestedFilter");
            return Ok(new PerformanceResult { TestName = "Summary Nested Filter", Success = false, Message = ex.Message, Metrics = new PerformanceMetrics() });
        }
    }

    /// <summary>
    /// Summary: full pipeline (WHERE + GROUP + HAVING + ORDER + PAGE).
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
                        new Condition { Sort = 1, Field = "Price", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["0"] }
                    ]
                },
                GroupBy = new GroupBy
                {
                    Fields = ["IsActive"],
                    AggregateBy =
                    [
                        new AggregateBy { Alias = "ProductCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price", Alias = "AvgPrice", Aggregator = Aggregator.Average },
                        new AggregateBy { Field = "Price", Alias = "TotalValue", Aggregator = Aggregator.Sumation }
                    ]
                },
                Having = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "ProductCount", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["1"] }
                    ]
                },
                Orders = [new OrderBy { Sort = 1, Field = "TotalValue", Direction = Direction.Descending }],
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var result = await _context.Products.ToListAsync(summary);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.QueryGenerated = result.QueryString;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                ["PageNumber"] = result.PageNumber, ["PageSize"] = result.PageSize,
                ["PageCount"] = result.PageCount, ["TotalCount"] = result.TotalCount
            };

            return Ok(new PerformanceResult
            {
                TestName = "Summary Full Pipeline - WHERE + GROUP + HAVING + ORDER + PAGE",
                Metrics = metrics, Input = summary, Output = result.Data, Success = true,
                Message = "Complete Summary pipeline"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSummaryFullPipeline");
            return Ok(new PerformanceResult { TestName = "Summary Full Pipeline", Success = false, Message = ex.Message, Metrics = new PerformanceMetrics() });
        }
    }

    #endregion

    #region ToList (Sync)

    /// <summary>
    /// ToList sync: Summary.
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
                GroupBy = new GroupBy
                {
                    Fields = ["IsActive"],
                    AggregateBy =
                    [
                        new AggregateBy { Alias = "ProductCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price", Alias = "AvgPrice", Aggregator = Aggregator.Average }
                    ]
                },
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var result = _context.Products.ToList(summary);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                ["TotalCount"] = result.TotalCount, ["PageCount"] = result.PageCount
            };

            return Ok(new PerformanceResult
            {
                TestName = "ToList Sync Summary",
                Metrics = metrics, Input = summary, Output = result, Success = true,
                Message = $"Sync Summary: {result.Data.Count} of {result.TotalCount}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListSync");
            return Ok(new PerformanceResult { TestName = "ToList Sync Summary", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// ToList sync: with QueryString.
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
                        new AggregateBy { Alias = "ProductCount", Aggregator = Aggregator.Count }
                    ]
                }
            };

            sw.Restart();
            var result = _context.Products.ToList(summary, getQueryString: true);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.QueryGenerated = result.QueryString;

            return Ok(new PerformanceResult
            {
                TestName = "ToList Sync Summary With QueryString",
                Metrics = metrics, Input = summary, Output = result, Success = true,
                Message = $"QueryString included: {result.QueryString != null}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListSyncWithQueryString");
            return Ok(new PerformanceResult { TestName = "ToList Sync QueryString", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    #endregion

    #region ToList (In-Memory)

    /// <summary>
    /// ToList in-memory: IEnumerable overload.
    /// </summary>
    [HttpGet("tolist/in-memory")]
    public async Task<ActionResult<PerformanceResult>> TestToListInMemory()
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
                        new AggregateBy { Alias = "ProductCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price", Alias = "AvgPrice", Aggregator = Aggregator.Average }
                    ]
                }
            };

            sw.Restart();
            var data = await _context.Products.ToListAsync();
            var loadTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var result = data.ToList(summary);
            var filterTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = loadTime;
            metrics.ExecutionTimeMs = filterTime;
            metrics.TotalTimeMs = loadTime + filterTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                ["TotalCount"] = result.TotalCount, ["InMemoryDataSize"] = data.Count
            };

            return Ok(new PerformanceResult
            {
                TestName = "ToList In-Memory Summary",
                Metrics = metrics, Input = summary, Output = result, Success = true,
                Message = $"In-memory summary on {data.Count} items, {result.Data.Count} groups"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListInMemory");
            return Ok(new PerformanceResult { TestName = "ToList In-Memory Summary", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    #endregion

    #region ToListAsync

    /// <summary>
    /// ToListAsync: basic.
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
                    Fields = ["IsActive"],
                    AggregateBy =
                    [
                        new AggregateBy { Alias = "ProductCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "Price", Alias = "AvgPrice", Aggregator = Aggregator.Average }
                    ]
                },
                Orders = [new OrderBy { Sort = 1, Field = "ProductCount", Direction = Direction.Descending }],
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var result = await _context.Products.ToListAsync(summary);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                ["PageNumber"] = result.PageNumber, ["PageSize"] = result.PageSize,
                ["PageCount"] = result.PageCount, ["TotalCount"] = result.TotalCount
            };

            return Ok(new PerformanceResult
            {
                TestName = "ToListAsync Summary",
                Metrics = metrics, Input = summary, Output = result, Success = true,
                Message = $"Async Summary: {result.Data.Count} of {result.TotalCount}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListAsync");
            return Ok(new PerformanceResult { TestName = "ToListAsync Summary", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// ToListAsync: with QueryString.
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
                GroupBy = new GroupBy
                {
                    Fields = ["IsActive"],
                    AggregateBy =
                    [
                        new AggregateBy { Alias = "ProductCount", Aggregator = Aggregator.Count }
                    ]
                }
            };

            sw.Restart();
            var result = await _context.Products.ToListAsync(summary, getQueryString: true);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.QueryGenerated = result.QueryString;

            return Ok(new PerformanceResult
            {
                TestName = "ToListAsync Summary With QueryString",
                Metrics = metrics, Input = summary, Output = result, Success = true,
                Message = $"Async with QueryString: {result.QueryString != null}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListAsyncWithQueryString");
            return Ok(new PerformanceResult { TestName = "ToListAsync Summary QueryString", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    #endregion

    #region Multi-Entity — Orders

    /// <summary>
    /// Summary: Orders by Status.
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
                        new AggregateBy { Alias = "OrderCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "TotalAmount", Alias = "TotalRevenue", Aggregator = Aggregator.Sumation },
                        new AggregateBy { Field = "TotalAmount", Alias = "AvgOrderValue", Aggregator = Aggregator.Average }
                    ]
                },
                Orders = [new OrderBy { Sort = 1, Field = "TotalRevenue", Direction = Direction.Descending }]
            };

            sw.Restart();
            var query = _context.Orders.Summary(summary);
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
                TestName = "Summary Orders By Status",
                Metrics = metrics, Input = summary, Output = results, Success = true,
                Message = $"Orders by status: {results.Count} groups"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestOrdersByStatus");
            return Ok(new PerformanceResult { TestName = "Orders By Status", Success = false, Message = ex.Message, Metrics = new PerformanceMetrics() });
        }
    }

    /// <summary>
    /// Summary: Orders by PaymentMethod.
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
                GroupBy = new GroupBy
                {
                    Fields = ["PaymentMethod"],
                    AggregateBy =
                    [
                        new AggregateBy { Alias = "OrderCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "TotalAmount", Alias = "TotalRevenue", Aggregator = Aggregator.Sumation }
                    ]
                }
            };

            sw.Restart();
            var query = _context.Orders.Summary(summary);
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
                TestName = "Summary Orders By PaymentMethod",
                Metrics = metrics, Input = summary, Output = results, Success = true,
                Message = $"Orders by payment: {results.Count} groups"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestOrdersByPaymentMethod");
            return Ok(new PerformanceResult { TestName = "Orders By PaymentMethod", Success = false, Message = ex.Message, Metrics = new PerformanceMetrics() });
        }
    }

    /// <summary>
    /// Summary: Orders by IsPaid.
    /// </summary>
    [HttpGet("orders/by-paid")]
    public async Task<ActionResult<PerformanceResult>> TestOrdersByPaid()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var summary = new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = ["IsPaid"],
                    AggregateBy =
                    [
                        new AggregateBy { Alias = "OrderCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "TotalAmount", Alias = "AvgAmount", Aggregator = Aggregator.Average }
                    ]
                }
            };

            sw.Restart();
            var query = _context.Orders.Summary(summary);
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
                TestName = "Summary Orders By IsPaid",
                Metrics = metrics, Input = summary, Output = results, Success = true,
                Message = $"Orders by paid status: {results.Count} groups"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestOrdersByPaid");
            return Ok(new PerformanceResult { TestName = "Orders By IsPaid", Success = false, Message = ex.Message, Metrics = new PerformanceMetrics() });
        }
    }

    #endregion

    #region Multi-Entity — Customers

    /// <summary>
    /// Summary: Customers by Gender.
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
                GroupBy = new GroupBy
                {
                    Fields = ["Gender"],
                    AggregateBy =
                    [
                        new AggregateBy { Alias = "CustomerCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "TotalSpent", Alias = "AvgSpending", Aggregator = Aggregator.Average }
                    ]
                }
            };

            sw.Restart();
            var query = _context.Customers.Summary(summary);
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
                TestName = "Summary Customers By Gender",
                Metrics = metrics, Input = summary, Output = results, Success = true,
                Message = $"Customers by gender: {results.Count} groups"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestCustomersByGender");
            return Ok(new PerformanceResult { TestName = "Customers By Gender", Success = false, Message = ex.Message, Metrics = new PerformanceMetrics() });
        }
    }

    /// <summary>
    /// Summary: Customers by Tier.
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
                        new AggregateBy { Alias = "CustomerCount", Aggregator = Aggregator.Count },
                        new AggregateBy { Field = "TotalSpent", Alias = "TotalSpending", Aggregator = Aggregator.Sumation },
                        new AggregateBy { Field = "TotalSpent", Alias = "MaxSpent", Aggregator = Aggregator.Maximum }
                    ]
                },
                Orders = [new OrderBy { Sort = 1, Field = "TotalSpending", Direction = Direction.Descending }]
            };

            sw.Restart();
            var query = _context.Customers.Summary(summary);
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
                TestName = "Summary Customers By Tier",
                Metrics = metrics, Input = summary, Output = results, Success = true,
                Message = $"Customers by tier: {results.Count} groups"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestCustomersByTier");
            return Ok(new PerformanceResult { TestName = "Customers By Tier", Success = false, Message = ex.Message, Metrics = new PerformanceMetrics() });
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
            // Summary IQueryable
            ("Summary Simple",          TestSummarySimple),
            ("Summary With Filter",     TestSummaryWithFilter),
            ("Summary With Order",      TestSummaryWithOrder),
            ("Summary With Page",       TestSummaryWithPage),
            ("Summary All Aggregations",TestSummaryAllAggregations),
            ("Summary Nested Filter",   TestSummaryNestedFilter),
            ("Summary Full Pipeline",   TestSummaryFullPipeline),
            // ToList sync
            ("ToList Sync",             () => Task.FromResult(TestToListSync())),
            ("ToList Sync QueryString", () => Task.FromResult(TestToListSyncWithQueryString())),
            // In-memory
            ("ToList In-Memory",        TestToListInMemory),
            // ToListAsync
            ("ToListAsync",             TestToListAsync),
            ("ToListAsync QueryString", TestToListAsyncWithQueryString),
            // Multi-entity orders
            ("Orders By Status",        TestOrdersByStatus),
            ("Orders By PaymentMethod", TestOrdersByPaymentMethod),
            ("Orders By Paid",          TestOrdersByPaid),
            // Multi-entity customers
            ("Customers By Gender",     TestCustomersByGender),
            ("Customers By Tier",       TestCustomersByTier)
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
                _logger.LogError(ex, "Error executing Summary test: {name}", name);
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
}
