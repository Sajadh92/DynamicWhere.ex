using DynamicWhere.API.Data;
using DynamicWhere.ex.Source;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Linq.Dynamic.Core;

namespace DynamicWhere.API.Controllers;

/// <summary>
/// Controller for testing Select and SelectDynamic extension methods.
/// Covers: scalar projection, nested reference navigation, nested collection navigation,
/// whole navigation objects/collections, complex multi-path, and merged dotted fields.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SelectTestController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<SelectTestController> _logger;

    public SelectTestController(AppDbContext context, ILogger<SelectTestController> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Select<T> (Typed Projection)

    /// <summary>
    /// Select: direct scalar fields only.
    /// </summary>
    [HttpGet("select/scalars")]
    public async Task<ActionResult<PerformanceResult>> TestSelectScalars()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Price", "Rating", "IsActive" };

            sw.Restart();
            var query = _context.Products.Select(fields);
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
                TestName = "Select Scalars - Id, Name, Price, Rating, IsActive",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = $"Projected {fields.Count} scalar fields from {results.Count} products"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectScalars");
            return Ok(new PerformanceResult
            {
                TestName = "Select Scalars - Id, Name, Price, Rating, IsActive",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Select: dotted path through a reference navigation (Category.Name).
    /// </summary>
    [HttpGet("select/nested-reference")]
    public async Task<ActionResult<PerformanceResult>> TestSelectNestedReference()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Category.Name" };

            sw.Restart();
            var query = _context.Products.Include(p => p.Category).Select(fields);
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
                TestName = "Select Nested Reference - Id, Name, Category.Name",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Projected dotted reference navigation Category.Name"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectNestedReference");
            return Ok(new PerformanceResult
            {
                TestName = "Select Nested Reference - Id, Name, Category.Name",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Select: dotted path through a collection navigation (OrderItems.Quantity).
    /// </summary>
    [HttpGet("select/nested-collection")]
    public async Task<ActionResult<PerformanceResult>> TestSelectNestedCollection()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "OrderItems.Quantity" };

            sw.Restart();
            var query = _context.Products.Include(p => p.OrderItems).Select(fields);
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
                TestName = "Select Nested Collection - Id, Name, OrderItems.Quantity",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Projected collection navigation path OrderItems.Quantity"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectNestedCollection");
            return Ok(new PerformanceResult
            {
                TestName = "Select Nested Collection - Id, Name, OrderItems.Quantity",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Select: whole navigation object (non-dotted Category).
    /// </summary>
    [HttpGet("select/whole-navigation")]
    public async Task<ActionResult<PerformanceResult>> TestSelectWholeNavigation()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Category" };

            sw.Restart();
            var query = _context.Products.Include(p => p.Category).Select(fields);
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
                TestName = "Select Whole Navigation - Id, Name, Category (entire object)",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Projected whole Category navigation object"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectWholeNavigation");
            return Ok(new PerformanceResult
            {
                TestName = "Select Whole Navigation - Id, Name, Category (entire object)",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Select: complex multi-path (scalars + Tags list + nested ref + nested collection).
    /// </summary>
    [HttpGet("select/complex")]
    public async Task<ActionResult<PerformanceResult>> TestSelectComplex()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Tags", "Category.Name", "OrderItems.Quantity" };

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
                TestName = "Select Complex - Scalars + Tags + Category.Name + OrderItems.Quantity",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Complex multi-path projection with list, reference, and collection navigations"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectComplex");
            return Ok(new PerformanceResult
            {
                TestName = "Select Complex - Scalars + Tags + Category.Name + OrderItems.Quantity",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    #endregion

    #region SelectDynamic (Dynamic Projection)

    /// <summary>
    /// SelectDynamic: direct scalar fields.
    /// </summary>
    [HttpGet("select-dynamic/scalars")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicScalars()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Price", "Rating" };

            sw.Restart();
            var query = _context.Products.SelectDynamic(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(100).ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Scalars - Id, Name, Price, Rating",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Dynamic projection of scalar fields"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicScalars");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Scalars - Id, Name, Price, Rating",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// SelectDynamic: dotted path through reference navigation producing nested dynamic object.
    /// </summary>
    [HttpGet("select-dynamic/nested-reference")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicNestedReference()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Price", "Category.Name" };

            sw.Restart();
            var query = _context.Products.Include(p => p.Category).SelectDynamic(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(50).ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Nested Reference - Category.Name as nested object",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Dynamic projection with nested Category: { Name: \"...\" }"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicNestedReference");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Nested Reference - Category.Name as nested object",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// SelectDynamic: dotted path through collection navigation producing Select lambda.
    /// </summary>
    [HttpGet("select-dynamic/nested-collection")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicNestedCollection()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "OrderItems.Quantity" };

            sw.Restart();
            var query = _context.Products.Include(p => p.OrderItems).SelectDynamic(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(50).ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Nested Collection - OrderItems.Quantity via Select lambda",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Dynamic projection through collection navigation with Select lambda"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicNestedCollection");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Nested Collection - OrderItems.Quantity via Select lambda",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// SelectDynamic: multiple dotted fields merged under the same root segment.
    /// </summary>
    [HttpGet("select-dynamic/merged-fields")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicMergedFields()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Category.Name", "Category.Id" };

            sw.Restart();
            var query = _context.Products.Include(p => p.Category).SelectDynamic(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(50).ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Merged Fields - Category.Name + Category.Id merged",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Multiple dotted fields under same root merged into one nested object"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicMergedFields");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Merged Fields - Category.Name + Category.Id merged",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// SelectDynamic: whole navigation object (non-dotted).
    /// </summary>
    [HttpGet("select-dynamic/whole-navigation")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicWholeNavigation()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Category" };

            sw.Restart();
            var query = _context.Products.Include(p => p.Category).SelectDynamic(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(50).ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Whole Navigation - Category as entire object",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Dynamic projection of whole Category navigation object"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicWholeNavigation");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Whole Navigation - Category as entire object",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    #endregion

    #region Run All Tests

    /// <summary>
    /// Run all Select tests and return aggregated results.
    /// </summary>
    [HttpGet("all")]
    public async Task<ActionResult<AllTestsResult>> TestAll()
    {
        var allResults = new List<PerformanceResult>();
        var overallSw = Stopwatch.StartNew();

        var testMethods = new List<(string name, Func<Task<ActionResult<PerformanceResult>>> method)>
        {
            ("Select Scalars",                 TestSelectScalars),
            ("Select Nested Reference",        TestSelectNestedReference),
            ("Select Nested Collection",       TestSelectNestedCollection),
            ("Select Whole Navigation",        TestSelectWholeNavigation),
            ("Select Complex",                 TestSelectComplex),
            ("SelectDynamic Scalars",          TestSelectDynamicScalars),
            ("SelectDynamic Nested Reference", TestSelectDynamicNestedReference),
            ("SelectDynamic Nested Collection",TestSelectDynamicNestedCollection),
            ("SelectDynamic Merged Fields",    TestSelectDynamicMergedFields),
            ("SelectDynamic Whole Navigation", TestSelectDynamicWholeNavigation)
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
                _logger.LogError(ex, "Error executing Select test: {name}", name);
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
