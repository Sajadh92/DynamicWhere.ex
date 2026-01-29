using DynamicWhere.API.Data;
using DynamicWhere.ex;
using DynamicWhere.ex.Classes;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Source;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Diagnostics;

namespace DynamicWhere.API.Controllers;

/// <summary>
/// Controller for testing DynamicWhere extension methods with performance metrics
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PerformanceTestController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<PerformanceTestController> _logger;

    public PerformanceTestController(AppDbContext context, ILogger<PerformanceTestController> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Test Select Extension

    /// <summary>
    /// Test Select extension with field projection
    /// </summary>
    [HttpGet("select/simple")]
    public async Task<ActionResult<PerformanceResult>> TestSelectSimple()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            // Initialize test data
            var fields = new List<string> { "Id", "Name", "Price", "Rating", "IsActive" };

            // Measure translation time
            sw.Restart();
            var query = _context.Products.Select(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            // Measure database execution time
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
                TestName = "Select Simple - Field Projection",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = $"Successfully projected {fields.Count} fields from {results.Count} products"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectSimple");
            return Ok(new PerformanceResult
            {
                TestName = "Select Simple - Field Projection",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Select extension with complex field projection including nested properties
    /// </summary>
    [HttpGet("select/complex")]
    public async Task<ActionResult<PerformanceResult>> TestSelectComplex()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string>
            {
                "Id", // direct prop
                "Name", // direct prop 
                "Tags", // direct list of string 
                "Category.Name", // neasted prop
                "OrderItems.Quantity" // neasted prop inside list 
            };
            sw.Restart();
            var query = _context.Products
                .Include(p => p.Category)
                .Include(p => p.OrderItems)
                .Select(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(50).ToListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Select Complex - Nested Properties",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectComplex");
            return Ok(new PerformanceResult
            {
                TestName = "Select Complex - Nested Properties",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    #endregion

    #region Test Where with Condition Extension

    /// <summary>
    /// Test Where extension with simple condition
    /// </summary>
    [HttpGet("where/simple")]
    public async Task<ActionResult<PerformanceResult>> TestWhereSimple()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var condition = new Condition
            {
                Field = "Price",
                DataType = DataType.Number,
                Operator = Operator.GreaterThan,
                Values = ["50"]
            };

            sw.Restart();
            var query = _context.Products.Where(condition);
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
                TestName = "Where Simple - Single Condition",
                Metrics = metrics,
                Input = condition,
                Output = results,
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestWhereSimple");
            return Ok(new PerformanceResult
            {
                TestName = "Where Simple - Single Condition",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Where extension with complex condition (case-insensitive text search)
    /// </summary>
    [HttpGet("where/text-search")]
    public async Task<ActionResult<PerformanceResult>> TestWhereTextSearch()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var condition = new Condition
            {
                Field = "Name",
                DataType = DataType.Text,
                Operator = Operator.Contains,
                Values = ["Pro"]
            };

            sw.Restart();
            var query = _context.Products.Where(condition);
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
                TestName = "Where Text Search - Case Sensitive Contains",
                Metrics = metrics,
                Input = condition,
                Output = results,
                Success = true,
                Message = "Using case-sensitive Contains instead of IContains due to EF.Functions limitation"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestWhereTextSearch");
            return Ok(new PerformanceResult
            {
                TestName = "Where Text Search - Case Sensitive Contains",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Where extension with alternative case-insensitive search 
    /// </summary>
    [HttpGet("where/text-search-insensitive")]
    public async Task<ActionResult<PerformanceResult>> TestWhereTextSearchCaseInsensitive()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var condition = new Condition
            {
                Field = "Name",
                DataType = DataType.Text,
                Operator = Operator.IContains,  // Case-insensitive
                Values = ["pro"]  // lowercase will work with IContains
            };

            sw.Restart();
            var query = _context.Products.Where(condition);
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
                TestName = "Where Text Search - Case Insensitive (LOWER workaround)",
                Metrics = metrics,
                Input = condition,
                Output = results,
                Success = true,
                Message = "Using EF.Functions.ILike"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestWhereTextSearchCaseInsensitive");
            return Ok(new PerformanceResult
            {
                TestName = "Where Text Search - Case Insensitive (LOWER workaround)",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    #endregion

    #region Test Where with ConditionGroup Extension

    /// <summary>
    /// Test Where extension with ConditionGroup (AND logic)
    /// </summary>
    [HttpGet("where/group-and")]
    public async Task<ActionResult<PerformanceResult>> TestWhereGroupAnd()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var conditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions =
                [
                    new Condition
                    {
                        Field = "Price",
                        DataType = DataType.Number,
                        Operator = Operator.Between,
                        Values = ["20", "100"],
                        Sort = 1
                    },
                    new Condition
                    {
                        Field = "Rating",
                        DataType = DataType.Number,
                        Operator = Operator.GreaterThanOrEqual,
                        Values = ["3.5"],
                        Sort = 2
                    },
                    new Condition
                    {
                        Field = "IsActive",
                        DataType = DataType.Boolean,
                        Operator = Operator.Equal,
                        Values = ["true"],
                        Sort = 3
                    }
                ]
            };

            sw.Restart();
            var query = _context.Products.Where(conditionGroup);
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
                TestName = "Where ConditionGroup - AND Logic",
                Metrics = metrics,
                Input = conditionGroup,
                Output = results,
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestWhereGroupAnd");
            return Ok(new PerformanceResult
            {
                TestName = "Where ConditionGroup - AND Logic",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Where extension with nested ConditionGroups
    /// </summary>
    [HttpGet("where/group-nested")]
    public async Task<ActionResult<PerformanceResult>> TestWhereGroupNested()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var conditionGroup = new ConditionGroup
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
                                Field = "Price",
                                DataType = DataType.Number,
                                Operator = Operator.LessThan,
                                Values = ["30"],
                                Sort = 1
                            },
                            new Condition
                            {
                                Field = "Price",
                                DataType = DataType.Number,
                                Operator = Operator.GreaterThan,
                                Values = ["200"],
                                Sort = 2
                            }
                        ]
                    }
                ]
            };

            sw.Restart();
            var query = _context.Products.Where(conditionGroup);
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
                TestName = "Where ConditionGroup - Nested Groups",
                Metrics = metrics,
                Input = conditionGroup,
                Output = results,
                Success = true,
                Message = "Testing: IsActive = true AND (Price < 30 OR Price > 200)"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestWhereGroupNested");
            return Ok(new PerformanceResult
            {
                TestName = "Where ConditionGroup - Nested Groups",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    #endregion

    #region Test Order Extension

    /// <summary>
    /// Test Order extension with single sort
    /// </summary>
    [HttpGet("order/single")]
    public async Task<ActionResult<PerformanceResult>> TestOrderSingle()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var order = new OrderBy
            {
                Field = "Price",
                Direction = Direction.Descending,
                Sort = 1
            };

            sw.Restart();
            var query = _context.Products.Order(order);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(50).ToListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Order Single - Price Descending",
                Metrics = metrics,
                Input = order,
                Output = results,
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestOrderSingle");
            return Ok(new PerformanceResult
            {
                TestName = "Order Single - Price Descending",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test Order extension with multiple sorts
    /// </summary>
    [HttpGet("order/multiple")]
    public async Task<ActionResult<PerformanceResult>> TestOrderMultiple()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var orders = new List<OrderBy>
            {
                new() { Field = "IsActive", Direction = Direction.Descending, Sort = 1 },
                new() { Field = "Rating", Direction = Direction.Descending, Sort = 2 },
                new() { Field = "Price", Direction = Direction.Ascending, Sort = 3 },
                new() { Field = "Name", Direction = Direction.Ascending, Sort = 4 }
            };

            sw.Restart();
            var query = _context.Products.Order(orders);
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
                TestName = "Order Multiple - 4 Level Sort",
                Metrics = metrics,
                Input = orders,
                Output = results,
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestOrderMultiple");
            return Ok(new PerformanceResult
            {
                TestName = "Order Multiple - 4 Level Sort",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    #endregion

    #region Test Page Extension

    /// <summary>
    /// Test Page extension
    /// </summary>
    [HttpGet("page/basic")]
    public async Task<ActionResult<PerformanceResult>> TestPageBasic()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var page = new PageBy
            {
                PageNumber = 2,
                PageSize = 10
            };

            sw.Restart();
            var query = _context.Products.Page(page);
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
                TestName = "Page Basic - Page 2, Size 10",
                Metrics = metrics,
                Input = page,
                Output = results,
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestPageBasic");
            return Ok(new PerformanceResult
            {
                TestName = "Page Basic - Page 2, Size 10",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    #endregion

    #region Test Filter Extension (Combined)

    /// <summary>
    /// Test Filter extension with all features combined
    /// </summary>
    [HttpGet("filter/complete")]
    public async Task<ActionResult<PerformanceResult>> TestFilterComplete()
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
                        new Condition
                        {
                            Field = "Price",
                            DataType = DataType.Number,
                            Operator = Operator.Between,
                            Values = ["10", "500"],
                            Sort = 1
                        },
                        new Condition
                        {
                            Field = "IsActive",
                            DataType = DataType.Boolean,
                            Operator = Operator.Equal,
                            Values = ["true"],
                            Sort = 2
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
                                    Field = "Name",
                                    DataType = DataType.Text,
                                    Operator = Operator.IContains,
                                    Values = ["ultra"],
                                    Sort = 1
                                },
                                new Condition
                                {
                                    Field = "Rating",
                                    DataType = DataType.Number,
                                    Operator = Operator.GreaterThanOrEqual,
                                    Values = ["4.5"],
                                    Sort = 2
                                }
                            ]
                        }
                    ]
                },
                Selects =
                [
                    "Id", "Name", "Price", "Rating", "StockQuantity", "IsActive", "CreatedAt"
                ],
                Orders =
                [
                    new OrderBy { Field = "Rating", Direction = Direction.Descending, Sort = 1 },
                    new OrderBy { Field = "Price", Direction = Direction.Ascending, Sort = 2 }
                ],
                Page = new PageBy
                {
                    PageNumber = 1,
                    PageSize = 20
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
                TestName = "Filter Complete - Where + Select + Order + Page",
                Metrics = metrics,
                Input = filter,
                Output = results,
                Success = true,
                Message = "Testing complete filter with nested conditions, projection, sorting, and pagination"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestFilterComplete");
            return Ok(new PerformanceResult
            {
                TestName = "Filter Complete - Where + Select + Order + Page",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    #endregion

    #region Test ToList Extension

    /// <summary>
    /// Test ToList extension with Filter (synchronous)
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
                        new Condition
                        {
                            Field = "Price",
                            DataType = DataType.Number,
                            Operator = Operator.LessThan,
                            Values = ["100"],
                            Sort = 1
                        }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "Price", Direction = Direction.Ascending, Sort = 1 }
                ],
                Page = new PageBy { PageNumber = 1, PageSize = 15 }
            };

            sw.Restart();
            var results = _context.Products.ToList(filter);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = results.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                { "PageNumber", results.PageNumber },
                { "PageSize", results.PageSize },
                { "PageCount", results.PageCount },
                { "TotalCount", results.TotalCount }
            };

            return Ok(new PerformanceResult
            {
                TestName = "ToList Sync - FilterResult with Pagination",
                Metrics = metrics,
                Input = filter,
                Output = results,
                Success = true,
                Message = $"Retrieved page {results.PageNumber} of {results.PageCount} ({results.TotalCount} total records)"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListSync");
            return Ok(new PerformanceResult
            {
                TestName = "ToList Sync - FilterResult with Pagination",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test ToListAsync extension with Filter
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
                            Field = "StockQuantity",
                            DataType = DataType.Number,
                            Operator = Operator.GreaterThan,
                            Values = ["50"],
                            Sort = 2
                        }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "Rating", Direction = Direction.Descending, Sort = 1 },
                    new OrderBy { Field = "StockQuantity", Direction = Direction.Descending, Sort = 2 }
                ],
                Page = new PageBy { PageNumber = 1, PageSize = 25 }
            };

            sw.Restart();
            var results = await _context.Products.ToListAsync(filter);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = results.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                { "PageNumber", results.PageNumber },
                { "PageSize", results.PageSize },
                { "PageCount", results.PageCount },
                { "TotalCount", results.TotalCount }
            };

            return Ok(new PerformanceResult
            {
                TestName = "ToList Async - FilterResult with OR Conditions",
                Metrics = metrics,
                Input = filter,
                Output = results,
                Success = true,
                Message = $"Retrieved page {results.PageNumber} of {results.PageCount} ({results.TotalCount} total records)"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestToListAsync");
            return Ok(new PerformanceResult
            {
                TestName = "ToList Async - FilterResult with OR Conditions",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    #endregion

    #region Test Segment Extension

    /// <summary>
    /// Test ToListAsync with Segment (Union, Intersect, Except operations)
    /// </summary>
    [HttpGet("segment/intersections")]
    public async Task<ActionResult<PerformanceResult>> TestSegmentIntersections()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var segment = new Segment
            {
                ConditionSets =
                [
                    // First set: Products with price < 50
                    new ConditionSet
                    {
                        Sort = 1,
                        Intersection = null, // First set doesn't need intersection
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition
                                {
                                    Field = "Price",
                                    DataType = DataType.Number,
                                    Operator = Operator.LessThan,
                                    Values = ["50"],
                                    Sort = 1
                                },
                                new Condition
                                {
                                    Field = "IsActive",
                                    DataType = DataType.Boolean,
                                    Operator = Operator.Equal,
                                    Values = ["true"],
                                    Sort = 2
                                }
                            ]
                        }
                    },
                    // Second set: Union with products rated >= 4.0
                    new ConditionSet
                    {
                        Sort = 2,
                        Intersection = Intersection.Union,
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
                                    Values = ["4.0"],
                                    Sort = 1
                                }
                            ]
                        }
                    },
                    // Third set: Except products with low stock
                    new ConditionSet
                    {
                        Sort = 3,
                        Intersection = Intersection.Except,
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition
                                {
                                    Field = "StockQuantity",
                                    DataType = DataType.Number,
                                    Operator = Operator.LessThan,
                                    Values = ["10"],
                                    Sort = 1
                                }
                            ]
                        }
                    }
                ],
                Orders =
                [
                    new OrderBy { Field = "Price", Direction = Direction.Ascending, Sort = 1 }
                ],
                Page = new PageBy { PageNumber = 1, PageSize = 30 }
            };

            sw.Restart();
            var results = await _context.Products.ToListAsync(segment);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = results.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                { "PageNumber", results.PageNumber },
                { "PageSize", results.PageSize },
                { "PageCount", results.PageCount },
                { "TotalCount", results.TotalCount },
                { "ConditionSets", segment.ConditionSets.Count }
            };

            return Ok(new PerformanceResult
            {
                TestName = "Segment - Union, Intersect, Except Operations",
                Metrics = metrics,
                Input = segment,
                Output = results,
                Success = true,
                Message = $"Processed {segment.ConditionSets.Count} condition sets with set operations"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSegmentIntersections");
            return Ok(new PerformanceResult
            {
                TestName = "Segment - Union, Intersect, Except Operations",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    #endregion

    #region Complex Scenario Tests

    /// <summary>
    /// Test complex scenario with Customers (date range, text search, nested conditions)
    /// </summary>
    [HttpGet("complex/customers")]
    public async Task<ActionResult<PerformanceResult>> TestComplexCustomers()
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
                                    Field = "FirstName",
                                    DataType = DataType.Text,
                                    Operator = Operator.IStartsWith,
                                    Values = ["J"],
                                    Sort = 1
                                },
                                new Condition
                                {
                                    Field = "LastName",
                                    DataType = DataType.Text,
                                    Operator = Operator.IStartsWith,
                                    Values = ["S"],
                                    Sort = 2
                                },
                                new Condition
                                {
                                    Field = "TotalSpent",
                                    DataType = DataType.Number,
                                    Operator = Operator.GreaterThan,
                                    Values = ["1000"],
                                    Sort = 3
                                }
                            ]
                        }
                    ]
                },
                Selects =
                [
                    "Id", "FirstName", "LastName", "Username", "TotalSpent", "IsActive"
                ],
                Orders =
                [
                    new OrderBy { Field = "TotalSpent", Direction = Direction.Descending, Sort = 1 },
                    new OrderBy { Field = "LastName", Direction = Direction.Ascending, Sort = 2 }
                ],
                Page = new PageBy { PageNumber = 1, PageSize = 20 }
            };

            sw.Restart();
            var query = _context.Customers.Filter(filter);
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
                TestName = "Complex Customers - Date Range + Text Search + Nested OR",
                Metrics = metrics,
                Input = filter,
                Output = results,
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestComplexCustomers");
            return Ok(new PerformanceResult
            {
                TestName = "Complex Customers - Date Range + Text Search + Nested OR",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Test complex scenario with Orders (multiple data types and operators)
    /// </summary>
    [HttpGet("complex/orders")]
    public async Task<ActionResult<PerformanceResult>> TestComplexOrders()
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
                        new Condition
                        {
                            Field = "Status",
                            DataType = DataType.Enum,
                            Operator = Operator.In,
                            Values = ["pending", "processing", "Shipped"],
                            Sort = 1
                        },
                        new Condition
                        {
                            Field = "OrderDate",
                            DataType = DataType.DateTime,
                            Operator = Operator.GreaterThanOrEqual,
                            Values = ["2023-01-01"],
                            Sort = 2
                        },
                        new Condition
                        {
                            Field = "TotalAmount",
                            DataType = DataType.Number,
                            Operator = Operator.Between,
                            Values = ["100", "5000"],
                            Sort = 3
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
                                    Field = "ShippingAddress.Country",
                                    DataType = DataType.Text,
                                    Operator = Operator.IEqual,
                                    Values = ["USA"],
                                    Sort = 1
                                },
                                new Condition
                                {
                                    Field = "ShippingAddress.Country",
                                    DataType = DataType.Text,
                                    Operator = Operator.IEqual,
                                    Values = ["Canada"],
                                    Sort = 2
                                }
                            ]
                        }
                    ]
                },
                Orders =
                [
                    new OrderBy { Field = "OrderDate", Direction = Direction.Descending, Sort = 1 },
                    new OrderBy { Field = "TotalAmount", Direction = Direction.Descending, Sort = 2 }
                ],
                Page = new PageBy { PageNumber = 1, PageSize = 15 }
            };

            sw.Restart();
            var results = await _context.Orders.ToListAsync(filter, getQueryString: true);
            var totalTime = sw.Elapsed.TotalMilliseconds;

            metrics.TotalTimeMs = totalTime;
            metrics.RecordsReturned = results.Data.Count;
            metrics.AdditionalInfo = new Dictionary<string, object>
            {
                { "PageNumber", results.PageNumber },
                { "PageSize", results.PageSize },
                { "PageCount", results.PageCount },
                { "TotalCount", results.TotalCount }
            };

            return Ok(new PerformanceResult
            {
                TestName = "Complex Orders - Multi-Type Conditions + Nested Properties",
                Metrics = metrics,
                Input = filter,
                Output = results,
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestComplexOrders");
            return Ok(new PerformanceResult
            {
                TestName = "Complex Orders - Multi-Type Conditions + Nested Properties",
                Success = false,
                Message = ex.Message,
                Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Run all performance tests and return aggregated results
    /// </summary>
    [HttpGet("all")]
    public async Task<ActionResult<AllTestsResult>> TestAll()
    {
        var allResults = new List<PerformanceResult>();
        var overallSw = Stopwatch.StartNew();

        // Execute all test methods
        var testMethods = new List<(string name, Func<Task<ActionResult<PerformanceResult>>> method)>
        {
            ("Select Simple", TestSelectSimple),
            ("Select Complex", TestSelectComplex),
            ("Where Simple", TestWhereSimple),
            ("Where Text Search", TestWhereTextSearch),
            ("Where Text Search Case Insensitive", TestWhereTextSearchCaseInsensitive),
            ("Where Group AND", TestWhereGroupAnd),
            ("Where Group Nested", TestWhereGroupNested),
            ("Order Single", TestOrderSingle),
            ("Order Multiple", TestOrderMultiple),
            ("Page Basic", TestPageBasic),
            ("Filter Complete", TestFilterComplete),
            ("ToList Async", TestToListAsync),
            ("Segment Intersections", TestSegmentIntersections),
            ("Complex Customers", TestComplexCustomers),
            ("Complex Orders", TestComplexOrders)
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
                _logger.LogError(ex, "Error executing test: {name}", name);
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
            AverageTranslationTimeMs = allResults.Average(r => r.Metrics.TranslationTimeMs),
            AverageExecutionTimeMs = allResults.Average(r => r.Metrics.ExecutionTimeMs),
            AverageTotalTimeMs = allResults.Average(r => r.Metrics.TotalTimeMs),
            TotalRecordsReturned = allResults.Sum(r => r.Metrics.RecordsReturned),
            Results = allResults
        });
    }

    /// <summary>
    /// Test all text operators supported by DynamicWhere against a single field.
    /// </summary>
    [HttpGet("where/text-all")]
    public async Task<ActionResult<AllTestsResult>> TestWhereAllTextOperators()
    {
        var overallSw = Stopwatch.StartNew();
        var allResults = new List<PerformanceResult>();

        var ops = new List<Operator>
        {
            Operator.Equal,
            Operator.IEqual,
            Operator.NotEqual,
            Operator.INotEqual,
            Operator.Contains,
            Operator.IContains,
            Operator.NotContains,
            Operator.INotContains,
            Operator.StartsWith,
            Operator.IStartsWith,
            Operator.NotStartsWith,
            Operator.INotStartsWith,
            Operator.EndsWith,
            Operator.IEndsWith,
            Operator.NotEndsWith,
            Operator.INotEndsWith,
            Operator.In,
            Operator.IIn,
            Operator.NotIn,
            Operator.INotIn,
            Operator.IsNull,
            Operator.IsNotNull
        };

        foreach (var op in ops)
        {
            var metrics = new PerformanceMetrics();
            var sw = Stopwatch.StartNew();

            try
            {
                var condition = new Condition
                {
                    Field = "Name",
                    DataType = DataType.Text,
                    Operator = op,
                    Values = op switch
                    {
                        Operator.In or Operator.IIn or Operator.NotIn or Operator.INotIn => ["Pro", "Ultra", "Basic"],
                        Operator.IsNull or Operator.IsNotNull => [],
                        _ => ["Pro"]
                    }
                };

                sw.Restart();
                var query = _context.Products.Where(condition);
                var translationTime = sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                var results = await query.Take(100).ToListAsync();
                var executionTime = sw.Elapsed.TotalMilliseconds;

                metrics.TranslationTimeMs = translationTime;
                metrics.ExecutionTimeMs = executionTime;
                metrics.TotalTimeMs = translationTime + executionTime;
                metrics.RecordsReturned = results.Count;
                metrics.QueryGenerated = query.ToQueryString();

                allResults.Add(new PerformanceResult
                {
                    TestName = $"Where Text Operator - {op}",
                    Metrics = metrics,
                    Input = condition,
                    Output = results,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in TestWhereAllTextOperators for {op}", op);
                metrics.TotalTimeMs = sw.Elapsed.TotalMilliseconds;

                allResults.Add(new PerformanceResult
                {
                    TestName = $"Where Text Operator - {op}",
                    Success = false,
                    Message = ex.Message,
                    Metrics = metrics
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

#region Performance Result Models

/// <summary>
/// Performance metrics for a single test
/// </summary>
public class PerformanceMetrics
{
    /// <summary>
    /// Time taken to translate the query to SQL (in milliseconds)
    /// </summary>
    public double TranslationTimeMs { get; set; }

    /// <summary>
    /// Time taken to execute the query in database (in milliseconds)
    /// </summary>
    public double ExecutionTimeMs { get; set; }

    /// <summary>
    /// Total time (translation + execution) in milliseconds
    /// </summary>
    public double TotalTimeMs { get; set; }

    /// <summary>
    /// Number of records returned by the query
    /// </summary>
    public int RecordsReturned { get; set; }

    /// <summary>
    /// Generated SQL query string
    /// </summary>
    public string? QueryGenerated { get; set; }

    /// <summary>
    /// Additional performance information
    /// </summary>
    public Dictionary<string, object>? AdditionalInfo { get; set; }
}

/// <summary>
/// Result of a single performance test
/// </summary>
public class PerformanceResult
{
    /// <summary>
    /// Name of the test
    /// </summary>
    public string TestName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the test succeeded
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Optional message about the test
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Performance metrics
    /// </summary>
    public PerformanceMetrics Metrics { get; set; } = new();

    /// <summary>
    /// Input data used for the test
    /// </summary>
    public object? Input { get; set; }

    /// <summary>
    /// Output value produced by the operation.
    /// </summary>
    public object? Output { get; set; }
}

/// <summary>
/// Aggregated results from all tests
/// </summary>
public class AllTestsResult
{
    /// <summary>
    /// Total number of tests executed
    /// </summary>
    public int TotalTests { get; set; }

    /// <summary>
    /// Number of successful tests
    /// </summary>
    public int SuccessfulTests { get; set; }

    /// <summary>
    /// Number of failed tests
    /// </summary>
    public int FailedTests { get; set; }

    /// <summary>
    /// Total execution time for all tests (in milliseconds)
    /// </summary>
    public double TotalExecutionTimeMs { get; set; }

    /// <summary>
    /// Average translation time across all tests (in milliseconds)
    /// </summary>
    public double AverageTranslationTimeMs { get; set; }

    /// <summary>
    /// Average database execution time across all tests (in milliseconds)
    /// </summary>
    public double AverageExecutionTimeMs { get; set; }

    /// <summary>
    /// Average total time across all tests (in milliseconds)
    /// </summary>
    public double AverageTotalTimeMs { get; set; }

    /// <summary>
    /// Total number of records returned across all tests
    /// </summary>
    public int TotalRecordsReturned { get; set; }

    /// <summary>
    /// Individual test results
    /// </summary>
    public List<PerformanceResult> Results { get; set; } = [];
}

#endregion
