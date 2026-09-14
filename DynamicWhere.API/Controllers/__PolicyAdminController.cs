using DynamicWhere.API.Models;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Discovery;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.EntityFrameworkCore;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

namespace DynamicWhere.API.Controllers;

/// <summary>
/// Controller for administering runtime policy rules and inspecting what the engine decided.
/// Covers: store health, listing and writing rules, the sealed-field refusal, explain, schema
/// discovery, and simulation.
/// </summary>
/// <remarks>
/// The package's own administrative endpoints are mounted separately, by
/// <c>app.MapDwPolicyAdmin(...)</c> in Program.cs, and live under <c>/dw-policies</c>. They are the
/// surface a real operator uses and they refuse to mount without a named authorization policy.
/// <para>
/// This controller is the demonstration next to them: it drives the same store and the same
/// resolver through Swagger, and shows the one thing the endpoints alone cannot — the <em>before
/// and after</em> of a rule, on a query, against real data. Rules written here are visible to
/// <c>/dw-policies/rules</c> and vice versa, because both go through the one store.
/// </para>
/// </remarks>
[ApiController]
[Route("api/[controller]")]
public class PolicyAdminController : ControllerBase
{
    /// <summary>The rule this controller writes and removes, so the demo is repeatable.</summary>
    private static readonly Guid DemoRuleId = new("b6f3a0de-0000-4000-8000-000000000001");

    private readonly DwPolicyDbContext _store;
    private readonly ILogger<PolicyAdminController> _logger;

    public PolicyAdminController(DwPolicyDbContext store, ILogger<PolicyAdminController> logger)
    {
        _store = store;
        _logger = logger;
    }

    private static async Task<DwPolicyContext> CallerAsync(string role = "Support") =>
        await DwPolicy.PrepareAsync(
            new DwPolicyContext()
                .WithSubject(DwSubjectKind.User, "u-1001")
                .WithSubject(DwSubjectKind.Role, role)
                .WithSubject(DwSubjectKind.Tenant, "acme"));

    private static IDwPolicyWritableStore Writable() =>
        new EfPolicyStore(
            () => new DwPolicyDbContext(
                new DbContextOptionsBuilder<DwPolicyDbContext>()
                    .UseNpgsql(DemoConnection)
                    .Options),

            // Not optional. SealedFields.Refuse holds a rule's entity name as a string and needs a
            // Type to check it against, so a store that is handed no resolver cannot perform the
            // check and accepts — which would make SealedFieldIsRefused below report FAILED OPEN
            // against a store that had merely never been told how to resolve a name.
            DwPolicy.Options.Entities.Resolve);

    private static string DemoConnection { get; set; } = string.Empty;

    /// <summary>Handed the connection at startup so the store can be opened per call.</summary>
    internal static void UseConnection(string connection) => DemoConnection = connection;

    #region Health

    /// <summary>
    /// What the store snapshot is, how old it is, and whether it is degraded.
    /// </summary>
    [HttpGet("health")]
    public ActionResult<PerformanceResult> Health()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        IReadOnlyList<StorePolicyProvider> providers = DwPolicy.StoreProviders;

        metrics.TotalTimeMs = sw.Elapsed.TotalMilliseconds;
        metrics.RecordsReturned = providers.Count;

