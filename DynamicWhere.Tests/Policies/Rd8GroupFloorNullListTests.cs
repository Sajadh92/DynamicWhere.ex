using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>
    /// A request body carrying <c>"conditions": null</c> or <c>"subConditionGroups": null</c> overwrites
    /// the list's initializer. The pipeline takes such a group and every other reader in the gate
    /// checks for it; the group floor's own walk over <c>Having</c> did not, so with the floor on, which
    /// is the default, a summary that runs unguarded failed guarded with a null reference.
    /// </summary>
    public sealed class Rd8GroupFloorNullListTests
    {
        private static readonly Rd8Line[] Rows = { new() { Id = 1 }, new() { Id = 1 }, new() { Id = 2 } };

        private static GroupBy Grouping() => new()
        {
            Fields = new() { "Id" },
            AggregateBy = new() { new AggregateBy { Aggregator = Aggregator.Count, Alias = "n" } }
        };

        public static TheoryData<ConditionGroup> Havings() => new()
        {
            new ConditionGroup { Connector = Connector.And, Conditions = null! },
            new ConditionGroup { Connector = Connector.And, SubConditionGroups = null! },
            new ConditionGroup
            {
                Connector = Connector.And,
                SubConditionGroups = new() { new ConditionGroup { Connector = Connector.And, Conditions = null!, SubConditionGroups = null! } }
            }
        };

        [Theory]
        [MemberData(nameof(Havings))]
        public void A_having_with_a_null_list_runs_guarded_as_it_runs_unguarded_and_the_floor_still_applies(ConditionGroup having)
        {
            Assert.Equal(2, Rows.AsQueryable().ToList(new Summary { GroupBy = Grouping(), Having = having.Clone() }).Data!.Count);

            List<dynamic> kept = Rows.AsQueryable()
                .ApplyPolicy(
                    new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                    new DwPolicyOptions { Tier = DwTier.Strict, Caps = { MinGroupSize = 2 } },
                    new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }))
                .ToList(new Summary { GroupBy = Grouping(), Having = having }).Data!;

            // The group of one is below the floor of two.
            Assert.Equal(new[] { 1 }, kept.Select(row => (int)row.Id));
        }
    }
}
