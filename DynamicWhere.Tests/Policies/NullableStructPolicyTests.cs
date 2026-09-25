using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;

namespace DynamicWhere.Tests.Policies
{
    public readonly record struct ZwIban(string Country, string Number);

    public struct ZwPair
    {
        public string Shown { get; set; }

        [DwDenied]
        public string Hidden { get; set; }

        [DwMask(MaskStrategy.Full)]
        public string Code { get; set; }

        [DwMask(MaskStrategy.Full, AllowAggregate = true, MinGroupSize = 3)]
        public string Counted { get; set; }
    }

    // Structs all the way down, so a typed selection builds the chain in memory as it would from EF Core.
    public struct ZwDeep1 { public ZwDeep2 Next { get; set; } }

    public struct ZwDeep2 { public ZwDeep3 Next { get; set; } }

    public struct ZwDeep3 { public ZwDeep4 Next { get; set; } }

    public struct ZwDeep4 { public ZwPair? Pair { get; set; } }

    public class ZwRow
    {
        public int Id { get; set; }

        [DwDenied]
        public ZwIban? Iban { get; set; }

        public ZwPair? Pair { get; set; }

        public ZwDeep1 First { get; set; }
    }

    /// <summary>
    /// A member of a nullable struct, reached as the query spells it: <c>Iban.Value.Number</c>. The
    /// attribute walk looks through the nullable and names the member <c>Iban.Number</c>, and so do the
    /// type's transforms and fragments, so a lookup by the query's spelling matched none of them: the
    /// nullable member's denial, a denial on the struct's own member and a mask on one all missed it, in
    /// every clause and both terminals, since the policy layer shipped.
    /// </summary>
    public sealed class NullableStructPolicyTests
    {
        private static List<ZwRow> Source(int count = 1) => Enumerable.Range(1, count).Select(i => new ZwRow
        {
            Id = i,
            Iban = new ZwIban("IQ", "123"),
            Pair = new ZwPair { Shown = "s", Hidden = "SECRET", Code = "CODE", Counted = "C" },
            First = new ZwDeep1 { Next = new ZwDeep2 { Next = new ZwDeep3 { Next = new ZwDeep4 { Pair = new ZwPair { Code = "DEEP" } } } } }
        }).ToList();

