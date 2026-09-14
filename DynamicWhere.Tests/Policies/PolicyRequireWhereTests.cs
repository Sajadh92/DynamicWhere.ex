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
/// Covers <c>[DwRequireWhere]</c>: the demand that the caller supply a scope of their own, and the
/// question of what actually counts as supplying one.
/// </summary>
/// <remarks>
/// The distinction this file exists for is that naming the required field is not the test. A caller
/// sending <c>Status = A OR TenantId = 5</c> has named it and still receives every other tenant's
/// rows matching <c>Status = A</c>. A requirement met on paper and defeated in fact is worse than
/// no requirement, because the attribute in the source reads as though it is in force.
/// </remarks>
public class PolicyRequireWhereTests
{
    private static DwPolicyContext Caller() =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1").WithValue("TenantId", 5);

    private static DwPolicyOptions Options(DwTier tier = DwTier.Convenience, bool dryRun = false) =>
        new() { Tier = tier, DryRun = dryRun };

    private static PolicyResolver Attributes() =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    private static (Filter Result, PolicyTrace Trace) Sanitize<T>(
        Filter filter, DwTier tier = DwTier.Convenience, bool dryRun = false)
        where T : class
    {
        PolicyTrace trace = new(tier, dryRun);

        Filter result = FilterSanitizer.Sanitize<T>(
            filter, Attributes(), Caller(), Options(tier, dryRun), trace);

        return (result, trace);
    }

    private static Condition Tenant(Operator op = Operator.Equal, int sort = 0) =>
        new() { Sort = sort, Field = "TenantId", DataType = DataType.Number, Operator = op, Values = { 5 } };

    private static Condition Other(int sort = 1) =>
        new() { Sort = sort, Field = "Amount", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = { 0 } };

    private static ConditionGroup Group(Connector connector, params Condition[] conditions)
    {
        ConditionGroup group = new() { Connector = connector };

        foreach (Condition condition in conditions)
        {
            group.Conditions.Add(condition);
        }

        return group;
    }

    // ------------------------------------------------------------------------ what binds

    [Fact]
    public void A_condition_in_an_and_group_satisfies_the_requirement()
    {
        Filter result = Sanitize<TenantScopedLedger>(
            new Filter { ConditionGroup = Group(Connector.And, Tenant(), Other()) }).Result;

        Assert.NotNull(result.ConditionGroup);
    }

    [Fact]
    public void A_condition_in_an_or_group_does_not_satisfy_the_requirement()
    {
        // The headline case. The caller has named the field and the query still returns every other
        // tenant's rows matching the other arm of the OR.
        PolicyException error = Assert.Throws<PolicyException>(() => Sanitize<TenantScopedLedger>(
            new Filter { ConditionGroup = Group(Connector.Or, Tenant(), Other()) }));

        Assert.Equal(PolicyErrorCode.RequiredFilterMissing, error.ErrorCode);
        Assert.Equal("TenantId", error.FieldPath);
    }

    [Fact]
    public void A_condition_nested_under_an_and_ancestor_still_binds()
    {
        // Refusing this would push callers into flattening perfectly sound filters to please a
        // checker, so the walk tracks the whole path to the root rather than looking at one group.
        ConditionGroup root = Group(Connector.And, Other());

        root.SubConditionGroups.Add(Group(Connector.And, Tenant()));

        Assert.NotNull(Sanitize<TenantScopedLedger>(new Filter { ConditionGroup = root }).Result.ConditionGroup);
    }

    [Fact]
    public void A_condition_nested_under_an_or_ancestor_does_not_bind()
    {
        // The inner group is an AND, so a checker looking only at the condition's own group would
        // accept this. The disjunction one level up is what defeats the scope.
        ConditionGroup root = Group(Connector.Or, Other());

        root.SubConditionGroups.Add(Group(Connector.And, Tenant()));

        Assert.Throws<PolicyException>(
            () => Sanitize<TenantScopedLedger>(new Filter { ConditionGroup = root }));
    }

    [Fact]
    public void A_lone_condition_in_an_or_group_binds_because_there_is_nothing_to_disjoin()
    {
        // Precision rather than leniency: a group with one child emits that child, whatever its
        // connector says. Refusing it would reject a filter that does narrow the result.
        Filter result = Sanitize<TenantScopedLedger>(
            new Filter { ConditionGroup = Group(Connector.Or, Tenant()) }).Result;

        Assert.NotNull(result.ConditionGroup);
    }

    // ----------------------------------------------------------------------- which operators

    [Theory]
    [InlineData(Operator.Equal)]
    [InlineData(Operator.IEqual)]
    [InlineData(Operator.In)]
    [InlineData(Operator.IIn)]
    public void A_positive_membership_operator_satisfies_the_default_set(Operator op)
    {
        Filter result = Sanitize<TenantScopedLedger>(
            new Filter { ConditionGroup = Group(Connector.And, Tenant(op)) }).Result;

        Assert.NotNull(result.ConditionGroup);
    }

    [Theory]
    [InlineData(Operator.NotEqual)]
    [InlineData(Operator.NotIn)]
    [InlineData(Operator.IsNotNull)]
    [InlineData(Operator.GreaterThan)]
    public void An_inverting_or_widening_operator_does_not(Operator op)
    {
        // Each of these names the field while inverting the scope or opening it out. TenantId != 5
        // is every tenant but one.
        Assert.Throws<PolicyException>(() => Sanitize<TenantScopedLedger>(
            new Filter { ConditionGroup = Group(Connector.And, Tenant(op)) }));
    }

