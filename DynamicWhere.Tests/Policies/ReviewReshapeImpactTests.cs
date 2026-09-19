using System.Linq.Expressions;
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

// What the needless projection behind a reshaping lambda costs on shapes other than a plain entity: an abstract
// hierarchy, a concrete root with derived rows, and a constructor-bound entity. The only denial (ZyiKey.Secret) sits
// beneath Owner.Keys, which nothing loads. Each test asserts the rows come back as the unguarded query returns them.

namespace DynamicWhere.Tests.Policies
{
    public class ZyiOwner
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<ZyiKey> Keys { get; set; } = new();

        public List<ZyiPayment> Payments { get; set; } = new();

        public List<ZyiDoc> Docs { get; set; } = new();

        public List<ZyiMember> Members { get; set; } = new();

        public List<ZyiInvoice> Invoices { get; set; } = new();
    }

    /// <summary>DDD style: a private constructor for EF Core, a public one for the domain, private setters.</summary>
    public class ZyiInvoice
    {
        private ZyiInvoice()
        {
        }

        public ZyiInvoice(string number) => Number = number;

        public int Id { get; private set; }

        public string Number { get; private set; } = string.Empty;

        public int ZyiOwnerId { get; private set; }

        public ZyiOwner? Owner { get; private set; }
    }

    public class ZyiKey
    {
        public int Id { get; set; }

        public int ZyiOwnerId { get; set; }

        [DwDenied]
        public string? Secret { get; set; }
    }

    public abstract class ZyiPayment
    {
        public int Id { get; set; }

        public long Cents { get; set; }

        public int ZyiOwnerId { get; set; }

        public ZyiOwner? Owner { get; set; }
    }

    public class ZyiCardPayment : ZyiPayment
    {
        public string Last4 { get; set; } = string.Empty;
    }

    public class ZyiCashPayment : ZyiPayment
    {
        public string Till { get; set; } = string.Empty;
    }

    public class ZyiDoc
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public int ZyiOwnerId { get; set; }

        public ZyiOwner? Owner { get; set; }
    }

    public class ZyiRichDoc : ZyiDoc
    {
        public int Pages { get; set; }
    }

    /// <summary>Constructor-bound: EF Core binds it, and it has no parameterless constructor.</summary>
    public class ZyiMember
    {
        public ZyiMember(int id, string name, int zyiOwnerId)
        {
            Id = id;
            Name = name;
            ZyiOwnerId = zyiOwnerId;
        }

        public int Id { get; private set; }

        public string Name { get; private set; }

        public int ZyiOwnerId { get; private set; }

        public ZyiOwner? Owner { get; private set; }
    }

    public static class ZyiSpecs
    {
        public static readonly Expression<Func<ZyiPayment, bool>> Paid = p => p.Cents > 0;

        public static readonly Expression<Func<ZyiDoc, bool>> Titled = d => d.Title != string.Empty;

        public static readonly Expression<Func<ZyiMember, bool>> Named = m => m.Name != string.Empty;

        public static readonly Expression<Func<ZyiInvoice, bool>> Numbered = i => i.Number != string.Empty;
    }

    public sealed class ZyiContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZyiContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZyiOwner> Owners => Set<ZyiOwner>();

        public DbSet<ZyiPayment> Payments => Set<ZyiPayment>();

        public DbSet<ZyiDoc> Docs => Set<ZyiDoc>();

        public DbSet<ZyiMember> Members => Set<ZyiMember>();

        /// <summary>A queryable function, as EF Core maps a table-valued function (not mapped here; only its shape is read).</summary>
        public IQueryable<ZyiDoc> DocsOf(int ownerId) => Docs.Where(d => d.ZyiOwnerId == ownerId);

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<ZyiCardPayment>();
            model.Entity<ZyiCashPayment>();
            model.Entity<ZyiRichDoc>();
            model.Entity<ZyiMember>().HasOne(m => m.Owner).WithMany(o => o.Members).HasForeignKey(m => m.ZyiOwnerId);
        }
    }

    public sealed class ReviewReshapeImpactTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZyiContext _db;

        public ReviewReshapeImpactTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZyiContext(_connection);
            _db.Database.EnsureCreated();

            ZyiOwner owner = new()
            {
                Name = "W1",
                Keys = { new ZyiKey { Secret = "zyi-secret" } },
                Payments = { new ZyiCardPayment { Cents = 100, Last4 = "4242" }, new ZyiCashPayment { Cents = 200, Till = "T1" } },
                Docs = { new ZyiDoc { Title = "plain" }, new ZyiRichDoc { Title = "rich", Pages = 3 } },
                Invoices = { new ZyiInvoice("INV-1") }
            };

            _db.Owners.Add(owner);
            _db.SaveChanges();
            _db.Members.Add(new ZyiMember(0, "M1", owner.Id));
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = DwTier.Strict, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private string Typed<T>(string label, IQueryable<T> source) where T : class
        {
            string unguarded = JsonSerializer.Serialize(source.AsNoTracking().ToList().Select(r => r.GetType().Name));
            PolicyQueryable<T> guarded = Guard(source);
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

            ZyLog.Write(_out, $"I {label}: {ZyLog.Shape(source.Expression)} unguarded types={unguarded} guarded {outcome} dropped=[{ZyLog.Decisions(guarded)}]");

            return outcome;
        }

        [Fact]
        public void Zy_I1_control_abstract_rows_through_a_navigation_collection()
        {
            string outcome = Typed("I1 control SelectMany(o => o.Payments)", _db.Owners.SelectMany(o => o.Payments));

            Assert.StartsWith("OK", outcome);
            Assert.Contains("ZyiCardPayment", outcome);
        }

        [Fact]
        public void Zy_I2_abstract_rows_through_a_navigation_collection_with_a_specification()
        {
            string outcome = Typed("I2 SelectMany(o => o.Payments.AsQueryable().Where(spec))", _db.Owners.SelectMany(o => o.Payments.AsQueryable().Where(ZyiSpecs.Paid)));

            Assert.StartsWith("OK", outcome);
            Assert.Contains("ZyiCardPayment", outcome);
        }

        [Fact]
        public void Zy_I3_control_derived_rows_of_a_concrete_root()
        {
            string outcome = Typed("I3 control SelectMany(o => o.Docs)", _db.Owners.SelectMany(o => o.Docs));

            Assert.Contains("ZyiRichDoc", outcome);
        }

        [Fact]
        public void Zy_I4_derived_rows_of_a_concrete_root_with_a_specification()
        {
            string outcome = Typed("I4 SelectMany(o => o.Docs.AsQueryable().Where(spec))", _db.Owners.SelectMany(o => o.Docs.AsQueryable().Where(ZyiSpecs.Titled)));

            Assert.Contains("ZyiRichDoc", outcome);
        }

        [Fact]
        public void Zy_I5_control_a_constructor_bound_entity()
        {
            string outcome = Typed("I5 control SelectMany(o => o.Members)", _db.Owners.SelectMany(o => o.Members));

            Assert.StartsWith("OK", outcome);
        }

        [Fact]
        public void Zy_I6_a_constructor_bound_entity_with_a_specification()
        {
            string outcome = Typed("I6 SelectMany(o => o.Members.AsQueryable().Where(spec))", _db.Owners.SelectMany(o => o.Members.AsQueryable().Where(ZyiSpecs.Named)));

            Assert.StartsWith("OK", outcome);
        }

        [Fact]
        public void Zy_I9_control_a_DDD_aggregate_with_a_private_constructor()
        {
            string outcome = Typed("I9 control SelectMany(o => o.Invoices)", _db.Owners.SelectMany(o => o.Invoices));

            Assert.StartsWith("OK", outcome);
            Assert.Contains("INV-1", outcome);
        }

        [Fact]
        public void Zy_I10_a_DDD_aggregate_with_a_private_constructor_and_a_specification()
        {
            string outcome = Typed("I10 SelectMany(o => o.Invoices.AsQueryable().Where(spec))", _db.Owners.SelectMany(o => o.Invoices.AsQueryable().Where(ZyiSpecs.Numbered)));

            Assert.StartsWith("OK", outcome);
            Assert.Contains("INV-1", outcome);
        }

        [Fact]
        public void Zy_I11_a_DDD_aggregate_through_a_left_join_in_query_syntax()
        {
            IQueryable<ZyiInvoice> source =
                from o in _db.Owners
                join i in _db.Set<ZyiInvoice>() on o.Id equals i.ZyiOwnerId into invoices
                from i in invoices.DefaultIfEmpty()
                select i;

            string outcome = Typed("I11 left join to a DDD aggregate", source);

            Assert.StartsWith("OK", outcome);
            Assert.Contains("INV-1", outcome);
        }

        [Fact]
        public void Zy_I7_a_queryable_function_on_the_context_is_read_as_building()
        {
            // Shape only: EF Core maps such a method to a table-valued function; SQLite cannot run one.
            IQueryable<ZyiDoc> source = _db.Owners.SelectMany(o => _db.DocsOf(o.Id));
            string shape = ZyLog.Shape(source.Expression);

            ZyLog.Write(_out, $"I I7 SelectMany(o => _db.DocsOf(o.Id)) (shape only): {shape}");

            Assert.DoesNotContain("builds=True", shape);
        }

    }
}
