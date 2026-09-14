using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers the deep clones the sanitizer relies on.
/// </summary>
/// <remarks>
/// <c>Validator</c> rewrites what it validates in place — <c>condition.Field =
/// condition.Field.Validate&lt;T&gt;()</c>, and the same for orders, group-by, and aggregates. The
/// guarded path therefore sanitizes a copy, so that neither canonicalization nor a policy
/// rewrite can reach the caller's own object. A shallow copy anywhere in this graph would let the
/// pipeline write through the clone into the original, which is why every test below mutates the
/// clone and then asserts on the original.
/// </remarks>
public class FilterCloneTests
{
    private static ConditionGroup NestedGroup() => new()
    {
        Sort = 1,
        Connector = Connector.And,
        Conditions =
        {
            new Condition
            {
                Sort = 1,
                Field = "Name",
                DataType = DataType.Text,
                Operator = Operator.Equal,
                Values = { "alpha" }
            }
        },
        SubConditionGroups =
        {
            new ConditionGroup
            {
                Sort = 2,
                Connector = Connector.Or,
                Conditions =
                {
                    new Condition
                    {
                        Sort = 1,
                        Field = "Salary",
                        DataType = DataType.Number,
                        Operator = Operator.GreaterThan,
                        Values = { 100000 }
                    }
                }
            }
        }
    };

    private static Filter FullFilter() => new()
    {
        ConditionGroup = NestedGroup(),
        Selects = new List<string> { "Id", "Name" },
        Orders = new List<OrderBy> { new() { Sort = 1, Field = "Name", Direction = Direction.Ascending } },
        Page = new PageBy { PageNumber = 1, PageSize = 25 }
    };

    [Fact]
    public void A_cloned_filter_is_a_different_instance_carrying_the_same_values()
    {
        Filter original = FullFilter();

        Filter clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.Equal("Name", clone.ConditionGroup!.Conditions[0].Field);
        Assert.Equal(new[] { "Id", "Name" }, clone.Selects!);
        Assert.Equal("Name", clone.Orders![0].Field);
        Assert.Equal(25, clone.Page!.PageSize);
    }

    [Fact]
    public void Rewriting_a_nested_condition_field_on_the_clone_leaves_the_original_alone()
    {
        // This is the exact write Validator performs, at the depth most likely to be missed.
        Filter original = FullFilter();

        Filter clone = original.Clone();
        clone.ConditionGroup!.SubConditionGroups[0].Conditions[0].Field = "REWRITTEN";

        Assert.Equal("Salary", original.ConditionGroup!.SubConditionGroups[0].Conditions[0].Field);
    }

    [Fact]
    public void Condition_values_are_a_new_list_on_the_clone()
    {
        Filter original = FullFilter();

        Filter clone = original.Clone();
        clone.ConditionGroup!.Conditions[0].Values.Add("added");

        Assert.Single(original.ConditionGroup!.Conditions[0].Values);
    }

    [Fact]
    public void Dropping_a_select_from_the_clone_leaves_the_original_alone()
    {
        Filter original = FullFilter();

        Filter clone = original.Clone();
        clone.Selects!.Remove("Name");

        Assert.Equal(new[] { "Id", "Name" }, original.Selects!);
    }

    [Fact]
    public void Dropping_an_order_from_the_clone_leaves_the_original_alone()
    {
        Filter original = FullFilter();

        Filter clone = original.Clone();
        clone.Orders!.Clear();

        Assert.Single(original.Orders!);
    }

    [Fact]
    public void Capping_the_page_size_on_the_clone_leaves_the_original_alone()
    {
        Filter original = FullFilter();

        Filter clone = original.Clone();
        clone.Page!.PageSize = 10;

        Assert.Equal(25, original.Page!.PageSize);
    }

    [Fact]
    public void A_null_branch_stays_null_rather_than_becoming_empty()
    {
        // The sanitizer decides whether to synthesize a projection by testing Selects for null, so
        // a clone that turned null into an empty list would change what the gate does.
        Filter original = new();

        Filter clone = original.Clone();

        Assert.Null(clone.ConditionGroup);
        Assert.Null(clone.Selects);
        Assert.Null(clone.Orders);
        Assert.Null(clone.Page);
    }

    [Fact]
    public void A_cloned_summary_separates_both_of_its_condition_groups()
    {
        // Summary reaches ConditionGroup twice. Having is the branch a Filter-shaped clone misses.
        Summary original = new()
        {
            ConditionGroup = NestedGroup(),
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Department" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = "Salary", Alias = "TotalPay", Aggregator = Aggregator.Sumation }
                }
            },
            Having = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = "TotalPay",
                        DataType = DataType.Number,
                        Operator = Operator.GreaterThan,
                        Values = { 1000 }
                    }
                }
            },
            Orders = new List<OrderBy> { new() { Field = "TotalPay", Direction = Direction.Descending } },
            Page = new PageBy { PageNumber = 1, PageSize = 50 }
        };

        Summary clone = original.Clone();
        clone.Having!.Conditions[0].Field = "REWRITTEN";
        clone.GroupBy!.Fields[0] = "REWRITTEN";
        clone.GroupBy.AggregateBy[0].Field = "REWRITTEN";
        clone.ConditionGroup!.Conditions[0].Field = "REWRITTEN";

        Assert.Equal("TotalPay", original.Having!.Conditions[0].Field);
        Assert.Equal("Department", original.GroupBy!.Fields[0]);
        Assert.Equal("Salary", original.GroupBy.AggregateBy[0].Field);
        Assert.Equal("Name", original.ConditionGroup!.Conditions[0].Field);
    }

    [Fact]
    public void A_cloned_segment_separates_every_condition_set()
    {
        Segment original = new()
        {
            ConditionSets =
            {
                new ConditionSet { Sort = 1, ConditionGroup = NestedGroup() },
                new ConditionSet { Sort = 2, Intersection = Intersection.Except, ConditionGroup = NestedGroup() }
            },
            Selects = new List<string> { "Id" },
            Orders = new List<OrderBy> { new() { Field = "Id" } },
            Page = new PageBy { PageNumber = 1, PageSize = 5 }
        };

        Segment clone = original.Clone();
        clone.ConditionSets[1].ConditionGroup.Conditions[0].Field = "REWRITTEN";
        clone.Selects!.Clear();

        Assert.Equal("Name", original.ConditionSets[1].ConditionGroup.Conditions[0].Field);
        Assert.Single(original.Selects!);
        Assert.Equal(Intersection.Except, clone.ConditionSets[1].Intersection);
    }
}
