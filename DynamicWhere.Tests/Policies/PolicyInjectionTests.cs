using DynamicWhere.ex.Classes.Result;
using System.Linq.Dynamic.Core;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
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
/// Covers predicate injection: the predicates the library adds to a query the caller never asked
/// for, which is what makes a tenant boundary a boundary rather than a convention.
/// </summary>
public class PolicyInjectionTests
{
    private static DwPolicyContext Tenant(int id = 5) =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1").WithValue("TenantId", id);

    private static DwPolicyOptions Options(DwTier tier = DwTier.Convenience, bool dryRun = false) =>
        new() { Tier = tier, DryRun = dryRun };

    private static PolicyResolver Attributes() =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    private static (Filter Result, PolicyTrace Trace) Sanitize<T>(
        Filter filter, DwPolicyContext? context = null, DwTier tier = DwTier.Convenience,
        bool dryRun = false)
        where T : class
    {
        PolicyTrace trace = new(tier, dryRun);

        Filter result = FilterSanitizer.Sanitize<T>(
            filter, Attributes(), context ?? Tenant(), Options(tier, dryRun), trace);

        return (result, trace);
    }

    private static Condition On(string field, Operator op = Operator.Equal, string value = "A") =>
        new() { Field = field, DataType = DataType.Text, Operator = op, Values = { value } };

    // -------------------------------------------------------------------------- the pinned shape

    [Fact]
    public void The_forced_predicate_wraps_the_callers_group_and_never_joins_it()
    {
        // This is a correctness requirement, not a detail. Appending the tenant term inside a
        // caller group of (Status = A OR Status = B) yields
        // (Status = A OR Status = B OR TenantId = 5), which returns every tenant's rows matching A
        // or B. Written so that "simplifying" the injection into a merge fails loudly here.
        ConditionGroup callers = new() { Connector = Connector.Or };

        callers.Conditions.Add(On("Number", value: "A"));
        callers.Conditions.Add(new Condition
        {
            Sort = 1, Field = "Number", DataType = DataType.Text, Operator = Operator.Equal,
            Values = { "B" }
        });

        Filter result = Sanitize<ScopedInvoice>(new Filter { ConditionGroup = callers }).Result;

        ConditionGroup root = result.ConditionGroup!;

        Assert.Equal(Connector.And, root.Connector);

        ConditionGroup preserved = Assert.Single(root.SubConditionGroups);

        Assert.Equal(Connector.Or, preserved.Connector);
        Assert.Equal(2, preserved.Conditions.Count);
        Assert.All(preserved.Conditions, c => Assert.Equal("Number", c.Field));

        Assert.Contains(root.Conditions, c => c.Field == "TenantId");
        Assert.Contains(root.Conditions, c => c.Field == "IsDeleted");
    }

    [Fact]
    public void The_injected_condition_carries_the_value_from_the_callers_context()
    {
        Filter result = Sanitize<ScopedInvoice>(new Filter(), Tenant(42)).Result;

        Condition injected = result.ConditionGroup!.Conditions.Single(c => c.Field == "TenantId");

        Assert.Equal(Operator.Equal, injected.Operator);
        Assert.Equal(DataType.Number, injected.DataType);
        Assert.Equal(42, Assert.Single(injected.Values));
    }

    [Fact]
    public void A_caller_who_sent_no_filter_at_all_still_gets_the_scope()
    {
        // Precisely the caller a forced scope exists for. A null condition group reaches the
        // pipeline as "no where clause", so leaving it null would hand back every tenant's rows.
        Filter result = Sanitize<ScopedInvoice>(new Filter()).Result;

        Assert.NotNull(result.ConditionGroup);
        Assert.Empty(result.ConditionGroup!.SubConditionGroups);
        Assert.Equal(2, result.ConditionGroup.Conditions.Count);
    }