        return Ok(new PerformanceResult
        {
            TestName = "Policy store health",
            Metrics = metrics,
            Success = true,
            Output = providers.Select(p => new
            {
                p.Version,
                p.IsDegraded,
                p.LoadedAt,
                AgeSeconds = p.Age.TotalSeconds,
                LastError = p.LastError?.Message
            }),
            Message = providers.Count == 0
                ? "No store provider is registered; attributes are the whole policy."
                : "A degraded provider is still serving, from the last snapshot it loaded "
                  + "successfully, bounded by MaxSnapshotAge."
        });
    }

    #endregion

    #region Rules

    /// <summary>
    /// Every rule currently in the store.
    /// </summary>
    [HttpGet("rules")]
    public async Task<ActionResult<PerformanceResult>> Rules()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        List<DwPolicyRuleRecord> rows = await _store.PolicyRules
            .AsNoTracking()
            .OrderBy(r => r.EntityType)
            .ThenBy(r => r.FieldPath)
            .ToListAsync();

        metrics.TotalTimeMs = sw.Elapsed.TotalMilliseconds;
        metrics.RecordsReturned = rows.Count;

        return Ok(new PerformanceResult
        {
            TestName = "Policy rules in the store",
            Metrics = metrics,
            Success = true,
            Output = rows,
            Message = $"{rows.Count} rule(s). These are the dynamic half; the attributes on Employee "
                      + "are the sealed half and are not listed here."
        });
    }

    /// <summary>
    /// Writes a rule denying Position to the Support role, then reports what changed.
    /// </summary>
    /// <remarks>
    /// Position carries no attribute at all, which is the point: the sealed level says nothing about
    /// it, so a runtime rule is free to decide. A rule aimed at a field the source code seals would
    /// be refused instead — see <see cref="SealedFieldIsRefused"/>.
    /// </remarks>
    [HttpPost("rules/deny-position-for-support")]
    public async Task<ActionResult<PerformanceResult>> AddDemoRule()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var rule = new PolicyRule(
                subjectKind: DwSubjectKind.Role,
                subjectKey: "Support",
                entityType: typeof(Employee).FullName!,
                fieldPath: "Position",
                features: PolicyFeature.Select | PolicyFeature.Order,
                effect: PolicyEffect.Deny,
                priority: 10,
                purpose: null,
                createdBy: "swagger-demo",
                createdAt: DateTimeOffset.UtcNow,
                id: DemoRuleId);

            await Writable().UpsertAsync(rule, HttpContext.RequestAborted);

            // The provider polls on its own timer; refreshing here makes the demo immediate rather
            // than eventually consistent.
            foreach (StorePolicyProvider provider in DwPolicy.StoreProviders)
            {
                await provider.RefreshAsync(HttpContext.RequestAborted);
            }

            metrics.TotalTimeMs = sw.Elapsed.TotalMilliseconds;

            return Ok(new PerformanceResult
            {
                TestName = "Add rule - deny Position to Support",
                Metrics = metrics,
                Input = new { rule.SubjectKind, rule.SubjectKey, rule.FieldPath, rule.Features, rule.Effect },
                Output = await ExplainAsync("Position"),
                Success = true,
                Message = "Written and the provider refreshed. Call /api/PolicyTest with a Support "
                          + "caller and Position is now dropped from the projection."
            });
        }
        catch (Exception error)
        {
            _logger.LogError(error, "Failed to write the demo rule");

            return Ok(new PerformanceResult
            {
                TestName = "Add rule - deny Position to Support",
                Metrics = metrics,
                Success = false,
                Message = error.Message
            });
        }
    }

    /// <summary>
    /// Removes the demo rule and refreshes, so the endpoint can be run again.
    /// </summary>
    [HttpDelete("rules/deny-position-for-support")]
    public async Task<ActionResult<PerformanceResult>> RemoveDemoRule()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        await Writable().DeleteAsync(DemoRuleId, HttpContext.RequestAborted);

        foreach (StorePolicyProvider provider in DwPolicy.StoreProviders)
        {
            await provider.RefreshAsync(HttpContext.RequestAborted);
        }

        metrics.TotalTimeMs = sw.Elapsed.TotalMilliseconds;

        return Ok(new PerformanceResult
        {
            TestName = "Remove rule - deny Position to Support",
            Metrics = metrics,
            Success = true,
            Output = await ExplainAsync("Position"),
            Message = "Removed and refreshed."
        });
    }

    /// <summary>
    /// A rule aimed at a field the source code seals is refused at the store boundary.
    /// </summary>
    /// <remarks>
    /// The refusal is in <see cref="PolicyRule"/>'s own constructor, so it holds for every store and
    /// for the admin endpoints alike. An operator cannot even attempt to grant WorkSchedule: it is
    /// absent from the schema and rejected on write, at configuration time and at resolution time
    /// both.
    /// </remarks>
    [HttpPost("rules/sealed-field-is-refused")]
    public async Task<ActionResult<PerformanceResult>> SealedFieldIsRefused()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        try
        {
            var rule = new PolicyRule(
                subjectKind: DwSubjectKind.Role,
                subjectKey: "Support",
                entityType: typeof(Employee).FullName!,
                fieldPath: "WorkSchedule",
                features: PolicyFeature.Select,
                effect: PolicyEffect.Allow,
                priority: 100);

            await Writable().UpsertAsync(rule, HttpContext.RequestAborted);

            metrics.TotalTimeMs = sw.Elapsed.TotalMilliseconds;

            return Ok(new PerformanceResult
            {
                TestName = "Sealed field - grant WorkSchedule to Support",
                Metrics = metrics,
                Success = false,
                Message = "FAILED OPEN. The rule was accepted. A runtime rule just granted a field "
                          + "the source code seals."
            });
        }
        catch (Exception refusal)
        {
            metrics.TotalTimeMs = sw.Elapsed.TotalMilliseconds;

            return Ok(new PerformanceResult
            {
                TestName = "Sealed field - grant WorkSchedule to Support",
                Metrics = metrics,
                Output = new { Refusal = refusal.GetType().Name, refusal.Message },
                Success = true,
                Message = "Refused, as it should be. WorkSchedule is [DwDenied] and sealed by "
                          + "default, so no rule at any level can lift it."
            });
        }
    }

    #endregion

    #region Explain, schema and simulate

    /// <summary>
    /// The whole decision chain for one field: what won, what it overrode, what was ignored.
    /// </summary>
    [HttpGet("explain/{field}")]
    public async Task<ActionResult<PerformanceResult>> Explain(string field)
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        object explanation = await ExplainAsync(field);

        metrics.TotalTimeMs = sw.Elapsed.TotalMilliseconds;

        return Ok(new PerformanceResult
        {
            TestName = $"Explain - Employee.{field}",
            Metrics = metrics,
            Success = true,
            Output = explanation,
            Message = "When two sources tie on level, specificity, priority and effect alike, the "
                      + "decided effect is still deterministic; only which of the equal sources is "
                      + "named here is arbitrary."
        });
    }

    /// <summary>
    /// The fields a caller may build a filter UI from. Sealed fields are absent.
    /// </summary>
    [HttpGet("schema")]
    public async Task<ActionResult<PerformanceResult>> Schema()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        DwPolicyContext caller = await CallerAsync();

        PolicySchema schema = PolicySchemaBuilder.Describe(
            typeof(Employee),
            DwPolicy.Options.Entities,
            caller,
            DwPolicy.Options,
            DwPolicy.Resolver);

        metrics.TotalTimeMs = sw.Elapsed.TotalMilliseconds;
        metrics.RecordsReturned = schema.Fields.Count;

        return Ok(new PerformanceResult
        {
            TestName = "Schema - Employee",
            Metrics = metrics,
            Success = true,
            Output = schema,
            Message = "Built from the entity rather than from a hand-maintained copy of it, so a "
                      + "field that becomes denied disappears from the UI without a front-end change. "
                      + "WorkSchedule is absent because it is denied outright."
        });
    }

    /// <summary>
    /// What would happen to a filter, without executing it.
    /// </summary>
    [HttpPost("simulate")]
    public async Task<ActionResult<PerformanceResult>> Simulate()
    {
        var metrics = new PerformanceMetrics();
        var sw = Stopwatch.StartNew();

        DwPolicyContext caller = await CallerAsync();

        var filter = new Filter
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
                    }
                ]
            },
            Selects = ["Id", "FirstName", "Email", "Salary", "WorkSchedule"]
        };

        PolicySimulation<Filter> simulation = PolicySimulator.Simulate<Employee>(
            filter, caller, DwPolicy.Options, DwPolicy.Resolver);

        metrics.TotalTimeMs = sw.Elapsed.TotalMilliseconds;

        return Ok(new PerformanceResult
        {
            TestName = "Simulate - a filter naming a denied field",
            Metrics = metrics,
            Input = filter,
            Success = true,
            Output = simulation,
            Message = "Nothing was executed and nothing was audited. The sanitized clause shows "
                      + "WorkSchedule gone; the trace says why."
        });
    }

    private async Task<object> ExplainAsync(string field)
    {
        DwPolicyContext caller = await CallerAsync();

        PolicyExplanation explanation =
            DwPolicy.Resolver.Explain(typeof(Employee), field, caller);

        return explanation;
    }

    #endregion
}
