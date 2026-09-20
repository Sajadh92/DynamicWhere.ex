using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ---- complex types, which only EF Core 8 has ---------------------------------------------------------

    /// <summary>A complex type: its members are columns of the owner's table, and it has a getter too.</summary>
    public class OcxMoney
    {
        public decimal Amount { get; set; }

        public string Currency { get; set; } = string.Empty;

        /// <summary>A getter over two columns: no database can answer it.</summary>
        public bool IsFree => Amount == 0m;
    }

    /// <summary>A complex type nested inside another one.</summary>
    public class OcxAddress
    {
        public string City { get; set; } = string.Empty;

        public OcxPostCode Post { get; set; } = new();

        public string Full => $"{City} {Post.Code}";
    }

    public class OcxPostCode
    {
        public string Code { get; set; } = string.Empty;

        public bool IsBlank => string.IsNullOrEmpty(Code);
    }

    public class OcxInvoice
    {
        public int Id { get; set; }

        public string Number { get; set; } = string.Empty;

        public OcxMoney Total { get; set; } = new();

        public OcxAddress ShipTo { get; set; } = new();
    }

    /// <summary>A row a caller projects out of the invoice, complex members copied whole.</summary>
    public class OcxRow
    {
        public int Id { get; set; }

        public OcxMoney Total { get; set; } = new();

        public OcxAddress ShipTo { get; set; } = new();
    }

    public sealed class OcxContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public OcxContext(SqliteConnection connection) => _connection = connection;

        public DbSet<OcxInvoice> Invoices => Set<OcxInvoice>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<OcxInvoice>().ComplexProperty(invoice => invoice.Total);
            model.Entity<OcxInvoice>().ComplexProperty(
                invoice => invoice.ShipTo, ship => ship.ComplexProperty(address => address.Post));
        }
    }

    /// <summary>
    /// EF Core 8 shapes the floor leg has no API for: complex properties, which
    /// <c>RowShape.ExpressesInComplex</c> walks entirely through reflection, and the provider
    /// identity of a second relational provider and a pooled factory.
    /// </summary>
    public sealed class Ob4ComplexProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly OcxContext _db;
        private readonly List<string> _findings = new();

        public Ob4ComplexProbes(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new OcxContext(_connection);
            _db.Database.EnsureCreated();
            _db.Invoices.Add(new OcxInvoice
            {
                Number = "INV-1",
                Total = new OcxMoney { Amount = 10m, Currency = "IQD" },
                ShipTo = new OcxAddress { City = "Baghdad", Post = new OcxPostCode { Code = "10001" } }
            });
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
                new DwPolicyOptions { Tier = DwTier.Strict },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static string Guarded<T>(IQueryable<T> source, string field, DataType type, string value)
            where T : class
        {
            try
            {
                FilterResult<T> result = Guard(source).ToList(new Filter
                {
                    ConditionGroup = new ConditionGroup
                    {
                        Conditions =
                        {
                            new Condition
                            {
                                Field = field, DataType = type, Operator = Operator.Equal, Values = { value }
                            }
                        }
                    }
                });

                return $"OK({result.Data.Count})";
            }
            catch (PolicyException refusal)
            {
                return $"REFUSED({refusal.ErrorCode})";
            }
            catch (Exception failure)
            {
                return failure.GetType().Name;
            }
        }

        private static string Raw(Func<object> query)
        {
            try
            {
                object value = query();

                return value is System.Collections.ICollection rows ? $"OK({rows.Count})" : "OK";
            }
            catch (Exception failure)
            {
                return failure.GetType().Name;
            }
        }

        private void Case(string probe, string unguarded, string guarded)
        {
            _out.WriteLine($"{probe,-60} unguarded={unguarded,-26} guarded={guarded}");

            if (unguarded.StartsWith("OK", StringComparison.Ordinal)
                && guarded.StartsWith("REFUSED", StringComparison.Ordinal))
            {
                _findings.Add($"{probe}: unguarded {unguarded}, guarded {guarded}");
            }
        }

        private void Done() => Assert.True(_findings.Count == 0, string.Join(" || ", _findings));

        // =========================================================================================
        // Ob4-H. Complex properties: the reflection walk must find every column, or a member that
        //        maps to one is refused as if it were a getter.
        // =========================================================================================

        [Fact]
        public void Ob4_H_A_complex_types_columns_are_found_by_the_reflection_walk()
        {
            Case("H1 column of a complex property",
                Raw(() => _db.Invoices.Where(i => i.Total.Currency == "IQD").ToList()),
                Guarded(_db.Invoices, "Total.Currency", DataType.Text, "IQD"));

            Case("H2 numeric column of a complex property",
                Raw(() => _db.Invoices.Where(i => i.Total.Amount == 10m).ToList()),
                Guarded(_db.Invoices, "Total.Amount", DataType.Number, "10"));

            Case("H3 column of a NESTED complex property",
                Raw(() => _db.Invoices.Where(i => i.ShipTo.Post.Code == "10001").ToList()),
                Guarded(_db.Invoices, "ShipTo.Post.Code", DataType.Text, "10001"));

            Case("H4 column of the outer complex property",
                Raw(() => _db.Invoices.Where(i => i.ShipTo.City == "Baghdad").ToList()),
                Guarded(_db.Invoices, "ShipTo.City", DataType.Text, "Baghdad"));

            Case("H5 framework member of a complex type's column",
                Raw(() => _db.Invoices.Where(i => i.Total.Currency.Length == 3).ToList()),
                Guarded(_db.Invoices, "Total.Currency.Length", DataType.Number, "3"));

            Case("H6 the complex member itself, named whole",
                Raw(() => _db.Invoices.Where(i => i.Number == "INV-1").ToList()),
                Guarded(_db.Invoices, "Number", DataType.Text, "INV-1"));

            // Getters on a complex type: the refusal is the point, and the unguarded query fails.
            Case("H7 getter on a complex type (the refusal is right here)",
                Raw(() => _db.Invoices.Where(i => i.Total.IsFree).ToList()),
                Guarded(_db.Invoices, "Total.IsFree", DataType.Boolean, "false"));

            Case("H8 getter on a nested complex type (the refusal is right here)",
                Raw(() => _db.Invoices.Where(i => i.ShipTo.Post.IsBlank).ToList()),
                Guarded(_db.Invoices, "ShipTo.Post.IsBlank", DataType.Boolean, "false"));

            Done();
        }

        [Fact]
        public void Ob4_H_A_projection_copying_a_complex_member_reads_it_from_the_model()
        {
            IQueryable<OcxRow> projected = _db.Invoices.Select(invoice => new OcxRow
            {
                Id = invoice.Id,
                Total = invoice.Total,
                ShipTo = invoice.ShipTo
            });

            Case("H9 complex member copied whole, a column beneath it",
                Raw(() => _db.Invoices.Select(i => new OcxRow { Id = i.Id, Total = i.Total, ShipTo = i.ShipTo })
                    .Where(r => r.Total.Currency == "IQD").ToList()),
                Guarded(projected, "Total.Currency", DataType.Text, "IQD"));

            Case("H10 complex member copied whole, a nested column beneath it",
                Raw(() => _db.Invoices.Select(i => new OcxRow { Id = i.Id, Total = i.Total, ShipTo = i.ShipTo })
                    .Where(r => r.ShipTo.Post.Code == "10001").ToList()),
                Guarded(projected, "ShipTo.Post.Code", DataType.Text, "10001"));

            IQueryable<OcxRow> rebuilt = _db.Invoices.Select(invoice => new OcxRow
            {
                Id = invoice.Id,
                Total = new OcxMoney { Amount = invoice.Total.Amount, Currency = invoice.Total.Currency }
            });

            Case("H11 complex member rebuilt member by member",
                Raw(() => _db.Invoices
                    .Select(i => new OcxRow
                    {
                        Id = i.Id,
                        Total = new OcxMoney { Amount = i.Total.Amount, Currency = i.Total.Currency }
                    })
                    .Where(r => r.Total.Currency == "IQD").ToList()),
                Guarded(rebuilt, "Total.Currency", DataType.Text, "IQD"));

            Done();
        }

        // =========================================================================================
        // Ob4-I. A second relational provider, and a pooled factory: the exact provider comparison
        //        has to hold for every one of them.
        // =========================================================================================

        private const string EfCoreProvider = "Microsoft.EntityFrameworkCore.Query.Internal.EntityQueryProvider";

        [Fact]
        public void Ob4_I_A_second_provider_and_a_pooled_factory_carry_EF_Cores_own_provider()
        {
            List<string> lost = new();

            void Check(string name, IQueryProvider provider)
            {
                bool exact = provider.GetType().FullName == EfCoreProvider;

                _out.WriteLine($"{name,-34} {provider.GetType().FullName} exact={exact}");

                if (!exact)
                {
                    lost.Add($"{name} -> {provider.GetType().FullName}");
                }
            }

            // Npgsql: a relational provider other than SQLite. No server is contacted — the provider
            // a DbSet hands out is decided when the model is built.
            using OcxNpgsqlContext npgsql = new();

            Check("Npgsql", ((IQueryable<OcxInvoice>)npgsql.Invoices).Provider);
            Check("Npgsql + FromSqlRaw", npgsql.Invoices.FromSqlRaw("SELECT 1").Provider);

            // A pooled factory, which is how a host with AddPooledDbContextFactory gets its context.
            DbContextOptions<OcxNpgsqlContext> options = new DbContextOptionsBuilder<OcxNpgsqlContext>()
                .UseNpgsql("Host=localhost;Database=none")
                .Options;

            PooledDbContextFactory<OcxNpgsqlContext> factory = new(options);

            using OcxNpgsqlContext pooled = factory.CreateDbContext();

            Check("pooled context", ((IQueryable<OcxInvoice>)pooled.Invoices).Provider);

            using OcxNpgsqlContext pooledAgain = factory.CreateDbContext();

            Check("pooled context, reused", ((IQueryable<OcxInvoice>)pooledAgain.Invoices).Provider);

            Check("SQLite", ((IQueryable<OcxInvoice>)_db.Invoices).Provider);

            Assert.True(lost.Count == 0, "refusal silently lost on: " + string.Join(", ", lost));
        }

        /// <summary>A Npgsql-backed context. Nothing connects; only the model and the provider are read.</summary>
        public sealed class OcxNpgsqlContext : DbContext
        {
            public OcxNpgsqlContext()
            {
            }

            public OcxNpgsqlContext(DbContextOptions<OcxNpgsqlContext> options)
                : base(options)
            {
            }

            public DbSet<OcxInvoice> Invoices => Set<OcxInvoice>();

            protected override void OnConfiguring(DbContextOptionsBuilder options)
            {
                if (!options.IsConfigured)
                {
                    options.UseNpgsql("Host=localhost;Database=none");
                }
            }

            protected override void OnModelCreating(ModelBuilder model)
            {
                model.Entity<OcxInvoice>().ComplexProperty(invoice => invoice.Total);
                model.Entity<OcxInvoice>().ComplexProperty(
                    invoice => invoice.ShipTo, ship => ship.ComplexProperty(address => address.Post));
            }
        }
    }
}
