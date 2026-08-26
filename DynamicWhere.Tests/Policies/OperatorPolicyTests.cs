using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers per-field operator restrictions.
/// </summary>
/// <remarks>
/// This is the control that stops enumeration. Allowing <c>Equal</c> on a national identifier while
/// refusing <c>Contains</c> and <c>StartsWith</c> means a caller who already knows the value can
/// confirm it, but one who does not cannot walk the column a character at a time.
/// </remarks>
public class OperatorPolicyTests
{
    private static DwPolicyContext Caller() =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    private static (Filter Result, PolicyTrace Trace) Guard<T>(Filter filter, DwTier tier)
        where T : class
    {
        PolicyTrace trace = new(tier, dryRun: false);

        Filter result = FilterSanitizer.Sanitize<T>(
            filter,
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }),
            Caller(),
            new DwPolicyOptions { Tier = tier },
            trace);

        return (result, trace);
    }

    private static Filter Where(string field, Operator op) => new()
    {
        ConditionGroup = new ConditionGroup
        {
            Conditions =
            {
                new Condition
                {
                    Field = field,
                    DataType = DataType.Text,
                    Operator = op,
                    Values = { "x" }
                }
            }
        }
    };

    [Fact]
    public void An_operator_on_the_allow_list_passes()
    {
        (Filter result, PolicyTrace trace) = Guard<SecuredAccount>(
            Where("NationalId", Operator.Equal), DwTier.Strict);

        Assert.Equal(Operator.Equal, result.ConditionGroup!.Conditions[0].Operator);
        Assert.Empty(trace.Decisions);
    }

    [Fact]
    public void An_operator_off_the_allow_list_is_refused_in_both_tiers()
    {
        // Refusing Contains is the whole point: it is the operator that turns an equality check
        // into a search, and a search is an enumeration.
        foreach (DwTier tier in new[] { DwTier.Convenience, DwTier.Strict })
        {
            PolicyException exception = Assert.Throws<PolicyException>(
                () => Guard<SecuredAccount>(Where("NationalId", Operator.Contains), tier));

            Assert.Equal(PolicyErrorCode.OperatorNotAllowed, exception.ErrorCode);
            Assert.Equal("NationalId", exception.FieldPath);
            Assert.Equal(PolicyFeature.Where, exception.Feature);
        }
    }

    [Fact]
    public void A_deny_list_refuses_what_it_names_and_permits_the_rest()
    {
        Assert.Throws<PolicyException>(
            () => Guard<SecuredAccount>(Where("Iban", Operator.Contains), DwTier.Strict));

        (Filter result, _) = Guard<SecuredAccount>(Where("Iban", Operator.Equal), DwTier.Strict);

        Assert.Equal(Operator.Equal, result.ConditionGroup!.Conditions[0].Operator);
    }

    [Fact]
    public void A_field_with_no_operator_attribute_accepts_any_operator()
    {
        (Filter result, _) = Guard<SecuredAccount>(Where("Balance", Operator.Between), DwTier.Strict);

        Assert.Equal(Operator.Between, result.ConditionGroup!.Conditions[0].Operator);
    }

    [Fact]
    public void A_restricted_operator_is_caught_inside_a_nested_group_too()
    {
        Filter filter = new()
        {
            ConditionGroup = new ConditionGroup
            {
                SubConditionGroups =
                {
                    new ConditionGroup
                    {
                        Conditions =
                        {
                            new Condition
                            {
                                Field = "NationalId",
                                DataType = DataType.Text,
                                Operator = Operator.StartsWith,
                                Values = { "12" }
                            }
                        }
                    }
                }
            }
        };

        Assert.Throws<PolicyException>(() => Guard<SecuredAccount>(filter, DwTier.Convenience));
    }

    // -------------------------------------------------------------- resolution

    [Fact]
    public void A_field_nothing_restricts_has_no_operator_list_at_all()
    {
        PolicyResolver resolver = new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        FieldPolicy policy = resolver.Resolve(typeof(SecuredAccount), "Balance", Caller());

        Assert.Null(policy.AllowedOperators);
        Assert.True(policy.AllowsOperator(Operator.Contains));
    }

    [Fact]
    public void Restrictions_from_several_fragments_intersect_rather_than_compete()
    {
        // A restriction is not an effect, so it cannot go through the per-feature election: the
        // resolver keeps one winner per feature and discards the rest, and a restriction riding on
        // a discarded fragment would silently vanish. Intersecting means more fragments can only
        // narrow the set, never widen it.
        FakePolicyProvider provider = new FakePolicyProvider()
            .AddOperators("Code", PolicyLevel.SealedAttribute, Operator.Equal, Operator.In, Operator.Contains)
            .AddOperators("Code", PolicyLevel.DynamicGlobal, Operator.Equal, Operator.Contains)
            .AddOperators("*", PolicyLevel.DynamicRole, Operator.Equal, Operator.In);

        PolicyResolver resolver = new(new IDwPolicyProvider[] { provider });

        FieldPolicy policy = resolver.Resolve(typeof(PlainProduct), "Code", Caller());

        Assert.Equal(new[] { Operator.Equal }, policy.AllowedOperators!);
        Assert.True(policy.AllowsOperator(Operator.Equal));
        Assert.False(policy.AllowsOperator(Operator.In));
        Assert.False(policy.AllowsOperator(Operator.Contains));
    }

    [Fact]
    public void An_intersection_that_empties_refuses_every_operator()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .AddOperators("Code", PolicyLevel.SealedAttribute, Operator.Equal)
            .AddOperators("Code", PolicyLevel.DynamicGlobal, Operator.Contains);

        PolicyResolver resolver = new(new IDwPolicyProvider[] { provider });

        FieldPolicy policy = resolver.Resolve(typeof(PlainProduct), "Code", Caller());

        Assert.Empty(policy.AllowedOperators!);
        Assert.False(policy.AllowsOperator(Operator.Equal));
    }

    [Fact]
    public void The_attribute_turns_a_deny_list_into_the_complement()
    {
        DwOperatorsAttribute attribute = new() { Deny = new[] { Operator.Contains } };

        IReadOnlyList<Operator> allowed = attribute.Resolve();

        Assert.DoesNotContain(Operator.Contains, allowed);
        Assert.Contains(Operator.Equal, allowed);
    }

    [Fact]
    public void An_allow_list_wins_over_a_deny_list_naming_the_same_operator()
    {
        // Both given is a contradiction the author has to lose; losing it closed is the only safe
        // direction.
        DwOperatorsAttribute attribute = new()
        {
            Allow = new[] { Operator.Equal, Operator.Contains },
            Deny = new[] { Operator.Contains }
        };

        Assert.Equal(new[] { Operator.Equal }, attribute.Resolve());
    }

    // -------------------------------------------------------------- having

    [Fact]
    public void A_having_clause_cannot_use_a_refused_operator_through_an_alias()
    {
        Summary summary = new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Id" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = "NationalId", Alias = "Ids", Aggregator = Aggregator.Maximum }
                }
            },
            Having = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = "Ids",
                        DataType = DataType.Text,
                        Operator = Operator.Contains,
                        Values = { "12" }
                    }
                }
            }
        };

        PolicyTrace trace = new(DwTier.Strict, dryRun: false);

        PolicyException exception = Assert.Throws<PolicyException>(() => FilterSanitizer.Sanitize<SecuredAccount>(
            summary,
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }),
            Caller(),
            new DwPolicyOptions { Tier = DwTier.Strict },
            trace));

        Assert.Equal(PolicyErrorCode.OperatorNotAllowed, exception.ErrorCode);
        Assert.Equal("NationalId", exception.FieldPath);
    }
}