    [Fact]
    public void An_explicit_operator_set_replaces_the_default()
    {
        // A field that genuinely wants a range says so, and then the range operators are the ones
        // that satisfy it while Equal does not.
        ConditionGroup group = Group(
            Connector.And,
            new Condition { Sort = 0, Field = "TenantId", DataType = DataType.Number, Operator = Operator.Equal, Values = { 5 } },
            new Condition
            {
                Sort = 1, Field = "OccurredAt", DataType = DataType.DateTime,
                Operator = Operator.GreaterThanOrEqual, Values = { "2026-01-01" }
            });

        Assert.NotNull(Sanitize<RequiredScopeLedger>(new Filter { ConditionGroup = group }).Result.ConditionGroup);
    }

    [Fact]
    public void An_operator_outside_an_explicit_set_does_not_satisfy_it()
    {
        ConditionGroup group = Group(
            Connector.And,
            new Condition { Sort = 0, Field = "TenantId", DataType = DataType.Number, Operator = Operator.Equal, Values = { 5 } },
            new Condition
            {
                Sort = 1, Field = "OccurredAt", DataType = DataType.DateTime,
                Operator = Operator.Equal, Values = { "2026-01-01" }
            });

        PolicyException error = Assert.Throws<PolicyException>(
            () => Sanitize<RequiredScopeLedger>(new Filter { ConditionGroup = group }));

        Assert.Equal("OccurredAt", error.FieldPath);
    }

    // ------------------------------------------------------------------------ missing entirely

    [Theory]
    [InlineData(DwTier.Convenience)]
    [InlineData(DwTier.Strict)]
    public void A_missing_required_filter_throws_in_both_tiers(DwTier tier)
    {
        // Spec section 2.5 puts this in the throw-in-both-tiers row: a missing tenant scope is never
        // acceptable, and dropping the request is the only refusal that does not widen it.
        Assert.Throws<PolicyException>(() => Sanitize<TenantScopedLedger>(new Filter(), tier));
    }

    [Fact]
    public void A_refusal_names_the_alias_so_the_caller_knows_what_to_send()
    {
        // Unlike a denial, the caller has to act on this one. Naming the field in the vocabulary
        // they are allowed to use is the difference between a fixable error and a riddle.
        PolicyException error = Assert.Throws<PolicyException>(
            () => Sanitize<AliasRequiredLedger>(new Filter()));

        Assert.Equal("tenant_id", error.FieldPath);
    }

    [Fact]
    public void An_injected_predicate_satisfies_the_requirement_it_supplies()
    {
        // Verification runs after injection precisely so this holds. Forcing the scope and
        // requiring it is a sound pairing: the library supplies it, and a caller who somehow
        // reaches the query without it is still refused.
        Filter result = Sanitize<ForcedAndRequiredLedger>(new Filter()).Result;

        Assert.Contains(result.ConditionGroup!.Conditions, c => c.Field == "TenantId");
    }

    [Fact]
    public void A_dry_run_records_the_missing_filter_rather_than_throwing()
    {
        (Filter result, PolicyTrace trace) = Sanitize<TenantScopedLedger>(
            new Filter(), DwTier.Convenience, dryRun: true);

        Assert.Null(result.ConditionGroup);
        Assert.Contains(
            trace.Decisions,
            d => d.Action == PolicyAction.Denied && d.FieldPath == "TenantId");
    }

    [Fact]
    public void A_type_requiring_nothing_is_unaffected()
    {
        Assert.Null(Sanitize<PlainProduct>(new Filter()).Result.ConditionGroup);
    }

    // ------------------------------------------------------------------------- other surfaces

    [Fact]
    public void A_summary_must_supply_the_scope_too()
    {
        Summary summary = new()
        {
            GroupBy = new GroupBy { Fields = new List<string> { "Amount" } }
        };

        Assert.Throws<PolicyException>(() => FilterSanitizer.Sanitize<TenantScopedLedger>(
            summary, Attributes(), Caller(), Options(), new PolicyTrace(DwTier.Convenience, false)));
    }

    [Fact]
    public void Every_condition_set_in_a_segment_must_supply_the_scope()
    {
        // One unscoped arm of a set operation is enough. Union hands back the unscoped rows
        // directly; Except hands back their complement.
        Segment segment = new()
        {
            ConditionSets = new List<ConditionSet>
            {
                new() { Sort = 0, ConditionGroup = Group(Connector.And, Tenant()) },
                new() { Sort = 1, Intersection = Intersection.Union, ConditionGroup = Group(Connector.And, Other(0)) }
            }
        };

        Assert.Throws<PolicyException>(() => FilterSanitizer.Sanitize<TenantScopedLedger>(
            segment, Attributes(), Caller(), Options(), new PolicyTrace(DwTier.Convenience, false)));
    }

    [Fact]
    public void A_composable_clause_on_a_required_type_is_refused()
    {
        // Deliberately not the projection-synthesis reasoning from Phase 2. There, a caller who
        // named no projection had asked for nothing to refuse; here, a caller who named no scope
        // has asked for everything.
        PolicyQueryable<TenantScopedLedger> handle = new List<TenantScopedLedger>()
            .AsQueryable()
            .ApplyPolicy(Caller(), Options(), Attributes());

        Assert.Throws<PolicyException>(() => handle.Select(new List<string> { "Id" }));
    }
}
