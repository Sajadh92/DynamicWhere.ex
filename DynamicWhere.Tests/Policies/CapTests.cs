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
/// Covers the numeric limits applied to every guarded query.
/// </summary>
/// <remarks>
/// These exist independently of access control. Before this release the library accepted a filter
/// of any size and a navigation path of any depth, so one request could generate an unbounded join
/// — a denial of service that needed no policy violation at all. Caps refuse in both tiers, because
/// silently truncating a filter would widen its result the same way dropping a condition does.
/// </remarks>
public class CapTests
{
    private static DwPolicyContext Caller() =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    private static (Filter Result, PolicyTrace Trace) Guard<T>(
        Filter filter, DwTier tier, Action<DwCaps>? configure = null)
        where T : class
    {
        DwPolicyOptions options = new() { Tier = tier };

        configure?.Invoke(options.Caps);

        PolicyTrace trace = new(tier, dryRun: false);

        Filter result = FilterSanitizer.Sanitize<T>(
            filter,
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }),
            Caller(),
            options,
            trace);

        return (result, trace);
    }

    private static Condition On(string field) => new()
    {
        Field = field,
        DataType = DataType.Text,
        Operator = Operator.Equal,
        Values = { "x" }
    };

    [Fact]
    public void Conditions_are_counted_across_the_whole_nested_tree()
    {
        // Counting only the root group would let any budget be evaded by nesting, which is the
        // cheapest possible way to rebuild the unbounded join the cap exists to prevent.
        Filter filter = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions = { On("Name") },
                SubConditionGroups =
                {
                    new ConditionGroup
                    {
                        Conditions = { On("Name") },
                        SubConditionGroups =
                        {
                            new ConditionGroup { Conditions = { On("Name"), On("Name") } }
                        }
                    }
                }
            }
        };

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<SecuredEmployee>(filter, DwTier.Convenience, caps => caps.MaxConditions = 3));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
        Assert.Contains("MaxConditions", exception.SourceOrigin!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_filter_inside_the_condition_budget_passes()
    {
        Filter filter = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions = { On("Name") },
                SubConditionGroups = { new ConditionGroup { Conditions = { On("Name") } } }
            }
        };

        (Filter result, _) = Guard<SecuredEmployee>(filter, DwTier.Strict, caps => caps.MaxConditions = 2);

        Assert.NotNull(result.ConditionGroup);
    }

    [Fact]
    public void Too_many_order_fields_are_refused()
    {
        Filter filter = new()
        {
            Orders = new List<OrderBy>
            {
                new() { Field = "Name" },
                new() { Field = "Id" },
                new() { Field = "Salary" }
            }
        };

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<SecuredEmployee>(filter, DwTier.Convenience, caps => caps.MaxOrderFields = 2));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
        Assert.Contains("MaxOrderFields", exception.SourceOrigin!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_oversized_page_is_refused_rather_than_quietly_clamped()
    {
        // Clamping would hand back a page smaller than the caller asked for while reporting
        // success, and a caller paging through results would silently skip rows.
        Filter filter = new() { Page = new PageBy { PageNumber = 1, PageSize = 5000 } };

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<SecuredEmployee>(filter, DwTier.Convenience));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
        Assert.Contains("MaxPageSize", exception.SourceOrigin!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_inside_the_limit_passes()
    {
        Filter filter = new() { Page = new PageBy { PageNumber = 1, PageSize = 50 } };

        (Filter result, _) = Guard<SecuredEmployee>(filter, DwTier.Strict);

        Assert.Equal(50, result.Page!.PageSize);
    }

    [Fact]
    public void A_navigation_path_deeper_than_the_cap_is_refused()
    {
        // PlainNode points at itself, so a caller can write an arbitrarily long path and make the
        // provider emit one join per segment.
        Filter filter = new() { Selects = new List<string> { "Next.Next.Next.Next.Label" } };

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<PlainNode>(filter, DwTier.Convenience));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
        Assert.Equal("Next.Next.Next.Next.Label", exception.FieldPath);
        Assert.Contains("MaxNavigationDepth", exception.SourceOrigin!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_navigation_path_at_the_cap_passes()
    {
        Filter filter = new() { Selects = new List<string> { "Next.Next.Next.Label" } };

        (Filter result, _) = Guard<PlainNode>(filter, DwTier.Strict);

        Assert.Equal("Next.Next.Next.Label", result.Selects![0]);
    }

    [Fact]
    public void Depth_is_checked_on_conditions_and_orders_too()
    {
        Filter conditions = new()
        {
            ConditionGroup = new ConditionGroup { Conditions = { On("Next.Next.Next.Next.Label") } }
        };

        Assert.Throws<PolicyException>(() => Guard<PlainNode>(conditions, DwTier.Strict));

        Filter orders = new()
        {
            Orders = new List<OrderBy> { new() { Field = "Next.Next.Next.Next.Label" } }
        };

        Assert.Throws<PolicyException>(() => Guard<PlainNode>(orders, DwTier.Strict));
    }

    [Fact]
    public void Caps_refuse_in_the_convenience_tier_as_well_as_the_strict_one()
    {
        Filter filter = new() { Page = new PageBy { PageSize = 5000 } };

        foreach (DwTier tier in new[] { DwTier.Convenience, DwTier.Strict })
        {
            Assert.Throws<PolicyException>(() => Guard<SecuredEmployee>(filter, tier));
        }
    }

    // ---- the page a caller does not send ---------------------------------------------------

    [Fact]
    public void An_unpaged_guarded_filter_is_given_the_default_page()
    {
        // MaxPageSize reads a page the caller sent, so the request with no page was the one request
        // no cap applied to: it returned every row while the same request naming that size was
        // refused.
        (Filter result, _) = Guard<SecuredEmployee>(
            new Filter(), DwTier.Strict, caps => caps.DefaultPageSize = 25);

        Assert.NotNull(result.Page);
        Assert.Equal(1, result.Page!.PageNumber);
        Assert.Equal(25, result.Page.PageSize);
    }

    [Fact]
    public void With_no_default_page_configured_an_unpaged_filter_is_left_alone()
    {
        // Off unless a deployment asks for it: a page that appeared on upgrade would truncate an
        // existing caller's results with nothing to see in the response.
        (Filter result, _) = Guard<SecuredEmployee>(new Filter(), DwTier.Strict);

        Assert.Null(result.Page);
    }

    [Fact]
    public void A_page_the_caller_sent_is_never_replaced_by_the_default()
    {
        (Filter result, _) = Guard<SecuredEmployee>(
            new Filter { Page = new PageBy { PageNumber = 3, PageSize = 10 } },
            DwTier.Strict,
            caps => caps.DefaultPageSize = 25);

        Assert.Equal(3, result.Page!.PageNumber);
        Assert.Equal(10, result.Page.PageSize);
    }

    [Fact]
    public void The_default_page_cannot_exceed_the_maximum_page()
    {
        // Otherwise the two caps could be configured into contradicting each other, and the default
        // would hand out a page the same query was not allowed to ask for.
        (Filter result, _) = Guard<SecuredEmployee>(
            new Filter(),
            DwTier.Strict,
            caps =>
            {
                caps.MaxPageSize = 50;
                caps.DefaultPageSize = 5000;
            });

        Assert.Equal(50, result.Page!.PageSize);
    }

    [Fact]
    public void A_negative_default_page_is_refused_and_zero_means_none() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DwCaps().DefaultPageSize = -1);

    // ---- nesting depth ------------------------------------------------------------------------

    [Fact]
    public void A_filter_nesting_deeper_than_the_cap_is_refused()
    {
        // MaxConditions bounds how many conditions there are and says nothing about their shape:
        // the planner pays for each parenthesised level, not for the count.
        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<SecuredEmployee>(Nested(6), DwTier.Strict, caps => caps.MaxConditionDepth = 5));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
    }

    [Fact]
    public void A_filter_nesting_exactly_to_the_cap_is_allowed()
    {
        // The root group is depth one, so a cap of five allows four levels under it.
        Guard<SecuredEmployee>(Nested(5), DwTier.Strict, caps => caps.MaxConditionDepth = 5);
    }

    [Fact]
    public void Nesting_is_capped_in_the_convenience_tier_as_well()
    {
        foreach (DwTier tier in new[] { DwTier.Convenience, DwTier.Strict })
        {
            Assert.Throws<PolicyException>(
                () => Guard<SecuredEmployee>(Nested(6), tier, caps => caps.MaxConditionDepth = 5));
        }
    }

    [Fact]
    public void A_summary_nesting_its_conditions_deeper_than_the_cap_is_refused()
    {
        Summary summary = Counted();

        summary.ConditionGroup = NestedGroup(6, () => On("Name"));

        PolicyException exception = Assert.Throws<PolicyException>(
            () => GuardSummary(summary, caps => caps.MaxConditionDepth = 5));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
        Assert.Contains("MaxConditionDepth", exception.SourceOrigin!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_summary_nesting_its_having_deeper_than_the_cap_is_refused()
    {
        // HAVING is a second condition tree on the same request. Measuring only the WHERE side
        // would leave the cap one property away from not applying.
        Summary summary = Counted();

        summary.Having = NestedGroup(6, OnCount);

        PolicyException exception = Assert.Throws<PolicyException>(
            () => GuardSummary(summary, caps => caps.MaxConditionDepth = 5));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
    }

    [Fact]
    public void A_summary_nesting_each_tree_exactly_to_the_cap_is_allowed()
    {
        // The deeper of the two is measured, not their sum.
        Summary summary = Counted();

        summary.ConditionGroup = NestedGroup(5, () => On("Name"));
        summary.Having = NestedGroup(5, OnCount);

        GuardSummary(summary, caps => caps.MaxConditionDepth = 5);
    }

    [Fact]
    public void A_segment_set_nesting_deeper_than_the_cap_is_refused()
    {
        // The shallow first set must not vouch for the deep second one: each set is its own tree.
        Segment segment = new()
        {
            ConditionSets = new List<ConditionSet>
            {
                new() { Sort = 1, ConditionGroup = NestedGroup(1, () => On("Name")) },
                new()
                {
                    Sort = 2,
                    Intersection = Intersection.Union,
                    ConditionGroup = NestedGroup(6, () => On("Name"))
                }
            }
        };

        PolicyException exception = Assert.Throws<PolicyException>(
            () => GuardSegment(segment, caps => caps.MaxConditionDepth = 5));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
        Assert.Contains("MaxConditionDepth", exception.SourceOrigin!, StringComparison.Ordinal);
    }

    // ---- condition sets -----------------------------------------------------------------------

    [Fact]
    public void A_segment_carrying_more_condition_sets_than_the_cap_is_refused()
    {
        // Each set adds a condition, or a subquery, to the one statement a segment becomes. A set
        // with no conditions spends nothing from MaxConditions or MaxConditionDepth, so the number of
        // sets is the only bound on that statement.
        PolicyException exception = Assert.Throws<PolicyException>(
            () => GuardSegment(EmptySets(4), caps => caps.MaxConditionSets = 3));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
        Assert.Equal("*", exception.FieldPath);
        Assert.Equal("MaxConditionSets cap (3), request had 4", exception.SourceOrigin);
    }

    [Fact]
    public void A_segment_carrying_exactly_the_cap_of_condition_sets_is_allowed()
    {
        Segment result = GuardSegment(EmptySets(3), caps => caps.MaxConditionSets = 3);

        Assert.Equal(3, result.ConditionSets.Count);
    }

    [Fact]
    public void Condition_sets_are_capped_in_the_convenience_tier_as_well()
    {
        foreach (DwTier tier in new[] { DwTier.Convenience, DwTier.Strict })
        {
            PolicyException exception = Assert.Throws<PolicyException>(
                () => GuardSegment(EmptySets(4), caps => caps.MaxConditionSets = 3, tier));

            Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
            Assert.Contains("MaxConditionSets", exception.SourceOrigin!, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_default_condition_set_cap_allows_ten_sets_and_refuses_eleven()
    {
        // The shipped default rather than a configured one, because the default is what an API that
        // never heard of this cap is running.
        GuardSegment(EmptySets(10), _ => { });

        PolicyException exception = Assert.Throws<PolicyException>(() => GuardSegment(EmptySets(11), _ => { }));

        Assert.Equal("MaxConditionSets cap (10), request had 11", exception.SourceOrigin);
    }

    [Fact]
    public void An_unpaged_guarded_summary_is_given_the_default_page()
    {
        Summary result = GuardSummary(Counted(), caps => caps.DefaultPageSize = 25);

        Assert.Equal(1, result.Page!.PageNumber);
        Assert.Equal(25, result.Page.PageSize);
    }

    [Fact]
    public void An_unpaged_guarded_segment_is_given_the_default_page()
    {
        Segment segment = new()
        {
            ConditionSets = new List<ConditionSet>
            {
                new() { Sort = 1, ConditionGroup = NestedGroup(1, () => On("Name")) }
            }
        };

        Segment result = GuardSegment(segment, caps => caps.DefaultPageSize = 25);

        Assert.Equal(1, result.Page!.PageNumber);
        Assert.Equal(25, result.Page.PageSize);
    }

    /// <summary>A filter whose groups nest <paramref name="depth"/> levels, counting the root.</summary>
    private static Filter Nested(int depth) => new() { ConditionGroup = NestedGroup(depth, () => On("Name")) };

    /// <summary>A group tree <paramref name="depth"/> levels deep, counting the root.</summary>
    private static ConditionGroup NestedGroup(int depth, Func<Condition> condition)
    {
        ConditionGroup root = new() { Conditions = { condition() } };
        ConditionGroup current = root;

        for (int level = 1; level < depth; level++)
        {
            ConditionGroup child = new() { Conditions = { condition() } };

            current.SubConditionGroups.Add(child);
            current = child;
        }

        return root;
    }

    /// <summary>Employees counted by name, with no floor, so nothing but the caps shapes the result.</summary>
    private static Summary Counted() => new()
    {
        GroupBy = new GroupBy
        {
            Fields = new List<string> { "Name" },
            AggregateBy = new List<AggregateBy>
            {
                new() { Field = "Id", Aggregator = Aggregator.Count, Alias = "n" }
            }
        }
    };

    private static Condition OnCount() => new()
    {
        Field = "n",
        DataType = DataType.Number,
        Operator = Operator.GreaterThan,
        Values = { 0 }
    };

    private static Summary GuardSummary(Summary summary, Action<DwCaps> configure)
    {
        DwPolicyOptions options = new() { Tier = DwTier.Strict };

        options.Caps.MinGroupSize = 1;
        configure(options.Caps);

        return FilterSanitizer.Sanitize<SecuredEmployee>(
            summary,
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }),
            Caller(),
            options,
            new PolicyTrace(DwTier.Strict, dryRun: false));
    }

    private static Segment GuardSegment(Segment segment, Action<DwCaps> configure, DwTier tier = DwTier.Strict)
    {
        DwPolicyOptions options = new() { Tier = tier };

        configure(options.Caps);

        return FilterSanitizer.Sanitize<SecuredEmployee>(
            segment,
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }),
            Caller(),
            options,
            new PolicyTrace(tier, dryRun: false));
    }

    /// <summary>A segment of <paramref name="count"/> unions, none of them holding a condition.</summary>
    private static Segment EmptySets(int count)
    {
        Segment segment = new() { ConditionSets = new List<ConditionSet>() };

        for (int sort = 1; sort <= count; sort++)
        {
            segment.ConditionSets.Add(new ConditionSet
            {
                Sort = sort,
                Intersection = sort == 1 ? null : Intersection.Union
            });
        }

        return segment;
    }

    [Fact]
    public void A_cap_is_checked_before_the_field_policy_is_consulted()
    {
        // Resolving policy for every field of an unbounded filter is the work the cap exists to
        // avoid, so the cheap structural check has to come first.
        Filter filter = new()
        {
            Page = new PageBy { PageSize = 5000 },
            ConditionGroup = new ConditionGroup { Conditions = { On("Contact.Email") } }
        };

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<SecuredEmployee>(filter, DwTier.Strict));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
    }

    // ---------------------------------------------------------------- values and aggregates (3.1.0)

    private static Condition InValues(string field, int count)
    {
        Condition condition = new() { Field = field, DataType = DataType.Text, Operator = Operator.In };

        condition.Values.AddRange(Enumerable.Range(0, count).Select(i => (object)$"v{i}"));

        return condition;
    }

    private static Filter WhereIn(int count) =>
        new() { ConditionGroup = new ConditionGroup { Conditions = { InValues("Name", count) } } };

    private static Summary Aggregating(int count, string? field)
    {
        Summary summary = new()
        {
            GroupBy = new GroupBy { Fields = new List<string> { "Name" }, AggregateBy = new List<AggregateBy>() }
        };

        for (int i = 0; i < count; i++)
        {
            summary.GroupBy.AggregateBy.Add(new AggregateBy { Field = field, Aggregator = Aggregator.Count, Alias = $"n{i}" });
        }

        return summary;
    }

    [Fact]
    public void A_condition_carrying_more_values_than_the_cap_is_refused_in_both_tiers()
    {
        // An In is one comparison per value, so one condition could make a predicate of any size while
        // spending one condition and one field.
        foreach (DwTier tier in new[] { DwTier.Convenience, DwTier.Strict })
        {
            PolicyException exception = Assert.Throws<PolicyException>(
                () => Guard<SecuredEmployee>(WhereIn(4), tier, caps => caps.MaxConditionValues = 3));

            Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
            Assert.Equal("MaxConditionValues cap (3), request had 4", exception.SourceOrigin);

            Guard<SecuredEmployee>(WhereIn(3), tier, caps => caps.MaxConditionValues = 3);
        }
    }

    [Fact]
    public void Values_are_counted_in_a_having_clause_and_in_every_set_of_a_segment()
    {
        Summary summary = Counted();
        Condition having = OnCount();

        having.Operator = Operator.In;
        having.Values.AddRange(new object[] { 1, 2, 3 });
        summary.Having = new ConditionGroup { Conditions = { having } };

        Assert.Equal(
            "MaxConditionValues cap (3), request had 4",
            Assert.Throws<PolicyException>(() => GuardSummary(summary, caps => caps.MaxConditionValues = 3)).SourceOrigin);

        Segment segment = EmptySets(2);

        segment.ConditionSets[1].ConditionGroup = new ConditionGroup { Conditions = { InValues("Name", 4) } };

        Assert.Equal(
            "MaxConditionValues cap (3), request had 4",
            Assert.Throws<PolicyException>(() => GuardSegment(segment, caps => caps.MaxConditionValues = 3)).SourceOrigin);
    }

    [Fact]
    public void The_default_value_cap_allows_a_thousand_values_and_refuses_one_more()
    {
        Guard<SecuredEmployee>(WhereIn(1000), DwTier.Strict);

        Assert.Equal(
            "MaxConditionValues cap (1000), request had 1001",
            Assert.Throws<PolicyException>(() => Guard<SecuredEmployee>(WhereIn(1001), DwTier.Strict)).SourceOrigin);
    }

    [Fact]
    public void A_summary_computing_more_aggregates_than_the_cap_is_refused()
    {
        PolicyException exception = Assert.Throws<PolicyException>(
            () => GuardSummary(Aggregating(4, "Id"), caps => caps.MaxAggregates = 3));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
        Assert.Equal("*", exception.FieldPath);
        Assert.Equal("MaxAggregates cap (3), request had 4", exception.SourceOrigin);

        GuardSummary(Aggregating(3, "Id"), caps => caps.MaxAggregates = 3);
    }

    [Fact]
    public void The_default_aggregate_cap_allows_fifty_and_refuses_fifty_one()
    {
        GuardSummary(Aggregating(50, null), _ => { });

        Assert.Equal(
            "MaxAggregates cap (50), request had 51",
            Assert.Throws<PolicyException>(() => GuardSummary(Aggregating(51, null), _ => { })).SourceOrigin);
    }

    [Fact]
    public void An_aggregate_with_no_field_is_charged_the_default_field_cost()
    {
        // A Count names nothing a weight could be set on, and was free: any number of them cost nothing.
        // Grouping by Name costs one and each Count one more, so three Counts break a budget of three.
        GuardSummary(Aggregating(2, null), caps => caps.MaxQueryCost = 3);

        Assert.Equal(
            PolicyErrorCode.QueryCostExceeded,
            Assert.Throws<PolicyException>(() => GuardSummary(Aggregating(3, null), caps => caps.MaxQueryCost = 3)).ErrorCode);
    }

    [Fact]
    public void Counts_are_checked_before_any_name_is_resolved()
    {
        // In the convenience tier a name that matches nothing fails the moment it is resolved, so the
        // order is visible: an oversized request of unknown names is refused by the cap, not by its
        // first name, and none of them is resolved at all.
        Filter filter = new() { ConditionGroup = new ConditionGroup() };

        for (int i = 0; i < 4; i++)
        {
            filter.ConditionGroup.Conditions.Add(On("NoSuchColumn"));
        }

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<SecuredEmployee>(filter, DwTier.Convenience, caps => caps.MaxConditions = 3));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
    }
}
