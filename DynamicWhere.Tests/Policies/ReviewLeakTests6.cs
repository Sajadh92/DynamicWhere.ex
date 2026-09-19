using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ---- P1c: a top-level denial forces the projection, and the owned member is still kept whole -----------

    public class ZrClinic2
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwDenied]
        public string? Notes { get; set; }

        public ZrContact Contact { get; set; } = new();
    }

    public sealed class ZrProbeContext6 : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZrProbeContext6(SqliteConnection connection) => _connection = connection;

        public DbSet<ZrClinic2> Clinics => Set<ZrClinic2>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model) =>
            model.Entity<ZrClinic2>().OwnsOne(c => c.Contact, o => o.Property<string?>("_phone"));
    }

    public sealed class ReviewLeakTests6 : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZrProbeContext6 _db;

        public ReviewLeakTests6(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZrProbeContext6(_connection);
            _db.Database.EnsureCreated();

            ZrClinic2 clinic = new() { Name = "C1", Notes = "notes-secret", Contact = new ZrContact { City = "Basra" } };
            clinic.Contact.SetPhone("phone-secret");
            _db.Clinics.Add(clinic);
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zr_P1c_an_owned_member_kept_whole_by_a_synthesized_projection(DwTier tier)
        {
            PolicyQueryable<ZrClinic2> guarded = _db.Clinics.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

            ZrClinic2 row = guarded.ToList(new Filter()).Data.Single();

            foreach (PolicyDecision decision in guarded.LastTrace!.Decisions)
            {
                _out.WriteLine($"trace: {decision.FieldPath} {decision.Feature} {decision.Action} {decision.Reason}");
            }

            // The projection ran: the top-level denial is withheld.
            Assert.Null(row.Notes);
            Assert.Equal("Basra", row.Contact.City);

            // And the owned member it kept whole carries the value its unmapped getter exposes.
            Assert.Null(row.Contact.Phone.Number);
        }
    }
}
