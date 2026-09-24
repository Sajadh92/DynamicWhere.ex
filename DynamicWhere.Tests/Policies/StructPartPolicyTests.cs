using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Discovery;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;

namespace DynamicWhere.Tests.Policies
{
    public readonly record struct ZsIban(string Country, string Number);

    public struct ZsPair
    {
        public string Ar { get; set; }

        [DwNoOrder]
        public string En { get; set; }

        [DwMask(MaskStrategy.Full)]
        public string Code { get; set; }
    }

    public struct ZsCard
    {
        [DwNoWhere]
        public ZsPair Inner { get; set; }

        public string Label { get; set; }
    }

    public class ZsOwner
    {
        public int Id { get; set; }

        [DwDenied]
        public ZsIban Iban { get; set; }

        public ZsPair Name { get; set; }
    }

    public class ZsLevel1
    {
        public ZsLevel2? Next { get; set; }
    }

    public class ZsLevel2
    {
        public ZsLevel3? Next { get; set; }
    }

    public class ZsLevel3
    {
        public ZsLevel4? Next { get; set; }
    }

    public class ZsLevel4
    {
        [DwDenied]
        public ZsIban Deep { get; set; }

        public ZsPair Pair { get; set; }
    }

    public class ZsRow
    {
        public int Id { get; set; }

        [DwDenied]
        public ZsIban Iban { get; set; }

        [DwNoWhere, DwNoOrder]
        public ZsPair Name { get; set; }

        [DwNoSelect]
        public ZsCard Card { get; set; }

        [DwDenied]
        public List<ZsIban> Ibans { get; set; } = new();

        [DwDeny(PolicyFeature.All, Overridable = true)]
        public ZsIban Soft { get; set; }

        [DwMask(MaskStrategy.Full)]
        public ZsPair Masked { get; set; }

        /// <summary>A class: a navigation, whose members stay separate fields.</summary>
        [DwDenied]
        public ZsOwner? Owner { get; set; }

        public ZsLevel1? First { get; set; }
    }

