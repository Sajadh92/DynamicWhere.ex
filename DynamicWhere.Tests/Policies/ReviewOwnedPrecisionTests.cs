using System.Text.Json;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

// A converted JSON bag inside an owned member, beside a top-level denial. A converter can hand back any object, so
// the owned member is narrowed around the bag: it comes back with its plain fields, and without the bag.

namespace DynamicWhere.Tests.Policies
{
    public class ZyoSettings
    {
        public string Theme { get; set; } = string.Empty;

        public Dictionary<string, object> Prefs { get; set; } = new();
    }

    public class ZyoAddress
    {
        public string City { get; set; } = string.Empty;

        public Dictionary<string, object> Extra { get; set; } = new();
    }

    public class ZyoProfile
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwDenied]
        public string? Secret { get; set; }

        public ZyoSettings Settings { get; set; } = new();

        public List<ZyoAddress> Addresses { get; set; } = new();
    }

    public sealed class ZyoContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZyoContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZyoProfile> Profiles => Set<ZyoProfile>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<ZyoProfile>().OwnsOne(p => p.Settings, s => s.Property(x => x.Prefs).HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                text => JsonSerializer.Deserialize<Dictionary<string, object>>(text, (JsonSerializerOptions?)null)!));
            model.Entity<ZyoProfile>().OwnsMany(p => p.Addresses, a =>
            {
                a.WithOwner().HasForeignKey("ZyoProfileId");
                a.Property<int>("Id");
                a.HasKey("Id");
                a.Property(x => x.Extra).HasConversion(
                    value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                    text => JsonSerializer.Deserialize<Dictionary<string, object>>(text, (JsonSerializerOptions?)null)!);
            });
        }
    }

    public sealed class ReviewOwnedPrecisionTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZyoContext _db;

        public ReviewOwnedPrecisionTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZyoContext(_connection);
            _db.Database.EnsureCreated();
            _db.Profiles.Add(new ZyoProfile
            {
                Name = "P1",
                Secret = "zyo-secret",
                Settings = new ZyoSettings { Theme = "dark", Prefs = { ["lang"] = "ar" } },
                Addresses = { new ZyoAddress { City = "Basra", Extra = { ["floor"] = 3 } } }
            });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private (ZyoProfile? Row, string Outcome) Run()
        {
            PolicyQueryable<ZyoProfile> guarded = _db.Profiles.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = DwTier.Strict, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

            try
            {
                ZyoProfile row = guarded.ToList(new Filter()).Data.Single();
                string outcome = $"OK json={JsonSerializer.Serialize(row)} dropped=[{ZyLog.Decisions(guarded)}]";

                ZyLog.Write(_out, "O owned: " + outcome);

                return (row, outcome);
            }
            catch (Exception e)
            {
                string outcome = $"THREW {e.GetType().Name} {e.Message.Split('\n')[0]} dropped=[{ZyLog.Decisions(guarded)}]";

                ZyLog.Write(_out, "O owned: " + outcome);

                return (null, outcome);
            }
        }

        [Fact]
        public void Zy_O1_an_owned_member_holding_a_converted_bag_keeps_its_plain_fields()
        {
            (ZyoProfile? row, string outcome) = Run();

            Assert.NotNull(row);
            Assert.Null(row!.Secret);
            Assert.Equal("dark", row.Settings?.Theme);
        }

        [Fact]
        public void Zy_O2_an_owned_member_holding_a_converted_bag_is_narrowed_around_the_bag()
        {
            (ZyoProfile? row, string outcome) = Run();

            Assert.NotNull(row);
            Assert.NotNull(row!.Settings);
            Assert.False(row.Settings!.Prefs.ContainsKey("lang"), outcome);
            Assert.Contains("Settings.Prefs Dropped: left out whole: it can hold what the policy cannot name", outcome);
        }

        [Fact]
        public void Zy_O3_an_owned_collection_holding_a_converted_bag_keeps_its_elements()
        {
            (ZyoProfile? row, string outcome) = Run();

            Assert.NotNull(row);
            Assert.Equal("Basra", Assert.Single(row!.Addresses).City);
        }

        [Fact]
        public void Zy_O4_an_owned_collection_holding_a_converted_bag_is_narrowed_around_the_bag()
        {
            (ZyoProfile? row, string outcome) = Run();

            Assert.NotNull(row);
            Assert.False(Assert.Single(row!.Addresses).Extra.ContainsKey("floor"), outcome);
        }
    }
}
