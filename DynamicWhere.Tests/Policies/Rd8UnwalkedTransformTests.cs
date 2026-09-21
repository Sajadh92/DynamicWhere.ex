using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Masking;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;

namespace DynamicWhere.Tests.Policies
{
    public class Rd8M1 { public int Id { get; set; } public Rd8M2? B { get; set; } public Rd8M5? Near { get; set; } }

    /// <summary>The same two members the other way round, so the far one is reached first.</summary>
    public class Rd8M1Far { public int Id { get; set; } public Rd8M2? B { get; set; } public Rd8M5? Near { get; set; } public Rd8M5? Last { get; set; } }

    public class Rd8M1Near { public int Id { get; set; } public Rd8M5? Near { get; set; } public Rd8M2? B { get; set; } }

    public class Rd8M2 { public int Id { get; set; } public Rd8M3? C { get; set; } }

    public class Rd8M3 { public int Id { get; set; } public Rd8M4? D { get; set; } }

    public class Rd8M4
    {
        public int Id { get; set; }

        public Rd8M5? E { get; set; }

        [DwMask(MaskStrategy.Full)]
        public string? Pin { get; set; }
    }

    public class Rd8M5
    {
        public int Id { get; set; }

        [DwMask(MaskStrategy.Full)]
        public string? Card { get; set; }

        [DwMask(MaskStrategy.Hash)]
        public string? Token { get; set; }
    }

    public class Rd8Animal { public int Id { get; set; } public string? Name { get; set; } public Rd8Tag? Tag { get; set; } }

    public class Rd8Dog : Rd8Animal
    {
        [DwMask(MaskStrategy.Full)]
        public string? Chip { get; set; }
    }

    public class Rd8Tag { public int Id { get; set; } }

    public class Rd8GoldTag : Rd8Tag
    {
        [DwMask(MaskStrategy.Full)]
        public string? Serial { get; set; }
    }

    public class Rd8Wallet { public int Id { get; set; } public Dictionary<string, Rd8Pan> Cards { get; set; } = new(); }

    public class Rd8Pan
    {
        [DwMask(MaskStrategy.Full)]
        public string? Number { get; set; }
    }

    /// <summary>Nothing in what a row of this type can hold declares a transform.</summary>
    public class Rd8Plain { public int Id { get; set; } public Rd8PlainChild? Child { get; set; } }

    public class Rd8PlainChild { public int Id { get; set; } public string? Name { get; set; } }

    /// <summary>One navigation leads to a transform and one cannot; reading the second throws.</summary>
    public class Rd8Careful
    {
        public int Id { get; set; }

        public Rd8Dog? Pet { get; set; }

        public Rd8PlainChild? Untouched => throw new InvalidOperationException("read a navigation that leads to no transform");
    }

    public class Rd8Frozen { public int Id { get; set; } public Rd8FrozenTag? Tag { get; set; } }

    public class Rd8FrozenTag { public int Id { get; set; } }

    public class Rd8FrozenGold : Rd8FrozenTag
    {
        [DwMask(MaskStrategy.Full)]
        public string Serial => "SER-1";
    }

    /// <summary>
    /// A transform is applied along the paths the attribute walk names: the declared types, four segments
    /// deep. A value that sits where none of them goes, on a member only a subtype of the row declares,
    /// five segments down, on an object a dictionary holds, came back exactly as stored while the same
    /// member four segments up was masked. The rows are walked by run-time type as well, and a member
    /// that declares a transform where no path reaches is transformed by its own attributes.
    /// </summary>
    public sealed class Rd8UnwalkedTransformTests
    {
        private const string Salt = "pepper-and-more-pepper";

