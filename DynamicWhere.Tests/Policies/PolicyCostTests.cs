using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The query budget: what <c>[DwCost]</c> charges and what <c>MaxQueryCost</c> refuses.
/// </summary>
/// <remarks>
/// A denial-of-service control rather than an access control. Before this release a filter could
/// name a field any number of times, so one request could generate work bounded by nothing but the
/// caller's patience.
/// </remarks>
public class PolicyCostTests
{
    private static DwPolicyOptions Options(int? maxCost = null, int? fieldCost = null)
    {
        DwPolicyOptions options = new();

        if (maxCost is int max)
        {
            options.Caps.MaxQueryCost = max;
        }

        if (fieldCost is int each)
        {
            options.Caps.DefaultFieldCost = each;
        }

        options.Freeze();

        return options;
    }

    private static PolicyQueryable<DescribedEmployee> Query(DwPolicyOptions options) =>
        Array.Empty<DescribedEmployee>()
            .AsQueryable()
            .ApplyPolicy(
                new DwPolicyContext(),
                options,
                new PolicyResolver(new[] { new AttributePolicyProvider() }));

    /// <summary>
    /// A filter naming each field once, with a value the field's own type accepts — the pipeline
    /// downstream of the policy still validates, and a cost test that trips on a format error is
    /// testing the wrong thing.
    /// </summary>
    private static Filter On(params string[] fields) =>
        new()
        {
            ConditionGroup = new ConditionGroup
            {
                Sort = 1,
                Conditions = fields
                    .Select((field, i) => Numeric(field)
                        ? new Condition
                        {
                            Sort = i + 1,
                            Field = field,
                            DataType = DataType.Number,
                            Operator = Operator.Equal,
                            Values = { 1 }
                        }
                        : new Condition
                        {
                            Sort = i + 1,
                            Field = field,
                            DataType = DataType.Text,
                            Operator = Operator.Equal,
                            Values = { "x" }
                        })
                    .ToList()
            }
        };

    private static bool Numeric(string field) =>
        field is "Id" or "Salary" or "Amount" or "TenantId";

    // ---- the cap itself ------------------------------------------------------------------------

    [Fact]
    public void A_query_within_budget_runs()
    {
        FilterResult<DescribedEmployee> result = Query(Options(maxCost: 100)).ToList(On("Name", "Status"));

        Assert.Empty(result.Data!);
    }

    /// <summary>
    /// Salary is weighed at ten, so eleven references cost 110 against a budget of 100.
    /// </summary>
    [Fact]
    public void A_query_over_budget_is_refused()
    {
        PolicyException error = Assert.Throws<PolicyException>(
            () => Query(Options(maxCost: 100)).ToList(On(Enumerable.Repeat("Salary", 11).ToArray())));

        Assert.Equal(PolicyErrorCode.QueryCostExceeded, error.ErrorCode);
    }

    /// <summary>
    /// The refusal names the budget and what the query actually cost, because an operator raising
    /// the cap needs to know by how much.
    /// </summary>
    [Fact]
    public void The_refusal_names_the_budget_and_the_total()
    {
        PolicyException error = Assert.Throws<PolicyException>(
            () => Query(Options(maxCost: 20)).ToList(On("Salary", "Salary", "Salary")));

        Assert.Contains("20", error.SourceOrigin);
        Assert.Contains("30", error.SourceOrigin);
    }

    /// <summary>
    /// Every reference is charged, not every distinct field. Charging per field would let a caller
    /// generate the same work by naming one field a thousand times.
    /// </summary>
    [Fact]
    public void One_field_named_twice_is_charged_twice()
    {
        Assert.Throws<PolicyException>(
            () => Query(Options(maxCost: 15)).ToList(On("Salary", "Salary")));

        FilterResult<DescribedEmployee> ok = Query(Options(maxCost: 15)).ToList(On("Salary"));

        Assert.Empty(ok.Data!);
    }

    [Fact]
    public void An_unweighed_field_costs_the_standing_default()
    {
        Assert.Throws<PolicyException>(
            () => Query(Options(maxCost: 2, fieldCost: 1)).ToList(On("Name", "Status", "Email")));
    }

    /// <summary>
    /// A weight of zero is a field the budget does not charge for, so any number of references to
    /// it stays free.
    /// </summary>
    [Fact]
    public void A_field_weighed_at_zero_is_free()
    {
        FilterResult<DescribedEmployee> result = Query(Options(maxCost: 5, fieldCost: 0))
            .ToList(On(Enumerable.Repeat("Id", 40).ToArray()));

        Assert.Empty(result.Data!);
    }

