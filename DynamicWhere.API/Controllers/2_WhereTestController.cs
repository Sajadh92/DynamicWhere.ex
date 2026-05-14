using DynamicWhere.API.Data;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Source;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace DynamicWhere.API.Controllers;

/// <summary>
/// Controller for testing Where(Condition) and Where(ConditionGroup) extension methods.
/// Covers: all text operators, number/boolean/datetime/date/enum/guid operators,
/// nested property conditions, ConditionGroup AND/OR/nested, null checks, In/NotIn.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class WhereTestController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<WhereTestController> _logger;

    public WhereTestController(AppDbContext context, ILogger<WhereTestController> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Where(Condition) — Text Operators

    /// <summary>
    /// Text: case-sensitive Contains.
    /// </summary>
    [HttpGet("condition/text-contains")]
    public async Task<ActionResult<PerformanceResult>> TestTextContains()
    {
        return await RunConditionTest("Text Contains", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.Contains, Values = ["Pro"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: case-insensitive IContains.
    /// </summary>
    [HttpGet("condition/text-icontains")]
    public async Task<ActionResult<PerformanceResult>> TestTextIContains()
    {
        return await RunConditionTest("Text IContains", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.IContains, Values = ["pro"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: case-sensitive Equal.
    /// </summary>
    [HttpGet("condition/text-equal")]
    public async Task<ActionResult<PerformanceResult>> TestTextEqual()
    {
        return await RunConditionTest("Text Equal", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.Equal, Values = ["Pro"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: case-insensitive IEqual.
    /// </summary>
    [HttpGet("condition/text-iequal")]
    public async Task<ActionResult<PerformanceResult>> TestTextIEqual()
    {
        return await RunConditionTest("Text IEqual", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.IEqual, Values = ["pro"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: StartsWith.
    /// </summary>
    [HttpGet("condition/text-startswith")]
    public async Task<ActionResult<PerformanceResult>> TestTextStartsWith()
    {
        return await RunConditionTest("Text StartsWith", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.StartsWith, Values = ["Pro"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: IStartsWith (case-insensitive).
    /// </summary>
    [HttpGet("condition/text-istartswith")]
    public async Task<ActionResult<PerformanceResult>> TestTextIStartsWith()
    {
        return await RunConditionTest("Text IStartsWith", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.IStartsWith, Values = ["pro"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: EndsWith.
    /// </summary>
    [HttpGet("condition/text-endswith")]
    public async Task<ActionResult<PerformanceResult>> TestTextEndsWith()
    {
        return await RunConditionTest("Text EndsWith", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.EndsWith, Values = ["Pro"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: IEndsWith (case-insensitive).
    /// </summary>
    [HttpGet("condition/text-iendswith")]
    public async Task<ActionResult<PerformanceResult>> TestTextIEndsWith()
    {
        return await RunConditionTest("Text IEndsWith", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.IEndsWith, Values = ["pro"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: NotContains.
    /// </summary>
    [HttpGet("condition/text-notcontains")]
    public async Task<ActionResult<PerformanceResult>> TestTextNotContains()
    {
        return await RunConditionTest("Text NotContains", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.NotContains, Values = ["Pro"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: INotContains (case-insensitive).
    /// </summary>
    [HttpGet("condition/text-inotcontains")]
    public async Task<ActionResult<PerformanceResult>> TestTextINotContains()
    {
        return await RunConditionTest("Text INotContains", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.INotContains, Values = ["pro"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: NotEqual.
    /// </summary>
    [HttpGet("condition/text-notequal")]
    public async Task<ActionResult<PerformanceResult>> TestTextNotEqual()
    {
        return await RunConditionTest("Text NotEqual", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.NotEqual, Values = ["Pro"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: INotEqual (case-insensitive).
    /// </summary>
    [HttpGet("condition/text-inotequal")]
    public async Task<ActionResult<PerformanceResult>> TestTextINotEqual()
    {
        return await RunConditionTest("Text INotEqual", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.INotEqual, Values = ["pro"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: NotStartsWith.
    /// </summary>
    [HttpGet("condition/text-notstartswith")]
    public async Task<ActionResult<PerformanceResult>> TestTextNotStartsWith()
    {
        return await RunConditionTest("Text NotStartsWith", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.NotStartsWith, Values = ["Pro"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: INotStartsWith (case-insensitive).
    /// </summary>
    [HttpGet("condition/text-inotstartswith")]
    public async Task<ActionResult<PerformanceResult>> TestTextINotStartsWith()
    {
        return await RunConditionTest("Text INotStartsWith", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.INotStartsWith, Values = ["pro"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: NotEndsWith.
    /// </summary>
    [HttpGet("condition/text-notendswith")]
    public async Task<ActionResult<PerformanceResult>> TestTextNotEndsWith()
    {
        return await RunConditionTest("Text NotEndsWith", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.NotEndsWith, Values = ["Pro"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: INotEndsWith (case-insensitive).
    /// </summary>
    [HttpGet("condition/text-inotendswith")]
    public async Task<ActionResult<PerformanceResult>> TestTextINotEndsWith()
    {
        return await RunConditionTest("Text INotEndsWith", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.INotEndsWith, Values = ["pro"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: In (multiple values, case-sensitive).
    /// </summary>
    [HttpGet("condition/text-in")]
    public async Task<ActionResult<PerformanceResult>> TestTextIn()
    {
        return await RunConditionTest("Text In", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.In, Values = ["Pro", "Ultra", "Basic"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: IIn (multiple values, case-insensitive).
    /// </summary>
    [HttpGet("condition/text-iin")]
    public async Task<ActionResult<PerformanceResult>> TestTextIIn()
    {
        return await RunConditionTest("Text IIn", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.IIn, Values = ["pro", "ultra", "basic"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: NotIn.
    /// </summary>
    [HttpGet("condition/text-notin")]
    public async Task<ActionResult<PerformanceResult>> TestTextNotIn()
    {
        return await RunConditionTest("Text NotIn", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.NotIn, Values = ["Pro", "Ultra"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: INotIn (case-insensitive).
    /// </summary>
    [HttpGet("condition/text-inotin")]
    public async Task<ActionResult<PerformanceResult>> TestTextINotIn()
    {
        return await RunConditionTest("Text INotIn", new Condition
        {
            Field = "Name", DataType = DataType.Text, Operator = Operator.INotIn, Values = ["pro", "ultra"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: IsNull.
    /// </summary>
    [HttpGet("condition/text-isnull")]
    public async Task<ActionResult<PerformanceResult>> TestTextIsNull()
    {
        return await RunConditionTest("Text IsNull", new Condition
        {
            Field = "Description", DataType = DataType.Text, Operator = Operator.IsNull, Values = []
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Text: IsNotNull.
    /// </summary>
    [HttpGet("condition/text-isnotnull")]
    public async Task<ActionResult<PerformanceResult>> TestTextIsNotNull()
    {
        return await RunConditionTest("Text IsNotNull", new Condition
        {
            Field = "Description", DataType = DataType.Text, Operator = Operator.IsNotNull, Values = []
        }, q => _context.Products.Where(q));
    }

    #endregion

    #region Where(Condition) — Number Operators

    /// <summary>
    /// Number: GreaterThan.
    /// </summary>
    [HttpGet("condition/number-gt")]
    public async Task<ActionResult<PerformanceResult>> TestNumberGreaterThan()
    {
        return await RunConditionTest("Number GreaterThan", new Condition
        {
            Field = "Price", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["50"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Number: GreaterThanOrEqual.
    /// </summary>
    [HttpGet("condition/number-gte")]
    public async Task<ActionResult<PerformanceResult>> TestNumberGreaterThanOrEqual()
    {
        return await RunConditionTest("Number GreaterThanOrEqual", new Condition
        {
            Field = "Rating", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = ["3.5"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Number: LessThan.
    /// </summary>
    [HttpGet("condition/number-lt")]
    public async Task<ActionResult<PerformanceResult>> TestNumberLessThan()
    {
        return await RunConditionTest("Number LessThan", new Condition
        {
            Field = "Price", DataType = DataType.Number, Operator = Operator.LessThan, Values = ["100"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Number: LessThanOrEqual.
    /// </summary>
    [HttpGet("condition/number-lte")]
    public async Task<ActionResult<PerformanceResult>> TestNumberLessThanOrEqual()
    {
        return await RunConditionTest("Number LessThanOrEqual", new Condition
        {
            Field = "StockQuantity", DataType = DataType.Number, Operator = Operator.LessThanOrEqual, Values = ["50"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Number: Between.
    /// </summary>
    [HttpGet("condition/number-between")]
    public async Task<ActionResult<PerformanceResult>> TestNumberBetween()
    {
        return await RunConditionTest("Number Between", new Condition
        {
            Field = "Price", DataType = DataType.Number, Operator = Operator.Between, Values = ["20", "200"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Number: NotBetween.
    /// </summary>
    [HttpGet("condition/number-notbetween")]
    public async Task<ActionResult<PerformanceResult>> TestNumberNotBetween()
    {
        return await RunConditionTest("Number NotBetween", new Condition
        {
            Field = "Price", DataType = DataType.Number, Operator = Operator.NotBetween, Values = ["20", "200"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Number: Equal.
    /// </summary>
    [HttpGet("condition/number-equal")]
    public async Task<ActionResult<PerformanceResult>> TestNumberEqual()
    {
        return await RunConditionTest("Number Equal", new Condition
        {
            Field = "StockQuantity", DataType = DataType.Number, Operator = Operator.Equal, Values = ["0"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Number: NotEqual.
    /// </summary>
    [HttpGet("condition/number-notequal")]
    public async Task<ActionResult<PerformanceResult>> TestNumberNotEqual()
    {
        return await RunConditionTest("Number NotEqual", new Condition
        {
            Field = "StockQuantity", DataType = DataType.Number, Operator = Operator.NotEqual, Values = ["0"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Number: In (set of values).
    /// </summary>
    [HttpGet("condition/number-in")]
    public async Task<ActionResult<PerformanceResult>> TestNumberIn()
    {
        return await RunConditionTest("Number In", new Condition
        {
            Field = "StockQuantity", DataType = DataType.Number, Operator = Operator.In, Values = ["0", "10", "50", "100"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Number: IsNull (nullable field).
    /// </summary>
    [HttpGet("condition/number-isnull")]
    public async Task<ActionResult<PerformanceResult>> TestNumberIsNull()
    {
        return await RunConditionTest("Number IsNull", new Condition
        {
            Field = "CategoryId", DataType = DataType.Guid, Operator = Operator.IsNull, Values = []
        }, q => _context.Products.Where(q));
    }

    #endregion

    #region Where(Condition) — Boolean Operators

    /// <summary>
    /// Boolean: Equal true.
    /// </summary>
    [HttpGet("condition/boolean-true")]
    public async Task<ActionResult<PerformanceResult>> TestBooleanTrue()
    {
        return await RunConditionTest("Boolean Equal True", new Condition
        {
            Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Boolean: Equal false.
    /// </summary>
    [HttpGet("condition/boolean-false")]
    public async Task<ActionResult<PerformanceResult>> TestBooleanFalse()
    {
        return await RunConditionTest("Boolean Equal False", new Condition
        {
            Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["false"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Boolean: NotEqual.
    /// </summary>
    [HttpGet("condition/boolean-notequal")]
    public async Task<ActionResult<PerformanceResult>> TestBooleanNotEqual()
    {
        return await RunConditionTest("Boolean NotEqual", new Condition
        {
            Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.NotEqual, Values = ["true"]
        }, q => _context.Products.Where(q));
    }

    #endregion

    #region Where(Condition) — DateTime / Date Operators

    /// <summary>
    /// DateTime: GreaterThanOrEqual.
    /// </summary>
    [HttpGet("condition/datetime-gte")]
    public async Task<ActionResult<PerformanceResult>> TestDateTimeGte()
    {
        return await RunConditionTest("DateTime GreaterThanOrEqual", new Condition
        {
            Field = "CreatedAt", DataType = DataType.DateTime, Operator = Operator.GreaterThanOrEqual, Values = ["2024-01-01T00:00:00"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// DateTime: Between.
    /// </summary>
    [HttpGet("condition/datetime-between")]
    public async Task<ActionResult<PerformanceResult>> TestDateTimeBetween()
    {
        return await RunConditionTest("DateTime Between", new Condition
        {
            Field = "CreatedAt", DataType = DataType.DateTime, Operator = Operator.Between, Values = ["2023-01-01T00:00:00", "2024-12-31T23:59:59"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// DateTime: IsNull on nullable field.
    /// </summary>
    [HttpGet("condition/datetime-isnull")]
    public async Task<ActionResult<PerformanceResult>> TestDateTimeIsNull()
    {
        return await RunConditionTest("DateTime IsNull", new Condition
        {
            Field = "UpdatedAt", DataType = DataType.DateTime, Operator = Operator.IsNull, Values = []
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// DateTime: IsNotNull on nullable field.
    /// </summary>
    [HttpGet("condition/datetime-isnotnull")]
    public async Task<ActionResult<PerformanceResult>> TestDateTimeIsNotNull()
    {
        return await RunConditionTest("DateTime IsNotNull", new Condition
        {
            Field = "UpdatedAt", DataType = DataType.DateTime, Operator = Operator.IsNotNull, Values = []
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Date: GreaterThan (on Order.OrderDate).
    /// </summary>
    [HttpGet("condition/date-gt")]
    public async Task<ActionResult<PerformanceResult>> TestDateGreaterThan()
    {
        return await RunConditionTest("Date GreaterThan", new Condition
        {
            Field = "OrderDate", DataType = DataType.Date, Operator = Operator.GreaterThan, Values = ["2023-06-01"]
        }, q => _context.Orders.Where(q), useOrders: true);
    }

    #endregion

    #region Where(Condition) — Enum Operators

    /// <summary>
    /// Enum: Equal (Order.Status).
    /// </summary>
    [HttpGet("condition/enum-equal")]
    public async Task<ActionResult<PerformanceResult>> TestEnumEqual()
    {
        return await RunConditionTest("Enum Equal", new Condition
        {
            Field = "Status", DataType = DataType.Enum, Operator = Operator.Equal, Values = ["Pending"]
        }, q => _context.Orders.Where(q), useOrders: true);
    }

    /// <summary>
    /// Enum: In (multiple enum values).
    /// </summary>
    [HttpGet("condition/enum-in")]
    public async Task<ActionResult<PerformanceResult>> TestEnumIn()
    {
        return await RunConditionTest("Enum In", new Condition
        {
            Field = "Status", DataType = DataType.Enum, Operator = Operator.In, Values = ["Pending", "Processing", "Shipped"]
        }, q => _context.Orders.Where(q), useOrders: true);
    }

    /// <summary>
    /// Enum: NotEqual.
    /// </summary>
    [HttpGet("condition/enum-notequal")]
    public async Task<ActionResult<PerformanceResult>> TestEnumNotEqual()
    {
        return await RunConditionTest("Enum NotEqual", new Condition
        {
            Field = "Status", DataType = DataType.Enum, Operator = Operator.NotEqual, Values = ["Cancelled"]
        }, q => _context.Orders.Where(q), useOrders: true);
    }

    #endregion

    #region Where(Condition) — Guid Operators

    /// <summary>
    /// Guid: IsNotNull.
    /// </summary>
    [HttpGet("condition/guid-isnotnull")]
    public async Task<ActionResult<PerformanceResult>> TestGuidIsNotNull()
    {
        return await RunConditionTest("Guid IsNotNull", new Condition
        {
            Field = "CategoryId", DataType = DataType.Guid, Operator = Operator.IsNotNull, Values = []
        }, q => _context.Products.Where(q));
    }

    #endregion

    #region Where(Condition) — Nested Property

    /// <summary>
    /// Nested property: ShippingAddress.Country (owned type).
    /// </summary>
    [HttpGet("condition/nested-property")]
    public async Task<ActionResult<PerformanceResult>> TestNestedProperty()
    {
        return await RunConditionTest("Nested Property - ShippingAddress.Country", new Condition
        {
            Field = "ShippingAddress.Country", DataType = DataType.Text, Operator = Operator.IEqual, Values = ["USA"]
        }, q => _context.Orders.Where(q), useOrders: true);
    }

    #endregion

    #region Where(ConditionGroup) — AND / OR / Nested

    /// <summary>
    /// ConditionGroup: AND logic with multiple conditions.
    /// </summary>
    [HttpGet("group/and")]
    public async Task<ActionResult<PerformanceResult>> TestGroupAnd()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var group = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions =
                [
                    new Condition { Field = "Price", DataType = DataType.Number, Operator = Operator.Between, Values = ["20", "100"], Sort = 1 },
                    new Condition { Field = "Rating", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = ["3.5"], Sort = 2 },
                    new Condition { Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"], Sort = 3 }
                ]
            };

            sw.Restart();
            var query = _context.Products.Where(group);
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
                TestName = "ConditionGroup AND - Price 20-100, Rating >= 3.5, Active",
                Metrics = metrics, Input = group, Output = results, Success = true,
                Message = "AND logic: Price BETWEEN 20 AND 100 AND Rating >= 3.5 AND IsActive = true"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupAnd");
            return Ok(new PerformanceResult
            {
                TestName = "ConditionGroup AND - Price 20-100, Rating >= 3.5, Active",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// ConditionGroup: OR logic.
    /// </summary>
    [HttpGet("group/or")]
    public async Task<ActionResult<PerformanceResult>> TestGroupOr()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var group = new ConditionGroup
            {
                Connector = Connector.Or,
                Conditions =
                [
                    new Condition { Field = "Price", DataType = DataType.Number, Operator = Operator.LessThan, Values = ["25"], Sort = 1 },
                    new Condition { Field = "Price", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["300"], Sort = 2 },
                    new Condition { Field = "Rating", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = ["4.8"], Sort = 3 }
                ]
            };

            sw.Restart();
            var query = _context.Products.Where(group);
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
                TestName = "ConditionGroup OR - Budget OR Premium OR Top-Rated",
                Metrics = metrics, Input = group, Output = results, Success = true,
                Message = "OR logic: Price < 25 OR Price > 300 OR Rating >= 4.8"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupOr");
            return Ok(new PerformanceResult
            {
                TestName = "ConditionGroup OR - Budget OR Premium OR Top-Rated",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// ConditionGroup: nested SubConditionGroups (AND outer with OR inner).
    /// </summary>
    [HttpGet("group/nested")]
    public async Task<ActionResult<PerformanceResult>> TestGroupNested()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var group = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions =
                [
                    new Condition { Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"], Sort = 1 }
                ],
                SubConditionGroups =
                [
                    new ConditionGroup
                    {
                        Sort = 1,
                        Connector = Connector.Or,
                        Conditions =
                        [
                            new Condition { Field = "Price", DataType = DataType.Number, Operator = Operator.LessThan, Values = ["30"], Sort = 1 },
                            new Condition { Field = "Price", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["200"], Sort = 2 }
                        ]
                    }
                ]
            };

            sw.Restart();
            var query = _context.Products.Where(group);
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
                TestName = "ConditionGroup Nested - IsActive AND (Price < 30 OR Price > 200)",
                Metrics = metrics, Input = group, Output = results, Success = true,
                Message = "Nested AND/OR groups"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupNested");
            return Ok(new PerformanceResult
            {
                TestName = "ConditionGroup Nested - IsActive AND (Price < 30 OR Price > 200)",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// ConditionGroup: deeply nested — AND outer, OR sub-group, AND sub-sub-group.
    /// </summary>
    [HttpGet("group/deep-nested")]
    public async Task<ActionResult<PerformanceResult>> TestGroupDeepNested()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var group = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions =
                [
                    new Condition { Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"], Sort = 1 },
                    new Condition { Field = "StockQuantity", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["0"], Sort = 2 }
                ],
                SubConditionGroups =
                [
                    new ConditionGroup
                    {
                        Sort = 1,
                        Connector = Connector.Or,
                        Conditions =
                        [
                            new Condition { Field = "Rating", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = ["4.5"], Sort = 1 }
                        ],
                        SubConditionGroups =
                        [
                            new ConditionGroup
                            {
                                Sort = 1,
                                Connector = Connector.And,
                                Conditions =
                                [
                                    new Condition { Field = "Price", DataType = DataType.Number, Operator = Operator.LessThan, Values = ["50"], Sort = 1 },
                                    new Condition { Field = "Name", DataType = DataType.Text, Operator = Operator.IContains, Values = ["pro"], Sort = 2 }
                                ]
                            }
                        ]
                    }
                ]
            };

            sw.Restart();
            var query = _context.Products.Where(group);
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
                TestName = "ConditionGroup Deep Nested - AND + OR + AND sub-sub",
                Metrics = metrics, Input = group, Output = results, Success = true,
                Message = "IsActive AND InStock AND (Rating >= 4.5 OR (Price < 50 AND Name IContains 'pro'))"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupDeepNested");
            return Ok(new PerformanceResult
            {
                TestName = "ConditionGroup Deep Nested - AND + OR + AND sub-sub",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// ConditionGroup: multi-entity Customers with nested OR sub-group.
    /// </summary>
    [HttpGet("group/customers")]
    public async Task<ActionResult<PerformanceResult>> TestGroupCustomers()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var group = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions =
                [
                    new Condition { Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"], Sort = 1 }
                ],
                SubConditionGroups =
                [
                    new ConditionGroup
                    {
                        Sort = 1,
                        Connector = Connector.Or,
                        Conditions =
                        [
                            new Condition { Field = "FirstName", DataType = DataType.Text, Operator = Operator.IStartsWith, Values = ["J"], Sort = 1 },
                            new Condition { Field = "TotalSpent", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = ["1000"], Sort = 2 }
                        ]
                    }
                ]
            };

            sw.Restart();
            var query = _context.Customers.Where(group);
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
                TestName = "ConditionGroup Customers - Active AND (FirstName starts J OR Spent > 1000)",
                Metrics = metrics, Input = group, Output = results, Success = true,
                Message = "Multi-entity ConditionGroup on Customers"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupCustomers");
            return Ok(new PerformanceResult
            {
                TestName = "ConditionGroup Customers - Active AND (FirstName starts J OR Spent > 1000)",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    /// <summary>
    /// ConditionGroup: Orders with nested address property OR sub-group.
    /// </summary>
    [HttpGet("group/orders-nested")]
    public async Task<ActionResult<PerformanceResult>> TestGroupOrdersNested()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var group = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions =
                [
                    new Condition { Field = "IsPaid", DataType = DataType.Boolean, Operator = Operator.Equal, Values = ["true"], Sort = 1 }
                ],
                SubConditionGroups =
                [
                    new ConditionGroup
                    {
                        Sort = 1,
                        Connector = Connector.Or,
                        Conditions =
                        [
                            new Condition { Field = "ShippingAddress.Country", DataType = DataType.Text, Operator = Operator.IEqual, Values = ["USA"], Sort = 1 },
                            new Condition { Field = "ShippingAddress.Country", DataType = DataType.Text, Operator = Operator.IEqual, Values = ["Canada"], Sort = 2 }
                        ]
                    }
                ]
            };

            sw.Restart();
            var query = _context.Orders.Where(group);
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
                TestName = "ConditionGroup Orders - Paid AND (Country = USA OR Canada)",
                Metrics = metrics, Input = group, Output = results, Success = true,
                Message = "Nested owned-type property ConditionGroup on Orders"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in TestGroupOrdersNested");
            return Ok(new PerformanceResult
            {
                TestName = "ConditionGroup Orders - Paid AND (Country = USA OR Canada)",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    #endregion

    #region Bulk Text Operator Test

    /// <summary>
    /// Bulk: test all text operators against the Name field in a single endpoint.
    /// </summary>
    [HttpGet("condition/text-all")]
    public async Task<ActionResult<AllTestsResult>> TestAllTextOperators()
    {
        var overallSw = Stopwatch.StartNew();
        var allResults = new List<PerformanceResult>();

        var ops = new List<(Operator op, List<object> values)>
        {
            (Operator.Equal, ["Pro"]),
            (Operator.IEqual, ["pro"]),
            (Operator.NotEqual, ["Pro"]),
            (Operator.INotEqual, ["pro"]),
            (Operator.Contains, ["Pro"]),
            (Operator.IContains, ["pro"]),
            (Operator.NotContains, ["Pro"]),
            (Operator.INotContains, ["pro"]),
            (Operator.StartsWith, ["Pro"]),
            (Operator.IStartsWith, ["pro"]),
            (Operator.NotStartsWith, ["Pro"]),
            (Operator.INotStartsWith, ["pro"]),
            (Operator.EndsWith, ["Pro"]),
            (Operator.IEndsWith, ["pro"]),
            (Operator.NotEndsWith, ["Pro"]),
            (Operator.INotEndsWith, ["pro"]),
            (Operator.In, ["Pro", "Ultra", "Basic"]),
            (Operator.IIn, ["pro", "ultra", "basic"]),
            (Operator.NotIn, ["Pro", "Ultra"]),
            (Operator.INotIn, ["pro", "ultra"]),
            (Operator.IsNull, []),
            (Operator.IsNotNull, [])
        };

        foreach (var (op, values) in ops)
        {
            var metrics = new PerformanceMetrics();
            var sw = Stopwatch.StartNew();

            try
            {
                var condition = new Condition
                {
                    Field = op is Operator.IsNull or Operator.IsNotNull ? "Description" : "Name",
                    DataType = DataType.Text,
                    Operator = op,
                    Values = values
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
                    TestName = $"Text Operator - {op}",
                    Metrics = metrics,
                    Input = condition,
                    Output = results,
                    Success = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing text operator {op}", op);
                metrics.TotalTimeMs = sw.Elapsed.TotalMilliseconds;
                allResults.Add(new PerformanceResult
                {
                    TestName = $"Text Operator - {op}",
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

    #region Run All Tests

    /// <summary>
    /// Run all Where tests and return aggregated results.
    /// </summary>
    [HttpGet("all")]
    public async Task<ActionResult<AllTestsResult>> TestAll()
    {
        var allResults = new List<PerformanceResult>();
        var overallSw = Stopwatch.StartNew();

        var testMethods = new List<(string name, Func<Task<ActionResult<PerformanceResult>>> method)>
        {
            // Text operators
            ("Text Contains",       TestTextContains),
            ("Text IContains",      TestTextIContains),
            ("Text Equal",          TestTextEqual),
            ("Text IEqual",         TestTextIEqual),
            ("Text StartsWith",     TestTextStartsWith),
            ("Text IStartsWith",    TestTextIStartsWith),
            ("Text EndsWith",       TestTextEndsWith),
            ("Text IEndsWith",      TestTextIEndsWith),
            ("Text NotContains",    TestTextNotContains),
            ("Text INotContains",   TestTextINotContains),
            ("Text NotEqual",       TestTextNotEqual),
            ("Text INotEqual",      TestTextINotEqual),
            ("Text NotStartsWith",  TestTextNotStartsWith),
            ("Text INotStartsWith", TestTextINotStartsWith),
            ("Text NotEndsWith",    TestTextNotEndsWith),
            ("Text INotEndsWith",   TestTextINotEndsWith),
            ("Text In",             TestTextIn),
            ("Text IIn",            TestTextIIn),
            ("Text NotIn",          TestTextNotIn),
            ("Text INotIn",         TestTextINotIn),
            ("Text IsNull",         TestTextIsNull),
            ("Text IsNotNull",      TestTextIsNotNull),
            // Number operators
            ("Number GreaterThan",        TestNumberGreaterThan),
            ("Number GreaterThanOrEqual", TestNumberGreaterThanOrEqual),
            ("Number LessThan",           TestNumberLessThan),
            ("Number LessThanOrEqual",    TestNumberLessThanOrEqual),
            ("Number Between",            TestNumberBetween),
            ("Number NotBetween",         TestNumberNotBetween),
            ("Number Equal",              TestNumberEqual),
            ("Number NotEqual",           TestNumberNotEqual),
            ("Number In",                 TestNumberIn),
            ("Number IsNull",             TestNumberIsNull),
            // Boolean operators
            ("Boolean True",     TestBooleanTrue),
            ("Boolean False",    TestBooleanFalse),
            ("Boolean NotEqual", TestBooleanNotEqual),
            // DateTime / Date operators
            ("DateTime GTE",       TestDateTimeGte),
            ("DateTime Between",   TestDateTimeBetween),
            ("DateTime IsNull",    TestDateTimeIsNull),
            ("DateTime IsNotNull", TestDateTimeIsNotNull),
            ("Date GreaterThan",   TestDateGreaterThan),
            // Enum operators
            ("Enum Equal",    TestEnumEqual),
            ("Enum In",       TestEnumIn),
            ("Enum NotEqual", TestEnumNotEqual),
            // Guid operators
            ("Guid IsNotNull", TestGuidIsNotNull),
            // Nested property
            ("Nested Property", TestNestedProperty),
            // ConditionGroup
            ("Group AND",            TestGroupAnd),
            ("Group OR",             TestGroupOr),
            ("Group Nested",         TestGroupNested),
            ("Group Deep Nested",    TestGroupDeepNested),
            ("Group Customers",      TestGroupCustomers),
            ("Group Orders Nested",  TestGroupOrdersNested)
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
                _logger.LogError(ex, "Error executing Where test: {name}", name);
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

    #region Where(Condition) — Heterogeneous Values (List<object>)

    /// <summary>
    /// Number with raw int (no quotes). Verifies <c>Values = [100]</c> normalizes correctly.
    /// </summary>
    [HttpGet("condition/values-raw-int")]
    public async Task<ActionResult<PerformanceResult>> TestValuesRawInt()
    {
        return await RunConditionTest("Values Raw Int (100)", new Condition
        {
            Field = "StockQuantity",
            DataType = DataType.Number,
            Operator = Operator.GreaterThan,
            Values = [100]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Number with raw double (no quotes). Verifies <c>Values = [99.99]</c> uses InvariantCulture.
    /// </summary>
    [HttpGet("condition/values-raw-double")]
    public async Task<ActionResult<PerformanceResult>> TestValuesRawDouble()
    {
        return await RunConditionTest("Values Raw Double (99.99)", new Condition
        {
            Field = "Price",
            DataType = DataType.Number,
            Operator = Operator.LessThanOrEqual,
            Values = [99.99]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Number Between with raw mixed numeric primitives. Verifies <c>Values = [10, 500.5]</c>.
    /// </summary>
    [HttpGet("condition/values-raw-between")]
    public async Task<ActionResult<PerformanceResult>> TestValuesRawBetween()
    {
        return await RunConditionTest("Values Raw Between (10, 500.5)", new Condition
        {
            Field = "Price",
            DataType = DataType.Number,
            Operator = Operator.Between,
            Values = [10, 500.5]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Number In with raw int collection. Verifies <c>Values = [1, 5, 10, 50]</c>.
    /// </summary>
    [HttpGet("condition/values-raw-in")]
    public async Task<ActionResult<PerformanceResult>> TestValuesRawIn()
    {
        return await RunConditionTest("Values Raw In (1,5,10,50)", new Condition
        {
            Field = "StockQuantity",
            DataType = DataType.Number,
            Operator = Operator.In,
            Values = [1, 5, 10, 50]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Boolean raw <c>true</c> (no quotes). Verifies bool primitive emits lowercase <c>"true"</c>.
    /// </summary>
    [HttpGet("condition/values-raw-bool-true")]
    public async Task<ActionResult<PerformanceResult>> TestValuesRawBoolTrue()
    {
        return await RunConditionTest("Values Raw Bool True", new Condition
        {
            Field = "IsActive",
            DataType = DataType.Boolean,
            Operator = Operator.Equal,
            Values = [true]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Boolean raw <c>false</c> (no quotes). Verifies bool primitive emits lowercase <c>"false"</c>.
    /// </summary>
    [HttpGet("condition/values-raw-bool-false")]
    public async Task<ActionResult<PerformanceResult>> TestValuesRawBoolFalse()
    {
        return await RunConditionTest("Values Raw Bool False", new Condition
        {
            Field = "IsActive",
            DataType = DataType.Boolean,
            Operator = Operator.Equal,
            Values = [false]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Mixed list: raw primitive + quoted string in the same list. Validator must accept both.
    /// </summary>
    [HttpGet("condition/values-mixed")]
    public async Task<ActionResult<PerformanceResult>> TestValuesMixed()
    {
        return await RunConditionTest("Values Mixed (10, \"500\")", new Condition
        {
            Field = "StockQuantity",
            DataType = DataType.Number,
            Operator = Operator.Between,
            Values = [10, "500"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Backward compatibility: quoted-string number (legacy shape). Must still produce a valid query.
    /// </summary>
    [HttpGet("condition/values-legacy-string-number")]
    public async Task<ActionResult<PerformanceResult>> TestValuesLegacyStringNumber()
    {
        return await RunConditionTest("Values Legacy String Number (\"100\")", new Condition
        {
            Field = "StockQuantity",
            DataType = DataType.Number,
            Operator = Operator.GreaterThan,
            Values = ["100"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// Backward compatibility: quoted-string boolean (legacy shape). Must still produce a valid query.
    /// </summary>
    [HttpGet("condition/values-legacy-string-bool")]
    public async Task<ActionResult<PerformanceResult>> TestValuesLegacyStringBool()
    {
        return await RunConditionTest("Values Legacy String Bool (\"true\")", new Condition
        {
            Field = "IsActive",
            DataType = DataType.Boolean,
            Operator = Operator.Equal,
            Values = ["true"]
        }, q => _context.Products.Where(q));
    }

    /// <summary>
    /// End-to-end JSON deserialization: client posts raw JSON with un-quoted primitives.
    /// Exercises the <c>JsonElement</c> branch of <c>Normalizer</c>.
    /// <para>
    /// Example body:
    /// <code>
    /// { "Field": "Price", "DataType": 2, "Operator": 9, "Values": [10, 500.5] }
    /// </code>
    /// </para>
    /// </summary>
    [HttpPost("condition/values-from-json")]
    public async Task<ActionResult<PerformanceResult>> TestValuesFromJson([FromBody] Condition condition)
    {
        return await RunConditionTest("Values From JSON Body", condition,
            q => _context.Products.Where(q));
    }

    /// <summary>
    /// Bulk: runs every heterogeneous-values scenario and reports per-case pass/fail timings.
    /// </summary>
    [HttpGet("condition/values-all")]
    public async Task<ActionResult<AllTestsResult>> TestAllValuesShapes()
    {
        var overallSw = Stopwatch.StartNew();
        var results = new List<PerformanceResult>();

        var cases = new List<(string name, Condition cond)>
        {
            ("Raw Int",                  new Condition { Field = "StockQuantity", DataType = DataType.Number,  Operator = Operator.GreaterThan,      Values = [100] }),
            ("Raw Double",               new Condition { Field = "Price",         DataType = DataType.Number,  Operator = Operator.LessThanOrEqual,  Values = [99.99] }),
            ("Raw Decimal",              new Condition { Field = "Price",         DataType = DataType.Number,  Operator = Operator.GreaterThan,      Values = [49.5m] }),
            ("Raw Long",                 new Condition { Field = "StockQuantity", DataType = DataType.Number,  Operator = Operator.LessThan,         Values = [10000L] }),
            ("Raw Between Numeric",      new Condition { Field = "Price",         DataType = DataType.Number,  Operator = Operator.Between,          Values = [10, 500.5] }),
            ("Raw In Numeric",           new Condition { Field = "StockQuantity", DataType = DataType.Number,  Operator = Operator.In,               Values = [1, 5, 10, 50] }),
            ("Raw Bool True",            new Condition { Field = "IsActive",      DataType = DataType.Boolean, Operator = Operator.Equal,            Values = [true] }),
            ("Raw Bool False",           new Condition { Field = "IsActive",      DataType = DataType.Boolean, Operator = Operator.Equal,            Values = [false] }),
            ("Mixed Raw + String",       new Condition { Field = "StockQuantity", DataType = DataType.Number,  Operator = Operator.Between,          Values = [10, "500"] }),
            ("Legacy String Number",     new Condition { Field = "StockQuantity", DataType = DataType.Number,  Operator = Operator.GreaterThan,      Values = ["100"] }),
            ("Legacy String Bool True",  new Condition { Field = "IsActive",      DataType = DataType.Boolean, Operator = Operator.Equal,            Values = ["true"] }),
            ("Legacy String Bool False", new Condition { Field = "IsActive",      DataType = DataType.Boolean, Operator = Operator.Equal,            Values = ["false"] }),
            ("Raw Text",                 new Condition { Field = "Name",          DataType = DataType.Text,    Operator = Operator.IContains,        Values = ["Pro"] }),
            ("Text In Strings",          new Condition { Field = "Name",          DataType = DataType.Text,    Operator = Operator.In,               Values = ["Pro", "Ultra", "Basic"] }),
        };

        foreach (var (name, cond) in cases)
        {
            var sw = Stopwatch.StartNew();
            var metrics = new PerformanceMetrics();

            try
            {
                sw.Restart();
                var query = _context.Products.Where(cond);
                var translationTime = sw.Elapsed.TotalMilliseconds;

                sw.Restart();
                var rows = await query.Take(100).ToListAsync();
                var executionTime = sw.Elapsed.TotalMilliseconds;

                metrics.TranslationTimeMs = translationTime;
                metrics.ExecutionTimeMs = executionTime;
                metrics.TotalTimeMs = translationTime + executionTime;
                metrics.RecordsReturned = rows.Count;
                metrics.QueryGenerated = query.ToQueryString();

                results.Add(new PerformanceResult
                {
                    TestName = $"Values Shape - {name}",
                    Metrics = metrics,
                    Input = cond,
                    Success = true,
                    Message = $"{cond.Operator} on {cond.Field} returned {rows.Count} rows"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Values shape test failed: {name}", name);
                results.Add(new PerformanceResult
                {
                    TestName = $"Values Shape - {name}",
                    Metrics = metrics,
                    Input = cond,
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        overallSw.Stop();

        return Ok(new AllTestsResult
        {
            TotalTests = results.Count,
            SuccessfulTests = results.Count(r => r.Success),
            FailedTests = results.Count(r => !r.Success),
            TotalExecutionTimeMs = overallSw.Elapsed.TotalMilliseconds,
            AverageTranslationTimeMs = results.Count == 0 ? 0 : results.Average(r => r.Metrics.TranslationTimeMs),
            AverageExecutionTimeMs = results.Count == 0 ? 0 : results.Average(r => r.Metrics.ExecutionTimeMs),
            AverageTotalTimeMs = results.Count == 0 ? 0 : results.Average(r => r.Metrics.TotalTimeMs),
            TotalRecordsReturned = results.Sum(r => r.Metrics.RecordsReturned),
            Results = results
        });
    }

    #endregion

    #region Helper

    private async Task<ActionResult<PerformanceResult>> RunConditionTest<T>(
        string testName, Condition condition, Func<Condition, IQueryable<T>> applyWhere, bool useOrders = false) where T : class
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            sw.Restart();
            var query = applyWhere(condition);
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
                TestName = $"Where Condition - {testName}",
                Metrics = metrics, Input = condition, Output = results, Success = true,
                Message = $"Operator {condition.Operator} on {condition.Field} returned {results.Count} results"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in {testName}", testName);
            return Ok(new PerformanceResult
            {
                TestName = $"Where Condition - {testName}",
                Success = false, Message = ex.Message, Metrics = metrics
            });
        }
    }

    #endregion
}
