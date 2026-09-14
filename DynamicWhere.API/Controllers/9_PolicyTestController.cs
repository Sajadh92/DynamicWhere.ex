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
