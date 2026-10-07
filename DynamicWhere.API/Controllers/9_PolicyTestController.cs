using DynamicWhere.API.Data;
using DynamicWhere.API.Models;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Audit;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace DynamicWhere.API.Controllers;

/// <summary>
/// Controller for testing the field-level policy layer against a real database.
/// Covers: the unguarded refusal, gating, operator restriction, alias resolution, forced predicates,
/// required filters, all transform kinds, the k-anonymity floor, cost, audit, trace, and the
/// no-write-back guarantee.
/// </summary>
/// <remarks>
/// Every endpoint runs against <see cref="Employee"/>, the one entity no other controller queries,
/// so <c>[DwEntity(RequirePolicy = true)]</c> can refuse unguarded reads here without failing the
/// nine suites next door.
/// <para>
/// These endpoints deviate from the house shape in one way, and for one reason. Most of what a
/// policy does is <em>refuse</em>, so the expected result of half this file is an exception. The
/// usual <c>catch</c>-reports-failure block would mark a working denial as a broken test and a
/// silently removed control as a pass — precisely inverted. <see cref="Refuses"/> asserts the
/// refusal and its error code; <see cref="Allows"/> asserts the query ran.
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class PolicyTestController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<PolicyTestController> _logger;

    public PolicyTestController(AppDbContext context, ILogger<PolicyTestController> logger)
    {
        _context = context;
        _logger = logger;
    }

    // ------------------------------------------------------------------ context and helpers

    /// <summary>
    /// Builds the caller for a request and loads whatever the store pinned to it.
    /// </summary>
    /// <remarks>
    /// Once per request, never once per query. A context carries the snapshot it was served, and the
    /// staleness ceiling measures how old that snapshot is — so a context reused across requests
    /// eventually fails rather than silently serving yesterday's policy.
    /// <para>
    /// An unprepared context is refused by the store provider rather than quietly resolving from
    /// attributes alone, which would look like a working policy with the dynamic half missing.
    /// </para>
    /// </remarks>
    private static async Task<DwPolicyContext> CallerAsync(
        string user = "u-1001", string role = "Support", string? purpose = null)
    {
        var context = new DwPolicyContext
        {
            Purpose = purpose
        };

        context
            .WithSubject(DwSubjectKind.User, user)
            .WithSubject(DwSubjectKind.Role, role)
            .WithSubject(DwSubjectKind.Tenant, "acme");

        return await DwPolicy.PrepareAsync(context);
    }

    /// <summary>A filter naming a department, which <c>[DwRequireWhere]</c> demands.</summary>
    private static ConditionGroup InDepartment(string department = "Engineering") => new()
    {
        Connector = Connector.And,
        Conditions =
        [
            new Condition
            {
                Sort = 1,
                Field = "Department",
                DataType = DataType.Text,
                Operator = Operator.Equal,
                Values = [department]
            }
        ]
    };

    /// <summary>Runs a query that is expected to succeed.</summary>
    private async Task<ActionResult<PerformanceResult>> Allows(
        string name, string expectation, Func<Task<object?>> run)
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            object? output = await run();

            metrics.TotalTimeMs = sw.Elapsed.TotalMilliseconds;
            metrics.RecordsReturned = Count(output);

            return Ok(new PerformanceResult
            {
                TestName = name,
                Metrics = metrics,
                Input = expectation,
                Output = output,
                Success = true,
                Message = expectation
            });
        }
        catch (Exception error)
        {
            _logger.LogError(error, "Policy test {Name} threw where it should have run", name);

            return Ok(new PerformanceResult
            {
                TestName = name,
                Metrics = metrics,
                Input = expectation,
                Success = false,
                Message = $"Expected this to be allowed. It threw: {error.Message}"
            });
        }
    }

    /// <summary>
    /// Runs a query that the policy must refuse, and fails when it is allowed.
    /// </summary>
    /// <remarks>
    /// The error code is asserted, not just the throw. Every refusal on this branch has failed open
    /// at least once during development — twenty-six times across eight phases — and an unmatched
    /// fragment resolves to <em>Allow</em>, so "something threw" is not evidence that the control
    /// under test is the one that fired.
    /// </remarks>
    private async Task<ActionResult<PerformanceResult>> Refuses(
        string name, PolicyErrorCode expected, string why, Func<Task> run)
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            await run();

            metrics.TotalTimeMs = sw.Elapsed.TotalMilliseconds;

            return Ok(new PerformanceResult
            {
                TestName = name,
                Metrics = metrics,
                Input = why,
                Success = false,
                Message = $"FAILED OPEN. Expected {expected} and the query was allowed to run."
            });
        }
        catch (PolicyException refusal)
        {
            metrics.TotalTimeMs = sw.Elapsed.TotalMilliseconds;

            bool right = refusal.ErrorCode == expected;

            return Ok(new PerformanceResult
            {
                TestName = name,
                Metrics = metrics,
                Input = why,
                Output = new { refusal.ErrorCode, refusal.Message },
                Success = right,
                Message = right
                    ? $"Refused with {expected}, as it should be. {why}"
                    : $"Refused, but with {refusal.ErrorCode} rather than the expected {expected}."
            });
        }
    }

    private static int Count(object? output) => output switch
    {
        FilterResult<Employee> filter => filter.Data?.Count ?? 0,
        System.Collections.ICollection collection => collection.Count,
        null => 0,
        _ => 1
    };

    #region The unguarded refusal

    /// <summary>
    /// An unguarded query on a type marked RequirePolicy throws instead of returning rows.
    /// </summary>
    [HttpGet("guard/unguarded-is-refused")]
    public Task<ActionResult<PerformanceResult>> TestUnguardedIsRefused() =>
        Refuses(
            "Guard - unguarded read of Employee",
            PolicyErrorCode.PolicyRequired,
            "Employee is [DwEntity(RequirePolicy = true)], so a call that never went through "
            + "ApplyPolicy is refused rather than served. Without this, forgetting the guard on one "
            + "code path returns everything.",
            () =>
            {
                _ = _context.Employees.Select(new List<string> { "Id", "Email" }).Take(1).ToList();
                return Task.CompletedTask;
            });

    #endregion

    #region Gating

    /// <summary>
    /// A field denied for every feature cannot be projected.
    /// </summary>
    [HttpGet("gating/denied-field")]
    public async Task<ActionResult<PerformanceResult>> TestDeniedField()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Allows(
            "Gating - project a [DwDenied] field",
            "In the Convenience tier a denied field is DROPPED from the projection, not refused. "
            + "Strict throws instead. Design 2.5: the tier decides whether a blocked action fails "
            + "the request or quietly narrows it, which is why the trace is the only way to tell a "
            + "policy drop from a null value.",
            async () =>
            {
                FilterResult<dynamic> result = await _context.Employees
                    .ApplyPolicy(caller)
                    .ToListAsyncDynamic(new Filter
                    {
                        ConditionGroup = InDepartment(),
                        Selects = ["Id", "WorkSchedule"],
                        Page = new PageBy { PageNumber = 1, PageSize = 3 }
                    });

                List<PolicyDecision> dropped = result.Policy?.Decisions
                    .Where(d => d.Action == PolicyAction.Dropped)
                    .ToList() ?? [];

                if (dropped.TrueForAll(d => !d.FieldPath.Contains("WorkSchedule", StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        "FAILED OPEN. WorkSchedule is [DwDenied] and the trace does not record it "
                        + "as dropped.");
                }

                return new { Rows = result.Data, Dropped = dropped };
            });
    }

    /// <summary>
    /// A field restricted to Equal and In refuses a Contains sweep.
    /// </summary>
    [HttpGet("gating/operator-restricted")]
    public async Task<ActionResult<PerformanceResult>> TestOperatorRestricted()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Refuses(
            "Gating - Contains on an operator-restricted field",
            PolicyErrorCode.OperatorNotAllowed,
            "Design 7.3. Permitting WHERE on a protected field lets TotalCount count the matches "
            + "without selecting anything. [DwOperators(Allow = Equal, In)] lets a caller confirm a "
            + "code it already knows and refuses the sweep that would discover one.",
            async () =>
            {
                await _context.Employees
                    .ApplyPolicy(caller)
                    .ToListAsync(new Filter
                    {
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions =
                            [
                                new Condition
                                {
                                    Sort = 1, Field = "Department", DataType = DataType.Text,
                                    Operator = Operator.Equal, Values = ["Engineering"]
                                },
                                new Condition
                                {
                                    Sort = 2, Field = "Code", DataType = DataType.Text,
                                    Operator = Operator.Contains, Values = ["EMP"]
                                }
                            ]
                        }
                    });
            });
    }

    /// <summary>
    /// A masked field cannot be sorted on, because sorting ranks the real value.
    /// </summary>
    [HttpGet("gating/masked-field-is-unsortable")]
    public async Task<ActionResult<PerformanceResult>> TestMaskedFieldUnsortable()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Allows(
            "Gating - order by a masked field",
            "Design 7.4. Ordering runs in SQL against the stored value, so paging a masked column "
            + "ranks the true order and, with range filters, converges on the values the mask hides. "
            + "Email carries [DwNoOrder], so the sort is dropped here and the rows come back in the "
            + "database's own order rather than ranked by the hidden value.",
            async () =>
            {
                FilterResult<Employee> result = await _context.Employees
                    .ApplyPolicy(caller)
                    .ToListAsync(new Filter
                    {
                        ConditionGroup = InDepartment(),
                        Selects = ["Id", "Email"],
                        Orders = [new OrderBy { Sort = 1, Field = "Email", Direction = Direction.Ascending }],
                        Page = new PageBy { PageNumber = 1, PageSize = 5 }
                    });

                List<PolicyDecision> dropped = result.Policy?.Decisions
                    .Where(d => d.Action == PolicyAction.Dropped && d.Feature == PolicyFeature.Order)
                    .ToList() ?? [];

                if (dropped.Count == 0)
                {
                    throw new InvalidOperationException(
                        "FAILED OPEN. The sort on a masked field was not dropped.");
                }

                return new
                {
                    Rows = result.Data?.ConvertAll(e => new { e.Id, e.Email }),
                    DroppedSorts = dropped
                };
            });
    }

    #endregion

    #region Injection

    /// <summary>
    /// The forced predicate reaches the generated SQL whether the caller asked for it or not.
    /// </summary>
    [HttpGet("injection/forced-predicate")]
    public async Task<ActionResult<PerformanceResult>> TestForcedPredicate()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Allows(
            "Injection - [DwForceWhere] reaches the SQL",
            "IsActive = true is ANDed into every guarded query. Forced predicates are collected and "
            + "ANDed rather than elected, so a lower-authority rule can narrow the scope further but "
            + "can never widen or discard it.",
            async () =>
            {
                FilterResult<Employee> result = await _context.Employees
                    .ApplyPolicy(caller)
                    .ToListAsync(
                        new Filter
                        {
                            ConditionGroup = InDepartment(),
                            Selects = ["Id", "FirstName", "IsActive"]
                        },
                        getQueryString: true);

                int inactive = await _context.Employees.CountAsync(e => !e.IsActive);

                return new
                {
                    result.TotalCount,
                    Returned = result.Data?.Count ?? 0,
                    InactiveEmployeesInTheDatabase = inactive,
                    EveryRowIsActive = result.Data?.TrueForAll(e => e.IsActive) ?? true,
                    result.QueryString,
                    Decisions = result.Policy?.Decisions
                };
            });
    }

    /// <summary>
    /// Every condition set of a segment is scoped before the sets are combined in the database.
    /// </summary>
    /// <remarks>
    /// The seed keeps one former engineer, a Senior Engineer with <c>IsActive = false</c>, who matches
    /// the sets of every operation below. Each result is compared with the same sets evaluated in
    /// plain LINQ over serving employees, so a set that reached the table unscoped fails the test by
    /// returning that row.
    /// </remarks>
    [HttpGet("injection/forced-predicate-segment")]
    public async Task<ActionResult<PerformanceResult>> TestForcedPredicateSegment()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Allows(
            "Injection - [DwForceWhere] scopes every set of a Segment",
            "IsActive = true is ANDed into every condition set before Union, Intersect and Except "
            + "combine them in the database. The former engineer in the seed matches the sets of all "
            + "three operations, and none of the three results may contain them.",
            async () =>
            {
                var outcomes = new List<object>();

                foreach (Intersection operation in new[] { Intersection.Union, Intersection.Intersect, Intersection.Except })
                {
                    // The two sets, and the same two sets as LINQ predicates.
                    ConditionGroup first, second;
                    Func<Employee, bool> inFirst, inSecond;

                    switch (operation)
                    {
                        // The former engineer sits in the second set of the Union and the first set of
                        // the Except, so a scope missing from either position returns them.
                        case Intersection.Union:
                            first = InDepartment("Support");
                            second = InPosition("Engineering", "Senior Engineer");
                            inFirst = e => e.Department == "Support";
                            inSecond = e => e.Department == "Engineering" && e.Position == "Senior Engineer";
                            break;

                        case Intersection.Intersect:
                            first = InDepartment();
                            second = InPosition("Engineering", "Senior Engineer");
                            inFirst = e => e.Department == "Engineering";
                            inSecond = e => e.Department == "Engineering" && e.Position == "Senior Engineer";
                            break;

                        default:
                            first = InDepartment();
                            second = InPosition("Engineering", "Engineer");
                            inFirst = e => e.Department == "Engineering";
                            inSecond = e => e.Department == "Engineering" && e.Position == "Engineer";
                            break;
                    }

                    SegmentResult<Employee> result = await _context.Employees
                        .ApplyPolicy(caller)
                        .ToListAsync(new Segment
                        {
                            ConditionSets =
                            [
                                new ConditionSet { Sort = 1, ConditionGroup = first },
                                new ConditionSet { Sort = 2, Intersection = operation, ConditionGroup = second }
                            ],
                            Page = new PageBy { PageNumber = 1, PageSize = 50 }
                        });

                    List<Employee> everyone = await _context.Employees.AsNoTracking().ToListAsync();

                    bool Combined(Employee e) => operation switch
                    {
                        Intersection.Union => inFirst(e) || inSecond(e),
                        Intersection.Intersect => inFirst(e) && inSecond(e),
                        _ => inFirst(e) && !inSecond(e)
                    };

                    var expected = everyone.Where(e => e.IsActive && Combined(e)).Select(e => e.Id).ToHashSet();
                    int outOfScope = everyone.Count(e => !e.IsActive && Combined(e));
                    var returned = (result.Data ?? []).Select(e => e.Id).ToHashSet();

                    if (!returned.SetEquals(expected) || result.TotalCount != expected.Count)
                    {
                        throw new InvalidOperationException(
                            $"FAILED OPEN. {operation} returned {returned.Count} rows (TotalCount "
                            + $"{result.TotalCount}) where the scoped sets hold {expected.Count}. "
                            + $"{outOfScope} inactive employees match the unscoped sets.");
                    }

                    outcomes.Add(new
                    {
                        Operation = operation.ToString(),
                        result.TotalCount,
                        Returned = returned.Count,
                        InactiveEmployeesTheUnscopedSetsWouldAdd = outOfScope,
                        EveryRowIsActive = (result.Data ?? []).TrueForAll(e => e.IsActive)
                    });
                }

                return outcomes;
            });
    }

    /// <summary>A filter naming a department and a position within it.</summary>
    private static ConditionGroup InPosition(string department, string position)
    {
        ConditionGroup group = InDepartment(department);

        group.Conditions.Add(new Condition
        {
            Sort = 2,
            Field = "Position",
            DataType = DataType.Text,
            Operator = Operator.Equal,
            Values = [position]
        });

        return group;
    }

    /// <summary>
    /// A request that does not filter on a required field is refused.
    /// </summary>
    [HttpGet("injection/required-filter-missing")]
    public async Task<ActionResult<PerformanceResult>> TestRequiredFilterMissing()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Refuses(
            "Injection - [DwRequireWhere] with no filter supplied",
            PolicyErrorCode.RequiredFilterMissing,
            "Department carries [DwRequireWhere]. An unscoped read of every department is the query "
            + "worth refusing, and a requirement refuses it without also blocking the legitimate "
            + "scoped one.",
            async () =>
            {
                await _context.Employees
                    .ApplyPolicy(caller)
                    .ToListAsync(new Filter { Selects = ["Id", "FirstName"] });
            });
    }

    /// <summary>
    /// A field addressed by its alias resolves, and comes back under the alias.
    /// </summary>
    [HttpGet("injection/alias-round-trip")]
    public async Task<ActionResult<PerformanceResult>> TestAliasRoundTrip()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Allows(
            "Injection - [DwAlias] in and out",
            "EmployeeCode is addressed as \"Code\". The rename happens after materialization, in the "
            + "result transformer, so the caller never learns the internal name and the projection "
            + "itself is untouched.",
            async () =>
            {
                FilterResult<dynamic> result = await _context.Employees
                    .ApplyPolicy(caller)
                    .ToListAsyncDynamic(new Filter
                    {
                        ConditionGroup = InDepartment(),
                        Selects = ["Id", "Code"],
                        Page = new PageBy { PageNumber = 1, PageSize = 5 }
                    });

                return result.Data;
            });
    }

    #endregion

    #region Transformation

    /// <summary>
    /// Email comes back masked, and the address it hides never leaves the database.
    /// </summary>
    [HttpGet("transform/mask-email")]
    public async Task<ActionResult<PerformanceResult>> TestMaskEmail()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Allows(
            "Transform - MaskStrategy.Email",
            "Masking runs in memory after materialization, never in SQL. Filtering and sorting still "
            + "see the real value; what reaches the caller does not.",
            async () =>
            {
                FilterResult<Employee> result = await _context.Employees
                    .ApplyPolicy(caller)
                    .ToListAsync(new Filter
                    {
                        ConditionGroup = InDepartment(),
                        Selects = ["Id", "FirstName", "Email"],
                        Page = new PageBy { PageNumber = 1, PageSize = 5 }
                    });

                return new
                {
                    Masked = result.Data?.ConvertAll(e => new { e.Id, e.FirstName, e.Email }),
                    Trace = result.Policy?.Decisions
                };
            });
    }

    /// <summary>
    /// A masked field one collection deep is masked too.
    /// </summary>
    [HttpGet("transform/nested-collection")]
    public async Task<ActionResult<PerformanceResult>> TestNestedCollectionMask()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Allows(
            "Transform - masking through a collection",
            "EmergencyContacts is a list, so reaching PhoneNumber means descending into it and "
            + "transforming every element. A field one navigation deeper than the walk reaches is a "
            + "field that is quietly not protected.",
            async () =>
            {
                FilterResult<Employee> result = await _context.Employees
                    .ApplyPolicy(caller)
                    .ToListAsync(new Filter
                    {
                        ConditionGroup = InDepartment(),
                        Selects = ["Id", "EmergencyContacts"],
                        Page = new PageBy { PageNumber = 1, PageSize = 5 }
                    });

                return result.Data?.ConvertAll(e => new { e.Id, e.EmergencyContacts });
            });
    }

    /// <summary>
    /// Salary comes back rounded, and the exact figure stays in the database.
    /// </summary>
    [HttpGet("transform/generalize-salary")]
    public async Task<ActionResult<PerformanceResult>> TestGeneralizeSalary()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Allows(
            "Transform - GeneralizeMode.Round",
            "Generalization is the half of the type space masking cannot reach: arithmetic runs in "
            + "decimal and the result converts back to the member's own type, so a rounded decimal is "
            + "still a decimal and the property can still hold it.",
            async () =>
            {
                FilterResult<Employee> result = await _context.Employees
                    .ApplyPolicy(caller)
                    .ToListAsync(new Filter
                    {
                        ConditionGroup = InDepartment(),
                        Selects = ["Id", "Salary"],
                        Page = new PageBy { PageNumber = 1, PageSize = 5 }
                    });

                List<Guid> ids = result.Data?.ConvertAll(e => e.Id) ?? [];

                var stored = await _context.Employees
                    .AsNoTracking()
                    .Where(e => ids.Contains(e.Id))
                    .Select(e => new { e.Id, e.Salary })
                    .ToListAsync();

                return new
                {
                    Rounded = result.Data?.ConvertAll(e => new { e.Id, e.Salary }),
                    StoredUnrounded = stored
                };
            });
    }

    #endregion

    #region k-anonymity

    /// <summary>
    /// A group smaller than the floor is refused rather than returned.
    /// </summary>
    [HttpGet("kanonymity/group-too-small")]
    public async Task<ActionResult<PerformanceResult>> TestGroupTooSmall()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Allows(
            "k-anonymity - aggregate over groups below MinGroupSize",
            "Design 7.2, the control a reader cannot guess from the API. SUM, MAX and MIN run in SQL "
            + "against the stored value before any transform applies, so MAX(Salary) over a "
            + "department of one returns that person's exact pay. Salary sets MinGroupSize = 5 and "
            + "grouping by employee code makes every group a singleton, so every group is SUPPRESSED "
            + "\u2014 the floor removes rows rather than refusing the query, and GroupTooSmall is "
            + "reserved for a summary that already uses the reserved size alias.",
            async () =>
            {
                SummaryResult result = await _context.Employees
                    .ApplyPolicy(caller)
                    .ToListAsync(new Summary
                    {
                        // A summary is a queryable surface like any other, so the required filter
                        // applies here too.
                        ConditionGroup = InDepartment(),
                        GroupBy = new GroupBy
                        {
                            Fields = ["Code"],
                            AggregateBy =
                            [
                                new AggregateBy
                                {
                                    Field = "Salary", Alias = "MaxSalary", Aggregator = Aggregator.Maximum
                                }
                            ]
                        }
                    });

                int returned = result.Data?.Count ?? 0;

                int engineers = await _context.Employees
                    .CountAsync(e => e.Department == "Engineering" && e.IsActive);

                if (returned > 0)
                {
                    throw new InvalidOperationException(
                        $"FAILED OPEN. {engineers} singleton groups existed and {returned} came "
                        + "back. Each one is one person's exact salary.");
                }

                return new
                {
                    SingletonGroupsInTheData = engineers,
                    GroupsReturned = returned,
                    Trace = result.Policy?.Decisions
                };
            });
    }

    /// <summary>
    /// The same aggregate over a group at or above the floor is served.
    /// </summary>
    [HttpGet("kanonymity/group-large-enough")]
    public async Task<ActionResult<PerformanceResult>> TestGroupLargeEnough()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Allows(
            "k-anonymity - aggregate over a group at or above MinGroupSize",
            "The other half of the same control. AllowAggregate = true opts Salary in; MinGroupSize "
            + "= 5 bounds it. Either one alone is a hole with a lid on it.",
            async () =>
            {
                SummaryResult result = await _context.Employees
                    .ApplyPolicy(caller)
                    .ToListAsync(new Summary
                    {
                        ConditionGroup = InDepartment(),
                        GroupBy = new GroupBy
                        {
                            Fields = ["Department"],
                            AggregateBy =
                            [
                                new AggregateBy { Alias = "Headcount", Aggregator = Aggregator.Count },
                                new AggregateBy
                                {
                                    Field = "Salary", Alias = "AvgSalary", Aggregator = Aggregator.Average
                                }
                            ]
                        }
                    });

                return new { Groups = result.Data, Trace = result.Policy?.Decisions };
            });
    }

    #endregion

    #region Cost, audit and trace

    /// <summary>
    /// A query naming more than its budget allows is refused before it runs.
    /// </summary>
    [HttpGet("cost/budget-exceeded")]
    public async Task<ActionResult<PerformanceResult>> TestCostExceeded()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Refuses(
            "Cost - a query over the budget",
            PolicyErrorCode.QueryCostExceeded,
            "Salary carries [DwCost(10)]. The budget is charged per named field, before execution, "
            + "so an expensive query is refused rather than run and then regretted.",
            async () =>
            {
                var conditions = new List<Condition>
                {
                    new()
                    {
                        Sort = 1, Field = "Department", DataType = DataType.Text,
                        Operator = Operator.Equal, Values = ["Engineering"]
                    }
                };

                // Forty-five, not two hundred: MaxConditions is 50, and exceeding it first would
                // refuse with CapExceeded and prove nothing about the cost budget. At [DwCost(10)]
                // each, forty-five Salary conditions come to 450 against the demo's MaxQueryCost of
                // 400.
                for (int i = 0; i < 45; i++)
                {
                    conditions.Add(new Condition
                    {
                        Sort = i + 2,
                        Field = "Salary",
                        DataType = DataType.Number,
                        Operator = Operator.GreaterThan,
                        Values = ["0"]
                    });
                }

                await _context.Employees
                    .ApplyPolicy(caller)
                    .ToListAsync(new Filter
                    {
                        ConditionGroup = new ConditionGroup
                        {
                            Connector = Connector.And,
                            Conditions = conditions
                        }
                    });
            });
    }

    /// <summary>
    /// Every use of an audited field is recorded, and drains to a sink.
    /// </summary>
    [HttpGet("audit/drain")]
    public async Task<ActionResult<PerformanceResult>> TestAudit()
    {
        DwPolicyContext caller = await CallerAsync(purpose: "support");

        return await Allows(
            "Audit - [DwAudit] records a use",
            "Salary is audited. The resolver records the event where the policy is elected, not at "
            + "each call site, which is why a new queryable surface cannot forget to audit: "
            + "Gate.PolicyFor takes the feature it resolves for and writes the entry itself.",
            async () =>
            {
                await _context.Employees
                    .ApplyPolicy(caller)
                    .ToListAsync(new Filter
                    {
                        ConditionGroup = InDepartment(),
                        Selects = ["Id", "Salary"],
                        Page = new PageBy { PageNumber = 1, PageSize = 3 }
                    });

                var sink = new CollectingSink();
                int drained = await DwPolicy.DrainAuditAsync(caller, sink);

                return new { Drained = drained, sink.Events };
            });
    }

    /// <summary>
    /// The trace reports what the policy did, which is the only way to see a dropped field.
    /// </summary>
    [HttpGet("trace/decisions")]
    public async Task<ActionResult<PerformanceResult>> TestTrace()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Allows(
            "Trace - what the policy did to the query",
            "A dropped field leaves nothing behind in the data, so FilterResult.Policy is the only "
            + "way a caller can tell a policy drop from a null value.",
            async () =>
            {
                FilterResult<Employee> result = await _context.Employees
                    .ApplyPolicy(caller)
                    .ToListAsync(new Filter
                    {
                        ConditionGroup = InDepartment(),
                        Selects = ["Id", "FirstName", "Email", "Salary"],
                        Page = new PageBy { PageNumber = 1, PageSize = 3 }
                    });

                return new
                {
                    result.Policy?.Tier,
                    result.Policy?.DryRun,
                    Decisions = result.Policy?.Decisions
                };
            });
    }

    #endregion

    #region The blocking test

    /// <summary>
    /// A transformed read never writes the transformed value back to the database.
    /// </summary>
    /// <remarks>
    /// Design section 8.3 calls this the most important test in the suite, and it is the reason the
    /// transform pipeline detaches before it touches anything. The failure mode it guards against is
    /// silent, permanent data destruction: if a guarded read ever returned tracked entities, the
    /// masked value would be a pending modification, and the next unrelated <c>SaveChanges</c> on
    /// the same context would overwrite the real data with asterisks. No exception, no log line, and
    /// no way back without a restore.
    /// <para>
    /// Nothing else catches a regression here. The transform is correct, the read looks right, and
    /// the damage happens on a later write in a different method.
    /// </para>
    /// </remarks>
    [HttpGet("tracking/no-writeback")]
    public async Task<ActionResult<PerformanceResult>> TestNoWriteBack()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Allows(
            "Tracking - a transformed read cannot corrupt the database",
            "Reads transformed rows, proves every one is detached, then commits an unrelated change "
            + "on the same DbContext and re-reads the protected columns with raw SQL.",
            async () =>
            {
                // 1. A guarded read that masks Email and rounds Salary.
                FilterResult<Employee> guarded = await _context.Employees
                    .ApplyPolicy(caller)
                    .ToListAsync(new Filter
                    {
                        ConditionGroup = InDepartment(),
                        Page = new PageBy { PageNumber = 1, PageSize = 10 }
                    });

                List<Employee> rows = guarded.Data ?? [];

                if (rows.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No employees in Engineering to test against; seed the database first.");
                }

                // 2. Every returned entity must be untracked. A tracked one holds the transformed
                //    value as a pending modification.
                var tracked = rows
                    .Where(e => _context.Entry(e).State != EntityState.Detached)
                    .Select(e => new { e.Id, State = _context.Entry(e).State.ToString() })
                    .ToList();

                Guid probe = rows[0].Id;

                var before = await ReadRawAsync(probe);

                // 3. An unrelated write on the SAME context. If step 2 ever regresses, this is the
                //    call that destroys the data.
                Product? product = await _context.Products.FirstOrDefaultAsync();

                if (product is not null)
                {
                    product.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                }

                // 4. Re-read the protected columns, bypassing EF entirely.
                var after = await ReadRawAsync(probe);

                bool intact = before.Email == after.Email && before.Salary == after.Salary;

                if (!intact || tracked.Count > 0)
                {
                    throw new InvalidOperationException(
                        "The no-write-back guarantee is broken. "
                        + $"Tracked entities: {tracked.Count}. "
                        + $"Email before/after: {before.Email} / {after.Email}. "
                        + $"Salary before/after: {before.Salary} / {after.Salary}.");
                }

                return new
                {
                    RowsRead = rows.Count,
                    TrackedEntities = tracked,
                    MaskedEmailReturned = rows[0].Email,
                    RoundedSalaryReturned = rows[0].Salary,
                    StoredEmailBefore = before.Email,
                    StoredEmailAfter = after.Email,
                    StoredSalaryBefore = before.Salary,
                    StoredSalaryAfter = after.Salary,
                    UnrelatedWriteCommitted = product is not null,
                    Intact = intact
                };
            });
    }

    /// <summary>Reads the protected columns straight out of the database, bypassing EF.</summary>
    private async Task<(string Email, decimal Salary)> ReadRawAsync(Guid id)
    {
        var connection = _context.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """SELECT "Email", "Salary" FROM "Employees" WHERE "Id" = @id""";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@id";
        parameter.Value = id;
        command.Parameters.Add(parameter);

        using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException($"Employee {id} vanished mid-test.");
        }

        return (reader.GetString(0), reader.GetDecimal(1));
    }

    #endregion

    #region 3.5.0: a row read whole, a summary with no grouping, and pages that are total

    /// <summary>The active employees as cards, projected before the guard as a read model is.</summary>
    private IQueryable<EmployeeCardRow> Cards() => _context.Employees
        .Where(e => e.IsActive)
        .Select(e => new EmployeeCardRow
        {
            Id = e.Id,
            FirstName = e.FirstName,
            LastName = e.LastName,
            Department = e.Department,
            Position = e.Position
        });

    /// <summary>The active employees as a work queue, projected the same way.</summary>
    private IQueryable<EmployeeQueueRow> Queue() => _context.Employees
        .Where(e => e.IsActive)
        .Select(e => new EmployeeQueueRow
        {
            Id = e.Id,
            FirstName = e.FirstName,
            LastName = e.LastName,
            Department = e.Department,
            Position = e.Position
        });

    /// <summary>
    /// A caller who narrows a row type declared RefuseSelects is refused.
    /// </summary>
    [HttpGet("refuse-selects/selects-refused")]
    public async Task<ActionResult<PerformanceResult>> TestSelectsRefused()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Refuses(
            "RefuseSelects - a caller narrows a card",
            PolicyErrorCode.SelectsRefused,
            "EmployeeCardRow is [DwEntity(RefuseSelects = true)]: a card is read whole, and a narrowed one "
            + "would report an empty string for every member the caller did not name. The names are gated "
            + "first, so a denied one would still be FieldDeniedForSelect; both of these may be selected, so "
            + "the list itself is refused, in either tier.",
            async () => await Cards().ApplyPolicy(caller).ToListAsync(new Filter
            {
                Selects = ["FirstName", "Department"],
                Page = new PageBy { PageNumber = 1, PageSize = 3 }
            }));
    }

    /// <summary>
    /// The same row type read without Selects comes back whole.
    /// </summary>
    [HttpGet("refuse-selects/whole-cards")]
    public async Task<ActionResult<PerformanceResult>> TestWholeCards()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Allows(
            "RefuseSelects - a card read whole",
            "With no Selects the same query runs and every card comes back whole. A null or empty Selects is "
            + "never this refusal, and neither is a projection the policy synthesizes to withhold a denied member.",
            async () =>
            {
                FilterResult<EmployeeCardRow> result = await Cards().ApplyPolicy(caller).ToListAsync(new Filter
                {
                    Page = new PageBy { PageNumber = 1, PageSize = 3 }
                });

                if (result.Data.Count == 0
                    || result.Data.Exists(card => card.FirstName.Length == 0 || card.Department.Length == 0))
                {
                    throw new InvalidOperationException(
                        "Expected whole cards, and a card came back narrowed, or none came back.");
                }

                return result.Data;
            });
    }

    /// <summary>
    /// A dry run records the refusal and runs the projection as written.
    /// </summary>
    [HttpGet("refuse-selects/dry-run-traced")]
    public async Task<ActionResult<PerformanceResult>> TestSelectsRefusedInDryRun()
    {
        DwPolicyContext canary = new() { DryRun = true };

        canary
            .WithSubject(DwSubjectKind.User, "u-1001")
            .WithSubject(DwSubjectKind.Role, "Support")
            .WithSubject(DwSubjectKind.Tenant, "acme");

        DwPolicyContext caller = await DwPolicy.PrepareAsync(canary);

        return await Allows(
            "RefuseSelects - a canary caller in dry run",
            "A dry run refuses nothing and records everything: the narrowed query runs as written, and the "
            + "trace holds the Denied decision on \"*\" for Select whose reason names the attribute, which is "
            + "what an operator rolling the flag out reads before turning it on.",
            async () =>
            {
                PolicyQueryable<EmployeeCardRow> guarded = Cards().ApplyPolicy(caller);

                FilterResult<EmployeeCardRow> result = await guarded.ToListAsync(new Filter
                {
                    Selects = ["FirstName"],
                    Page = new PageBy { PageNumber = 1, PageSize = 3 }
                });

                PolicyDecision? recorded = guarded.LastTrace?.Decisions.FirstOrDefault(decision =>
                    decision.FieldPath == "*"
                    && decision.Feature == PolicyFeature.Select
                    && decision.Action == PolicyAction.Denied
                    && decision.Reason == "DwEntityAttribute(RefuseSelects = true)");

                if (recorded is null)
                {
                    throw new InvalidOperationException(
                        "The dry run ran, and its trace does not record what enforcement would have refused.");
                }

                return new { Rows = result.Data, Recorded = recorded };
            });
    }

    /// <summary>
    /// A summary with no grouping is a malformed request under a policy too.
    /// </summary>
    [HttpGet("summary/no-groupby-is-malformed")]
    public async Task<ActionResult<PerformanceResult>> TestSummaryWithoutGroupBy()
    {
        const string name = "Summary - no groupBy under a policy";
        const string why =
            "A request body that leaves out \"groupBy\" binds a Summary whose GroupBy is null. Since 3.5.0 it "
            + "is refused as a malformed request, LogicException GroupByMustHasAtLeastOneField, as an empty "
            + "grouping always was, and under a policy it is not a PolicyException. It used to leave as "
            + "ArgumentNullException, which a host answers with a five-hundred.";

        DwPolicyContext caller = await CallerAsync();
        var metrics = new PerformanceMetrics();

        try
        {
            await _context.Employees.ApplyPolicy(caller).ToListAsync(new Summary { ConditionGroup = InDepartment() });

            return Ok(new PerformanceResult
            {
                TestName = name,
                Metrics = metrics,
                Input = why,
                Success = false,
                Message = "Expected a malformed-request refusal, and the summary ran."
            });
        }
        catch (PolicyException refusal)
        {
            return Ok(new PerformanceResult
            {
                TestName = name,
                Metrics = metrics,
                Input = why,
                Success = false,
                Message = $"Refused by the policy with {refusal.ErrorCode}, where a malformed request is a LogicException."
            });
        }
        catch (LogicException malformed)
        {
            bool right = malformed.Message == "GroupByMustHasAtLeastOneField";

            return Ok(new PerformanceResult
            {
                TestName = name,
                Metrics = metrics,
                Input = why,
                Output = new { malformed.Message },
                Success = right,
                Message = right
                    ? "Refused as a malformed request, as it should be."
                    : $"Refused with '{malformed.Message}' rather than GroupByMustHasAtLeastOneField."
            });
        }
    }

    /// <summary>
    /// Paging by a field a whole team shares returns every row once, because the default breaks the ties.
    /// </summary>
    [HttpGet("tiebreak/pages-are-total")]
    public async Task<ActionResult<PerformanceResult>> TestTiebreakPagesAreTotal()
    {
        DwPolicyContext caller = await CallerAsync();

        return await Allows(
            "DefaultOrderAsTiebreak - paging by a shared department",
            "EmployeeQueueRow is [DwEntity(DefaultOrder = \"LastName, Id\", DefaultOrderAsTiebreak = true)]. A "
            + "caller ordering by Department, which a whole team shares, reads ORDER BY Department, LastName, Id: "
            + "the default's fields follow the caller's, so every row comes back once across the pages.",
            async () =>
            {
                const int pageSize = 4;

                List<Guid> read = [];
                int total = 0;
                string orderBy = string.Empty;

                for (int page = 1; page == 1 || read.Count < total; page++)
                {
                    FilterResult<EmployeeQueueRow> result = await Queue().ApplyPolicy(caller).ToListAsync(
                        new Filter
                        {
                            Orders = [new OrderBy { Sort = 1, Field = "Department" }],
                            Page = new PageBy { PageNumber = page, PageSize = pageSize }
                        },
                        getQueryString: page == 1);

                    if (page == 1)
                    {
                        total = result.TotalCount;
                        orderBy = result.QueryString is { } sql
                            ? sql[sql.LastIndexOf("ORDER BY", StringComparison.Ordinal)..]
                            : string.Empty;
                    }

                    if (result.Data.Count == 0)
                    {
                        break;
                    }

                    read.AddRange(result.Data.Select(row => row.Id));
                }

                int twice = read.Count - read.Distinct().Count();
                int never = total - read.Distinct().Count();

                if (total == 0 || twice != 0 || never != 0
                    || !orderBy.Contains("\"LastName\"", StringComparison.Ordinal)
                    || !orderBy.Contains("\"Id\"", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Expected every one of {total} rows once, ordered by Department, LastName and Id. "
                        + $"{twice} came back twice and {never} never; {orderBy}");
                }

                return new { Total = total, PageSize = pageSize, Read = read.Count, OrderBy = orderBy };
            });
    }

    #endregion

    /// <summary>Collects audit events in memory so an endpoint can show them.</summary>
    private sealed class CollectingSink : IDwAuditSink
    {
        public List<DwAuditEvent> Events { get; } = [];

        public ValueTask WriteAsync(DwAuditEvent auditEvent, CancellationToken ct)
        {
            Events.Add(auditEvent);
            return default;
        }
    }
}
