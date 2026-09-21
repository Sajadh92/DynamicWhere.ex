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
using DynamicWhere.ex.Source;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>A row whose policed members are all of types the framework declares.</summary>
    public class Rd8Row
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwDenied]
        public string Secret { get; set; } = string.Empty;

        [DwNoWhere]
        public DateTime Born { get; set; }

        [DwNoOrder]
        public string Rank { get; set; } = string.Empty;

        [DwDenied]
        public decimal? Salary { get; set; }

        [DwGeneralize(GeneralizeMode.Round, Step = 1000)]
        public decimal? Bonus { get; set; }

        [DwAudit]
        public string Notes { get; set; } = string.Empty;

        [DwCost(50)]
        public string Essay { get; set; } = string.Empty;

        [DwOperators(Allow = new[] { Operator.Equal })]
        public string Code { get; set; } = string.Empty;

        [DwDenied]
        public Rd8Lines Lines { get; set; } = new();
    }

    /// <summary>An application's own collection class, whose Count is its own member and not an element's.</summary>
    public class Rd8Lines : List<Rd8Line>
    {
    }

    public class Rd8Line
    {
        public int Id { get; set; }
    }

    /// <summary>
    /// A path that continues beneath a member whose type the framework declares, <c>Salary.Value</c>,
    /// <c>Secret.Length</c>, <c>Born.Year</c>, reads that member. No attribute can be placed there and no
    /// fragment names it, so resolved on its own it matched nothing and was allowed: a denied column was
    /// filtered on, sorted by, grouped by with its values as the keys, aggregated, and handed back by a
    /// dynamic projection, under either tier.
    /// </summary>
    public sealed class Rd8ValueMemberPathTests
    {
        private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyResolver Attributes() => new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Posture(DwTier tier) => new() { Tier = tier, Caps = { MinGroupSize = 1 } };

        private static Rd8Row[] Rows() => new[]
        {
            new Rd8Row { Id = 1, Name = "ab", Secret = "12345678", Born = new DateTime(1990, 1, 1), Rank = "x", Salary = 5100m, Bonus = 1234m, Notes = "n", Essay = "e", Code = "abcd" },
            new Rd8Row { Id = 2, Name = "abcdef", Secret = "12", Born = new DateTime(2001, 1, 1), Rank = "yy", Salary = 900m, Bonus = 4321m, Notes = "nn", Essay = "ee", Code = "ab" },
        };

        private static PolicyQueryable<Rd8Row> Guarded(DwTier tier) =>
            Rows().AsQueryable().ApplyPolicy(Caller(), Posture(tier), Attributes());

        private static Condition Cond(string field, DataType type, Operator op, params object[] values) =>
            new() { Sort = 0, Field = field, DataType = type, Operator = op, Values = values.ToList() };

        private static Filter Where(Condition condition) => new()
        {
            ConditionGroup = new ConditionGroup { Connector = Connector.And, Conditions = new() { condition } },
            Selects = new() { "Id" }
        };

        private static Summary GroupBy(string field) => new()
        {
            GroupBy = new GroupBy
            {
                Fields = new() { field },
                AggregateBy = new() { new AggregateBy { Aggregator = Aggregator.Count, Alias = "n" } }
            }
        };

        private static Summary Max(string field) => new()
        {
            GroupBy = new GroupBy
            {
                Fields = new() { "Name" },
                AggregateBy = new() { new AggregateBy { Field = field, Aggregator = Aggregator.Maximum, Alias = "m" } }
            }
        };

        public static TheoryData<DwTier, string, DataType, Operator, object> DeniedForWhere()
        {
            TheoryData<DwTier, string, DataType, Operator, object> data = new();

            foreach (DwTier tier in new[] { DwTier.Strict, DwTier.Convenience })
            {
                data.Add(tier, "Secret.Length", DataType.Number, Operator.GreaterThan, 5);
                data.Add(tier, "Salary.Value", DataType.Number, Operator.GreaterThan, 1000);
                data.Add(tier, "Salary.HasValue", DataType.Boolean, Operator.Equal, true);
                data.Add(tier, "Born.Year", DataType.Number, Operator.GreaterThan, 2000);
                data.Add(tier, "Born.Date.Year", DataType.Number, Operator.GreaterThan, 2000);
                data.Add(tier, "Lines.Count", DataType.Number, Operator.GreaterThan, 0);
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(DeniedForWhere))]
        public void A_condition_beneath_a_member_denied_for_where_is_refused(
            DwTier tier, string field, DataType type, Operator op, object value)
        {
            PolicyException refusal = Assert.Throws<PolicyException>(
                () => Guarded(tier).ToList(Where(Cond(field, type, op, value))));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Theory]
        [InlineData("Secret.Length")]
        [InlineData("Salary.Value")]
        [InlineData("Rank.Length")]
        public void An_order_beneath_a_member_denied_for_order_is_refused_or_dropped(string field)
        {
            Filter filter = new()
            {
                Orders = new() { new OrderBy { Sort = 0, Field = field, Direction = Direction.Ascending } },
                Selects = new() { "Id" }
            };

            PolicyException refusal = Assert.Throws<PolicyException>(() => Guarded(DwTier.Strict).ToList(filter));

            Assert.Equal(PolicyErrorCode.FieldDeniedForOrder, refusal.ErrorCode);

            // The second row sorts first by every one of these, so the source order coming back says
            // the order was dropped rather than applied.
            Assert.Equal(new[] { 1, 2 }, Guarded(DwTier.Convenience).ToList(filter).Data!.Select(row => row.Id));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_group_key_or_an_aggregate_beneath_a_denied_member_is_refused(DwTier tier)
        {
            foreach (string field in new[] { "Salary.Value", "Secret.Length" })
            {
                Assert.Equal(
                    PolicyErrorCode.FieldDeniedForGroup,
                    Assert.Throws<PolicyException>(() => Guarded(tier).ToList(GroupBy(field))).ErrorCode);

                Assert.Equal(
                    PolicyErrorCode.FieldDeniedForAggregate,
                    Assert.Throws<PolicyException>(() => Guarded(tier).ToList(Max(field))).ErrorCode);
            }
        }

        [Fact]
        public void A_projection_beneath_a_denied_member_is_refused_or_dropped()
        {
            foreach (string field in new[] { "Salary.Value", "Secret.Length" })
            {
                Filter filter = new() { Selects = new() { "Id", field } };

                Assert.Equal(
                    PolicyErrorCode.FieldDeniedForSelect,
                    Assert.Throws<PolicyException>(() => Guarded(DwTier.Strict).ToListDynamic(filter)).ErrorCode);

                string json = System.Text.Json.JsonSerializer.Serialize(Guarded(DwTier.Convenience).ToListDynamic(filter).Data);

                Assert.Equal("[{\"Id\":1},{\"Id\":2}]", json);
            }
        }

        /// <summary>
        /// A transform is applied to the member it was declared on. Beneath it there is nothing to apply
        /// it to, so every way the path hands a value back is refused; filtering runs on the stored value
        /// wherever the member is read from, and stays as the member's own policy leaves it.
        /// </summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Beneath_a_transformed_member_nothing_is_handed_back(DwTier tier)
        {
            Assert.Equal(
                PolicyErrorCode.FieldDeniedForGroup,
                Assert.Throws<PolicyException>(() => Guarded(tier).ToList(GroupBy("Bonus.Value"))).ErrorCode);

            Assert.Equal(
                PolicyErrorCode.FieldDeniedForAggregate,
                Assert.Throws<PolicyException>(() => Guarded(tier).ToList(Max("Bonus.Value"))).ErrorCode);

            Assert.Equal(
                new[] { 2 },
                Guarded(tier).ToList(Where(Cond("Bonus.Value", DataType.Number, Operator.GreaterThan, 2000))).Data!.Select(row => row.Id));

            // A projection hands the stored value back as surely as a grouping key does.
            Filter projection = new() { Selects = new() { "Id", "Bonus.Value" } };

            if (tier == DwTier.Strict)
            {
                Assert.Equal(
                    PolicyErrorCode.FieldDeniedForSelect,
                    Assert.Throws<PolicyException>(() => Guarded(tier).ToListDynamic(projection)).ErrorCode);
            }
            else
            {
                Assert.Equal(
                    "[{\"Id\":1},{\"Id\":2}]",
                    System.Text.Json.JsonSerializer.Serialize(Guarded(tier).ToListDynamic(projection).Data));
            }
        }

        [Fact]
        public void A_use_beneath_an_audited_member_is_recorded()
        {
            DwPolicyContext caller = Caller();

            Rows().AsQueryable().ApplyPolicy(caller, Posture(DwTier.Strict), Attributes())
                .ToList(Where(Cond("Notes.Length", DataType.Number, Operator.GreaterThan, 1)));

            Assert.Contains(caller.PendingAuditEvents, recorded => recorded.FieldPath == "Notes.Length" && recorded.Feature == PolicyFeature.Where);
        }

        [Fact]
        public void A_path_beneath_a_weighted_member_costs_what_the_member_costs()
        {
            DwPolicyOptions posture = new() { Tier = DwTier.Convenience, Caps = { MinGroupSize = 1, MaxQueryCost = 10 } };

            PolicyException refusal = Assert.Throws<PolicyException>(() => Rows().AsQueryable()
                .ApplyPolicy(Caller(), posture, Attributes())
                .ToList(Where(Cond("Essay.Length", DataType.Number, Operator.GreaterThan, 1))));

            Assert.Equal(PolicyErrorCode.QueryCostExceeded, refusal.ErrorCode);
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void An_operator_restriction_holds_beneath_the_member(DwTier tier)
        {
            PolicyException refusal = Assert.Throws<PolicyException>(
                () => Guarded(tier).ToList(Where(Cond("Code.Length", DataType.Number, Operator.GreaterThan, 3))));

            Assert.Equal(PolicyErrorCode.OperatorNotAllowed, refusal.ErrorCode);

            Assert.Equal(
                new[] { 1 },
                Guarded(tier).ToList(Where(Cond("Code.Length", DataType.Number, Operator.Equal, 4))).Data!.Select(row => row.Id));
        }

        /// <summary>A rule decides the member as an attribute does, so it decides what reads the member too.</summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_rule_denying_a_member_covers_the_paths_beneath_it(DwTier tier)
        {
            FakePolicyProvider rules = new FakePolicyProvider()
                .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);

            PolicyResolver resolver = new(new IDwPolicyProvider[] { new AttributePolicyProvider(), rules });

            PolicyException refusal = Assert.Throws<PolicyException>(() => Rows().AsQueryable()
                .ApplyPolicy(Caller(), Posture(tier), resolver)
                .ToList(Where(Cond("Name.Length", DataType.Number, Operator.GreaterThan, 3))));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        /// <summary>Precision: a member nothing denies is read beneath as it always was, and one feature is one feature.</summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_path_beneath_a_member_takes_that_members_policy_and_no_more(DwTier tier)
        {
            Assert.Equal(
                new[] { 2 },
                Guarded(tier).ToList(Where(Cond("Name.Length", DataType.Number, Operator.GreaterThan, 3))).Data!.Select(row => row.Id));

            // Born is denied for Where alone.
            Assert.Equal(2, Guarded(tier).ToList(GroupBy("Born.Year")).Data!.Count);
        }

        /// <summary>
        /// What is said to the caller about a member stays the member's: beneath it there is no member
        /// for a chain to be applied to, so the path reports none, while the denial that follows from the
        /// chain is there.
        /// </summary>
        [Fact]
        public void A_path_beneath_a_transformed_member_carries_no_transform_of_its_own()
        {
            PolicyResolver resolver = Attributes();

            Assert.True(resolver.Resolve(typeof(Rd8Row), "Bonus", Caller()).IsTransformed);

            FieldPolicy beneath = resolver.Resolve(typeof(Rd8Row), "Bonus.Value", Caller());

            Assert.False(beneath.IsTransformed);
            Assert.False(beneath.Allows(PolicyFeature.Select));
            Assert.True(beneath.Allows(PolicyFeature.Where));
        }

        [Fact]
        public void The_member_a_path_reads()
        {
            Assert.Equal("Salary", AttributePolicyProvider.Governing(typeof(Rd8Row), "Salary.Value"));
            Assert.Equal("Born", AttributePolicyProvider.Governing(typeof(Rd8Row), "Born.Date.Year"));
            Assert.Equal("Lines", AttributePolicyProvider.Governing(typeof(Rd8Row), "Lines.Count"));

            // An element's member is the walk's to name, and so is a member on its own.
            Assert.Null(AttributePolicyProvider.Governing(typeof(Rd8Row), "Lines.Id"));
            Assert.Null(AttributePolicyProvider.Governing(typeof(Rd8Row), "Salary"));

            // A name that matches nothing is refused as one, elsewhere.
            Assert.Null(AttributePolicyProvider.Governing(typeof(Rd8Row), "Nothing.Value"));
            Assert.Null(AttributePolicyProvider.Governing(typeof(Rd8Row), "Lines.Nothing"));
        }
    }

    public class Rd8A { public int Id { get; set; } public Rd8B? B { get; set; } }

    public class Rd8B { public int Id { get; set; } public Rd8C? C { get; set; } }

    public class Rd8C { public int Id { get; set; } public Rd8D? D { get; set; } }

    public class Rd8D { public int Id { get; set; } public Rd8E? E { get; set; } }

    public class Rd8E
    {
        public int Id { get; set; }

        [DwDenied]
        public string? Secret { get; set; }

        public string? Open { get; set; }

        [DwMask(MaskStrategy.Full)]
        public string? Card { get; set; }

        [DwNoWhere]
        public DateTime? Born { get; set; }
    }

    /// <summary>
    /// The attribute walk names paths of four segments, and the navigation-depth cap defaults to four. A
    /// host that raises the cap lets a request name a fifth, which no fragment reached: a denied member
    /// there was filtered on, grouped by and handed back under either tier.
    /// </summary>
    public sealed class Rd8DeepPathTests
    {
        private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyQueryable<Rd8A> Guarded(DwTier tier) => new[]
            {
                new Rd8A { Id = 1, B = new() { Id = 2, C = new() { Id = 3, D = new() { Id = 4, E = new() { Id = 5, Secret = "s3cret", Open = "o", Card = "4111111111111111", Born = new DateTime(2001, 1, 1) } } } } }
            }
            .AsQueryable()
            .ApplyPolicy(
                Caller(),
                new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1, MaxNavigationDepth = 6 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static Filter Where(string field, DataType type, Operator op, object value) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = new() { new Condition { Sort = 0, Field = field, DataType = type, Operator = op, Values = new() { value } } }
            },
            Selects = new() { "Id" }
        };

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_denied_member_past_the_walk_is_refused_in_every_clause(DwTier tier)
        {
            Assert.Equal(
                PolicyErrorCode.FieldDeniedForWhere,
                Assert.Throws<PolicyException>(() => Guarded(tier).ToList(Where("B.C.D.E.Secret", DataType.Text, Operator.StartsWith, "s3"))).ErrorCode);

            Assert.Equal(
                PolicyErrorCode.FieldDeniedForGroup,
                Assert.Throws<PolicyException>(() => Guarded(tier).ToList(new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = new() { "B.C.D.E.Secret" },
                        AggregateBy = new() { new AggregateBy { Aggregator = Aggregator.Count, Alias = "n" } }
                    }
                })).ErrorCode);

            // Beneath a member the framework declares the type of, past the walk as well.
            Assert.Equal(
                PolicyErrorCode.FieldDeniedForWhere,
                Assert.Throws<PolicyException>(() => Guarded(tier).ToList(Where("B.C.D.E.Born.Value", DataType.DateTime, Operator.GreaterThan, "2000-01-01"))).ErrorCode);
        }

        [Fact]
        public void A_denied_member_past_the_walk_is_never_handed_back()
        {
            Filter filter = new() { Selects = new() { "Id", "B.C.D.E.Secret" } };

            Assert.Equal(
                PolicyErrorCode.FieldDeniedForSelect,
                Assert.Throws<PolicyException>(() => Guarded(DwTier.Strict).ToListDynamic(filter)).ErrorCode);

            Assert.Equal(
                "[{\"Id\":1}]",
                System.Text.Json.JsonSerializer.Serialize(Guarded(DwTier.Convenience).ToListDynamic(filter).Data));
        }

        /// <summary>
        /// A member past the walk is still a member: named in a projection it comes back transformed,
        /// in a generated row as well, where no member carries an attribute to find the chain by. As a
        /// grouping key or an aggregate it is a column of a generated row the summary's own transform
        /// would not find, so those are refused.
        /// </summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_masked_member_past_the_walk_comes_back_masked_and_is_no_grouping_key(DwTier tier)
        {
            string generated = System.Text.Json.JsonSerializer.Serialize(
                Guarded(tier).ToListDynamic(new Filter { Selects = new() { "Id", "B.C.D.E.Card" } }).Data);

            Assert.DoesNotContain("4111", generated);
            Assert.Contains("****************", generated);

            Assert.Equal(
                PolicyErrorCode.FieldDeniedForGroup,
                Assert.Throws<PolicyException>(() => Guarded(tier).ToList(new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = new() { "B.C.D.E.Card" },
                        AggregateBy = new() { new AggregateBy { Aggregator = Aggregator.Count, Alias = "n" } }
                    }
                })).ErrorCode);

            Assert.Equal(
                PolicyErrorCode.FieldDeniedForAggregate,
                Assert.Throws<PolicyException>(() => Guarded(tier).ToList(new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = new() { "Id" },
                        AggregateBy = new() { new AggregateBy { Field = "B.C.D.E.Card", Aggregator = Aggregator.Maximum, Alias = "m" } }
                    }
                })).ErrorCode);
        }

        /// <summary>Precision: a member past the walk that declares nothing runs as it did.</summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_member_past_the_walk_that_declares_nothing_is_left_alone(DwTier tier)
        {
            Assert.Single(Guarded(tier).ToList(Where("B.C.D.E.Open", DataType.Text, Operator.Equal, "o")).Data!);

            // A masked member is filtered on its stored value wherever it is reached from.
            Assert.Single(Guarded(tier).ToList(Where("B.C.D.E.Card", DataType.Text, Operator.StartsWith, "4111")).Data!);
        }

        [Fact]
        public void Only_what_the_member_declares_about_itself_is_read_past_the_walk()
        {
            IReadOnlyList<PolicyFragment> fragments = AttributePolicyProvider.Unwalked(typeof(Rd8A), "B.C.D.E.Secret");

            PolicyFragment denial = Assert.Single(fragments);

            Assert.Equal(PolicyEffect.Deny, denial.Effect);
            Assert.Equal("B.C.D.E.Secret", denial.FieldPath);

            Assert.Empty(AttributePolicyProvider.Unwalked(typeof(Rd8A), "B.C.D.Id"));
            Assert.Empty(AttributePolicyProvider.Unwalked(typeof(Rd8A), "B.C.D.E.Nothing"));
        }
    }

    public class Rd8P { public int Id { get; set; } public Rd8Q? Q1 { get; set; } public Rd8Z? Late { get; set; } }

    public class Rd8P2 { public int Id { get; set; } public Rd8Z? Late { get; set; } public Rd8Q? Q1 { get; set; } }

    public class Rd8Q { public int Id { get; set; } public Rd8R? R { get; set; } }

    public class Rd8R { public int Id { get; set; } public Rd8S? S { get; set; } }

    public class Rd8S { public int Id { get; set; } public Rd8Z? Z { get; set; } }

    public class Rd8Z
    {
        public int Id { get; set; }

        [DwForceWhere(Operator.Equal, Value = "7")]
        public int Tenant { get; set; }

        [DwAlias("zname")]
        public string? Name { get; set; }
    }

    /// <summary>
    /// The attribute walk marks each type it is inside, to tell a cycle from a first visit, and returned
    /// at its depth limit with the type still marked. A type first met there then read as a cycle wherever
    /// it was met again, and what a cycle leaves out, a forced scope, a required filter and an alias, was
    /// left out of a path that reaches the type directly. Which member was declared first decided it.
    /// </summary>
    public sealed class Rd8WalkMarkerTests
    {
        [Theory]
        [InlineData(typeof(Rd8P))]
        [InlineData(typeof(Rd8P2))]
        public void A_type_first_met_at_the_depth_limit_is_not_a_cycle(Type root)
        {
            IReadOnlyList<PolicyFragment> fragments = new AttributePolicyProvider().GetFragments(root, new DwPolicyContext());

            Assert.Contains(fragments, fragment => fragment.FieldPath == "Late.Tenant" && fragment.Forced is not null);
            Assert.Contains(fragments, fragment => fragment.FieldPath == "Late.Name" && fragment.Alias == "zname");
        }

        [Fact]
        public void The_scope_forced_on_it_filters_the_rows()
        {
            Rd8P[] rows =
            {
                new() { Id = 1, Late = new Rd8Z { Tenant = 7 } },
                new() { Id = 2, Late = new Rd8Z { Tenant = 8 } },
            };

            List<Rd8P> kept = rows.AsQueryable()
                .ApplyPolicy(
                    new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                    new DwPolicyOptions { Tier = DwTier.Strict },
                    new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }))
                .ToList(new Filter { Selects = new() { "Id" } }).Data!;

            Assert.Equal(new[] { 1 }, kept.Select(row => row.Id));
        }
    }
}
