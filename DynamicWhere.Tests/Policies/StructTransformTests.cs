using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Masking;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>Appends a mark, so a value transformed twice reads differently from one transformed once.</summary>
    public sealed class ZtMark : IValueTransformer
    {
        public object? Transform(object? value, DwTransformContext context) => value is string text ? text + "!" : value;
    }

    public struct ZtPair
    {
        public string Shown { get; set; }

        [DwMask(MaskStrategy.Full)]
        public string Code { get; set; }

        [DwMutate(typeof(ZtMark))]
        public string Marked { get; set; }
    }

    public struct ZtOuter
    {
        public ZtPair Inner { get; set; }
    }

    public class ZtOwner
    {
        public ZtPair Name { get; set; }
    }

    public class ZtLevel1 { public ZtLevel2 Next { get; set; } = new(); }

    public class ZtLevel2 { public ZtLevel3 Next { get; set; } = new(); }

    public class ZtLevel3 { public ZtLevel4 Next { get; set; } = new(); }

    public class ZtLevel4 { public ZtPair Deep { get; set; } }

    public class ZtRow
    {
        public int Id { get; set; }

        public ZtPair Name { get; set; }

        public ZtPair? Maybe { get; set; }

        public List<ZtPair> Pairs { get; set; } = new();

        public ZtPair[] Array { get; set; } = System.Array.Empty<ZtPair>();

        public ZtOuter Outer { get; set; }

        public ZtOwner? Owner { get; set; }

        public ZtLevel1? First { get; set; }
    }

    public class ZtReadOnlyRow
    {
        public ZtReadOnlyRow(ZtPair name) => Name = name;

        public int Id { get; set; }

        public ZtPair Name { get; }
    }

    public class ZtSetRow
    {
        public int Id { get; set; }

        public HashSet<ZtPair> Pairs { get; set; } = new();
    }

    public class ZtBagRow
    {
        public int Id { get; set; }

        public Dictionary<string, ZtPair> Bag { get; set; } = new();
    }

    public class ZtEntity
    {
        public int Id { get; set; }

        public string Shown { get; set; } = string.Empty;

        public string Code { get; set; } = string.Empty;
    }

    public sealed class ZtContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZtContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZtEntity> Items => Set<ZtEntity>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    /// <summary>
    /// A transform declared on a struct's member. A struct is read as a copy in a box, and the setter
    /// unboxed a second copy to write into, so every mask on a struct's member was applied to a
    /// temporary and the stored value was emitted: in both tiers, from memory and from EF Core, since the
    /// policy layer shipped.
    /// </summary>
    public sealed class StructTransformTests : IDisposable
    {
        private const string Masked = "*";

        private readonly SqliteConnection _connection;
        private readonly ZtContext _db;

        public StructTransformTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZtContext(_connection);
            _db.Database.EnsureCreated();
            _db.Items.Add(new ZtEntity { Shown = "s", Code = "SECRET-EF" });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static ZtPair Pair(string code) => new() { Shown = "s", Code = code, Marked = "m" };

        private static List<ZtRow> Source() => new()
        {
            new ZtRow
            {
                Id = 1,
                Name = Pair("SECRET-NAME"),
                Maybe = Pair("SECRET-MAYBE"),
                Pairs = { Pair("SECRET-LIST-1"), Pair("SECRET-LIST-2") },
                Array = new[] { Pair("SECRET-ARRAY") },
                Outer = new ZtOuter { Inner = Pair("SECRET-OUTER") },
                Owner = new ZtOwner { Name = Pair("SECRET-OWNER") },
                First = new ZtLevel1 { Next = new ZtLevel2 { Next = new ZtLevel3 { Next = new ZtLevel4 { Deep = Pair("SECRET-DEEP") } } } }
            }
        };

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static void AllMasked(ZtRow row)
        {
            Assert.StartsWith(Masked, row.Name.Code);
            Assert.StartsWith(Masked, row.Maybe!.Value.Code);
            Assert.All(row.Pairs, pair => Assert.StartsWith(Masked, pair.Code));
            Assert.All(row.Array, pair => Assert.StartsWith(Masked, pair.Code));
            Assert.StartsWith(Masked, row.Outer.Inner.Code);
            Assert.StartsWith(Masked, row.Owner!.Name.Code);
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_mask_on_a_struct_member_applies_wherever_the_struct_is_held(DwTier tier)
        {
            FilterResult<ZtRow> result = Guard(Source().AsQueryable(), tier).ToList(new Filter());

            AllMasked(result.Data[0]);
            Assert.Equal("s", result.Data[0].Name.Shown);
        }

        [Fact]
        public void A_mask_on_a_struct_member_applies_when_the_struct_is_selected_whole()
        {
            FilterResult<ZtRow> result = Guard(Source().AsQueryable(), DwTier.Strict)
                .ToList(new Filter { Selects = new List<string> { "Name", "Maybe", "Pairs", "Array", "Outer", "Owner" } });

            AllMasked(result.Data[0]);
        }

        [Fact]
        public void A_transform_on_a_struct_member_is_applied_once()
        {
            // The pass along the policy's paths and the pass by run-time type both reach the member; the
            // second must not transform what the first did.
            ZtRow row = Guard(Source().AsQueryable(), DwTier.Strict).ToList(new Filter()).Data[0];

            Assert.Equal("m!", row.Name.Marked);
            Assert.Equal("m!", row.Maybe!.Value.Marked);
            Assert.All(row.Pairs, pair => Assert.Equal("m!", pair.Marked));
            Assert.Equal("m!", row.Outer.Inner.Marked);
            Assert.Equal("m!", row.Owner!.Name.Marked);
        }

        [Fact]
        public void A_struct_past_the_walk_depth_is_transformed_by_its_own_attributes_and_written_back()
        {
            ZtRow row = Guard(Source().AsQueryable(), DwTier.Strict).ToList(new Filter()).Data[0];

            ZtPair deep = row.First!.Next.Next.Next.Deep;

            Assert.StartsWith(Masked, deep.Code);
            Assert.Equal("m!", deep.Marked);
        }

        [Fact]
        public void The_source_rows_are_masked_in_place_as_class_members_are()
        {
            // A guarded query over rows in memory masks the caller's own objects, which is documented.
            List<ZtRow> source = Source();

            Guard(source.AsQueryable(), DwTier.Strict).ToList(new Filter());

            Assert.StartsWith(Masked, source[0].Name.Code);
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_struct_member_a_projection_builds_in_the_database_is_masked(DwTier tier)
        {
            IQueryable<ZtRow> rows = _db.Items.Select(item => new ZtRow
            {
                Id = item.Id,
                Name = new ZtPair { Shown = item.Shown, Code = item.Code },
                Maybe = new ZtPair { Shown = item.Shown, Code = item.Code }
            });

            ZtRow none = Guard(rows, tier).ToList(new Filter()).Data[0];
            ZtRow part = Guard(rows, tier).ToList(new Filter { Selects = new List<string> { "Name.Code", "Name.Shown" } }).Data[0];

            Assert.StartsWith(Masked, none.Name.Code);
            Assert.StartsWith(Masked, none.Maybe!.Value.Code);
            Assert.StartsWith(Masked, part.Name.Code);
            Assert.Equal("s", part.Name.Shown);
        }

        [Fact]
        public void A_struct_held_by_a_member_with_no_setter_fails_rather_than_escaping()
        {
            List<ZtReadOnlyRow> source = new() { new ZtReadOnlyRow(Pair("SECRET")) { Id = 1 } };

            InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
                () => Guard(source.AsQueryable(), DwTier.Strict).ToList(new Filter()));

            Assert.Contains("no setter", refusal.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_struct_only_the_run_time_walk_reaches_fails_when_it_cannot_be_written_back()
        {
            // No path of the policy names a dictionary's values; the run-time walk finds the mask, and a
            // pair's Value has no setter to take the masked struct back.
            List<ZtBagRow> source = new() { new ZtBagRow { Id = 1, Bag = { ["k"] = Pair("SECRET") } } };

            InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
                () => Guard(source.AsQueryable(), DwTier.Strict).ToList(new Filter()));

            Assert.Contains("cannot be written back", refusal.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Structs_in_a_collection_that_cannot_be_written_by_position_fail_rather_than_escaping()
        {
            List<ZtSetRow> source = new() { new ZtSetRow { Id = 1, Pairs = { Pair("SECRET") } } };

            InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(
                () => Guard(source.AsQueryable(), DwTier.Strict).ToList(new Filter()));

            Assert.Contains("list or an array", refusal.Message, StringComparison.Ordinal);
        }
    }
}
