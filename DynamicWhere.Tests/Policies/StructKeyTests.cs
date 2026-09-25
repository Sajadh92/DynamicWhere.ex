using System.Text.Json;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests.Policies
{
    public struct ZkRef
    {
        public int Id { get; set; }

        public string Why { get; set; }
    }

    public struct ZkSealed
    {
        [DwDenied]
        public int Id { get; set; }

        public string Shown { get; set; }
    }

    public class ZkOwner
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class ZkEntity
    {
        public int Id { get; set; }

        public string Shown { get; set; } = string.Empty;
    }

    public sealed class ZkContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZkContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZkEntity> Items => Set<ZkEntity>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    public class ZkRow
    {
        public int Id { get; set; }

        public ZkRef Ref { get; set; }

        public ZkRef? Maybe { get; set; }

        public ZkSealed Sealed { get; set; }

        public ZkOwner? Owner { get; set; }
    }

    /// <summary>
    /// The key of a node a selection passes through. The projection builder adds the key of a class it
    /// builds and never of a struct, and the gate added it to both: a selection through a struct holding
    /// an <c>Id</c> came back with that key beside what was named, and one whose <c>Id</c> is denied had
    /// every other member's selection refused.
    /// </summary>
    public sealed class StructKeyTests
    {
        private static List<ZkRow> Source() => new()
        {
            new ZkRow
            {
                Id = 1,
                Ref = new ZkRef { Id = 7, Why = "why" },
                Maybe = new ZkRef { Id = 8, Why = "maybe" },
                Sealed = new ZkSealed { Id = 9, Shown = "shown" },
                Owner = new ZkOwner { Id = 5, Name = "owner" }
            }
        };

        private static PolicyQueryable<ZkRow> Guard(DwTier tier = DwTier.Strict) =>
            Source().AsQueryable().ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static Filter Selecting(params string[] fields) => new() { Selects = fields.ToList() };

        [Fact]
        public void A_selection_through_a_struct_carries_no_key_it_did_not_name()
        {
            ZkRow row = Guard().ToList(Selecting("Id", "Ref.Why", "Maybe.Value.Why")).Data[0];

            Assert.Equal("why", row.Ref.Why);
            Assert.Equal(0, row.Ref.Id);
            Assert.Equal("maybe", row.Maybe!.Value.Why);
            Assert.Equal(0, row.Maybe.Value.Id);

            string json = JsonSerializer.Serialize(Guard().ToListDynamic(Selecting("Id", "Ref.Why", "Maybe.Value.Why")).Data);

            Assert.Contains("\"Ref\":{\"Why\":\"why\"}", json, StringComparison.Ordinal);
            Assert.Contains("\"Value\":{\"Why\":\"maybe\"}", json, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_denied_key_of_a_struct_does_not_refuse_its_other_members(DwTier tier)
        {
            ZkRow row = Guard(tier).ToList(Selecting("Id", "Sealed.Shown")).Data[0];

            Assert.Equal("shown", row.Sealed.Shown);
            Assert.Equal(0, row.Sealed.Id);

            string json = JsonSerializer.Serialize(Guard(tier).ToListDynamic(Selecting("Id", "Sealed.Shown")).Data);

            Assert.Contains("\"Sealed\":{\"Shown\":\"shown\"}", json, StringComparison.Ordinal);
        }

        [Fact]
        public void A_struct_whose_key_is_denied_is_narrowed_when_nothing_is_selected()
        {
            // Rows projected before ApplyPolicy, as a consumer's are: in memory an object member is left out
            // whole whenever something beneath it is denied.
            using SqliteConnection connection = new("DataSource=:memory:");
            connection.Open();

            using ZkContext db = new(connection);
            db.Database.EnsureCreated();
            db.Items.Add(new ZkEntity { Id = 9, Shown = "shown" });
            db.SaveChanges();

            IQueryable<ZkRow> rows = db.Items.AsNoTracking().Select(item => new ZkRow
            {
                Id = item.Id,
                Sealed = new ZkSealed { Id = item.Id, Shown = item.Shown }
            });

            FilterResult<ZkRow> result = rows.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = DwTier.Strict },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() })).ToList(new Filter());

            Assert.Equal("shown", result.Data[0].Sealed.Shown);
            Assert.Equal(0, result.Data[0].Sealed.Id);
        }

        [Fact]
        public void The_denied_key_itself_is_still_refused()
        {
            Assert.Throws<PolicyException>(() => Guard().ToList(Selecting("Id", "Sealed.Id")));
        }

        [Fact]
        public void A_class_navigation_still_carries_its_key()
        {
            string json = JsonSerializer.Serialize(Guard().ToListDynamic(Selecting("Id", "Owner.Name")).Data);

            Assert.Contains("\"Id\":5", json, StringComparison.Ordinal);
            Assert.Contains("\"Name\":\"owner\"", json, StringComparison.Ordinal);
        }
    }
}
