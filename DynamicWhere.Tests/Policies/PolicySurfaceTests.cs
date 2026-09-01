using System.Reflection;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DTOs = DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers the rest of the guarded surface.
/// </summary>
/// <remarks>
/// A method missing from the handle is a method a caller has to leave the guarded path to reach,
/// which is the hole <c>RequirePolicy</c> exists to close — so the surface is asserted as a whole,
/// not only method by method.
/// </remarks>
public class PolicySurfaceTests
{
    private static IQueryable<SecuredEmployee> People() => new List<SecuredEmployee>
    {
        new() { Id = 1, Name = "Ada", NationalId = "AAA", Salary = 100m, InternalNotes = "n1" },
        new() { Id = 2, Name = "Bo", NationalId = "BBB", Salary = 200m, InternalNotes = "n2" }
    }.AsQueryable();

    private static DwPolicyContext Caller() =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    private static PolicyQueryable<SecuredEmployee> Guarded(DwTier tier = DwTier.Convenience) =>
        People().ApplyPolicy(
            Caller(),
            new DwPolicyOptions { Tier = tier },
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

    [Fact]
    public void Every_public_extension_method_has_a_counterpart_on_the_handle()
    {
        // The comparison is by name rather than by signature: the handle takes no `this` parameter
        // and drops the ones that are now ambient, so the shapes differ on purpose.
        HashSet<string> unguarded = typeof(Extension)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.DeclaringType == typeof(Extension))
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        HashSet<string> guarded = typeof(PolicyQueryable<SecuredEmployee>)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(unguarded.Except(guarded));
    }

    [Fact]
    public void Select_drops_a_refused_field()
    {
        IQueryable<SecuredEmployee> query =
            Guarded().Select(new List<string> { "Name", "Salary" }).AsUnguardedQueryable();

        Assert.All(query.ToList(), row => Assert.Equal(0m, row.Salary));
        Assert.All(query.ToList(), row => Assert.NotEqual(string.Empty, row.Name));
    }

    [Fact]
    public void Select_refusing_everything_throws()
    {
        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guarded().Select(new List<string> { "Salary", "NationalId" }));

