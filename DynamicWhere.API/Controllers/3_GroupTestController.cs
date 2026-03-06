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
/// Controller for testing Group (GroupBy + Aggregations) and Having clause functionality.
/// Group extension returns IQueryable (dynamic) via query.Group(groupBy).
/// Having is tested through Summary with Having ConditionGroup.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class GroupTestController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<GroupTestController> _logger;

    public GroupTestController(AppDbContext context, ILogger<GroupTestController> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Group<T> — Products

    /// <summary>
    /// Group: simple single-field grouping (IsActive) with Count.
    /// </summary>
    [HttpGet("group/simple")]
    public async Task<ActionResult<PerformanceResult>> TestGroupSimple()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var groupBy = new GroupBy
            {
                Fields = ["IsActive"],
                AggregateBy =
                [
                    new AggregateBy { Alias = "TotalCount", Aggregator = Aggregator.Count }
                ]
            };

            sw.Restart();
            var query = _context.Products.Group(groupBy);
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
                TestName = "Group Simple - By IsActive + Count",
                Metrics = metrics, Input = groupBy, Output = results, Success = true,
                Message = $"Grouped into {results.Count} groups"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupSimple");
            return Ok(new PerformanceResult
            {
                TestName = "Group Simple - By IsActive + Count",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    /// <summary>
    /// Group: multiple grouping fields (IsActive, StockQuantity threshold via separate grouping).
    /// </summary>
    [HttpGet("group/multiple-fields")]
    public async Task<ActionResult<PerformanceResult>> TestGroupMultipleFields()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var groupBy = new GroupBy
            {
                Fields = ["IsActive"],
                AggregateBy =
                [
                    new AggregateBy { Alias = "TotalCount", Aggregator = Aggregator.Count },
                    new AggregateBy { Field = "Price", Alias = "AvgPrice", Aggregator = Aggregator.Average },
                    new AggregateBy { Field = "StockQuantity", Alias = "TotalStock", Aggregator = Aggregator.Sumation }
                ]
            };

            sw.Restart();
            var query = _context.Products.Group(groupBy);
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
                TestName = "Group Multiple Aggregations - Count, AvgPrice, TotalStock",
                Metrics = metrics, Input = groupBy, Output = results, Success = true,
                Message = "Multiple aggregations on a single grouping field"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupMultipleFields");
            return Ok(new PerformanceResult
            {
                TestName = "Group Multiple Aggregations - Count, AvgPrice, TotalStock",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    /// <summary>
    /// Group: all aggregation functions (Count, CountDistinct, Sum, Avg, Min, Max, First, Last).
    /// </summary>
    [HttpGet("group/all-aggregations")]
    public async Task<ActionResult<PerformanceResult>> TestGroupAllAggregations()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var groupBy = new GroupBy
            {
                Fields = ["IsActive"],
                AggregateBy =
                [
                    new AggregateBy { Alias = "TotalCount", Aggregator = Aggregator.Count },
                    new AggregateBy { Field = "StockQuantity", Alias = "DistinctStockCount", Aggregator = Aggregator.CountDistinct },
                    new AggregateBy { Field = "Price", Alias = "SumPrice", Aggregator = Aggregator.Sumation },
                    new AggregateBy { Field = "Price", Alias = "AvgPrice", Aggregator = Aggregator.Average },
                    new AggregateBy { Field = "Price", Alias = "MinPrice", Aggregator = Aggregator.Minimum },
                    new AggregateBy { Field = "Price", Alias = "MaxPrice", Aggregator = Aggregator.Maximum },
                    new AggregateBy { Field = "Name", Alias = "FirstName", Aggregator = Aggregator.FirstOrDefault },
                    new AggregateBy { Field = "Name", Alias = "LastName", Aggregator = Aggregator.LastOrDefault }
                ]
            };

            sw.Restart();
            var query = _context.Products.Group(groupBy);
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
                TestName = "Group All Aggregations - Count, CountDistinct, Sum, Avg, Min, Max, First, Last",
                Metrics = metrics, Input = groupBy, Output = results, Success = true,
                Message = "All 8 aggregation functions tested"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupAllAggregations");
            return Ok(new PerformanceResult
            {
                TestName = "Group All Aggregations - Count, CountDistinct, Sum, Avg, Min, Max, First, Last",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    /// <summary>
    /// Group: count-only (no aggregation field).
    /// </summary>
    [HttpGet("group/count-only")]
    public async Task<ActionResult<PerformanceResult>> TestGroupCountOnly()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var groupBy = new GroupBy
            {
                Fields = ["IsActive"],
                AggregateBy =
                [
                    new AggregateBy { Alias = "ItemCount", Aggregator = Aggregator.Count }
                ]
            };

            sw.Restart();
            var query = _context.Products.Group(groupBy);
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
                TestName = "Group Count Only - No aggregation field",
                Metrics = metrics, Input = groupBy, Output = results, Success = true,
                Message = "Count aggregation without specifying a field"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupCountOnly");
            return Ok(new PerformanceResult
            {
                TestName = "Group Count Only - No aggregation field",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    /// <summary>
    /// Group: CountDistinct on a specific field.
    /// </summary>
    [HttpGet("group/count-distinct")]
    public async Task<ActionResult<PerformanceResult>> TestGroupCountDistinct()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var groupBy = new GroupBy
            {
                Fields = ["IsActive"],
                AggregateBy =
                [
                    new AggregateBy { Field = "Name", Alias = "UniqueNames", Aggregator = Aggregator.CountDistinct }
                ]
            };

            sw.Restart();
            var query = _context.Products.Group(groupBy);
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
                TestName = "Group CountDistinct - Unique Names per IsActive",
                Metrics = metrics, Input = groupBy, Output = results, Success = true,
                Message = "CountDistinct aggregation on Name"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupCountDistinct");
            return Ok(new PerformanceResult
            {
                TestName = "Group CountDistinct - Unique Names per IsActive",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    /// <summary>
    /// Group: FirstOrDefault and LastOrDefault.
    /// </summary>
    [HttpGet("group/first-last")]
    public async Task<ActionResult<PerformanceResult>> TestGroupFirstLast()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var groupBy = new GroupBy
            {
                Fields = ["IsActive"],
                AggregateBy =
                [
                    new AggregateBy { Field = "Name", Alias = "FirstProduct", Aggregator = Aggregator.FirstOrDefault },
                    new AggregateBy { Field = "Name", Alias = "LastProduct", Aggregator = Aggregator.LastOrDefault }
                ]
            };

            sw.Restart();
            var query = _context.Products.Group(groupBy);
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
                TestName = "Group FirstOrDefault / LastOrDefault - First and Last product names",
                Metrics = metrics, Input = groupBy, Output = results, Success = true,
                Message = "FirstOrDefault and LastOrDefault aggregation"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupFirstLast");
            return Ok(new PerformanceResult
            {
                TestName = "Group FirstOrDefault / LastOrDefault - First and Last product names",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    /// <summary>
    /// Group: rating analysis — Min, Max, Average of Rating.
    /// </summary>
    [HttpGet("group/rating-analysis")]
    public async Task<ActionResult<PerformanceResult>> TestGroupRatingAnalysis()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var groupBy = new GroupBy
            {
                Fields = ["IsActive"],
                AggregateBy =
                [
                    new AggregateBy { Field = "Rating", Alias = "MinRating", Aggregator = Aggregator.Minimum },
                    new AggregateBy { Field = "Rating", Alias = "MaxRating", Aggregator = Aggregator.Maximum },
                    new AggregateBy { Field = "Rating", Alias = "AvgRating", Aggregator = Aggregator.Average },
                    new AggregateBy { Alias = "ProductCount", Aggregator = Aggregator.Count }
                ]
            };

            sw.Restart();
            var query = _context.Products.Group(groupBy);
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
                TestName = "Group Rating Analysis - Min, Max, Avg Rating + Count",
                Metrics = metrics, Input = groupBy, Output = results, Success = true,
                Message = "Rating analysis aggregation per IsActive"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupRatingAnalysis");
            return Ok(new PerformanceResult
            {
                TestName = "Group Rating Analysis - Min, Max, Avg Rating + Count",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    #endregion

    #region Group<T> — Orders / Customers

    /// <summary>
    /// Group: Orders by Status with aggregations.
    /// </summary>
    [HttpGet("group/orders-by-status")]
    public async Task<ActionResult<PerformanceResult>> TestGroupOrdersByStatus()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var groupBy = new GroupBy
            {
                Fields = ["Status"],
                AggregateBy =
                [
                    new AggregateBy { Alias = "OrderCount", Aggregator = Aggregator.Count },
                    new AggregateBy { Field = "TotalAmount", Alias = "TotalRevenue", Aggregator = Aggregator.Sumation },
                    new AggregateBy { Field = "TotalAmount", Alias = "AvgOrderValue", Aggregator = Aggregator.Average }
                ]
            };

            sw.Restart();
            var query = _context.Orders.Group(groupBy);
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
                TestName = "Group Orders By Status - Count, TotalRevenue, AvgValue",
                Metrics = metrics, Input = groupBy, Output = results, Success = true,
                Message = $"Orders grouped by Status into {results.Count} groups"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupOrdersByStatus");
            return Ok(new PerformanceResult
            {
                TestName = "Group Orders By Status - Count, TotalRevenue, AvgValue",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    /// <summary>
    /// Group: Orders by PaymentMethod.
    /// </summary>
    [HttpGet("group/orders-by-payment")]
    public async Task<ActionResult<PerformanceResult>> TestGroupOrdersByPayment()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var groupBy = new GroupBy
            {
                Fields = ["PaymentMethod"],
                AggregateBy =
                [
                    new AggregateBy { Alias = "OrderCount", Aggregator = Aggregator.Count },
                    new AggregateBy { Field = "TotalAmount", Alias = "TotalRevenue", Aggregator = Aggregator.Sumation }
                ]
            };

            sw.Restart();
            var query = _context.Orders.Group(groupBy);
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
                TestName = "Group Orders By PaymentMethod - Count, TotalRevenue",
                Metrics = metrics, Input = groupBy, Output = results, Success = true,
                Message = $"Orders grouped by PaymentMethod into {results.Count} groups"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupOrdersByPayment");
            return Ok(new PerformanceResult
            {
                TestName = "Group Orders By PaymentMethod - Count, TotalRevenue",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    /// <summary>
    /// Group: Customers by Gender.
    /// </summary>
    [HttpGet("group/customers-by-gender")]
    public async Task<ActionResult<PerformanceResult>> TestGroupCustomersByGender()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var groupBy = new GroupBy
            {
                Fields = ["Gender"],
                AggregateBy =
                [
                    new AggregateBy { Alias = "CustomerCount", Aggregator = Aggregator.Count },
                    new AggregateBy { Field = "TotalSpent", Alias = "AvgSpending", Aggregator = Aggregator.Average },
                    new AggregateBy { Field = "TotalSpent", Alias = "TotalSpending", Aggregator = Aggregator.Sumation }
                ]
            };

            sw.Restart();
            var query = _context.Customers.Group(groupBy);
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
                TestName = "Group Customers By Gender - Count, AvgSpending, TotalSpending",
                Metrics = metrics, Input = groupBy, Output = results, Success = true,
                Message = $"Customers grouped by Gender into {results.Count} groups"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupCustomersByGender");
            return Ok(new PerformanceResult
            {
                TestName = "Group Customers By Gender - Count, AvgSpending, TotalSpending",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    /// <summary>
    /// Group: Customers by Tier.
    /// </summary>
    [HttpGet("group/customers-by-tier")]
    public async Task<ActionResult<PerformanceResult>> TestGroupCustomersByTier()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var groupBy = new GroupBy
            {
                Fields = ["Tier"],
                AggregateBy =
                [
                    new AggregateBy { Alias = "CustomerCount", Aggregator = Aggregator.Count },
                    new AggregateBy { Field = "TotalSpent", Alias = "MaxSpent", Aggregator = Aggregator.Maximum },
                    new AggregateBy { Field = "TotalSpent", Alias = "MinSpent", Aggregator = Aggregator.Minimum }
                ]
            };

            sw.Restart();
            var query = _context.Customers.Group(groupBy);
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
                TestName = "Group Customers By Tier - Count, MaxSpent, MinSpent",
                Metrics = metrics, Input = groupBy, Output = results, Success = true,
                Message = $"Customers grouped by Tier into {results.Count} groups"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupCustomersByTier");
            return Ok(new PerformanceResult
            {
                TestName = "Group Customers By Tier - Count, MaxSpent, MinSpent",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    #endregion

    #region Having (via Summary)

    /// <summary>
    /// Having: simple — group products by IsActive and filter groups with Count > 5.
    /// </summary>
    [HttpGet("having/simple")]
    public async Task<ActionResult<PerformanceResult>> TestHavingSimple()
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
                Having = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "ProductCount", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["5"] }
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
                TestName = "Having Simple - ProductCount > 5",
                Metrics = metrics, Input = summary, Output = results, Success = true,
                Message = $"Filtered to {results.Count} groups with HAVING ProductCount > 5"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestHavingSimple");
            return Ok(new PerformanceResult
            {
                TestName = "Having Simple - ProductCount > 5",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    /// <summary>
    /// Having: with pre-filter (WHERE + GROUP + HAVING).
    /// </summary>
    [HttpGet("having/with-filter")]
    public async Task<ActionResult<PerformanceResult>> TestHavingWithFilter()
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
                },
                Having = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "AvgPrice", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["50"] }
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
                TestName = "Having With Filter - WHERE active, HAVING AvgPrice > 50",
                Metrics = metrics, Input = summary, Output = results, Success = true,
                Message = "WHERE + GROUP BY + HAVING pipeline"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestHavingWithFilter");
            return Ok(new PerformanceResult
            {
                TestName = "Having With Filter - WHERE active, HAVING AvgPrice > 50",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    /// <summary>
    /// Having: AND conditions — multiple having conditions combined with AND.
    /// </summary>
    [HttpGet("having/and")]
    public async Task<ActionResult<PerformanceResult>> TestHavingAndConditions()
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
                        new AggregateBy { Field = "Price", Alias = "AvgPrice", Aggregator = Aggregator.Average },
                        new AggregateBy { Field = "Price", Alias = "MaxPrice", Aggregator = Aggregator.Maximum }
                    ]
                },
                Having = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "ProductCount", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["3"] },
                        new Condition { Sort = 2, Field = "AvgPrice", DataType = DataType.Number, Operator = Operator.LessThan, Values = ["200"] }
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
                TestName = "Having AND - ProductCount > 3 AND AvgPrice < 200",
                Metrics = metrics, Input = summary, Output = results, Success = true,
                Message = "Having with multiple AND conditions"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestHavingAndConditions");
            return Ok(new PerformanceResult
            {
                TestName = "Having AND - ProductCount > 3 AND AvgPrice < 200",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    /// <summary>
    /// Having: nested group — Having with SubConditionGroups.
    /// </summary>
    [HttpGet("having/nested-group")]
    public async Task<ActionResult<PerformanceResult>> TestHavingNestedGroup()
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
                        new AggregateBy { Field = "Price", Alias = "AvgPrice", Aggregator = Aggregator.Average },
                        new AggregateBy { Field = "Price", Alias = "MinPrice", Aggregator = Aggregator.Minimum }
                    ]
                },
                Having = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "ProductCount", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["2"] }
                    ],
                    SubConditionGroups =
                    [
                        new ConditionGroup
                        {
                            Sort = 1,
                            Connector = Connector.Or,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "AvgPrice", DataType = DataType.Number, Operator = Operator.LessThan, Values = ["100"] },
                                new Condition { Sort = 2, Field = "MinPrice", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["10"] }
                            ]
                        }
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
                TestName = "Having Nested Group - Count > 2 AND (AvgPrice < 100 OR MinPrice > 10)",
                Metrics = metrics, Input = summary, Output = results, Success = true,
                Message = "Having with nested OR sub-group"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestHavingNestedGroup");
            return Ok(new PerformanceResult
            {
                TestName = "Having Nested Group - Count > 2 AND (AvgPrice < 100 OR MinPrice > 10)",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    /// <summary>
    /// Having: full pipeline — WHERE + GROUP BY + HAVING + ORDER + PAGE.
    /// </summary>
    [HttpGet("having/full-pipeline")]
    public async Task<ActionResult<PerformanceResult>> TestHavingFullPipeline()
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
                Orders =
                [
                    new OrderBy { Sort = 1, Field = "TotalValue", Direction = Direction.Descending }
                ],
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
                ["PageNumber"] = result.PageNumber,
                ["PageSize"] = result.PageSize,
                ["PageCount"] = result.PageCount,
                ["TotalCount"] = result.TotalCount
            };

            return Ok(new PerformanceResult
            {
                TestName = "Having Full Pipeline - WHERE + GROUP + HAVING + ORDER + PAGE",
                Metrics = metrics, Input = summary, Output = result.Data, Success = true,
                Message = "Complete pipeline: WHERE Price > 0 → GROUP BY IsActive → HAVING Count > 1 → ORDER BY TotalValue DESC → PAGE 1/10"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestHavingFullPipeline");
            return Ok(new PerformanceResult
            {
                TestName = "Having Full Pipeline - WHERE + GROUP + HAVING + ORDER + PAGE",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    /// <summary>
    /// Having: Orders — group by Status, having with high order count.
    /// </summary>
    [HttpGet("having/orders")]
    public async Task<ActionResult<PerformanceResult>> TestHavingOrders()
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
                        new AggregateBy { Field = "TotalAmount", Alias = "TotalRevenue", Aggregator = Aggregator.Sumation }
                    ]
                },
                Having = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "OrderCount", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["2"] }
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
                TestName = "Having Orders - Group Status HAVING OrderCount > 2",
                Metrics = metrics, Input = summary, Output = results, Success = true,
                Message = "Orders having filter on group count"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestHavingOrders");
            return Ok(new PerformanceResult
            {
                TestName = "Having Orders - Group Status HAVING OrderCount > 2",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    /// <summary>
    /// Having: Customers — group by Tier, having with high spending.
    /// </summary>
    [HttpGet("having/customers")]
    public async Task<ActionResult<PerformanceResult>> TestHavingCustomers()
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
                        new AggregateBy { Field = "TotalSpent", Alias = "AvgSpending", Aggregator = Aggregator.Average }
                    ]
                },
                Having = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "AvgSpending", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["100"] }
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
                TestName = "Having Customers - Group Tier HAVING AvgSpending > 100",
                Metrics = metrics, Input = summary, Output = results, Success = true,
                Message = "Customers having filter on average spending"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestHavingCustomers");
            return Ok(new PerformanceResult
            {
                TestName = "Having Customers - Group Tier HAVING AvgSpending > 100",
                Success = false, Message = ex.Message, Metrics = new PerformanceMetrics()
            });
        }
    }

    #endregion

    #region Run All Tests

    /// <summary>
    /// Run all Group and Having tests and return aggregated results.
    /// </summary>
    [HttpGet("all")]
    public async Task<ActionResult<AllTestsResult>> TestAll()
    {
        var allResults = new List<PerformanceResult>();
        var overallSw = Stopwatch.StartNew();

        var testMethods = new List<(string name, Func<Task<ActionResult<PerformanceResult>>> method)>
        {
            // Group tests
            ("Group Simple",               TestGroupSimple),
            ("Group Multiple Aggregations", TestGroupMultipleFields),
            ("Group All Aggregations",     TestGroupAllAggregations),
            ("Group Count Only",           TestGroupCountOnly),
            ("Group CountDistinct",        TestGroupCountDistinct),
            ("Group First/Last",           TestGroupFirstLast),
            ("Group Rating Analysis",      TestGroupRatingAnalysis),
            ("Group Orders By Status",     TestGroupOrdersByStatus),
            ("Group Orders By Payment",    TestGroupOrdersByPayment),
            ("Group Customers By Gender",  TestGroupCustomersByGender),
            ("Group Customers By Tier",    TestGroupCustomersByTier),
            // Having tests
            ("Having Simple",           TestHavingSimple),
            ("Having With Filter",      TestHavingWithFilter),
            ("Having AND",              TestHavingAndConditions),
            ("Having Nested Group",     TestHavingNestedGroup),
            ("Having Full Pipeline",    TestHavingFullPipeline),
            ("Having Orders",           TestHavingOrders),
            ("Having Customers",        TestHavingCustomers)
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
                _logger.LogError(ex, "Error executing Group/Having test: {name}", name);
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
