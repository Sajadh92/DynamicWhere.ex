using DynamicWhere.API.Data;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Source;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace DynamicWhere.API.Controllers;

/// <summary>
/// Controller for testing Segment (ToListAsync(Segment)) extension method.
/// Segments apply set operations (Union, Intersect, Except) between ConditionSets,
/// then optionally apply Select, Order, and Page on the result.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SegmentTestController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<SegmentTestController> _logger;

    public SegmentTestController(AppDbContext context, ILogger<SegmentTestController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Segment: Union — combine two condition sets.
    /// </summary>
    [HttpGet("union")]
    public async Task<ActionResult<PerformanceResult>> TestUnion()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var segment = new Segment
            {
                ConditionSets =
                [
                    new ConditionSet
                    {
                        Sort = 1,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "Price", DataType = DataType.Number, Operator = Operator.LessThan, Values = ["30"] }
                            ]
                        }
                    },
                    new ConditionSet
                    {
                        Sort = 2,
                        Intersection = Intersection.Union,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "Rating", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = ["4.5"] }
                            ]
                        }
                    }
                ]
            };

            sw.Restart();
            var result = await _context.Products.ToListAsync(segment);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object> { ["TotalCount"] = result.TotalCount };

            return Ok(new PerformanceResult
            {
                TestName = "Segment Union - Budget UNION TopRated",
                Metrics = metrics, Input = segment, Output = result, Success = true,
                Message = $"Union: {result.Data.Count} of {result.TotalCount} products"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestUnion");
            return Ok(new PerformanceResult { TestName = "Segment Union", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Segment: Intersect — items common to both sets.
    /// </summary>
    [HttpGet("intersect")]
    public async Task<ActionResult<PerformanceResult>> TestIntersect()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var segment = new Segment
            {
                ConditionSets =
                [
                    new ConditionSet
                    {
                        Sort = 1,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] }
                            ]
                        }
                    },
                    new ConditionSet
                    {
                        Sort = 2,
                        Intersection = Intersection.Intersect,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "Price", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["50"] }
                            ]
                        }
                    }
                ]
            };

            sw.Restart();
            var result = await _context.Products.ToListAsync(segment);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object> { ["TotalCount"] = result.TotalCount };

            return Ok(new PerformanceResult
            {
                TestName = "Segment Intersect - Active INTERSECT Premium",
                Metrics = metrics, Input = segment, Output = result, Success = true,
                Message = $"Intersect: {result.Data.Count} of {result.TotalCount} products"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestIntersect");
            return Ok(new PerformanceResult { TestName = "Segment Intersect", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Segment: Except — remove items from first set that exist in second set.
    /// </summary>
    [HttpGet("except")]
    public async Task<ActionResult<PerformanceResult>> TestExcept()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var segment = new Segment
            {
                ConditionSets =
                [
                    new ConditionSet
                    {
                        Sort = 1,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] }
                            ]
                        }
                    },
                    new ConditionSet
                    {
                        Sort = 2,
                        Intersection = Intersection.Except,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "Price", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["200"] }
                            ]
                        }
                    }
                ]
            };

            sw.Restart();
            var result = await _context.Products.ToListAsync(segment);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object> { ["TotalCount"] = result.TotalCount };

            return Ok(new PerformanceResult
            {
                TestName = "Segment Except - Active EXCEPT Expensive",
                Metrics = metrics, Input = segment, Output = result, Success = true,
                Message = $"Except: {result.Data.Count} of {result.TotalCount} products"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestExcept");
            return Ok(new PerformanceResult { TestName = "Segment Except", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Segment: chained operations — Union then Except.
    /// </summary>
    [HttpGet("union-except")]
    public async Task<ActionResult<PerformanceResult>> TestUnionExcept()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var segment = new Segment
            {
                ConditionSets =
                [
                    new ConditionSet
                    {
                        Sort = 1,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "Price", DataType = DataType.Number, Operator = Operator.LessThan, Values = ["50"] }
                            ]
                        }
                    },
                    new ConditionSet
                    {
                        Sort = 2,
                        Intersection = Intersection.Union,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "Rating", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = ["4"] }
                            ]
                        }
                    },
                    new ConditionSet
                    {
                        Sort = 3,
                        Intersection = Intersection.Except,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["false"] }
                            ]
                        }
                    }
                ]
            };

            sw.Restart();
            var result = await _context.Products.ToListAsync(segment);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object> { ["TotalCount"] = result.TotalCount };

            return Ok(new PerformanceResult
            {
                TestName = "Segment Union+Except - (Budget UNION TopRated) EXCEPT Inactive",
                Metrics = metrics, Input = segment, Output = result, Success = true,
                Message = $"Chained set ops: {result.Data.Count} of {result.TotalCount} products"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestUnionExcept");
            return Ok(new PerformanceResult { TestName = "Segment Union+Except", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Segment: with selects (field projection on segment result).
    /// </summary>
    [HttpGet("with-selects")]
    public async Task<ActionResult<PerformanceResult>> TestWithSelects()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var segment = new Segment
            {
                ConditionSets =
                [
                    new ConditionSet
                    {
                        Sort = 1,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] }
                            ]
                        }
                    },
                    new ConditionSet
                    {
                        Sort = 2,
                        Intersection = Intersection.Intersect,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "Price", DataType = DataType.Number, Operator = Operator.Between, Values = ["10", "100"] }
                            ]
                        }
                    }
                ],
                Selects = ["Id", "Name", "Price"],
                Orders = [new OrderBy { Sort = 1, Field = "Name", Direction = Direction.Ascending }]
            };

            sw.Restart();
            var result = await _context.Products.ToListAsync(segment);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object> { ["TotalCount"] = result.TotalCount };

            return Ok(new PerformanceResult
            {
                TestName = "Segment With Selects - Active INTERSECT PriceRange, project Id/Name/Price",
                Metrics = metrics, Input = segment, Output = result, Success = true,
                Message = $"Segment with select: {result.Data.Count} of {result.TotalCount}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestWithSelects");
            return Ok(new PerformanceResult { TestName = "Segment With Selects", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Segment: full pipeline — Union + Except + Order + Page.
    /// </summary>
    [HttpGet("full-pipeline")]
    public async Task<ActionResult<PerformanceResult>> TestFullPipeline()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var segment = new Segment
            {
                ConditionSets =
                [
                    new ConditionSet
                    {
                        Sort = 1,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "Price", DataType = DataType.Number, Operator = Operator.LessThan, Values = ["50"] }
                            ]
                        }
                    },
                    new ConditionSet
                    {
                        Sort = 2,
                        Intersection = Intersection.Union,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "Rating", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = ["4"] }
                            ]
                        }
                    },
                    new ConditionSet
                    {
                        Sort = 3,
                        Intersection = Intersection.Except,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["false"] }
                            ]
                        }
                    }
                ],
                Selects = ["Id", "Name", "Price", "Rating"],
                Orders =
                [
                    new OrderBy { Sort = 1, Field = "Price", Direction = Direction.Descending },
                    new OrderBy { Sort = 2, Field = "Name", Direction = Direction.Ascending }
                ],
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var result = await _context.Products.ToListAsync(segment);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                ["TotalCount"] = result.TotalCount,
                ["PageNumber"] = result.PageNumber,
                ["PageSize"] = result.PageSize,
                ["PageCount"] = result.PageCount
            };

            return Ok(new PerformanceResult
            {
                TestName = "Segment Full Pipeline - Union+Except + Select + Order + Page",
                Metrics = metrics, Input = segment, Output = result, Success = true,
                Message = $"Full segment pipeline: {result.Data.Count} of {result.TotalCount}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestFullPipeline");
            return Ok(new PerformanceResult { TestName = "Segment Full Pipeline", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Segment: Orders — Union of status conditions with pagination.
    /// </summary>
    [HttpGet("orders")]
    public async Task<ActionResult<PerformanceResult>> TestSegmentOrders()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var segment = new Segment
            {
                ConditionSets =
                [
                    new ConditionSet
                    {
                        Sort = 1,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "Status", DataType = DataType.Enum, Operator = Operator.Equal, Values = ["Pending"] }
                            ]
                        }
                    },
                    new ConditionSet
                    {
                        Sort = 2,
                        Intersection = Intersection.Union,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "Status", DataType = DataType.Enum, Operator = Operator.Equal, Values = ["Processing"] }
                            ]
                        }
                    },
                    new ConditionSet
                    {
                        Sort = 3,
                        Intersection = Intersection.Except,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition { Sort = 1, Field = "IsPaid", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["false"] }
                            ]
                        }
                    }
                ],
                Orders = [new OrderBy { Sort = 1, Field = "OrderDate", Direction = Direction.Descending }],
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var result = await _context.Orders.ToListAsync(segment);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object> { ["TotalCount"] = result.TotalCount };

            return Ok(new PerformanceResult
            {
                TestName = "Segment Orders - (Pending UNION Processing) EXCEPT Unpaid",
                Metrics = metrics, Input = segment, Output = result, Success = true,
                Message = $"Orders segment: {result.Data.Count} of {result.TotalCount}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSegmentOrders");
            return Ok(new PerformanceResult { TestName = "Segment Orders", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    #region Run All Tests

    /// <summary>
    /// Run all Segment tests and return aggregated results.
    /// </summary>
    [HttpGet("all")]
    public async Task<ActionResult<AllTestsResult>> TestAll()
    {
        var allResults = new List<PerformanceResult>();
        var overallSw = Stopwatch.StartNew();

        var testMethods = new List<(string name, Func<Task<ActionResult<PerformanceResult>>> method)>
        {
            ("Segment Union",        TestUnion),
            ("Segment Intersect",    TestIntersect),
            ("Segment Except",       TestExcept),
            ("Segment Union+Except", TestUnionExcept),
            ("Segment With Selects", TestWithSelects),
            ("Segment Full Pipeline",TestFullPipeline),
            ("Segment Orders",       TestSegmentOrders)
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
                _logger.LogError(ex, "Error executing Segment test: {name}", name);
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
