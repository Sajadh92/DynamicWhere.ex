using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Xunit.Abstractions;

// An application's own collection classes, members sharing a name or hidden with new, and the framework's value collections.

namespace DynamicWhere.Tests.Policies
{
    /// <summary>An application list of strings with a plain member of its own (a paged list, a tag set with a note).</summary>
    public class ZwLabelSet : List<string>
    {
        public string? Note { get; set; }
    }

    /// <summary>The ASP.NET Core tutorial's PaginatedList shape, over strings.</summary>
    public class ZwPagedNames : List<string>
    {
        public int PageIndex { get; set; }

        public bool HasNextPage => false;
    }

    /// <summary>An application list of strings whose own member is denied.</summary>
    public class ZwSecretTags : List<string>
    {
        [DwDenied]
        public string? OwnerSecret { get; set; }
    }

    public class ZwLabelRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwDenied]
        public string? Ssn { get; set; }

        public ZwLabelSet Labels { get; set; } = new();

        public ZwPagedNames Names { get; set; } = new();
    }

    public class ZwPlainLabelRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public ZwLabelSet Labels { get; set; } = new();
    }

    public class ZwNestedTagRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<ZwSecretTags> Sets { get; set; } = new();

        public ZwSecretTags[] Arr { get; set; } = Array.Empty<ZwSecretTags>();
    }

    public class ZwDeclaredFrameworkRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<string> Tags { get; set; } = new();

        public IEnumerable<string> Seq { get; set; } = Array.Empty<string>();
    }

    public class ZwCaseRow
    {
        public int Id { get; set; }

        public string? Code { get; set; }

        [DwDenied]
        public string? CODE { get; set; }
    }

    public class ZwHideValueBase
    {
        public int Id { get; set; }

        [DwDenied]
        public string? Code { get; set; }
    }

    public class ZwHideValueDerived : ZwHideValueBase
    {
        public new string? Code { get; set; }
    }

    public class ZwItemsBase
    {
        public int Id { get; set; }

        public List<string>? Items { get; set; }
    }

    public class ZwItemsDerived : ZwItemsBase
    {
        public new List<ZwPayCard>? Items { get; set; }
    }

    public sealed class ReviewGateTests
    {
        private readonly ITestOutputHelper _out;

        public ReviewGateTests(ITestOutputHelper output) => _out = output;

        private static PolicyQueryable<T> GuardWith<T>(IQueryable<T> source, DwTier tier, params IDwPolicyProvider[] more) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }.Concat(more).ToArray()));

        private object? Read<T>(string label, IQueryable<T> source, DwTier tier, Filter? filter = null, bool dynamic = false, params IDwPolicyProvider[] more) where T : class
        {
            PolicyQueryable<T> guarded = GuardWith(source, tier, more);
            object? data = null;
            string code = ZwKit.Code(() => data = dynamic ? guarded.ToListDynamic(filter ?? new Filter()).Data : guarded.ToList(filter ?? new Filter()).Data);

            _out.WriteLine($"{label} {tier} dynamic={dynamic}: code={code} sent={ZwKit.Json(data)} trace=[{ZwKit.Trace(guarded)}]");

            return data;
        }

        // ------------------------------------------------------------------ O: OwnsMembers on everyday list subclasses

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zw_O1_a_list_of_strings_subclass_with_a_plain_member_is_kept_beside_a_top_level_denial(DwTier tier)
        {
            ZwLabelRow[] rows =
            {
                new() { Id = 1, Name = "r1", Ssn = "zw-ssn-secret", Labels = new ZwLabelSet { "a", "b" }, Names = new ZwPagedNames { "x", "y" } }
            };
            rows[0].Labels.Note = "note";

            object? data = Read("O1", rows.AsQueryable(), tier);
            List<ZwLabelRow> sent = Assert.IsType<List<ZwLabelRow>>(data);

            Assert.False(ZwKit.Holds(data, "zw-ssn-secret"));
            Assert.Equal(2, sent[0].Labels.Count);
            Assert.Equal(2, sent[0].Names.Count);
        }

        [Fact]
        public void Zw_O2_under_a_star_deny_a_list_of_strings_subclass_granted_by_name_is_kept()
        {
            ZwPlainLabelRow[] rows = { new() { Id = 1, Name = "r1", Labels = new ZwLabelSet { "a", "b" } } };

            FakePolicyProvider rules = new FakePolicyProvider()
                .Add("*", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicGlobal)
                .Add("Id", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicGlobal)
                .Add("Name", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicGlobal)
                .Add("Labels", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicGlobal);

            object? whole = Read("O2 none", rows.AsQueryable(), DwTier.Convenience, null, false, rules);
            object? named = Read("O2 named", rows.AsQueryable(), DwTier.Convenience, new Filter { Selects = new List<string> { "Id", "Labels" } }, false, rules);

            List<ZwPlainLabelRow> a = Assert.IsType<List<ZwPlainLabelRow>>(whole);
            List<ZwPlainLabelRow> b = Assert.IsType<List<ZwPlainLabelRow>>(named);

            Assert.Equal(2, a[0].Labels.Count);
            Assert.Equal(2, b[0].Labels.Count);
        }

        // ------------------------------------------------------------------ K: an application collection's own members, one level removed

        [Theory]
        [InlineData(DwTier.Strict, false)]
        [InlineData(DwTier.Convenience, false)]
        [InlineData(DwTier.Strict, true)]
        public void Zw_K1_a_list_and_an_array_of_a_collection_class_with_a_denied_member(DwTier tier, bool dynamic)
        {
            ZwSecretTags tags = new() { "a" };
            tags.OwnerSecret = "zw-nested-owner-secret";

            ZwNestedTagRow[] rows = { new() { Id = 1, Name = "r1", Sets = new List<ZwSecretTags> { tags }, Arr = new[] { tags } } };

            object? data = Read("K1", rows.AsQueryable(), tier, null, dynamic);

            Assert.False(ZwKit.Holds(data, "zw-nested-owner-secret"));
        }

        // ------------------------------------------------------------------ S: shared names

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zw_S1_two_value_members_differing_only_in_case_the_denied_one_is_withheld(DwTier tier)
        {
            ZwCaseRow[] rows = { new() { Id = 1, Code = "open", CODE = "zw-case-secret" } };

            object? data = Read("S1", rows.AsQueryable(), tier);

            Assert.False(ZwKit.Holds(data, "zw-case-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zw_S2_a_denied_value_member_hidden_with_new_by_another_value(DwTier tier)
        {
            ZwHideValueDerived row = new() { Id = 1, Code = "open" };
            ((ZwHideValueBase)row).Code = "zw-hidden-base-secret";

            object? data = Read("S2", new[] { row }.AsQueryable(), tier);

            Assert.False(ZwKit.Holds(data, "zw-hidden-base-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zw_S3_a_list_of_values_hidden_by_a_list_of_cards(DwTier tier)
        {
            ZwItemsDerived row = new() { Id = 1, Items = new List<ZwPayCard> { new() { Label = "visa", Pan = "zw-items-pan-secret" } } };
            ((ZwItemsBase)row).Items = new List<string> { "a" };

            object? data = Read("S3", new[] { row }.AsQueryable(), tier);
            object? named = Read("S3 named", new[] { row }.AsQueryable(), tier, new Filter { Selects = new List<string> { "Id", "Items" } });

            Assert.False(ZwKit.Holds(data, "zw-items-pan-secret"));
            Assert.False(ZwKit.Holds(named, "zw-items-pan-secret"));
        }
    }
}