        private static PolicyQueryable<ZwRow> Guard(DwTier tier, int count = 1, int minGroupSize = 1, int depth = 4)
        {
            DwPolicyOptions options = new() { Tier = tier };

            options.Caps.MinGroupSize = minGroupSize;
            options.Caps.MaxNavigationDepth = depth;

            return Source(count).AsQueryable().ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                options,
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));
        }

        private static Filter Where(string field, string value, DataType type = DataType.Text)
        {
            Condition condition = new() { Field = field, DataType = type, Operator = Operator.Equal };
            condition.Values.Add(value);

            return new Filter { ConditionGroup = new ConditionGroup { Conditions = { condition } } };
        }

        private static Filter Selecting(params string[] fields) => new() { Selects = fields.ToList() };

        [Fact]
        public void The_policy_names_a_nullable_struct_member_without_the_nullable()
        {
            Assert.Equal("Iban.Number", AttributePolicyProvider.PolicyPath(typeof(ZwRow), "Iban.Value.Number"));
            Assert.Equal("Iban", AttributePolicyProvider.PolicyPath(typeof(ZwRow), "Iban.Value"));
            Assert.Equal("Iban.HasValue", AttributePolicyProvider.PolicyPath(typeof(ZwRow), "Iban.HasValue"));
            Assert.Equal("Id", AttributePolicyProvider.PolicyPath(typeof(ZwRow), "Id"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_member_of_a_denied_nullable_struct_is_refused_as_a_filter(DwTier tier)
        {
            PolicyException refusal = Assert.Throws<PolicyException>(() => Guard(tier).ToList(Where("Iban.Value.Number", "123")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Fact]
        public void Whether_a_denied_nullable_struct_has_a_value_is_refused_too()
        {
            PolicyException refusal = Assert.Throws<PolicyException>(
                () => Guard(DwTier.Strict).ToList(Where("Iban.HasValue", "true", DataType.Boolean)));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Fact]
        public void A_member_of_a_denied_nullable_struct_is_refused_as_a_sort_and_a_selection()
        {
            Assert.Equal(PolicyErrorCode.FieldDeniedForOrder, Assert.Throws<PolicyException>(() => Guard(DwTier.Strict)
                .ToList(new Filter { Orders = new List<OrderBy> { new() { Field = "Iban.Value.Number" } } })).ErrorCode);

            Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, Assert.Throws<PolicyException>(
                () => Guard(DwTier.Strict).ToList(Selecting("Iban.Value.Number"))).ErrorCode);

            Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, Assert.Throws<PolicyException>(
                () => Guard(DwTier.Strict).ToListDynamic(Selecting("Iban.Value.Number"))).ErrorCode);
        }

        [Fact]
        public void A_denied_member_inside_a_nullable_struct_is_refused()
        {
            Assert.Throws<PolicyException>(() => Guard(DwTier.Strict).ToList(Where("Pair.Value.Hidden", "SECRET")));
            Assert.Throws<PolicyException>(() => Guard(DwTier.Strict).ToList(Selecting("Pair.Value.Hidden")));
        }

        [Fact]
        public void An_allowed_member_inside_a_nullable_struct_stays_usable()
        {
            Assert.Single(Guard(DwTier.Strict).ToList(Where("Pair.Value.Shown", "s")).Data);

            ZwRow row = Guard(DwTier.Strict).ToList(Selecting("Pair.Value.Shown")).Data[0];

            Assert.Equal("s", row.Pair!.Value.Shown);
            Assert.Null(row.Pair.Value.Hidden);
        }

        [Fact]
        public void A_masked_member_inside_a_nullable_struct_is_masked_when_selected_through_value()
        {
            ZwRow typed = Guard(DwTier.Strict).ToList(Selecting("Pair.Value.Code")).Data[0];

            Assert.StartsWith("*", typed.Pair!.Value.Code);
        }

        [Fact]
        public void The_dynamic_terminal_masks_it_where_its_generated_row_holds_it_under_value()
        {
            FilterResult<dynamic> result = Guard(DwTier.Strict).ToListDynamic(Selecting("Pair.Value.Code"));

            string json = System.Text.Json.JsonSerializer.Serialize(result.Data);

            Assert.DoesNotContain("CODE", json, StringComparison.Ordinal);
            Assert.Contains("\"Code\":\"*", json, StringComparison.Ordinal);
        }

        [Fact]
        public void A_masked_member_inside_a_nullable_struct_is_masked_as_a_group_key()
        {
            SummaryResult result = Guard(DwTier.Convenience).ToList(new Summary { GroupBy = new GroupBy { Fields = { "Pair.Value.Code" } } });

            string json = System.Text.Json.JsonSerializer.Serialize(result.Data);

            Assert.DoesNotContain("CODE", json, StringComparison.Ordinal);
        }

        [Fact]
        public void The_group_floor_a_nullable_struct_member_declares_is_applied()
        {
            // Counted allows aggregation and asks for groups of at least three; two rows are one group of two.
            Summary summary = new()
            {
                GroupBy = new GroupBy
                {
                    Fields = { "Pair.Value.Shown" },
                    AggregateBy = { new AggregateBy { Field = "Pair.Value.Counted", Aggregator = Aggregator.Minimum, Alias = "N" } }
                }
            };

            Assert.Empty(Guard(DwTier.Convenience, count: 2).ToList(summary).Data);
            Assert.Single(Guard(DwTier.Convenience, count: 3).ToList(summary).Data);
        }

        [Fact]
        public void A_masked_member_past_the_walk_depth_inside_a_nullable_struct_is_masked()
        {
            ZwRow row = Guard(DwTier.Strict, depth: 8).ToList(Selecting("First.Next.Next.Next.Pair.Value.Code")).Data[0];

            Assert.StartsWith("*", row.First.Next.Next.Next.Pair!.Value.Code);
        }
    }
}