        Assert.Equal(PolicyErrorCode.AllSelectsDenied, exception.ErrorCode);
    }

    [Fact]
    public void A_composed_where_on_a_refused_field_throws()
    {
        Condition condition = new()
        {
            Field = "NationalId",
            DataType = DataType.Text,
            Operator = Operator.Equal,
            Values = { "AAA" }
        };

        Assert.Throws<PolicyException>(() => Guarded().Where(condition));
    }

    [Fact]
    public void A_composed_where_on_an_allowed_field_runs()
    {
        Condition condition = new()
        {
            Field = "Name",
            DataType = DataType.Text,
            Operator = Operator.Equal,
            Values = { "Ada" }
        };

        Assert.Single(Guarded().Where(condition).AsUnguardedQueryable().ToList());
    }

    [Fact]
    public void A_composed_where_is_not_refused_for_lacking_a_projection()
    {
        // The regression this guards: wrapping a lone clause in a Filter to reuse the sanitizer
        // would trip projection synthesis, and a type whose fields are all refused for select would
        // then fail a Where call with "every projection field denied".
        Condition condition = new()
        {
            Field = "Name",
            DataType = DataType.Text,
            Operator = Operator.Equal,
            Values = { "Ada" }
        };

        IQueryable<SecuredEmployee> query = Guarded().Where(condition).AsUnguardedQueryable();

        Assert.All(query.ToList(), row => Assert.NotEqual(string.Empty, row.NationalId));
    }

    [Fact]
    public void A_composed_order_on_a_refused_field_is_dropped_then_thrown_by_tier()
    {
        OrderBy order = new() { Field = "InternalNotes" };

        Assert.Equal(2, Guarded().Order(order).AsUnguardedQueryable().ToList().Count);
        Assert.Throws<PolicyException>(() => Guarded(DwTier.Strict).Order(order));
    }

    [Fact]
    public void A_composed_page_over_the_cap_throws()
    {
        Assert.Throws<PolicyException>(
            () => Guarded().Page(new PageBy { PageNumber = 1, PageSize = 5000 }));
    }

    [Fact]
    public void A_composed_group_on_a_refused_key_throws()
    {
        GroupBy groupBy = new() { Fields = new List<string> { "InternalNotes" } };

        PolicyException exception = Assert.Throws<PolicyException>(() => Guarded().Group(groupBy));

        Assert.Equal(PolicyErrorCode.FieldDeniedForGroup, exception.ErrorCode);
    }

    [Fact]
    public void The_handle_reports_the_last_decision_for_composed_calls()
    {
        // A composable method returns an IQueryable, which has nowhere to carry the record.
        PolicyQueryable<SecuredEmployee> handle = Guarded();

        handle.Select(new List<string> { "Name", "Salary" });

        Assert.Contains(
            handle.LastTrace!.Decisions,
            d => d.FieldPath == "Salary" && d.Action == PolicyAction.Dropped);
    }

    [Fact]
    public void A_dynamic_projection_is_gated_the_same_way()
    {
        Filter filter = new() { Selects = new List<string> { "Name", "Salary" } };

        var result = Guarded().ToListDynamic(filter);

        Assert.Equal(2, result.Data.Count);
        Assert.Contains(
            result.Policy!.Decisions,
            d => d.FieldPath == "Salary" && d.Action == PolicyAction.Dropped);
    }

    [Fact]
    public void A_summary_through_the_handle_carries_its_record()
    {
        Summary summary = new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Name" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = "Salary", Alias = "TotalPay", Aggregator = Aggregator.Sumation }
                }
            }
        };

        var result = Guarded().ToList(summary);

        Assert.Equal(2, result.Data.Count);
        Assert.NotNull(result.Policy);
    }

    [Fact]
    public void An_in_memory_sequence_can_be_guarded_too()
    {
        IEnumerable<SecuredEmployee> people = People().ToList();

        var result = people.ApplyPolicy(Caller()).ToList(new Filter());

        Assert.Equal(2, result.Data.Count);
        Assert.All(result.Data, row => Assert.Equal(string.Empty, row.NationalId));
    }

    // ------------------------------------------------------------------- segment

    [Fact]
    public void A_segment_refuses_a_field_kept_out_of_set_operations()
    {
        // NationalId is denied for every feature, Segment included. Set membership discloses
        // through which rows survive, not through which columns come back, so narrowing the
        // projection would not help.
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Name", PolicyFeature.Segment, PolicyEffect.Deny, PolicyLevel.SealedAttribute);

        Segment segment = new()
        {
            ConditionSets =
            {
                new ConditionSet
                {
                    Sort = 1,
                    ConditionGroup = new ConditionGroup
                    {
                        Conditions =
                        {
                            new Condition
                            {
                                Field = "Name",
                                DataType = DataType.Text,
                                Operator = Operator.Equal,
                                Values = { "Ada" }
                            }
                        }
                    }
                }
            }
        };

        PolicyException exception = Assert.Throws<PolicyException>(() => FilterSanitizer.Sanitize<SecuredEmployee>(
            segment,
            new PolicyResolver(new IDwPolicyProvider[] { provider }),
            Caller(),
            new DwPolicyOptions { Tier = DwTier.Convenience },
            new DTOs.PolicyTrace(DwTier.Convenience, false)));

        Assert.Equal(PolicyErrorCode.FieldDeniedForSegment, exception.ErrorCode);
        Assert.Equal("Name", exception.FieldPath);
    }

    [Fact]
    public void Every_condition_set_in_a_segment_is_gated_independently()
    {
        Segment segment = new()
        {
            ConditionSets =
            {
                new ConditionSet
                {
                    Sort = 1,
                    ConditionGroup = new ConditionGroup
                    {
                        Conditions =
                        {
                            new Condition
                            {
                                Field = "Name",
                                DataType = DataType.Text,
                                Operator = Operator.Equal,
                                Values = { "Ada" }
                            }
                        }
                    }
                },
                new ConditionSet
                {
                    Sort = 2,
                    Intersection = Intersection.Except,
                    ConditionGroup = new ConditionGroup
                    {
                        Conditions =
                        {
                            new Condition
                            {
                                Field = "NationalId",
                                DataType = DataType.Text,
                                Operator = Operator.Equal,
                                Values = { "AAA" }
                            }
                        }
                    }
                }
            }
        };

        PolicyException exception = Assert.Throws<PolicyException>(() => FilterSanitizer.Sanitize<SecuredEmployee>(
            segment,
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }),
            Caller(),
            new DwPolicyOptions { Tier = DwTier.Convenience },
            new DTOs.PolicyTrace(DwTier.Convenience, false)));

        Assert.Equal("NationalId", exception.FieldPath);
    }
}
