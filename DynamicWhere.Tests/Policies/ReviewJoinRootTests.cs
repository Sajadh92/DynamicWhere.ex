using System.Text.Json;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

// A join on a filtered set over roots a typed projection cannot build (DDD, constructor-bound, abstract): read from the model, not projected.
// The only denial (ZwKey.Secret) sits beneath Owner.Keys, which nothing loads.

namespace DynamicWhere.Tests.Policies
{
    public class ZwOwner
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<ZwKey> Keys { get; set; } = new();

        public List<ZwInvoice> Invoices { get; set; } = new();

        public List<ZwMember> Members { get; set; } = new();

        public List<ZwPayment> Payments { get; set; } = new();
    }

    public class ZwKey
    {
        public int Id { get; set; }

        public int ZwOwnerId { get; set; }

        [DwDenied]
        public string? Secret { get; set; }
    }

    /// <summary>DDD style: a private constructor for EF Core, a public one for the domain, private setters.</summary>
    public class ZwInvoice
    {
        private ZwInvoice()
        {
        }

        public ZwInvoice(string number) => Number = number;

        public int Id { get; private set; }

        public string Number { get; private set; } = string.Empty;

        public int ZwOwnerId { get; private set; }

        public ZwOwner? Owner { get; private set; }
    }

    /// <summary>Constructor-bound: EF Core binds it, and it has no parameterless constructor.</summary>
    public class ZwMember
    {
        public ZwMember(int id, string name, int zwOwnerId)
        {
            Id = id;
            Name = name;
            ZwOwnerId = zwOwnerId;
        }

        public int Id { get; private set; }

        public string Name { get; private set; }

        public int ZwOwnerId { get; private set; }

        public ZwOwner? Owner { get; private set; }
    }

    public abstract class ZwPayment
    {
        public int Id { get; set; }

        public long Cents { get; set; }

        public int ZwOwnerId { get; set; }

        public ZwOwner? Owner { get; set; }
    }

    public class ZwCardPayment : ZwPayment
    {
        public string Last4 { get; set; } = string.Empty;
    }

    public class ZwCashPayment : ZwPayment
    {
        public string Till { get; set; } = string.Empty;
    }

    public sealed class ZwOwnerContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZwOwnerContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZwOwner> Owners => Set<ZwOwner>();

        public DbSet<ZwPayment> Payments => Set<ZwPayment>();

        public DbSet<ZwMember> Members => Set<ZwMember>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<ZwCardPayment>();
            model.Entity<ZwCashPayment>();
            model.Entity<ZwMember>().HasOne(m => m.Owner).WithMany(o => o.Members).HasForeignKey(m => m.ZwOwnerId);
        }
    }

    public sealed class ReviewJoinRootTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZwOwnerContext _db;

        public ReviewJoinRootTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZwOwnerContext(_connection);
            _db.Database.EnsureCreated();

            ZwOwner owner = new()
            {
                Name = "W1",
                Keys = { new ZwKey { Secret = "zw-key-secret" } },
                Payments = { new ZwCardPayment { Cents = 100, Last4 = "4242" }, new ZwCashPayment { Cents = 200, Till = "T1" } },
                Invoices = { new ZwInvoice("INV-1") }
            };

            _db.Owners.Add(owner);
            _db.SaveChanges();
            _db.Members.Add(new ZwMember(0, "M1", owner.Id));
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private string Typed<T>(string label, IQueryable<T> source) where T : class
        {
            string unguarded = JsonSerializer.Serialize(source.AsNoTracking().ToList().Select(r => r.GetType().Name));
            PolicyQueryable<T> guarded = ZwKit.Guard(source);
            string outcome;

            _db.ChangeTracker.Clear();

            try
            {
                List<T> rows = guarded.ToList(new Filter()).Data;

                outcome = $"OK types={JsonSerializer.Serialize(rows.Select(r => r.GetType().Name))} json={JsonSerializer.Serialize<object>(rows)}";
            }
            catch (Exception e)
            {
                outcome = $"THREW {e.GetType().Name} {(e is PolicyException p ? p.ErrorCode.ToString() : string.Empty)} {e.Message.Split('\n')[0]}";
            }

            _out.WriteLine($"{label}: {ZwKit.Shape(source.Expression)} unguarded types={unguarded} guarded {outcome} trace=[{ZwKit.Trace(guarded)}]");

            return outcome;
        }

        [Fact]
        public void Zw_D0_control_a_DDD_aggregate_joined_on_a_bare_set()
        {
            string outcome = Typed("D0", _db.Owners.Join(_db.Set<ZwInvoice>(), o => o.Id, i => i.ZwOwnerId, (o, i) => i));

            Assert.StartsWith("OK", outcome);
            Assert.Contains("INV-1", outcome);
        }

        [Fact]
        public void Zw_D1_a_DDD_aggregate_joined_on_a_filtered_set()
        {
            string outcome = Typed("D1", _db.Owners.Join(_db.Set<ZwInvoice>().Where(i => i.Number != string.Empty), o => o.Id, i => i.ZwOwnerId, (o, i) => i));

            Assert.StartsWith("OK", outcome);
            Assert.Contains("INV-1", outcome);
        }

        [Fact]
        public void Zw_D2_a_constructor_bound_entity_joined_on_a_filtered_set()
        {
            string outcome = Typed("D2", _db.Owners.Join(_db.Members.Where(m => m.Name != string.Empty), o => o.Id, m => m.ZwOwnerId, (o, m) => m));

            Assert.StartsWith("OK", outcome);
        }

        [Fact]
        public void Zw_D3_abstract_rows_joined_on_a_filtered_set()
        {
            string outcome = Typed("D3", _db.Owners.Join(_db.Payments.Where(p => p.Cents > 0), o => o.Id, p => p.ZwOwnerId, (o, p) => p));

            Assert.StartsWith("OK", outcome);
            Assert.Contains("ZwCardPayment", outcome);
        }

        [Fact]
        public void Zw_D4_a_captured_filtered_query_as_the_inner_sequence()
        {
            IQueryable<ZwMember> named = _db.Members.Where(m => m.Name != string.Empty);

            string outcome = Typed("D4", _db.Owners.Join(named, o => o.Id, m => m.ZwOwnerId, (o, m) => m));

            Assert.StartsWith("OK", outcome);
        }
    }
}
