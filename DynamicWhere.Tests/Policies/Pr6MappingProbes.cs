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
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>An owned type stored as a JSON document.</summary>
    public class P6Json
    {
        public string City { get; set; } = string.Empty;

        public string Street { get; set; } = string.Empty;

        /// <summary>A getter over two members of the document.</summary>
        public string Full => $"{Street}, {City}";
    }

    /// <summary>A complex type: its members are columns of the owner's table.</summary>
    public class P6Money
    {
        public decimal Amount { get; set; }

        public string Currency { get; set; } = string.Empty;

        public bool IsFree => Amount == 0m;
    }

    public class P6Doc
    {
        public int Id { get; set; }

        public string Number { get; set; } = string.Empty;

        /// <summary>A JSON column.</summary>
        public P6Json Where { get; set; } = new();

        /// <summary>A complex property.</summary>
        public P6Money Total { get; set; } = new();

        /// <summary>A primitive collection: one column holding a list of scalars.</summary>
        public List<string> Tags { get; set; } = new();
    }

    public sealed class P6MappingContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public P6MappingContext(SqliteConnection connection) => _connection = connection;

        public DbSet<P6Doc> Docs => Set<P6Doc>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<P6Doc>().OwnsOne(doc => doc.Where, json => json.ToJson());
            model.Entity<P6Doc>().ComplexProperty(doc => doc.Total);
            model.Entity<P6Doc>().PrimitiveCollection(doc => doc.Tags);
        }
    }

    /// <summary>
    /// Round 6, the mapping shapes only EF Core 8 has: a JSON column, a complex property and a
    /// primitive collection, each run guarded and unguarded.
    /// </summary>
    public sealed class Pr6MappingProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly P6MappingContext _db;
        private readonly List<string> _findings = new();

        public Pr6MappingProbes(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new P6MappingContext(_connection);
            _db.Database.EnsureCreated();
            _db.Docs.Add(new P6Doc
            {
                Number = "D-1",
                Where = new P6Json { City = "Baghdad", Street = "Al Rasheed" },
                Total = new P6Money { Amount = 10m, Currency = "IQD" },
                Tags = new List<string> { "red", "blue" }
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
                new DwPolicyOptions { Tier = DwTier.Strict, Caps = { MinGroupSize = 1 } },
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

        /// <summary>The same request through the library with no policy attached at all.</summary>
        private static string Unguarded<T>(IQueryable<T> source, string field, DataType type, string value)
            where T : class
        {
            try
            {
                FilterResult<T> result = source.ToList(Clause(field, type, value));

                return $"OK({result.Data.Count})";
            }
            catch (Exception failure)
            {
                return failure.GetType().Name;
            }
        }

        private static Filter Clause(string field, DataType type, string value) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition { Field = field, DataType = type, Operator = Operator.Equal, Values = { value } }
                }
            }
        };

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
            _out.WriteLine($"{probe,-58} unguarded={unguarded,-26} guarded={guarded}");

            if (unguarded.StartsWith("OK", StringComparison.Ordinal)
                && guarded.StartsWith("REFUSED", StringComparison.Ordinal))
            {
                _findings.Add($"{probe}: unguarded {unguarded}, guarded {guarded}");
            }
        }

        [Fact]
        public void Pr6_F_The_three_mapping_shapes_only_EF_Core_8_has()
        {
            Case("F01 a member of a JSON column",
                Raw(() => _db.Docs.Where(d => d.Where.City == "Baghdad").ToList()),
                Guarded(_db.Docs, "Where.City", DataType.Text, "Baghdad"));

            Case("F02 a second member of the same JSON column",
                Raw(() => _db.Docs.Where(d => d.Where.Street == "Al Rasheed").ToList()),
                Guarded(_db.Docs, "Where.Street", DataType.Text, "Al Rasheed"));

            Case("F03 a getter over two members of a JSON column (refusal is right)",
                Raw(() => _db.Docs.Where(d => d.Where.Full == "Al Rasheed, Baghdad").ToList()),
                Guarded(_db.Docs, "Where.Full", DataType.Text, "Al Rasheed, Baghdad"));

            Case("F04 a column of a complex property",
                Raw(() => _db.Docs.Where(d => d.Total.Currency == "IQD").ToList()),
                Guarded(_db.Docs, "Total.Currency", DataType.Text, "IQD"));

            Case("F05 a numeric column of a complex property",
                Raw(() => _db.Docs.Where(d => d.Total.Amount == 10m).ToList()),
                Guarded(_db.Docs, "Total.Amount", DataType.Number, "10"));

            Case("F06 a getter on a complex type (refusal is right)",
                Raw(() => _db.Docs.Where(d => d.Total.IsFree).ToList()),
                Guarded(_db.Docs, "Total.IsFree", DataType.Boolean, "false"));

            // Printed, not flagged: the library refuses Tags.Count with no policy attached either
            // (F09), so the hand-written LINQ is not a control the guard can be held to.
            _out.WriteLine("F07 a primitive collection's Count, hand-written LINQ  unguarded="
                + Raw(() => _db.Docs.Where(d => d.Tags.Count == 2).ToList())
                + " guarded=" + Guarded(_db.Docs, "Tags.Count", DataType.Number, "2"));

            // The control that matters: the same field through the library with no policy at all.
            Case("F09 Tags.Count through the library, unguarded",
                Unguarded(_db.Docs, "Tags.Count", DataType.Number, "2"),
                Guarded(_db.Docs, "Tags.Count", DataType.Number, "2"));

            Case("F08 a plain column beside all three",
                Raw(() => _db.Docs.Where(d => d.Number == "D-1").ToList()),
                Guarded(_db.Docs, "Number", DataType.Text, "D-1"));

            Assert.True(_findings.Count == 0, string.Join(" || ", _findings));
        }
    }
}
