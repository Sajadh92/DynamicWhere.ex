using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Source;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ============================================================================ an application's own generic collections: models

    /// <summary>An application collection of values that declares a member of its own.</summary>
    public class ZxTagSet : List<string>
    {
        [DwDenied]
        public string? OwnerSecret { get; set; }
    }

    public class ZxPlainCard
    {
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>An application collection of objects that declares a member of its own.</summary>
    public class ZxCardList : List<ZxPlainCard>
    {
        [DwDenied]
        public string? MasterKey { get; set; }
    }

    /// <summary>An application dictionary that declares a member of its own.</summary>
    public class ZxPropertyBag : Dictionary<string, string>
    {
        [DwDenied]
        public string? BagSecret { get; set; }
    }

    public class ZxBagRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public ZxTagSet Tags { get; set; } = new();
    }

    public class ZxListRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public ZxCardList Cards { get; set; } = new();
    }

    public class ZxPropsRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public ZxPropertyBag Props { get; set; } = new();
    }

    /// <summary>The same, beside a top-level denial, so a projection is built for another reason.</summary>
    public class ZxBagRowDenied
    {
        public int Id { get; set; }

        [DwDenied]
        public string? Ssn { get; set; }

        public ZxTagSet Tags { get; set; } = new();

        public ZxCardList Cards { get; set; } = new();
    }

    // ============================================================================ an application's own generic collections: tests

    /// <summary>
    /// An application's own collection class, deriving from List&lt;string&gt;, List&lt;T&gt; or a Dictionary, declares
    /// members beside the elements it holds, and they are read as any type's are.
    /// </summary>
    public sealed class ReviewCollectionClassTests
    {
        private readonly ITestOutputHelper _out;

        public ReviewCollectionClassTests(ITestOutputHelper output) => _out = output;

        private object? Read<T>(IQueryable<T> source, DwTier tier, bool dynamic = false) where T : class
        {
            PolicyQueryable<T> guarded = ZxKit.Guard(source, tier);
            object? data = ZxKit.SafeRead(() => dynamic ? guarded.ToListDynamic(new Filter()).Data : guarded.ToList(new Filter()).Data);

            _out.WriteLine($"{typeof(T).Name} {tier} dynamic={dynamic}: sent={ZxKit.Json(data)} trace=[{ZxKit.Trace(guarded)}]");

            return data;
        }

        [Theory]
        [InlineData(DwTier.Strict, false)]
        [InlineData(DwTier.Convenience, false)]
        [InlineData(DwTier.Strict, true)]
        public void Zx_K1_a_collection_of_values_that_declares_a_denied_member(DwTier tier, bool dynamic)
        {
            ZxBagRow[] rows = { new() { Id = 1, Name = "r1", Tags = new ZxTagSet { "a", "b" } } };
            rows[0].Tags.OwnerSecret = "tagset-owner-secret";

            object? data = Read(rows.AsQueryable(), tier, dynamic);

            Assert.False(ZxKit.Holds(data, "tagset-owner-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict, false)]
        [InlineData(DwTier.Convenience, false)]
        [InlineData(DwTier.Strict, true)]
        public void Zx_K2_a_collection_of_objects_that_declares_a_denied_member(DwTier tier, bool dynamic)
        {
            ZxListRow[] rows = { new() { Id = 1, Name = "r1", Cards = new ZxCardList { new() { Label = "visa" } } } };
            rows[0].Cards.MasterKey = "cardlist-master-secret";

            object? data = Read(rows.AsQueryable(), tier, dynamic);

            Assert.False(ZxKit.Holds(data, "cardlist-master-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_K3_a_dictionary_that_declares_a_denied_member(DwTier tier)
        {
            ZxPropsRow[] rows = { new() { Id = 1, Name = "r1", Props = new ZxPropertyBag { ["k"] = "v" } } };
            rows[0].Props.BagSecret = "bag-own-secret";

            object? data = Read(rows.AsQueryable(), tier);

            Assert.False(ZxKit.Holds(data, "bag-own-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_K4_beside_a_top_level_denial_the_projection_keeps_the_collection(DwTier tier)
        {
            ZxBagRowDenied[] rows =
            {
                new() { Id = 1, Ssn = "ssn-secret", Tags = new ZxTagSet { "a" }, Cards = new ZxCardList { new() { Label = "visa" } } }
            };
            rows[0].Tags.OwnerSecret = "tagset-owner-secret-2";
            rows[0].Cards.MasterKey = "cardlist-master-secret-2";

            object? data = Read(rows.AsQueryable(), tier);

            Assert.False(ZxKit.Holds(data, "ssn-secret"));
            Assert.False(ZxKit.Holds(data, "tagset-owner-secret-2"));
            Assert.False(ZxKit.Holds(data, "cardlist-master-secret-2"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_K5_naming_the_collection_of_values_in_selects(DwTier tier)
        {
            ZxBagRow[] rows = { new() { Id = 1, Name = "r1", Tags = new ZxTagSet { "a" } } };
            rows[0].Tags.OwnerSecret = "tagset-owner-secret-3";

            PolicyQueryable<ZxBagRow> guarded = ZxKit.Guard(rows.AsQueryable(), tier);
            object? data = ZxKit.SafeRead(() => guarded.ToList(ZxKit.Selecting("Id", "Tags")).Data);

            _out.WriteLine($"{tier}: sent={ZxKit.Json(data)} trace=[{ZxKit.Trace(guarded)}]");

            Assert.False(ZxKit.Holds(data, "tagset-owner-secret-3"));
        }
    }
}