        private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyQueryable<T> Guarded<T>(IEnumerable<T> rows, DwTier tier) where T : class =>
            rows.AsQueryable().ApplyPolicy(
                Caller(),
                new DwPolicyOptions { Tier = tier, HashSalt = Salt, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static Rd8M1 Deep() => new()
        {
            Id = 1,
            B = new() { Id = 2, C = new() { Id = 3, D = new() { Id = 4, Pin = "9999", E = new() { Id = 5, Card = "4111111111111111" } } } }
        };

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_masked_member_five_segments_down_is_masked(DwTier tier)
        {
            Rd8M1 whole = Guarded(new[] { Deep() }, tier).ToList(new Filter()).Data!.Single();

            Assert.Equal("****", whole.B!.C!.D!.Pin);
            Assert.Equal("****************", whole.B.C.D.E!.Card);

            Rd8M1 named = Guarded(new[] { Deep() }, tier).ToList(new Filter { Selects = new() { "Id", "B" } }).Data!.Single();

            Assert.Equal("****************", named.B!.C!.D!.E!.Card);

            string dynamic = System.Text.Json.JsonSerializer.Serialize(
                Guarded(new[] { Deep() }, tier).ToListDynamic(new Filter { Selects = new() { "Id", "B.C.D.E" } }).Data);

            Assert.DoesNotContain("4111", dynamic);
            Assert.Contains("****************", dynamic);
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_masked_member_only_a_subtype_declares_is_masked(DwTier tier)
        {
            Rd8Animal[] rows = { new Rd8Dog { Id = 1, Name = "rex", Chip = "CHIP-123", Tag = new Rd8GoldTag { Id = 9, Serial = "SER-999" } } };

            Rd8Dog dog = Assert.IsType<Rd8Dog>(Guarded(rows, tier).ToList(new Filter()).Data!.Single());

            Assert.Equal("********", dog.Chip);
            Assert.Equal("*******", Assert.IsType<Rd8GoldTag>(dog.Tag).Serial);
            Assert.Equal("rex", dog.Name);
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_masked_member_of_an_object_a_dictionary_holds_is_masked(DwTier tier)
        {
            Rd8Wallet[] rows = { new() { Id = 1, Cards = { ["main"] = new Rd8Pan { Number = "5500" } } } };

            Rd8Wallet wallet = Guarded(rows, tier).ToList(new Filter()).Data!.Single();

            Assert.Equal("****", wallet.Cards["main"].Number);
        }

        /// <summary>
        /// One object reached twice, by a path the policy names and by one it does not, is transformed
        /// once: the digest of a digest is not the stand-in the first pass issued.
        /// </summary>
        [Fact]
        public void An_object_reached_by_a_named_path_and_an_unnamed_one_is_transformed_once()
        {
            // What the first pass alone makes of it, which the second has no part in.
            Rd8M1 alone = new() { Id = 1, Near = new Rd8M5 { Id = 5, Token = "tok" } };
            DwPolicyOptions posture = new() { Tier = DwTier.Strict, HashSalt = Salt };

            GraphWalker.Apply(
                new[] { alone }, typeof(Rd8M1),
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }).ResolveType(typeof(Rd8M1), Caller()),
                projected: null, Caller(), posture, new DynamicWhere.ex.Policies.DTOs.PolicyTrace(DwTier.Strict, dryRun: false),
                attributes: false);

            string once = alone.Near.Token!;

            Assert.NotEqual("tok", once);

            // Whichever of the two members is declared first, and so whichever way the object is met first.
            Rd8M5 held = new() { Id = 5, Token = "tok" };
            Rd8M1Far far = new() { Id = 1, Near = held, B = new() { C = new() { D = new() { E = held } } } };

            Assert.Equal(once, Guarded(new[] { far }, DwTier.Strict).ToList(new Filter()).Data!.Single().Near!.Token);

            held = new() { Id = 5, Token = "tok" };
            Rd8M1Near near = new() { Id = 1, Near = held, B = new() { C = new() { D = new() { E = held } } } };

            Rd8M1Near row = Guarded(new[] { near }, DwTier.Strict).ToList(new Filter()).Data!.Single();

            Assert.Equal(once, row.Near!.Token);
            Assert.Same(row.Near, row.B!.C!.D!.E);
        }

        [Fact]
        public void A_model_that_declares_no_transform_pays_for_no_second_pass()
        {
            Assert.False(GraphWalker.HoldsTransform(typeof(Rd8Plain)));
            Assert.True(GraphWalker.HoldsTransform(typeof(Rd8M1)));
            Assert.True(GraphWalker.HoldsTransform(typeof(Rd8Animal)));
            Assert.True(GraphWalker.HoldsTransform(typeof(Rd8Wallet)));
        }

        /// <summary>
        /// A resolver built over no attribute provider reads no attribute, along a named path or off it:
        /// the second pass follows the first, rather than enforcing what the caller left out.
        /// </summary>
        [Fact]
        public void A_resolver_that_reads_no_attributes_transforms_nothing_by_attribute()
        {
            Rd8M1 row = new[] { Deep() }.AsQueryable()
                .ApplyPolicy(
                    Caller(),
                    new DwPolicyOptions { Tier = DwTier.Strict, HashSalt = Salt },
                    new PolicyResolver(new IDwPolicyProvider[] { new FakePolicyProvider() }))
                .ToList(new Filter()).Data!.Single();

            Assert.Equal("9999", row.B!.C!.D!.Pin);
            Assert.Equal("4111111111111111", row.B.C.D.E!.Card);
        }

        /// <summary>
        /// Only what can lead to a transform is read. A navigation whose type reaches none is never
        /// touched, so a lazy loader behind one is not woken by a pass that had nothing to do there.
        /// </summary>
        [Fact]
        public void A_navigation_that_leads_to_no_transform_is_never_read()
        {
            Rd8Careful[] rows = { new() { Id = 1, Pet = new Rd8Dog { Chip = "CHIP-123" } } };

            Rd8Careful row = Guarded(rows, DwTier.Strict).ToList(new Filter()).Data!.Single();

            Assert.Equal("********", row.Pet!.Chip);
        }

        /// <summary>As along a named path: a transform that cannot replace the value fails the query.</summary>
        [Fact]
        public void A_transformed_member_with_no_setter_fails_the_query_rather_than_passing_the_value()
        {
            Rd8Frozen[] rows = { new() { Id = 1, Tag = new Rd8FrozenGold { Id = 2 } } };

            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
                () => Guarded(rows, DwTier.Strict).ToList(new Filter()));

            Assert.Contains("Tag.Serial", failure.Message);
        }
    }
}
