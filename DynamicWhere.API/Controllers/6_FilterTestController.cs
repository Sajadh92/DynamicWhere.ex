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
/// Controller for testing all Filter extension methods:
/// Filter, FilterDynamic, ToList(Filter), ToListDynamic(Filter),
/// ToList(IEnumerable,Filter), ToListDynamic(IEnumerable,Filter),
/// ToListAsync(Filter), ToListAsyncDynamic(Filter).
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class FilterTestController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<FilterTestController> _logger;

    public FilterTestController(AppDbContext context, ILogger<FilterTestController> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Filter<T> (IQueryable)

    /// <summary>
    /// Filter: simple — where only (no order/page/select).
    /// </summary>
    [HttpGet("filter/simple")]
    public async Task<ActionResult<PerformanceResult>> TestFilterSimple()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] }
                    ]
                }
            };

            sw.Restart();
            var query = _context.Products.Filter(filter);
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
                TestName = "Filter Simple - IsActive only",
                Metrics = metrics, Input = filter, Output = results, Success = true,
                Message = $"Simple filter returned {results.Count} active products"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestFilterSimple");
            return Ok(new PerformanceResult { TestName = "Filter Simple", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Filter: with order (where + order).
    /// </summary>
    [HttpGet("filter/with-order")]
    public async Task<ActionResult<PerformanceResult>> TestFilterWithOrder()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "Price", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["50"] }
                    ]
                },
                Orders =
                [
                    new OrderBy { Sort = 1, Field = "Price", Direction = Direction.Descending }
                ]
            };

            sw.Restart();
            var query = _context.Products.Filter(filter);
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
                TestName = "Filter With Order - Price > 50, ordered by Price DESC",
                Metrics = metrics, Input = filter, Output = results, Success = true,
                Message = $"Filter with order returned {results.Count} products"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestFilterWithOrder");
            return Ok(new PerformanceResult { TestName = "Filter With Order", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Filter: with pagination (where + order + page).
    /// </summary>
    [HttpGet("filter/with-page")]
    public async Task<ActionResult<PerformanceResult>> TestFilterWithPage()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] }
                    ]
                },
                Orders =
                [
                    new OrderBy { Sort = 1, Field = "Name", Direction = Direction.Ascending }
                ],
                Page = new PageBy { PageNumber = 1, PageSize = 5 }
            };

            sw.Restart();
            var query = _context.Products.Filter(filter);
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
                TestName = "Filter With Page - Active, Name ASC, Page 1/5",
                Metrics = metrics, Input = filter, Output = results, Success = true,
                Message = $"Filter with pagination returned {results.Count} products"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestFilterWithPage");
            return Ok(new PerformanceResult { TestName = "Filter With Page", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Filter: with select (field projection using typed Select).
    /// </summary>
    [HttpGet("filter/with-select")]
    public async Task<ActionResult<PerformanceResult>> TestFilterWithSelect()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] }
                    ]
                },
                Selects = ["Id", "Name", "Price"]
            };

            sw.Restart();
            var query = _context.Products.Filter(filter);
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
                TestName = "Filter With Select - Active, project Id/Name/Price",
                Metrics = metrics, Input = filter, Output = results, Success = true,
                Message = $"Filter with typed select projection returned {results.Count} products"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestFilterWithSelect");
            return Ok(new PerformanceResult { TestName = "Filter With Select", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Filter: AND conditions.
    /// </summary>
    [HttpGet("filter/and-conditions")]
    public async Task<ActionResult<PerformanceResult>> TestFilterAndConditions()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] },
                        new Condition { Sort = 2, Field = "Price", DataType = DataType.Number, Operator = Operator.Between, Values = ["10", "200"] },
                        new Condition { Sort = 3, Field = "Rating", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = ["3"] }
                    ]
                },
                Orders = [new OrderBy { Sort = 1, Field = "Rating", Direction = Direction.Descending }],
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var query = _context.Products.Filter(filter);
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
                TestName = "Filter AND Conditions - Active, Price 10-200, Rating >= 3",
                Metrics = metrics, Input = filter, Output = results, Success = true,
                Message = $"AND filter with order + page returned {results.Count} products"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestFilterAndConditions");
            return Ok(new PerformanceResult { TestName = "Filter AND Conditions", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Filter: OR conditions.
    /// </summary>
    [HttpGet("filter/or-conditions")]
    public async Task<ActionResult<PerformanceResult>> TestFilterOrConditions()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.Or,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "Price", DataType = DataType.Number, Operator = Operator.LessThan, Values = ["25"] },
                        new Condition { Sort = 2, Field = "Rating", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = ["4.5"] }
                    ]
                }
            };

            sw.Restart();
            var query = _context.Products.Filter(filter);
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
                TestName = "Filter OR Conditions - Budget OR Top-Rated",
                Metrics = metrics, Input = filter, Output = results, Success = true,
                Message = $"OR filter returned {results.Count} products"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestFilterOrConditions");
            return Ok(new PerformanceResult { TestName = "Filter OR Conditions", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Filter: nested groups.
    /// </summary>
    [HttpGet("filter/nested-groups")]
    public async Task<ActionResult<PerformanceResult>> TestFilterNestedGroups()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
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
                                new Condition { Sort = 1, Field = "Price", DataType = DataType.Number, Operator = Operator.LessThan, Values = ["30"] },
                                new Condition { Sort = 2, Field = "Price", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["200"] }
                            ]
                        }
                    ]
                },
                Orders = [new OrderBy { Sort = 1, Field = "Price", Direction = Direction.Ascending }]
            };

            sw.Restart();
            var query = _context.Products.Filter(filter);
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
                TestName = "Filter Nested Groups - Active AND (Budget OR Premium)",
                Metrics = metrics, Input = filter, Output = results, Success = true,
                Message = $"Nested filter returned {results.Count} products"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestFilterNestedGroups");
            return Ok(new PerformanceResult { TestName = "Filter Nested Groups", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Filter: full pipeline (where + select + order + page).
    /// </summary>
    [HttpGet("filter/full-pipeline")]
    public async Task<ActionResult<PerformanceResult>> TestFilterFullPipeline()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] },
                        new Condition { Sort = 2, Field = "Price", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["10"] }
                    ]
                },
                Selects = ["Id", "Name", "Price", "Rating"],
                Orders =
                [
                    new OrderBy { Sort = 1, Field = "Price", Direction = Direction.Descending },
                    new OrderBy { Sort = 2, Field = "Name", Direction = Direction.Ascending }
                ],
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var query = _context.Products.Filter(filter);
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
                TestName = "Filter Full Pipeline - Where + Select + Order + Page",
                Metrics = metrics, Input = filter, Output = results, Success = true,
                Message = "Complete typed filter pipeline"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestFilterFullPipeline");
            return Ok(new PerformanceResult { TestName = "Filter Full Pipeline", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    #endregion

    #region FilterDynamic<T>

    /// <summary>
    /// FilterDynamic: with select (dynamic projection).
    /// </summary>
    [HttpGet("filter-dynamic/with-select")]
    public async Task<ActionResult<PerformanceResult>> TestFilterDynamicWithSelect()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] }
                    ]
                },
                Selects = ["Id", "Name", "Price"]
            };

            sw.Restart();
            var query = _context.Products.FilterDynamic(filter);
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
                TestName = "FilterDynamic With Select - Active, project Id/Name/Price",
                Metrics = metrics, Input = filter, Output = results, Success = true,
                Message = "Dynamic filter with select projection"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestFilterDynamicWithSelect");
            return Ok(new PerformanceResult { TestName = "FilterDynamic With Select", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// FilterDynamic: with page.
    /// </summary>
    [HttpGet("filter-dynamic/with-page")]
    public async Task<ActionResult<PerformanceResult>> TestFilterDynamicWithPage()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "Price", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["10"] }
                    ]
                },
                Selects = ["Id", "Name", "Price"],
                Orders = [new OrderBy { Sort = 1, Field = "Price", Direction = Direction.Descending }],
                Page = new PageBy { PageNumber = 1, PageSize = 5 }
            };

            sw.Restart();
            var query = _context.Products.FilterDynamic(filter);
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
                TestName = "FilterDynamic With Page - Price > 10, Page 1/5",
                Metrics = metrics, Input = filter, Output = results, Success = true,
                Message = "Dynamic filter with ordering and pagination"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestFilterDynamicWithPage");
            return Ok(new PerformanceResult { TestName = "FilterDynamic With Page", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// FilterDynamic: full pipeline (where + select + order + page).
    /// </summary>
    [HttpGet("filter-dynamic/full-pipeline")]
    public async Task<ActionResult<PerformanceResult>> TestFilterDynamicFullPipeline()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] },
                        new Condition { Sort = 2, Field = "Name", DataType = DataType.Text, Operator = Operator.IContains, Values = ["pro"] }
                    ]
                },
                Selects = ["Id", "Name", "Price", "Rating"],
                Orders =
                [
                    new OrderBy { Sort = 1, Field = "Price", Direction = Direction.Descending }
                ],
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var query = _context.Products.FilterDynamic(filter);
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
                TestName = "FilterDynamic Full Pipeline - Where + Select + Order + Page",
                Metrics = metrics, Input = filter, Output = results, Success = true,
                Message = "Complete dynamic filter pipeline"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestFilterDynamicFullPipeline");
            return Ok(new PerformanceResult { TestName = "FilterDynamic Full Pipeline", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    #endregion

    #region ToList (Sync)

    /// <summary>
    /// ToList sync: basic.
    /// </summary>
    [HttpGet("tolist/sync")]
    public ActionResult<PerformanceResult> TestToListSync()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] }
                    ]
                },
                Orders = [new OrderBy { Sort = 1, Field = "Name", Direction = Direction.Ascending }],
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var result = _context.Products.ToList(filter);
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
                TestName = "ToList Sync - Active, Name ASC, Page 1/10",
                Metrics = metrics, Input = filter, Output = result, Success = true,
                Message = $"Sync ToList: {result.Data.Count} of {result.TotalCount} total"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListSync");
            return Ok(new PerformanceResult { TestName = "ToList Sync", Success = false, Message = ex.Message, Metrics = metrics });
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
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "Price", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["50"] }
                    ]
                },
                Page = new PageBy { PageNumber = 1, PageSize = 5 }
            };

            sw.Restart();
            var result = _context.Products.ToList(filter, getQueryString: true);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.QueryGenerated = result.QueryString;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                ["TotalCount"] = result.TotalCount, ["HasQueryString"] = result.QueryString != null
            };

            return Ok(new PerformanceResult
            {
                TestName = "ToList Sync With QueryString",
                Metrics = metrics, Input = filter, Output = result, Success = true,
                Message = $"QueryString included: {result.QueryString != null}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListSyncWithQueryString");
            return Ok(new PerformanceResult { TestName = "ToList Sync QueryString", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// ToList sync: Dynamic (ToListDynamic).
    /// </summary>
    [HttpGet("tolist/sync-dynamic")]
    public ActionResult<PerformanceResult> TestToListSyncDynamic()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] }
                    ]
                },
                Selects = ["Id", "Name", "Price"],
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var result = _context.Products.ToListDynamic(filter);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                ["TotalCount"] = result.TotalCount, ["PageCount"] = result.PageCount
            };

            return Ok(new PerformanceResult
            {
                TestName = "ToList Sync Dynamic - Active, Select Id/Name/Price",
                Metrics = metrics, Input = filter, Output = result, Success = true,
                Message = $"Sync dynamic ToList: {result.Data.Count} of {result.TotalCount} total"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListSyncDynamic");
            return Ok(new PerformanceResult { TestName = "ToList Sync Dynamic", Success = false, Message = ex.Message, Metrics = metrics });
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
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] }
                    ]
                },
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var data = await _context.Products.ToListAsync();
            var loadTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var result = data.ToList(filter);
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
                TestName = "ToList In-Memory - IEnumerable<T> overload",
                Metrics = metrics, Input = filter, Output = result, Success = true,
                Message = $"In-memory filter on {data.Count} items, returned {result.Data.Count}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListInMemory");
            return Ok(new PerformanceResult { TestName = "ToList In-Memory", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// ToList in-memory dynamic: IEnumerable overload with dynamic select.
    /// </summary>
    [HttpGet("tolist/in-memory-dynamic")]
    public async Task<ActionResult<PerformanceResult>> TestToListInMemoryDynamic()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] }
                    ]
                },
                Selects = ["Id", "Name", "Price"],
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var data = await _context.Products.ToListAsync();
            var loadTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var result = data.ToListDynamic(filter);
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
                TestName = "ToList In-Memory Dynamic - IEnumerable<dynamic> overload",
                Metrics = metrics, Input = filter, Output = result, Success = true,
                Message = $"In-memory dynamic filter on {data.Count} items, returned {result.Data.Count}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListInMemoryDynamic");
            return Ok(new PerformanceResult { TestName = "ToList In-Memory Dynamic", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    #endregion

    #region ToListAsync / ToListAsyncDynamic

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
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] }
                    ]
                },
                Orders = [new OrderBy { Sort = 1, Field = "Name", Direction = Direction.Ascending }],
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var result = await _context.Products.ToListAsync(filter);
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
                TestName = "ToListAsync - Active, Name ASC, Page 1/10",
                Metrics = metrics, Input = filter, Output = result, Success = true,
                Message = $"Async ToList: {result.Data.Count} of {result.TotalCount} total"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListAsync");
            return Ok(new PerformanceResult { TestName = "ToListAsync", Success = false, Message = ex.Message, Metrics = metrics });
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
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "Rating", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = ["4"] }
                    ]
                },
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var result = await _context.Products.ToListAsync(filter, getQueryString: true);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.QueryGenerated = result.QueryString;

            return Ok(new PerformanceResult
            {
                TestName = "ToListAsync With QueryString",
                Metrics = metrics, Input = filter, Output = result, Success = true,
                Message = $"Async with QueryString: {result.QueryString != null}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListAsyncWithQueryString");
            return Ok(new PerformanceResult { TestName = "ToListAsync QueryString", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// ToListAsyncDynamic: dynamic select async.
    /// </summary>
    [HttpGet("tolist/async-dynamic")]
    public async Task<ActionResult<PerformanceResult>> TestToListAsyncDynamic()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] }
                    ]
                },
                Selects = ["Id", "Name", "Price", "Rating"],
                Orders = [new OrderBy { Sort = 1, Field = "Price", Direction = Direction.Descending }],
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var result = await _context.Products.ToListAsyncDynamic(filter);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                ["TotalCount"] = result.TotalCount, ["PageCount"] = result.PageCount
            };

            return Ok(new PerformanceResult
            {
                TestName = "ToListAsyncDynamic - Active, Select Id/Name/Price/Rating",
                Metrics = metrics, Input = filter, Output = result, Success = true,
                Message = $"Async dynamic ToList: {result.Data.Count} of {result.TotalCount} total"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListAsyncDynamic");
            return Ok(new PerformanceResult { TestName = "ToListAsyncDynamic", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    #endregion

    #region Multi-Entity

    /// <summary>
    /// Filter on Orders: status filter.
    /// </summary>
    [HttpGet("orders/status-filter")]
    public async Task<ActionResult<PerformanceResult>> TestOrdersStatusFilter()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "Status", DataType = DataType.Enum, Operator = Operator.In, Values = ["Pending", "Processing"] }
                    ]
                },
                Orders = [new OrderBy { Sort = 1, Field = "OrderDate", Direction = Direction.Descending }],
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var result = await _context.Orders.ToListAsync(filter);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object> { ["TotalCount"] = result.TotalCount };

            return Ok(new PerformanceResult
            {
                TestName = "Orders Status Filter - Pending/Processing",
                Metrics = metrics, Input = filter, Output = result, Success = true,
                Message = $"Orders filter: {result.Data.Count} of {result.TotalCount}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestOrdersStatusFilter");
            return Ok(new PerformanceResult { TestName = "Orders Status Filter", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Filter on Orders: date range.
    /// </summary>
    [HttpGet("orders/date-range")]
    public async Task<ActionResult<PerformanceResult>> TestOrdersDateRange()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "OrderDate", DataType = DataType.DateTime, Operator = Operator.Between, Values = ["2023-01-01T00:00:00", "2024-12-31T23:59:59"] }
                    ]
                },
                Orders = [new OrderBy { Sort = 1, Field = "TotalAmount", Direction = Direction.Descending }],
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var result = await _context.Orders.ToListAsync(filter);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object> { ["TotalCount"] = result.TotalCount };

            return Ok(new PerformanceResult
            {
                TestName = "Orders Date Range - 2023-2024",
                Metrics = metrics, Input = filter, Output = result, Success = true,
                Message = $"Orders date range: {result.Data.Count} of {result.TotalCount}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestOrdersDateRange");
            return Ok(new PerformanceResult { TestName = "Orders Date Range", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Filter on Orders: nested address property.
    /// </summary>
    [HttpGet("orders/nested-address")]
    public async Task<ActionResult<PerformanceResult>> TestOrdersNestedAddress()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "ShippingAddress.Country", DataType = DataType.Text, Operator = Operator.IEqual, Values = ["USA"] }
                    ]
                },
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var result = await _context.Orders.ToListAsync(filter);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object> { ["TotalCount"] = result.TotalCount };

            return Ok(new PerformanceResult
            {
                TestName = "Orders Nested Address - ShippingAddress.Country = USA",
                Metrics = metrics, Input = filter, Output = result, Success = true,
                Message = $"Nested address filter: {result.Data.Count} of {result.TotalCount}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestOrdersNestedAddress");
            return Ok(new PerformanceResult { TestName = "Orders Nested Address", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Filter on Customers: text search.
    /// </summary>
    [HttpGet("customers/text-search")]
    public async Task<ActionResult<PerformanceResult>> TestCustomersTextSearch()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.Or,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "FirstName", DataType = DataType.Text, Operator = Operator.IContains, Values = ["john"] },
                        new Condition { Sort = 2, Field = "LastName", DataType = DataType.Text, Operator = Operator.IContains, Values = ["john"] }
                    ]
                },
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var result = await _context.Customers.ToListAsync(filter);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object> { ["TotalCount"] = result.TotalCount };

            return Ok(new PerformanceResult
            {
                TestName = "Customers Text Search - FirstName/LastName IContains john",
                Metrics = metrics, Input = filter, Output = result, Success = true,
                Message = $"Customer text search: {result.Data.Count} of {result.TotalCount}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestCustomersTextSearch");
            return Ok(new PerformanceResult { TestName = "Customers Text Search", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    /// <summary>
    /// Filter on Customers: spending filter with tier.
    /// </summary>
    [HttpGet("customers/spending-filter")]
    public async Task<ActionResult<PerformanceResult>> TestCustomersSpendingFilter()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var filter = new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Connector = Connector.And,
                    Conditions =
                    [
                        new Condition { Sort = 1, Field = "TotalSpent", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["500"] },
                        new Condition { Sort = 2, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"] }
                    ]
                },
                Orders = [new OrderBy { Sort = 1, Field = "TotalSpent", Direction = Direction.Descending }],
                Page = new PageBy { PageNumber = 1, PageSize = 10 }
            };

            sw.Restart();
            var result = await _context.Customers.ToListAsync(filter);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = result.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object> { ["TotalCount"] = result.TotalCount };

            return Ok(new PerformanceResult
            {
                TestName = "Customers Spending Filter - Active, TotalSpent > 500",
                Metrics = metrics, Input = filter, Output = result, Success = true,
                Message = $"Customers spending filter: {result.Data.Count} of {result.TotalCount}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestCustomersSpendingFilter");
            return Ok(new PerformanceResult { TestName = "Customers Spending Filter", Success = false, Message = ex.Message, Metrics = metrics });
        }
    }

    #endregion

    #region Run All Tests

    /// <summary>
    /// Run all Filter tests and return aggregated results.
    /// </summary>
    [HttpGet("all")]
    public async Task<ActionResult<AllTestsResult>> TestAll()
    {
        var allResults = new List<PerformanceResult>();
        var overallSw = Stopwatch.StartNew();

        var testMethods = new List<(string name, Func<Task<ActionResult<PerformanceResult>>> method)>
        {
            // Filter IQueryable
            ("Filter Simple",           TestFilterSimple),
            ("Filter With Order",       TestFilterWithOrder),
            ("Filter With Page",        TestFilterWithPage),
            ("Filter With Select",      TestFilterWithSelect),
            ("Filter AND Conditions",   TestFilterAndConditions),
            ("Filter OR Conditions",    TestFilterOrConditions),
            ("Filter Nested Groups",    TestFilterNestedGroups),
            ("Filter Full Pipeline",    TestFilterFullPipeline),
            // FilterDynamic
            ("FilterDynamic Select",    TestFilterDynamicWithSelect),
            ("FilterDynamic Page",      TestFilterDynamicWithPage),
            ("FilterDynamic Full",      TestFilterDynamicFullPipeline),
            // ToList sync
            ("ToList Sync",             () => Task.FromResult(TestToListSync())),
            ("ToList Sync QueryString", () => Task.FromResult(TestToListSyncWithQueryString())),
            ("ToList Sync Dynamic",     () => Task.FromResult(TestToListSyncDynamic())),
            // ToList in-memory
            ("ToList In-Memory",        TestToListInMemory),
            ("ToList In-Memory Dynamic",TestToListInMemoryDynamic),
            // ToList async
            ("ToListAsync",             TestToListAsync),
            ("ToListAsync QueryString", TestToListAsyncWithQueryString),
            ("ToListAsyncDynamic",      TestToListAsyncDynamic),
            // Multi-entity
            ("Orders Status Filter",    TestOrdersStatusFilter),
            ("Orders Date Range",       TestOrdersDateRange),
            ("Orders Nested Address",   TestOrdersNestedAddress),
            ("Customers Text Search",   TestCustomersTextSearch),
            ("Customers Spending",      TestCustomersSpendingFilter)
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
                _logger.LogError(ex, "Error executing Filter test: {name}", name);
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
