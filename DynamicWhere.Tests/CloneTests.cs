using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;

namespace DynamicWhere.Tests
{
    /// <summary>
    /// <c>Clone</c> on the three request types a caller builds.
    /// </summary>
    /// <remarks>
    /// Public since 3.3.0. A caller reading the same request again with one part changed — the next
    /// page, another order — used to rebuild the request around the caller's own clauses, which
    /// leaves both requests holding one condition tree. The bug that follows is the one the library
    /// already avoids internally by cloning before it rewrites anything.
    /// </remarks>
    public class CloneTests
    {
        private static Filter Filled() => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition { Field = "Name", DataType = DataType.Text, Operator = Operator.Equal, Values = { "a" } }
                },
                SubConditionGroups = new List<ConditionGroup>
                {
                    new()
                    {
                        Conditions =
                        {
                            new Condition { Field = "Age", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = { "1" } }
                        }
                    }
                }
            },
            Selects = new List<string> { "Id", "Name" },
            Orders = new List<OrderBy> { new() { Field = "Name", Direction = Direction.Ascending } },
            Page = new PageBy { PageNumber = 1, PageSize = 10 }
        };

        [Fact]
        public void A_filter_clone_shares_nothing_with_the_original()
        {
            Filter original = Filled();
            Filter copy = original.Clone();

            Assert.NotSame(original, copy);
            Assert.NotSame(original.ConditionGroup, copy.ConditionGroup);
            Assert.NotSame(original.ConditionGroup!.Conditions[0], copy.ConditionGroup!.Conditions[0]);
            Assert.NotSame(original.ConditionGroup.SubConditionGroups![0], copy.ConditionGroup.SubConditionGroups![0]);
            Assert.NotSame(original.Selects, copy.Selects);
            Assert.NotSame(original.Orders, copy.Orders);
            Assert.NotSame(original.Orders![0], copy.Orders![0]);
            Assert.NotSame(original.Page, copy.Page);
        }

        [Fact]
        public void Changing_the_copy_leaves_the_caller_s_request_alone()
        {
            Filter original = Filled();
            Filter copy = original.Clone();

            copy.Page!.PageNumber = 2;
            copy.Orders![0].Direction = Direction.Descending;
            copy.Selects!.Add("Age");
            copy.ConditionGroup!.Conditions[0].Values[0] = "b";

            Assert.Equal(1, original.Page!.PageNumber);
            Assert.Equal(Direction.Ascending, original.Orders![0].Direction);
            Assert.Equal(2, original.Selects!.Count);
            Assert.Equal("a", original.ConditionGroup!.Conditions[0].Values[0]);
        }

        [Fact]
        public void The_copy_carries_every_value()
        {
            Filter copy = Filled().Clone();

            Assert.Equal("Name", copy.ConditionGroup!.Conditions[0].Field);
            Assert.Equal("Age", copy.ConditionGroup.SubConditionGroups![0].Conditions[0].Field);
            Assert.Equal(new[] { "Id", "Name" }, copy.Selects!);
            Assert.Equal("Name", copy.Orders![0].Field);
            Assert.Equal(10, copy.Page!.PageSize);
        }

        [Fact]
        public void A_branch_the_caller_left_null_stays_null()
        {
            Filter copy = new Filter().Clone();

            Assert.Null(copy.ConditionGroup);
            Assert.Null(copy.Selects);
            Assert.Null(copy.Orders);
            Assert.Null(copy.Page);
        }

        [Fact]
        public void A_segment_clone_copies_every_set()
        {
            Segment original = new()
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
                                new Condition { Field = "Name", DataType = DataType.Text, Operator = Operator.Equal, Values = { "a" } }
                            }
                        }
                    }
                },
                Orders = new List<OrderBy> { new() { Field = "Name", Direction = Direction.Ascending } },
                Page = new PageBy { PageNumber = 1, PageSize = 5 }
            };

            Segment copy = original.Clone();

            copy.ConditionSets[0].ConditionGroup!.Conditions[0].Values[0] = "b";
            copy.Page!.PageSize = 50;

            Assert.NotSame(original.ConditionSets[0], copy.ConditionSets[0]);
            Assert.Equal("a", original.ConditionSets[0].ConditionGroup!.Conditions[0].Values[0]);
            Assert.Equal(5, original.Page!.PageSize);
        }

        [Fact]
        public void A_summary_clone_copies_the_having_clause_as_well()
        {
            Summary original = new()
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition { Field = "Name", DataType = DataType.Text, Operator = Operator.Equal, Values = { "a" } }
                    }
                },
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "Name" },
                    AggregateBy = new List<AggregateBy> { new() { Alias = "Total", Aggregator = Aggregator.Count } }
                },
                Having = new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition { Field = "Total", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = { "1" } }
                    }
                },
                Page = new PageBy { PageNumber = 1, PageSize = 5 }
            };

            Summary copy = original.Clone();

            copy.Having!.Conditions[0].Values[0] = "9";
            copy.GroupBy!.Fields[0] = "Age";

            Assert.NotSame(original.Having, copy.Having);
            Assert.NotSame(original.GroupBy, copy.GroupBy);
            Assert.Equal("1", original.Having!.Conditions[0].Values[0]);
            Assert.Equal("Name", original.GroupBy!.Fields[0]);
        }

        [Fact]
        public void The_next_page_is_what_this_exists_for()
        {
            Filter caller = Filled();

            Filter page2 = caller.Clone();
            page2.Page!.PageNumber = 2;

            Assert.Equal(1, caller.Page!.PageNumber);
            Assert.Equal(2, page2.Page.PageNumber);
            Assert.Equal(caller.Page.PageSize, page2.Page.PageSize);
        }
    }
}
