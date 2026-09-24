using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests.Policies
{
    public struct ZlPair
    {
        public string Shown { get; set; }

        [DwDenied]
        public string Hidden { get; set; }
    }

    public class ZlRow
    {
        public int Id { get; set; }

        public List<ZlPair> Pairs { get; set; } = new();

        public ZlPair[] Array { get; set; } = System.Array.Empty<ZlPair>();

        public List<ZlPair?> Maybe { get; set; } = new();

        public List<int> Numbers { get; set; } = new();
    }

    public class ZlOrder
    {
        public int Id { get; set; }

        public List<ZlLine> Lines { get; set; } = new();
    }

    public class ZlLine
    {
        public int Id { get; set; }

        public int ZlOrderId { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Secret { get; set; } = string.Empty;
    }

    public sealed class ZlContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZlContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZlOrder> Orders => Set<ZlOrder>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    /// <summary>
    /// A typed selection naming a path beneath a collection of structs. The typed projection bound
    /// every collection of values whole, so <c>Pairs.Shown</c> handed back each element whole, a
    /// <c>[DwDenied]</c> member included, in both tiers, while the policy had approved only
    /// <c>Pairs.Shown</c>. Present in every release since the policy layer shipped.
    /// </summary>
    public sealed class StructCollectionSelectTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ZlContext _db;

        public StructCollectionSelectTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZlContext(_connection);
            _db.Database.EnsureCreated();
            _db.Orders.Add(new ZlOrder
            {
                Lines = { new ZlLine { Code = "A", Secret = "S1" }, new ZlLine { Code = "B", Secret = "S2" } }
            });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static List<ZlRow> Source() => new()
        {
            new ZlRow
            {
                Id = 1,
                Pairs = { new ZlPair { Shown = "a", Hidden = "SECRET-A" }, new ZlPair { Shown = "b", Hidden = "SECRET-B" } },
                Array = new[] { new ZlPair { Shown = "x", Hidden = "SECRET-X" } },
                Maybe = { new ZlPair { Shown = "m", Hidden = "SECRET-M" }, null },
                Numbers = { 1, 2, 3 }
            }
        };

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static Filter Selecting(params string[] fields) => new() { Selects = fields.ToList() };

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_sibling_of_a_denied_member_comes_back_without_it(DwTier tier)
        {
            FilterResult<ZlRow> result = Guard(Source().AsQueryable(), tier).ToList(Selecting("Pairs.Shown"));

            Assert.Equal(new[] { "a", "b" }, result.Data[0].Pairs.Select(pair => pair.Shown));
            Assert.All(result.Data[0].Pairs, pair => Assert.Null(pair.Hidden));
        }

        [Fact]
        public void The_denied_member_itself_and_the_collection_whole_are_refused()
        {
            Assert.Throws<PolicyException>(() => Guard(Source().AsQueryable(), DwTier.Strict).ToList(Selecting("Pairs.Hidden")));
            Assert.Throws<PolicyException>(() => Guard(Source().AsQueryable(), DwTier.Strict).ToList(Selecting("Pairs")));
        }

        [Fact]
        public void An_array_of_structs_is_built_element_by_element_too()
        {
            List<ZlRow> rows = Source().AsQueryable().Select(new List<string> { "Array.Shown" }).ToList();

            ZlPair only = Assert.Single(rows[0].Array);

            Assert.Equal("x", only.Shown);
            Assert.Null(only.Hidden);
        }

        [Fact]
        public void A_collection_of_nullable_structs_carries_nothing_rather_than_everything()
        {
            List<ZlRow> rows = Source().AsQueryable().Select(new List<string> { "Maybe.Value.Shown", "Id" }).ToList();

            Assert.Empty(rows[0].Maybe);
            Assert.Equal(1, rows[0].Id);
        }

        [Fact]
        public void A_null_collection_in_memory_stays_null()
        {
            List<ZlRow> source = new() { new ZlRow { Id = 1, Pairs = null! } };

            List<ZlRow> rows = source.AsQueryable().Select(new List<string> { "Pairs.Shown", "Id" }).ToList();

            Assert.Null(rows[0].Pairs);
        }

        [Fact]
        public void A_collection_of_scalars_is_still_bound_whole()
        {
            FilterResult<ZlRow> result = Guard(Source().AsQueryable(), DwTier.Strict).ToList(Selecting("Numbers"));

            Assert.Equal(new[] { 1, 2, 3 }, result.Data[0].Numbers);
        }

        [Fact]
        public void A_projection_building_the_collection_in_the_database_is_narrowed_the_same_way()
        {
            IQueryable<ZlRow> rows = _db.Orders.Select(order => new ZlRow
            {
                Id = order.Id,
                Pairs = order.Lines.OrderBy(line => line.Id)
                    .Select(line => new ZlPair { Shown = line.Code, Hidden = line.Secret })
                    .ToList()
            });

            FilterResult<ZlRow> result = Guard(rows, DwTier.Strict).ToList(Selecting("Pairs.Shown"));

            Assert.Equal(new[] { "A", "B" }, result.Data[0].Pairs.Select(pair => pair.Shown));
            Assert.All(result.Data[0].Pairs, pair => Assert.Null(pair.Hidden));
        }
    }
}