    [Fact]
    public void A_type_forcing_nothing_keeps_the_filter_it_was_given()
    {
        // The policy layer stays invisible until it has something to say, so a type nobody scopes
        // generates the same SQL as the unguarded path.
        Filter result = Sanitize<PlainProduct>(new Filter()).Result;

        Assert.Null(result.ConditionGroup);
    }

    [Fact]
    public void The_injected_conditions_carry_distinct_sort_values()
    {
        // ConditionGroup.Validate refuses duplicates, so a second forced predicate would otherwise
        // turn every query on the type into a validation failure.
        Filter result = Sanitize<ScopedInvoice>(new Filter()).Result;

        List<int> sorts = result.ConditionGroup!.Conditions.ConvertAll(c => c.Sort);

        Assert.Equal(sorts.Count, sorts.Distinct().Count());
    }

    // ------------------------------------------------------------------------- context values

    [Theory]
    [InlineData(DwTier.Convenience)]
    [InlineData(DwTier.Strict)]
    public void A_missing_context_value_throws_in_both_tiers(DwTier tier)
    {
        // A tenant scope that silently fails to apply is worse than a failed request.
        DwPolicyContext blank = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        PolicyException error = Assert.Throws<PolicyException>(
            () => Sanitize<ScopedInvoice>(new Filter(), blank, tier));

        Assert.Equal(PolicyErrorCode.MissingContextValue, error.ErrorCode);
        Assert.Equal("TenantId", error.FieldPath);
    }

    [Fact]
    public void A_context_value_supplied_as_null_counts_as_missing()
    {
        // Present-but-null is the shape a forgotten claim actually takes, and filtering by null is
        // not the scope anybody meant.
        DwPolicyContext blank = new DwPolicyContext()
            .WithSubject(DwSubjectKind.User, "u1")
            .WithValue("TenantId", null);

        PolicyException error = Assert.Throws<PolicyException>(
            () => Sanitize<ScopedInvoice>(new Filter(), blank));

        Assert.Equal(PolicyErrorCode.MissingContextValue, error.ErrorCode);
    }

    [Fact]
    public void A_constant_predicate_needs_no_context_at_all()
    {
        DwPolicyContext blank = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        Filter result = Sanitize<SoftDeletedLedger>(new Filter(), blank).Result;

        Assert.Single(result.ConditionGroup!.Conditions);
    }

    [Fact]
    public void A_null_check_predicate_carries_no_values()
    {
        Filter result = Sanitize<SoftDeletedLedger>(new Filter()).Result;

        Condition injected = Assert.Single(result.ConditionGroup!.Conditions);

        Assert.Equal(Operator.IsNull, injected.Operator);
        Assert.Empty(injected.Values);
    }

    // ------------------------------------------------------------------- never gated, never billed

    [Fact]
    public void A_forced_predicate_is_not_gated_against_the_callers_own_policy()
    {
        // The library filtering on the caller's behalf, not the caller filtering. Gating it would
        // make the scope unusable on exactly the field it is most needed for.
        Filter result = Sanitize<HiddenTenantLedger>(new Filter(), Tenant(), DwTier.Strict).Result;

        Assert.Contains(result.ConditionGroup!.Conditions, c => c.Field == "TenantId");
    }