    // ---- what is charged -----------------------------------------------------------------------

    [Fact]
    public void An_order_is_charged()
    {
        Filter filter = new()
        {
            Orders = new List<OrderBy>
            {
                new() { Sort = 1, Field = "Salary", Direction = Direction.Ascending },
                new() { Sort = 2, Field = "Notes", Direction = Direction.Ascending }
            }
        };

        Assert.Throws<PolicyException>(() => Query(Options(maxCost: 10)).ToList(filter));
    }

    [Fact]
    public void A_projection_is_charged()
    {
        Filter filter = new() { Selects = new List<string> { "Salary", "Notes" } };

        Assert.Throws<PolicyException>(() => Query(Options(maxCost: 10)).ToList(filter));
    }

    /// <summary>
    /// A projection the library synthesized is not charged. The caller named no field, so charging
    /// them for every field of the type would refuse a query nobody made expensive.
    /// </summary>
    [Fact]
    public void A_synthesized_projection_is_not_charged()
    {
        FilterResult<DescribedEmployee> result =
            Query(Options(maxCost: 12, fieldCost: 5)).ToList(new Filter());

        Assert.Empty(result.Data!);
    }

    /// <summary>
    /// A forced predicate is the library filtering on the caller's behalf. Charging it could refuse
    /// a query purely because of the scope confining it, which is the library billing a caller for
    /// its own security control.
    /// </summary>
    [Fact]
    public void An_injected_predicate_is_not_charged()
    {
        DwPolicyOptions options = Options(maxCost: 2, fieldCost: 1);

        // Two predicates are injected here — a tenant scope and a soft-delete check — against a
        // budget of two, so a third charge would refuse the query.
        PolicyQueryable<ScopedInvoice> query = Array.Empty<ScopedInvoice>()
            .AsQueryable()
            .ApplyPolicy(
                new DwPolicyContext().WithValue("TenantId", 5),
                options,
                new PolicyResolver(new[] { new AttributePolicyProvider() }));

        FilterResult<ScopedInvoice> result = query.ToList(On("Amount", "Number"));

        Assert.Empty(result.Data!);
    }

    // ---- the cap's own guards ------------------------------------------------------------------

    [Fact]
    public void The_budget_cannot_be_changed_after_startup()
    {
        DwPolicyOptions options = Options(maxCost: 100);

        Assert.Throws<InvalidOperationException>(() => options.Caps.MaxQueryCost = 5);
    }

    [Fact]
    public void A_budget_below_one_is_refused()
    {
        DwPolicyOptions options = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Caps.MaxQueryCost = 0);
    }

    /// <summary>
    /// Zero is a real default field cost — a host that wants only annotated fields to count — so it
    /// is accepted where a cap of zero is not.
    /// </summary>
    [Fact]
    public void A_default_field_cost_of_zero_is_accepted()
    {
        DwPolicyOptions options = new();

        options.Caps.DefaultFieldCost = 0;

        Assert.Equal(0, options.Caps.DefaultFieldCost);
    }

    [Fact]
    public void A_negative_default_field_cost_is_refused()
    {
        DwPolicyOptions options = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Caps.DefaultFieldCost = -1);
    }

    /// <summary>
    /// A dry run records the refusal and lets the query through, exactly as every other cap does.
    /// </summary>
    [Fact]
    public void A_dry_run_records_the_overspend_without_refusing()
    {
        DwPolicyOptions options = new() { DryRun = true };

        options.Caps.MaxQueryCost = 5;
        options.Freeze();

        PolicyQueryable<DescribedEmployee> query = Query(options);
        FilterResult<DescribedEmployee> result = query.ToList(On("Salary", "Salary"));

        Assert.Empty(result.Data!);
        Assert.Contains(
            query.LastTrace!.Decisions,
            d => d.Action == PolicyAction.Denied && (d.Reason?.Contains("MaxQueryCost") ?? false));
    }

    /// <summary>
    /// The default budget is generous enough that an ordinary query never meets it, in keeping with
    /// every other cap here — a control that refuses working queries on upgrade is a control that
    /// gets switched off.
    /// </summary>
    [Fact]
    public void The_default_budget_leaves_an_ordinary_query_alone()
    {
        FilterResult<DescribedEmployee> result =
            Query(Options()).ToList(On("Name", "Status", "Email", "Notes"));

        Assert.Empty(result.Data!);
    }
}
