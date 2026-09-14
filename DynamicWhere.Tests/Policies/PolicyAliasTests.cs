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
/// Covers alias resolution: the rewrite that turns a name the caller wrote into the field path the
/// pipeline understands.
/// </summary>
/// <remarks>
/// This is the fourth spelling for a field in a system where three already existed and one of them
/// was missed, so the coverage here is by clause rather than by example — every place a caller can
/// name a field is a place an unresolved name would slip past the gate, and a clause the gate skips
/// is a field left allowed.
/// </remarks>
public class PolicyAliasTests
{
    private static DwPolicyContext Caller(string identity = "u1") =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, identity);

    private static DwPolicyOptions Options(DwTier tier = DwTier.Convenience) => new() { Tier = tier };

    private static PolicyResolver Attributes() =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    private static Filter Sanitize<T>(
        Filter filter, DwTier tier = DwTier.Convenience, IDwPolicyProvider? provider = null)
        where T : class =>
        FilterSanitizer.Sanitize<T>(
            filter,
            new PolicyResolver(new[] { provider ?? new AttributePolicyProvider() }),
            Caller(),
            Options(tier),
            new PolicyTrace(tier, dryRun: false));

    private static Condition On(string field, Operator op = Operator.Equal, string value = "x") =>
        new() { Field = field, DataType = DataType.Text, Operator = op, Values = { value } };

    private static ConditionGroup GroupOf(params Condition[] conditions)
    {
        ConditionGroup group = new() { Connector = Connector.And };

        for (int i = 0; i < conditions.Length; i++)
        {
            conditions[i].Sort = i;
            group.Conditions.Add(conditions[i]);
        }

        return group;
    }

    // --------------------------------------------------------------- every clause resolves

    [Fact]
    public void An_alias_in_a_condition_resolves_to_its_field()
    {
        Filter result = Sanitize<AliasedCustomer>(new Filter { ConditionGroup = GroupOf(On("customer_name")) });

        Assert.Equal("Name", result.ConditionGroup!.Conditions[0].Field);
    }

    [Fact]
    public void An_alias_nested_in_a_subgroup_resolves_too()
    {
        // The walk has to reach every depth. A gate that stopped at the top level would be bypassed
        // by nesting one level deeper than it reaches.
        ConditionGroup root = new() { Connector = Connector.And };

        root.SubConditionGroups.Add(GroupOf(On("customer_name")));

        Filter result = Sanitize<AliasedCustomer>(new Filter { ConditionGroup = root });

        Assert.Equal("Name", result.ConditionGroup!.SubConditionGroups[0].Conditions[0].Field);
    }

    [Fact]
    public void An_alias_in_a_projection_resolves_to_its_field()
    {
        Filter result = Sanitize<AliasedCustomer>(
            new Filter { Selects = new List<string> { "customer_name", "Id" } });

        Assert.Equal(new[] { "Name", "Id" }, result.Selects);
    }

    [Fact]
    public void An_alias_in_a_sort_resolves_to_its_field()
    {
        Filter result = Sanitize<AliasedCustomer>(
            new Filter { Orders = new List<OrderBy> { new() { Field = "customer_name", Direction = Direction.Ascending } } });

        Assert.Equal("Name", result.Orders![0].Field);
    }

    [Fact]
    public void An_alias_through_a_reference_navigation_resolves_to_the_dotted_path()
    {
        Filter result = Sanitize<AliasedCustomer>(
            new Filter { ConditionGroup = GroupOf(On("email_address")) });

        Assert.Equal("Contact.Email", result.ConditionGroup!.Conditions[0].Field);
    }

    [Fact]
    public void An_alias_is_matched_without_regard_to_case()
    {
        // Field paths arrive from JSON written by hand. A stricter comparison here would let a
        // differently-cased alias miss the rewrite and then miss the gate.
        Filter result = Sanitize<AliasedCustomer>(new Filter { ConditionGroup = GroupOf(On("CUSTOMER_NAME")) });

        Assert.Equal("Name", result.ConditionGroup!.Conditions[0].Field);
    }

    [Fact]
    public void An_alias_is_trimmed_before_it_is_matched()
    {
        Filter result = Sanitize<AliasedCustomer>(new Filter { ConditionGroup = GroupOf(On("  customer_name  ")) });

        Assert.Equal("Name", result.ConditionGroup!.Conditions[0].Field);
    }

    // ------------------------------------------------------------------- additive, not exclusive

    [Fact]
    public void The_internal_path_still_works_after_a_field_is_aliased()
    {
        // An alias adds a spelling and never removes one, so decorating a field is not a breaking
        // change to a filter already in production.
        Filter result = Sanitize<AliasedCustomer>(new Filter { ConditionGroup = GroupOf(On("Name")) });

        Assert.Equal("Name", result.ConditionGroup!.Conditions[0].Field);
    }

    [Fact]
    public void A_name_that_is_neither_an_alias_nor_a_path_fails_as_ordinary_validation()
    {
        // Identical to the unguarded failure, so a caller cannot probe for which fields exist by
        // watching how the policy layer refuses them.
        LogicException error = Assert.Throws<LogicException>(
            () => Sanitize<AliasedCustomer>(new Filter { ConditionGroup = GroupOf(On("nonsense")) }));

        Assert.IsNotType<PolicyException>(error);
    }

    // ------------------------------------------------------------------------------ collisions

    [Fact]
    public void A_name_meaning_both_an_alias_and_a_property_is_refused()
    {
        // Preferring the property ignores a rule the caller was granted; preferring the alias sends
        // the filter to a different column. Neither is safe to guess.
        PolicyException error = Assert.Throws<PolicyException>(
            () => Sanitize<CollidingAliasDto>(new Filter { ConditionGroup = GroupOf(On("Salary")) }));

        Assert.Equal(PolicyErrorCode.AmbiguousFieldName, error.ErrorCode);
        Assert.Equal("Salary", error.FieldPath);
    }

    [Fact]
    public void One_aliased_type_reached_by_two_navigations_is_refused()
    {
        PolicyException error = Assert.Throws<PolicyException>(
            () => Sanitize<TwoContactCustomer>(new Filter { ConditionGroup = GroupOf(On("email_address")) }));

        Assert.Equal(PolicyErrorCode.AmbiguousFieldName, error.ErrorCode);
    }

    [Fact]
    public void Two_rules_aliasing_different_fields_to_one_name_are_refused()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .AddAlias("Name", "label", PolicyLevel.DynamicUser)
            .AddAlias("Id", "label", PolicyLevel.DynamicUser);

        PolicyException error = Assert.Throws<PolicyException>(
            () => Sanitize<PlainProduct>(
                new Filter { ConditionGroup = GroupOf(On("label")) }, provider: provider));

        Assert.Equal(PolicyErrorCode.AmbiguousFieldName, error.ErrorCode);
    }

    [Fact]
    public void An_alias_naming_its_own_field_is_not_a_collision()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .AddAlias("Name", "Name", PolicyLevel.DynamicUser);

        Filter result = Sanitize<PlainProduct>(
            new Filter { ConditionGroup = GroupOf(On("Name")) }, provider: provider);

        Assert.Equal("Name", result.ConditionGroup!.Conditions[0].Field);
    }

    // ------------------------------------------------------------------------- per-caller names

    [Fact]
    public void A_name_one_caller_holds_is_not_a_name_another_caller_holds()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .OnlyFor("u1")
            .AddAlias("Name", "my_name", PolicyLevel.DynamicUser);

        PolicyResolver resolver = new(new IDwPolicyProvider[] { provider });

        Filter Run(string identity) => FilterSanitizer.Sanitize<PlainProduct>(
            new Filter { ConditionGroup = GroupOf(On("my_name")) },
            resolver, Caller(identity), Options(), new PolicyTrace(DwTier.Convenience, false));

        Assert.Equal("Name", Run("u1").ConditionGroup!.Conditions[0].Field);
        Assert.Throws<LogicException>(() => Run("u2"));
    }

    // ------------------------------------------------------------------------------- reporting

    [Fact]
    public void A_refusal_names_the_alias_the_caller_used_not_the_internal_path()
    {
        // Handing back the real column path for a field the caller only ever named by alias turns
        // every refusal into schema disclosure.
        PolicyException error = Assert.Throws<PolicyException>(
            () => Sanitize<AliasedCustomer>(
                new Filter
                {
                    Orders = new List<OrderBy> { new() { Field = "phone_number", Direction = Direction.Ascending } }
                },
                DwTier.Strict));

        Assert.Equal(PolicyErrorCode.FieldDeniedForOrder, error.ErrorCode);
        Assert.Equal("phone_number", error.FieldPath);
        Assert.DoesNotContain("Contact.Phone", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_trace_keeps_the_canonical_path_and_records_the_alias_as_the_reason()
    {
        // The exception crosses the boundary; the trace does not. It is read against the policy, so
        // it names the field the policy governs.
        PolicyTrace trace = new(DwTier.Convenience, dryRun: false);

        FilterSanitizer.Sanitize<AliasedCustomer>(
            new Filter
            {
                Orders = new List<OrderBy> { new() { Field = "phone_number", Direction = Direction.Ascending } }
            },
            Attributes(), Caller(), Options(), trace);

        PolicyDecision decision = trace.Decisions.Single(d => d.Feature == PolicyFeature.Order);

        Assert.Equal("Contact.Phone", decision.FieldPath);
        Assert.Contains("phone_number", decision.Reason!, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------- summary: the fourth spelling

    [Fact]
    public void An_alias_in_a_grouping_key_resolves_to_its_field()
    {
        Summary summary = new()
        {
            GroupBy = new GroupBy { Fields = new List<string> { "customer_name" } }
        };

        Summary result = FilterSanitizer.Sanitize<AliasedCustomer>(
            summary, Attributes(), Caller(), Options(), new PolicyTrace(DwTier.Convenience, false));

        Assert.Equal(new[] { "Name" }, result.GroupBy!.Fields);
    }

    [Fact]
    public void A_summary_sort_written_as_the_alias_still_reaches_the_gate()
    {
        // The defect this closes: the grouping key is rewritten to its canonical path, and the
        // caller then names the same key by its alias in Orders, where nothing recognized it. A
        // reference matching nothing is skipped, and a skipped reference is a field left allowed.
        Summary summary = new()
        {
            GroupBy = new GroupBy { Fields = new List<string> { "phone_number" } },
            Orders = new List<OrderBy> { new() { Field = "phone_number", Direction = Direction.Ascending } }
        };

        PolicyException error = Assert.Throws<PolicyException>(() => FilterSanitizer.Sanitize<AliasedCustomer>(
            summary, Attributes(), Caller(), Options(DwTier.Strict),
            new PolicyTrace(DwTier.Strict, false)));

        Assert.Equal(PolicyErrorCode.FieldDeniedForOrder, error.ErrorCode);
    }

    [Fact]
    public void A_type_with_no_alias_is_canonicalized_exactly_as_before()
    {
        // The fast path. A type nobody has named takes no extra reflection and produces the same
        // filter it always did, which is what keeps the policy layer invisible until it has
        // something to say.
        Filter result = Sanitize<PlainProduct>(new Filter { ConditionGroup = GroupOf(On("name")) });

        Assert.Equal("Name", result.ConditionGroup!.Conditions[0].Field);
    }
}
