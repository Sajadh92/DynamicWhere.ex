using System.Collections;
using System.Reflection;
using System.Text.Json;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ============================================================================ converted columns holding policed types

    /// <summary>The denial is declared on the interface member, which applies to the class's member too.</summary>
    public interface IZvVaultCard
    {
        [DwDenied]
        string? Pan { get; }
    }

    public class ZvVaultCard : IZvVaultCard
    {
        public string Label { get; set; } = string.Empty;

        public string? Pan { get; set; }
    }

    /// <summary>Control: the same card with the attribute on its own member.</summary>
    public class ZvVaultCardDirect
    {
        public string Label { get; set; } = string.Empty;

        [DwDenied]
        public string? Pan { get; set; }
    }

    public class ZvVault
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwDenied]
        public string? Note { get; set; }

        public Dictionary<string, ZvVaultCard> Cards { get; set; } = new();

        public Dictionary<string, ZvVaultCardDirect> DirectCards { get; set; } = new();
    }

    public sealed class ZvConvertedContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZvConvertedContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZvVault> Vaults => Set<ZvVault>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<ZvVault>().Property(v => v.Cards).HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                text => JsonSerializer.Deserialize<Dictionary<string, ZvVaultCard>>(text, (JsonSerializerOptions?)null)!);
            model.Entity<ZvVault>().Property(v => v.DirectCards).HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                text => JsonSerializer.Deserialize<Dictionary<string, ZvVaultCardDirect>>(text, (JsonSerializerOptions?)null)!);
        }
    }

    public sealed class ReviewConvertedColumnTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZvConvertedContext _db;

        public ReviewConvertedColumnTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZvConvertedContext(_connection);
            _db.Database.EnsureCreated();
            _db.Vaults.Add(new ZvVault
            {
                Name = "V1",
                Note = "note-secret",
                Cards = { ["a"] = new ZvVaultCard { Label = "visa", Pan = "iface-column-pan" } },
                DirectCards = { ["b"] = new ZvVaultCardDirect { Label = "mc", Pan = "direct-column-pan" } }
            });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        /// <summary>
        /// 3.1.0 left every non-scalar member out once Note asked for a projection. The column is now kept whole when
        /// the policy reads nothing denied in it; a denial declared on the interface member is not read there.
        /// </summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zv_F1_a_converted_column_holding_a_type_whose_interface_denies_a_member(DwTier tier)
        {
            PolicyQueryable<ZvVault> guarded = Guard(_db.Vaults, tier);
            string sent;

            try
            {
                sent = JsonSerializer.Serialize(guarded.ToList(new Filter()).Data);
            }
            catch (PolicyException refusal) when (refusal.ErrorCode == PolicyErrorCode.FieldDeniedForSelect)
            {
                sent = "refused";
            }

            _out.WriteLine("sent: " + sent + " decisions: "
                           + string.Join(" | ", guarded.LastTrace?.Decisions.Where(d => d.Action != PolicyAction.Allowed)
                               .Select(d => $"{d.FieldPath} {d.Action}") ?? Array.Empty<string>()));

            Assert.DoesNotContain("note-secret", sent);
            Assert.DoesNotContain("direct-column-pan", sent);
            Assert.DoesNotContain("iface-column-pan", sent);
        }
    }
}
