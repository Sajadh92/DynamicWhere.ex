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

    /// <summary>
    /// Select: nullable scalar fields (string?, DateTime?, DateOnly?, TimeOnly?).
    /// </summary>
    [HttpGet("select/nullable-scalars")]
    public async Task<ActionResult<PerformanceResult>> TestSelectNullableScalars()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Description", "UpdatedAt", "ManufactureDate", "AvailableFrom" };

            sw.Restart();
            var query = _context.Products.Select(fields);
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
                TestName = "Select Nullable Scalars - Description, UpdatedAt, ManufactureDate, AvailableFrom",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = $"Projected nullable scalar fields: {results.Count} records"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectNullableScalars");
            return Ok(new PerformanceResult
            {
                TestName = "Select Nullable Scalars - Description, UpdatedAt, ManufactureDate, AvailableFrom",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Select: multiple dotted paths through the same collection (Quantity + UnitPrice + Discount).
    /// </summary>
    [HttpGet("select/collection-multiple-fields")]
    public async Task<ActionResult<PerformanceResult>> TestSelectMultipleCollectionFields()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "OrderItems.Quantity", "OrderItems.UnitPrice", "OrderItems.Discount" };

            sw.Restart();
            var query = _context.Products.Include(p => p.OrderItems).Select(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(30).ToListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Select Collection Multiple Fields - OrderItems.Quantity + UnitPrice + Discount",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Multiple fields projected from the same collection navigation"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectMultipleCollectionFields");
            return Ok(new PerformanceResult
            {
                TestName = "Select Collection Multiple Fields - OrderItems.Quantity + UnitPrice + Discount",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Select: whole collection navigation object (non-dotted OrderItems).
    /// </summary>
    [HttpGet("select/whole-collection")]
    public async Task<ActionResult<PerformanceResult>> TestSelectWholeCollection()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "OrderItems" };

            sw.Restart();
            var query = _context.Products.Include(p => p.OrderItems).Select(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(30).ToListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Select Whole Collection - Id, Name, OrderItems (entire collection)",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Projected entire OrderItems collection as a navigation object"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectWholeCollection");
            return Ok(new PerformanceResult
            {
                TestName = "Select Whole Collection - Id, Name, OrderItems (entire collection)",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Select: mixed required + nullable navigation properties (Category.Name + Category.Description?).
    /// Verifies default-object behaviour when Category is absent.
    /// </summary>
    [HttpGet("select/nullable-nav-props")]
    public async Task<ActionResult<PerformanceResult>> TestSelectNullableNavProps()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Category.Name", "Category.Description" };

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
                TestName = "Select Nullable Nav Props - Category.Name + Category.Description (string?)",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Projected required + nullable props from reference navigation"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectNullableNavProps");
            return Ok(new PerformanceResult
            {
                TestName = "Select Nullable Nav Props - Category.Name + Category.Description (string?)",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Select: dotted paths through the Reviews collection (Rating + Title).
    /// </summary>
    [HttpGet("select/reviews-collection")]
    public async Task<ActionResult<PerformanceResult>> TestSelectReviewsCollection()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Reviews.Rating", "Reviews.Title" };

            sw.Restart();
            var query = _context.Products.Include(p => p.Reviews).Select(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(30).ToListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Select Reviews Collection - Reviews.Rating + Reviews.Title",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Projected two fields from Reviews collection navigation"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectReviewsCollection");
            return Ok(new PerformanceResult
            {
                TestName = "Select Reviews Collection - Reviews.Rating + Reviews.Title",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Select: nullable DateTime? and bool fields from a collection navigation (Reviews.UpdatedAt, Reviews.IsVerifiedPurchase).
    /// </summary>
    [HttpGet("select/reviews-nullable-field")]
    public async Task<ActionResult<PerformanceResult>> TestSelectReviewsNullableField()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Reviews.Rating", "Reviews.UpdatedAt", "Reviews.IsVerifiedPurchase" };

            sw.Restart();
            var query = _context.Products.Include(p => p.Reviews).Select(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(30).ToListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Select Reviews Nullable Field - UpdatedAt (DateTime?) + IsVerifiedPurchase (bool)",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Projected nullable DateTime? and bool from Reviews collection items"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectReviewsNullableField");
            return Ok(new PerformanceResult
            {
                TestName = "Select Reviews Nullable Field - UpdatedAt (DateTime?) + IsVerifiedPurchase (bool)",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Select: non-nullable value types (Guid, bool, int) through an optional reference navigation.
    /// Verifies that a null Category produces default values rather than a crash.
    /// </summary>
    [HttpGet("select/nullable-value-type-nav")]
    public async Task<ActionResult<PerformanceResult>> TestSelectNullableValueTypeNav()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Category.Id", "Category.IsActive", "Category.DisplayOrder" };

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
                TestName = "Select Nullable Value Type Nav - Category.Id (Guid) + IsActive (bool) + DisplayOrder (int)",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Non-nullable value types from optional reference nav: default when Category is null"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectNullableValueTypeNav");
            return Ok(new PerformanceResult
            {
                TestName = "Select Nullable Value Type Nav - Category.Id (Guid) + IsActive (bool) + DisplayOrder (int)",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Select: whole Reviews collection navigation (non-dotted).
    /// </summary>
    [HttpGet("select/whole-reviews")]
    public async Task<ActionResult<PerformanceResult>> TestSelectWholeReviews()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Reviews" };

            sw.Restart();
            var query = _context.Products.Include(p => p.Reviews).Select(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(30).ToListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Select Whole Reviews - Id, Name, Reviews (entire collection)",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Projected entire Reviews collection as a navigation object"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectWholeReviews");
            return Ok(new PerformanceResult
            {
                TestName = "Select Whole Reviews - Id, Name, Reviews (entire collection)",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Select: JSON primitive list field (Tags) projected alone.
    /// </summary>
    [HttpGet("select/tags-list")]
    public async Task<ActionResult<PerformanceResult>> TestSelectTagsList()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Tags" };

            sw.Restart();
            var query = _context.Products.Select(fields);
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
                TestName = "Select Tags List - Id, Name, Tags (List<string>)",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Projected JSON primitive list field (Tags)"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectTagsList");
            return Ok(new PerformanceResult
            {
                TestName = "Select Tags List - Id, Name, Tags (List<string>)",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// Select: two different collection navigations projected in one query.
    /// </summary>
    [HttpGet("select/two-collections")]
    public async Task<ActionResult<PerformanceResult>> TestSelectTwoCollections()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Reviews.Rating", "OrderItems.Quantity" };

            sw.Restart();
            var query = _context.Products
                .Include(p => p.Reviews)
                .Include(p => p.OrderItems)
                .Select(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(30).ToListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "Select Two Collections - Reviews.Rating + OrderItems.Quantity",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Two different collection navigations projected in one query"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectTwoCollections");
            return Ok(new PerformanceResult
            {
                TestName = "Select Two Collections - Reviews.Rating + OrderItems.Quantity",
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

    /// <summary>
    /// SelectDynamic: nullable scalar fields (Description as string?, UpdatedAt as DateTime?).
    /// </summary>
    [HttpGet("select-dynamic/nullable-scalars")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicNullableScalars()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Description", "UpdatedAt" };

            sw.Restart();
            var query = _context.Products.SelectDynamic(fields);
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
                TestName = "SelectDynamic Nullable Scalars - Description (string?), UpdatedAt (DateTime?)",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Dynamic projection of nullable scalar fields (null appears as null in output)"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicNullableScalars");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Nullable Scalars - Description (string?), UpdatedAt (DateTime?)",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// SelectDynamic: multiple dotted paths through the same collection (Quantity + UnitPrice).
    /// </summary>
    [HttpGet("select-dynamic/collection-multiple-fields")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicMultipleCollectionFields()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "OrderItems.Quantity", "OrderItems.UnitPrice" };

            sw.Restart();
            var query = _context.Products.Include(p => p.OrderItems).SelectDynamic(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(30).ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Collection Multiple Fields - OrderItems.Quantity + UnitPrice",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Multiple dotted paths through the same collection merged into one nested array"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicMultipleCollectionFields");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Collection Multiple Fields - OrderItems.Quantity + UnitPrice",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// SelectDynamic: whole collection navigation (non-dotted OrderItems).
    /// </summary>
    [HttpGet("select-dynamic/whole-collection")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicWholeCollection()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "OrderItems" };

            sw.Restart();
            var query = _context.Products.Include(p => p.OrderItems).SelectDynamic(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(30).ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Whole Collection - OrderItems as entire collection",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Dynamic projection of the whole OrderItems collection object"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicWholeCollection");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Whole Collection - OrderItems as entire collection",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// SelectDynamic: mixed required + nullable navigation properties (Category.Name + Category.Description?).
    /// Null Category produces { name: null, description: null } in the nested object.
    /// </summary>
    [HttpGet("select-dynamic/nullable-nav-props")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicNullableNavProps()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Category.Name", "Category.Description" };

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
                TestName = "SelectDynamic Nullable Nav Props - Category.Name + Category.Description (string?)",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Null Category produces { name: null, description: null } in nested object"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicNullableNavProps");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Nullable Nav Props - Category.Name + Category.Description (string?)",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// SelectDynamic: dotted paths through the Reviews collection (Rating + Title).
    /// </summary>
    [HttpGet("select-dynamic/reviews-collection")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicReviewsCollection()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Reviews.Rating", "Reviews.Title" };

            sw.Restart();
            var query = _context.Products.Include(p => p.Reviews).SelectDynamic(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(30).ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Reviews Collection - Reviews.Rating + Reviews.Title",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Dynamic projection through the Reviews collection with multiple fields"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicReviewsCollection");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Reviews Collection - Reviews.Rating + Reviews.Title",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// SelectDynamic: reference navigation and collection navigation combined in one projection.
    /// </summary>
    [HttpGet("select-dynamic/mixed-navigation")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicMixedNavigation()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Category.Name", "OrderItems.Quantity", "OrderItems.UnitPrice" };

            sw.Restart();
            var query = _context.Products
                .Include(p => p.Category)
                .Include(p => p.OrderItems)
                .SelectDynamic(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(30).ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Mixed Navigation - Category.Name + OrderItems.Quantity + UnitPrice",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Combined reference navigation (Category) and collection navigation (OrderItems) in one projection"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicMixedNavigation");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Mixed Navigation - Category.Name + OrderItems.Quantity + UnitPrice",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// SelectDynamic: non-nullable value types (Guid, bool, int) through an optional reference navigation.
    /// np() wrapping prevents "Nullable object must have a value" when Category is absent.
    /// </summary>
    [HttpGet("select-dynamic/nullable-value-type-nav")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicNullableValueTypeNav()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Category.Id", "Category.IsActive", "Category.DisplayOrder" };

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
                TestName = "SelectDynamic Nullable Value Type Nav - Category.Id (Guid) + IsActive (bool) + DisplayOrder (int)",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "np() wrapping prevents crash for Guid/bool/int from optional reference navigation"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicNullableValueTypeNav");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Nullable Value Type Nav - Category.Id (Guid) + IsActive (bool) + DisplayOrder (int)",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// SelectDynamic: nullable DateTime? and bool from a collection navigation (Reviews.UpdatedAt, Reviews.IsVerifiedPurchase).
    /// Collection elements are never null so no np() is needed inside the Select lambda.
    /// </summary>
    [HttpGet("select-dynamic/collection-nullable-field")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicCollectionNullableField()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Reviews.UpdatedAt", "Reviews.IsVerifiedPurchase" };

            sw.Restart();
            var query = _context.Products.Include(p => p.Reviews).SelectDynamic(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(30).ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Collection Nullable Field - Reviews.UpdatedAt (DateTime?) + Reviews.IsVerifiedPurchase (bool)",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Nullable DateTime? and bool projected from Reviews collection items"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicCollectionNullableField");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Collection Nullable Field - Reviews.UpdatedAt (DateTime?) + Reviews.IsVerifiedPurchase (bool)",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// SelectDynamic: whole Reviews collection navigation (non-dotted).
    /// </summary>
    [HttpGet("select-dynamic/whole-reviews")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicWholeReviews()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Reviews" };

            sw.Restart();
            var query = _context.Products.Include(p => p.Reviews).SelectDynamic(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(30).ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Whole Reviews - Id, Name, Reviews (entire collection)",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Dynamic projection of the whole Reviews collection object"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicWholeReviews");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Whole Reviews - Id, Name, Reviews (entire collection)",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// SelectDynamic: JSON primitive list field (Tags) projected alone.
    /// </summary>
    [HttpGet("select-dynamic/tags-list")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicTagsList()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Tags" };

            sw.Restart();
            var query = _context.Products.SelectDynamic(fields);
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
                TestName = "SelectDynamic Tags List - Id, Name, Tags (List<string>)",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Dynamic projection of JSON primitive list field (Tags)"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicTagsList");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Tags List - Id, Name, Tags (List<string>)",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// SelectDynamic: two different collection navigations projected in one query.
    /// </summary>
    [HttpGet("select-dynamic/two-collections")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicTwoCollections()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Name", "Reviews.Rating", "OrderItems.Quantity" };

            sw.Restart();
            var query = _context.Products
                .Include(p => p.Reviews)
                .Include(p => p.OrderItems)
                .SelectDynamic(fields);
            var translationTime = sw.Elapsed.TotalMilliseconds;

            sw.Restart();
            var results = await query.Take(30).ToDynamicListAsync();
            var executionTime = sw.Elapsed.TotalMilliseconds;

            metrics.TranslationTimeMs = translationTime;
            metrics.ExecutionTimeMs = executionTime;
            metrics.TotalTimeMs = translationTime + executionTime;
            metrics.RecordsReturned = results.Count;
            metrics.QueryGenerated = query.ToQueryString();

            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Two Collections - Reviews.Rating + OrderItems.Quantity",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "Two separate collection navigations projected in one dynamic query"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicTwoCollections");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Two Collections - Reviews.Rating + OrderItems.Quantity",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// SelectDynamic: multiple value-type fields merged under one reference navigation.
    /// Exercises np() wrapping for Guid, bool, and int simultaneously under Category.
    /// </summary>
    [HttpGet("select-dynamic/deep-merge")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicDeepMerge()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string> { "Id", "Category.Name", "Category.Id", "Category.IsActive", "Category.DisplayOrder" };

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
                TestName = "SelectDynamic Deep Merge - Category.Name + Id + IsActive + DisplayOrder all merged",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "np() applied to Guid/bool/int all merged under the same Category nested object"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicDeepMerge");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic Deep Merge - Category.Name + Id + IsActive + DisplayOrder all merged",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// SelectDynamic: all direct scalar fields on Product, including nullable FK (CategoryId?).
    /// </summary>
    [HttpGet("select-dynamic/all-scalars")]
    public async Task<ActionResult<PerformanceResult>> TestSelectDynamicAllScalars()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var fields = new List<string>
            {
                "Id", "Name", "Description", "Price", "Rating",
                "StockQuantity", "IsActive", "CreatedAt", "UpdatedAt", "CategoryId"
            };

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
                TestName = "SelectDynamic All Scalars - Id, Name, Description, Price, Rating, StockQuantity, IsActive, CreatedAt, UpdatedAt, CategoryId",
                Metrics = metrics,
                Input = new { Fields = fields },
                Output = results,
                Success = true,
                Message = "All direct scalar fields including nullable string, DateTime?, and Guid? FK"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestSelectDynamicAllScalars");
            return Ok(new PerformanceResult
            {
                TestName = "SelectDynamic All Scalars - Id, Name, Description, Price, Rating, StockQuantity, IsActive, CreatedAt, UpdatedAt, CategoryId",
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
            // Select<T>
            ("Select Scalars",                           TestSelectScalars),
            ("Select Nested Reference",                  TestSelectNestedReference),
            ("Select Nested Collection",                 TestSelectNestedCollection),
            ("Select Whole Navigation",                  TestSelectWholeNavigation),
            ("Select Complex",                           TestSelectComplex),
            ("Select Nullable Scalars",                  TestSelectNullableScalars),
            ("Select Collection Multiple Fields",        TestSelectMultipleCollectionFields),
            ("Select Whole Collection",                  TestSelectWholeCollection),
            ("Select Nullable Nav Props",                TestSelectNullableNavProps),
            ("Select Reviews Collection",                TestSelectReviewsCollection),
            ("Select Reviews Nullable Field",            TestSelectReviewsNullableField),
            ("Select Nullable Value Type Nav",           TestSelectNullableValueTypeNav),
            ("Select Whole Reviews",                     TestSelectWholeReviews),
            ("Select Tags List",                         TestSelectTagsList),
            ("Select Two Collections",                   TestSelectTwoCollections),
            // SelectDynamic
            ("SelectDynamic Scalars",                    TestSelectDynamicScalars),
            ("SelectDynamic Nested Reference",           TestSelectDynamicNestedReference),
            ("SelectDynamic Nested Collection",          TestSelectDynamicNestedCollection),
            ("SelectDynamic Merged Fields",              TestSelectDynamicMergedFields),
            ("SelectDynamic Whole Navigation",           TestSelectDynamicWholeNavigation),
            ("SelectDynamic Nullable Scalars",           TestSelectDynamicNullableScalars),
            ("SelectDynamic Collection Multiple Fields", TestSelectDynamicMultipleCollectionFields),
            ("SelectDynamic Whole Collection",           TestSelectDynamicWholeCollection),
            ("SelectDynamic Nullable Nav Props",         TestSelectDynamicNullableNavProps),
            ("SelectDynamic Reviews Collection",         TestSelectDynamicReviewsCollection),
            ("SelectDynamic Mixed Navigation",           TestSelectDynamicMixedNavigation),
            ("SelectDynamic Nullable Value Type Nav",    TestSelectDynamicNullableValueTypeNav),
            ("SelectDynamic Collection Nullable Field",  TestSelectDynamicCollectionNullableField),
            ("SelectDynamic Whole Reviews",              TestSelectDynamicWholeReviews),
            ("SelectDynamic Tags List",                  TestSelectDynamicTagsList),
            ("SelectDynamic Two Collections",            TestSelectDynamicTwoCollections),
            ("SelectDynamic Deep Merge",                 TestSelectDynamicDeepMerge),
            ("SelectDynamic All Scalars",                TestSelectDynamicAllScalars),
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