    /// <summary>
    /// A path beneath a member holding an application's own struct takes that member's policy, as a path
    /// beneath a framework type has since 3.3.0. A struct is a value, and a <c>[DwDenied]</c> on it left
    /// its parts to be filtered on and, once the typed projection could build them, selected.
    /// </summary>
    public sealed class StructPartPolicyTests
    {
        private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyResolver Attributes(params IDwPolicyProvider[] extra) =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() }.Concat(extra));

        private static FieldPolicy Resolve(string path, params IDwPolicyProvider[] extra) =>
            Attributes(extra).Resolve(typeof(ZsRow), path, Caller());

        private static readonly PolicyFeature[] Every =
        {
            PolicyFeature.Where, PolicyFeature.Select, PolicyFeature.Order,
            PolicyFeature.Group, PolicyFeature.Aggregate, PolicyFeature.Segment
        };

        // ---- the resolver ----------------------------------------------------------------------------

        [Fact]
        public void A_part_of_a_denied_struct_is_denied_for_everything()
        {
            FieldPolicy part = Resolve("Iban.Number");

            Assert.All(Every, feature => Assert.False(part.Allows(feature), feature.ToString()));
            Assert.True(part.IsSealed);
        }

        [Fact]
        public void A_part_takes_only_the_features_its_struct_is_denied()
        {
            FieldPolicy part = Resolve("Name.Ar");

            Assert.False(part.Allows(PolicyFeature.Where));
            Assert.False(part.Allows(PolicyFeature.Order));
            Assert.True(part.Allows(PolicyFeature.Select));
            Assert.True(part.Allows(PolicyFeature.Group));
        }

        [Fact]
        public void A_part_keeps_what_its_own_member_declares()
        {
            // En carries [DwNoOrder] of its own, inside a struct no one denied.
            FieldPolicy own = Attributes().Resolve(typeof(ZsOwner), "Name.En", Caller());

            Assert.False(own.Allows(PolicyFeature.Order));
            Assert.True(own.Allows(PolicyFeature.Where));
        }

        [Fact]
        public void Every_struct_a_path_passes_through_decides_it()
        {
            // Card is [DwNoSelect]; Inner, inside it, is [DwNoWhere].
            FieldPolicy part = Resolve("Card.Inner.Ar");

            Assert.False(part.Allows(PolicyFeature.Select));
            Assert.False(part.Allows(PolicyFeature.Where));
            Assert.True(part.Allows(PolicyFeature.Order));
        }

        [Fact]
        public void A_part_of_each_struct_in_a_collection_takes_the_collection_member_policy()
        {
            FieldPolicy part = Resolve("Ibans.Number");

            Assert.All(Every, feature => Assert.False(part.Allows(feature), feature.ToString()));
        }

        [Fact]
        public void A_class_is_still_a_navigation_whose_members_are_separate_fields()
        {
            // Docs trap 5: a denial of Owner denies that path only. Its own struct member is denied by its
            // own attribute, and so is every part of that struct.
            Assert.True(Resolve("Owner.Id").Allows(PolicyFeature.Where));
            Assert.False(Resolve("Owner.Iban.Number").Allows(PolicyFeature.Where));
            Assert.True(Resolve("Owner.Name.Ar").Allows(PolicyFeature.Where));
        }

        [Fact]
        public void A_runtime_rule_on_the_part_replaces_an_overridable_denial_of_the_struct()
        {
            FakePolicyProvider rule = new FakePolicyProvider()
                .Add("Soft.Number", PolicyFeature.Where, PolicyEffect.Allow, PolicyLevel.DynamicRole);

            Assert.False(Resolve("Soft.Number").Allows(PolicyFeature.Where));
            Assert.True(Resolve("Soft.Number", rule).Allows(PolicyFeature.Where));
        }

        [Fact]
        public void A_runtime_rule_on_the_part_cannot_replace_a_sealed_denial_of_the_struct()
        {
            FakePolicyProvider rule = new FakePolicyProvider()
                .Add("Iban.Number", PolicyFeature.All, PolicyEffect.Allow, PolicyLevel.DynamicGlobal);

            Assert.False(Resolve("Iban.Number", rule).Allows(PolicyFeature.Where));
        }

        [Fact]
        public void A_runtime_denial_of_the_struct_reaches_its_parts()
        {
            FakePolicyProvider rule = new FakePolicyProvider()
                .Add("Name", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

            Assert.True(Resolve("Name.Ar").Allows(PolicyFeature.Select));
            Assert.False(Resolve("Name.Ar", rule).Allows(PolicyFeature.Select));
        }

        [Fact]
        public void A_part_that_masks_itself_is_masked_and_stays_selectable()
        {
            // Code carries its own mask: the outbound walk applies it to that member, so nothing above it
            // has to be refused.
            FieldPolicy own = Attributes().Resolve(typeof(ZsOwner), "Name.Code", Caller());

            Assert.True(own.IsTransformed);
            Assert.True(own.Allows(PolicyFeature.Select));
        }

        [Fact]
        public void A_part_of_a_masked_struct_hands_back_nothing_the_mask_would_have_covered()
        {
            // The chain is the struct's, and has no member of the path's to apply to.
            FieldPolicy part = Resolve("Masked.Ar");

            Assert.False(part.Allows(PolicyFeature.Select));
            Assert.False(part.Allows(PolicyFeature.Group));
            Assert.False(part.Allows(PolicyFeature.Aggregate));
            Assert.False(part.IsTransformed);
        }

        [Fact]
        public void A_struct_past_the_walk_depth_still_decides_its_parts()
        {
            // First.Next.Next.Next.Deep.Number: Deep is the fifth segment, which no fragment of the walk
            // reaches, so its attributes are read directly.
            FieldPolicy part = Resolve("First.Next.Next.Next.Deep.Number");

            Assert.False(part.Allows(PolicyFeature.Where));
            Assert.False(part.Allows(PolicyFeature.Select));
        }

        [Fact]
        public void A_part_past_the_walk_depth_keeps_its_own_mask()
        {
            // First.Next.Next.Next.Pair.Code: the walk names neither Pair nor Code, and Code's own mask is
            // read directly, so the outbound walk still has a chain to apply.
            FieldPolicy part = Resolve("First.Next.Next.Next.Pair.Code");

            Assert.True(part.IsTransformed);
            Assert.True(part.Allows(PolicyFeature.Select));
            Assert.False(part.Allows(PolicyFeature.Group));
        }

        [Fact]
        public void The_explanation_names_the_attribute_that_decided_the_part()
        {
            PolicyExplanation explained = Attributes().Explain(typeof(ZsRow), "Iban.Number", Caller());

            FeatureExplanation where = explained.Features.Single(feature => feature.Feature == PolicyFeature.Where);

            Assert.Equal(PolicyEffect.Deny, where.Effect);
            Assert.NotNull(where.DecidedBy);
            Assert.Contains("DwDenied", where.DecidedBy!.Origin, StringComparison.Ordinal);
        }

        [Fact]
        public void The_schema_describes_a_part_as_its_struct_decides_it()
        {
            DwEntityCatalog catalog = new();

            catalog.Expose(typeof(ZsRow));
            catalog.Freeze();

            DwPolicyOptions options = new();

            options.Freeze();

            PolicySchema schema = PolicySchemaBuilder.Describe(
                typeof(ZsRow), catalog, new DwPolicyContext(), options, Attributes(),
                new PolicySchemaRequest { Paths = new List<string> { "Name" }, Depth = 1 });

            PolicySchemaField ar = schema.Fields.Single(field => field.Path == "Name.Ar");

            Assert.False(ar.CanWhere);
            Assert.False(ar.CanOrder);
            Assert.True(ar.CanSelect);
        }

        // ---- a guarded query -------------------------------------------------------------------------

        private static PolicyQueryable<ZsRow> Guard(DwTier tier) =>
            new List<ZsRow>
            {
                new() { Id = 1, Iban = new ZsIban("IQ", "123"), Name = new ZsPair { Ar = "أ", En = "a" } }
            }
            .AsQueryable()
            .ApplyPolicy(Caller(), new DwPolicyOptions { Tier = tier }, Attributes());

        private static Filter Where(string field, string value)
        {
            Condition condition = new() { Field = field, DataType = DataType.Text, Operator = Operator.Equal };
            condition.Values.Add(value);

            return new Filter { ConditionGroup = new ConditionGroup { Conditions = { condition } } };
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_filter_on_a_part_of_a_denied_struct_is_refused_in_both_tiers(DwTier tier)
        {
            // An equality oracle on a sealed field: which rows hold IBAN 123.
            PolicyException refusal = Assert.Throws<PolicyException>(() => Guard(tier).ToList(Where("Iban.Number", "123")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Fact]
        public void A_selection_of_a_part_of_a_denied_struct_is_refused_under_the_strict_tier()
        {
            PolicyException refusal = Assert.Throws<PolicyException>(() =>
                Guard(DwTier.Strict).ToList(new Filter { Selects = new List<string> { "Iban.Number" } }));

            Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, refusal.ErrorCode);
        }

        [Fact]
        public void A_selection_of_a_part_of_a_denied_struct_carries_nothing_under_the_convenience_tier()
        {
            FilterResult<ZsRow> result = Guard(DwTier.Convenience)
                .ToList(new Filter { Selects = new List<string> { "Id", "Iban.Number" } });

            Assert.Null(result.Data[0].Iban.Number);
            Assert.Equal(1, result.Data[0].Id);
        }

        [Fact]
        public void A_part_of_a_struct_denied_for_filtering_is_refused_where_and_returned_on_select()
        {
            // DCMP's LocalizedText Name, [DwNoWhere, DwNoOrder]: name.ar is refused as a filter and read
            // as a column.
            PolicyException refusal = Assert.Throws<PolicyException>(() => Guard(DwTier.Strict).ToList(Where("Name.Ar", "أ")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);

            FilterResult<ZsRow> read = Guard(DwTier.Strict).ToList(new Filter { Selects = new List<string> { "Name.Ar" } });

            Assert.Equal("أ", read.Data[0].Name.Ar);
        }

        [Fact]
        public void A_group_on_a_part_of_a_denied_struct_is_refused()
        {
            Summary summary = new() { GroupBy = new GroupBy { Fields = { "Iban.Number" } } };

            PolicyException refusal = Assert.Throws<PolicyException>(() => Guard(DwTier.Strict).ToList(summary));

            Assert.Equal(PolicyErrorCode.FieldDeniedForGroup, refusal.ErrorCode);
        }
    }
}