    [Fact]
    public void A_caller_naming_the_forced_field_themselves_is_still_refused()
    {
        // The exemption is for the predicate the library adds, not for the field it names.
        PolicyException error = Assert.Throws<PolicyException>(() => Sanitize<HiddenTenantLedger>(
            new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions = { new Condition { Field = "TenantId", DataType = DataType.Number, Operator = Operator.Equal, Values = { 9 } } }
                }
            }));

        Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, error.ErrorCode);
    }

    [Fact]
    public void An_injected_predicate_does_not_spend_the_callers_condition_budget()
    {
        // Caps count what the caller sent. A filter sitting exactly on the limit must not be
        // refused because the library added a term of its own.
        DwPolicyOptions options = new() { Tier = DwTier.Convenience };

        options.Caps.MaxConditions = 1;

        ConditionGroup callers = new() { Connector = Connector.And };

        callers.Conditions.Add(On("Number"));

        Filter result = FilterSanitizer.Sanitize<ScopedInvoice>(
            new Filter { ConditionGroup = callers }, Attributes(), Tenant(), options,
            new PolicyTrace(DwTier.Convenience, false));

        Assert.Equal(2, result.ConditionGroup!.Conditions.Count);
        Assert.Single(result.ConditionGroup.SubConditionGroups);
    }

    // --------------------------------------------------------------------------- trace and dry run

    [Fact]
    public void Every_injected_predicate_is_recorded()
    {
        PolicyTrace trace = Sanitize<ScopedInvoice>(new Filter()).Trace;

        PolicyDecision[] injected = trace.Decisions
            .Where(d => d.Action == PolicyAction.Injected)
            .ToArray();

        Assert.Equal(2, injected.Length);
        Assert.Contains(injected, d => d.FieldPath == "TenantId");
    }

    [Fact]
    public void A_dry_run_records_the_injection_without_applying_it()
    {
        // Dry run's promise is that the data comes back exactly as the unguarded path would return
        // it. Injecting anyway would narrow the result and make the canary lie about its own
        // blast radius.
        (Filter result, PolicyTrace trace) = Sanitize<ScopedInvoice>(
            new Filter(), Tenant(), DwTier.Convenience, dryRun: true);

        Assert.Null(result.ConditionGroup);
        Assert.Contains(trace.Decisions, d => d.Action == PolicyAction.Injected);
    }

    [Fact]
    public void A_dry_run_records_a_missing_context_value_rather_than_throwing()
    {
        // Dry run overrides the whole table. An operator running a canary needs to be told the
        // scope would not have resolved, and telling them is what the trace is for.
        DwPolicyContext blank = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        (Filter result, PolicyTrace trace) = Sanitize<ScopedInvoice>(
            new Filter(), blank, DwTier.Convenience, dryRun: true);

        Assert.Null(result.ConditionGroup);
        Assert.Contains(
            trace.Decisions,
            d => d.Action == PolicyAction.Denied && d.Reason!.Contains("TenantId", StringComparison.Ordinal));
    }

    // ----------------------------------------------------------- the other queryable surfaces

    private static List<ScopedInvoice> Books() => new()
    {
        new() { Id = 1, TenantId = 5, IsDeleted = false, Number = "A", Amount = 10m },
        new() { Id = 2, TenantId = 9, IsDeleted = false, Number = "B", Amount = 20m },
        new() { Id = 3, TenantId = 5, IsDeleted = true, Number = "C", Amount = 30m }
    };

    private static PolicyQueryable<ScopedInvoice> Handle(int tenant = 5) =>
        Books().AsQueryable().ApplyPolicy(Tenant(tenant), Options(), Attributes());

    [Fact]
    public void A_summary_carries_the_scope_too()
    {
        // Injecting on the filter path alone would let a caller read SUM over every tenant by
        // asking for a grouped result instead of a list.
        Summary summary = new()
        {
            GroupBy = new GroupBy { Fields = new List<string> { "Number" } }
        };

        Summary result = FilterSanitizer.Sanitize<ScopedInvoice>(
            summary, Attributes(), Tenant(), Options(), new PolicyTrace(DwTier.Convenience, false));

        Assert.NotNull(result.ConditionGroup);
        Assert.Contains(result.ConditionGroup!.Conditions, c => c.Field == "TenantId");
    }

    [Fact]
    public void Every_condition_set_in_a_segment_is_scoped_independently()
    {
        // Each set becomes its own subquery. Scoping one arm of an Except and not the other returns
        // precisely the rows the scope was meant to hide.
        Segment segment = new()
        {
            ConditionSets = new List<ConditionSet>
            {
                new() { Sort = 0, ConditionGroup = new ConditionGroup { Conditions = { On("Number", value: "A") } } },
                new()
                {
                    Sort = 1,
                    Intersection = Intersection.Except,
                    ConditionGroup = new ConditionGroup { Conditions = { On("Number", value: "B") } }
                }
            }
        };

        Segment result = FilterSanitizer.Sanitize<ScopedInvoice>(
            segment, Attributes(), Tenant(), Options(), new PolicyTrace(DwTier.Convenience, false));

        Assert.All(
            result.ConditionSets!,
            set => Assert.Contains(set.ConditionGroup.Conditions, c => c.Field == "TenantId"));
    }

    [Fact]
    public void A_segment_with_no_condition_sets_is_given_one()
    {
        // The pipeline treats an empty set list as "return everything", so there would otherwise be
        // no group for the scope to wrap.
        Segment result = FilterSanitizer.Sanitize<ScopedInvoice>(
            new Segment { ConditionSets = new List<ConditionSet>() },
            Attributes(), Tenant(), Options(), new PolicyTrace(DwTier.Convenience, false));

        ConditionSet only = Assert.Single(result.ConditionSets!);

        Assert.Contains(only.ConditionGroup.Conditions, c => c.Field == "TenantId");
    }

    [Fact]
    public void The_composable_where_applies_the_scope()
    {
        List<ScopedInvoice> rows = Handle()
            .Where(new ConditionGroup { Conditions = { On("Number", Operator.NotEqual, "Z") } })
            .AsUnguardedQueryable()
            .ToList();

        Assert.All(rows, r => Assert.Equal(5, r.TenantId));
        Assert.All(rows, r => Assert.False(r.IsDeleted));
    }

    [Fact]
    public void The_composable_where_on_one_condition_applies_the_scope()
    {
        // The single-condition overload used to hand the pipeline Conditions[0]. After injection
        // that index is a forced predicate and the caller's own condition sits in a subgroup, so
        // taking the first condition would have silently dropped what the caller asked for.
        List<ScopedInvoice> rows =
            Handle().Where(On("Number", Operator.NotEqual, "Z")).AsUnguardedQueryable().ToList();

        Assert.All(rows, r => Assert.Equal(5, r.TenantId));
        Assert.Single(rows);
    }

    [Fact]
    public void The_composable_select_applies_the_scope()
    {
        // A scope applied to ToList and not to Select is bypassed by composing instead of
        // terminating.
        List<ScopedInvoice> rows =
            Handle().Select(new List<string> { "Id", "TenantId" }).AsUnguardedQueryable().ToList();

        Assert.All(rows, r => Assert.Equal(5, r.TenantId));
        Assert.Single(rows);
    }

    [Fact]
    public void The_composable_order_applies_the_scope()
    {
        List<ScopedInvoice> rows = Handle()
            .Order(new List<OrderBy> { new() { Field = "Amount", Direction = Direction.Ascending } })
            .AsUnguardedQueryable()
            .ToList();

        Assert.All(rows, r => Assert.Equal(5, r.TenantId));
        Assert.Single(rows);
    }

    [Fact]
    public void The_composable_page_applies_the_scope()
    {
        List<ScopedInvoice> rows = Handle()
            .Page(new PageBy { PageNumber = 1, PageSize = 10 })
            .AsUnguardedQueryable()
            .ToList();

        Assert.All(rows, r => Assert.Equal(5, r.TenantId));
        Assert.Single(rows);
    }

    [Fact]
    public void The_composable_group_applies_the_scope()
    {
        int groups = Handle()
            .Group(new GroupBy { Fields = new List<string> { "Number" } })
            .ToDynamicList()
            .Count;

        // Tenant 5 has two rows, one of them soft deleted, so exactly one group survives.
        Assert.Equal(1, groups);
    }

    [Fact]
    public void A_terminal_list_call_applies_the_scope()
    {
        FilterResult<ScopedInvoice> result = Handle().ToList(new Filter());

        Assert.Single(result.Data!);
        Assert.All(result.Data!, r => Assert.Equal(5, r.TenantId));
    }
}
