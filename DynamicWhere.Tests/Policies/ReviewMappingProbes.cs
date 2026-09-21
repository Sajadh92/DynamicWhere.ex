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
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ---- mapping shapes the strict check now walks -------------------------------------------------------

    public class MpDoc
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        /// <summary>Owned, stored as JSON on EF Core 8.</summary>
        public MpMeta Meta { get; set; } = new();

        /// <summary>An owned collection.</summary>
        public List<MpLine> Lines { get; set; } = new();

        /// <summary>A primitive collection on EF Core 8.</summary>
        public List<string> Tags { get; set; } = new();
    }

    public class MpMeta
    {
        public string Author { get; set; } = string.Empty;

        public int Revision { get; set; }

        /// <summary>A getter, not a column.</summary>
        public bool IsDraft => Revision == 0;
    }

    public class MpLine
    {
        public int Id { get; set; }

        public string Sku { get; set; } = string.Empty;

        public int Qty { get; set; }
    }

    /// <summary>TPT: each type has a table of its own.</summary>
    public class MpAnimal
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class MpDog : MpAnimal
    {
        public string? Breed { get; set; }
    }

    /// <summary>Table splitting: two types share one table.</summary>
    public class MpAccount
    {
        public int Id { get; set; }

        public string Number { get; set; } = string.Empty;

        public MpAccountDetail Detail { get; set; } = null!;
    }

    public class MpAccountDetail
    {
        public int Id { get; set; }

        public string Iban { get; set; } = string.Empty;

        public MpAccount Account { get; set; } = null!;
    }

    /// <summary>Keyless, read through a raw query.</summary>
    public class MpTally
    {
        public string Code { get; set; } = string.Empty;

        public int Total { get; set; }
    }

    public sealed class MpContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public MpContext(SqliteConnection connection) => _connection = connection;

        public DbSet<MpDoc> Docs => Set<MpDoc>();

        public DbSet<MpAnimal> Animals => Set<MpAnimal>();

        public DbSet<MpDog> Dogs => Set<MpDog>();

        public DbSet<MpAccount> Accounts => Set<MpAccount>();

        public DbSet<MpAccountDetail> Details => Set<MpAccountDetail>();

        public DbSet<MpTally> Tallies => Set<MpTally>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<MpDoc>().OwnsOne(doc => doc.Meta, meta => meta.ToJson());
            model.Entity<MpDoc>().OwnsMany(doc => doc.Lines, lines => lines.HasKey(line => line.Id));

            model.Entity<MpAnimal>().ToTable("Animals");
            model.Entity<MpDog>().ToTable("Dogs");

            model.Entity<MpAccount>().ToTable("Accounts");
            model.Entity<MpAccountDetail>().ToTable("Accounts");
            model.Entity<MpAccountDetail>().HasOne(detail => detail.Account)
                .WithOne(account => account.Detail)
                .HasForeignKey<MpAccountDetail>(detail => detail.Id);

            model.Entity<MpTally>().HasNoKey().ToView(null);
        }
    }

    /// <summary>
    /// The mapping shapes <c>RowShape.Expresses</c> now walks, each run guarded and unguarded.
    /// </summary>
    public sealed class ReviewMappingProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly MpContext _db;

        public ReviewMappingProbes(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new MpContext(_connection);
            _db.Database.EnsureCreated();

            _db.Docs.Add(new MpDoc
            {
                Code = "D-1",
                Meta = new MpMeta { Author = "sajjad", Revision = 2 },
                Lines = { new MpLine { Id = 1, Sku = "S-1", Qty = 3 } },
                Tags = { "red", "blue" }
            });

            _db.Dogs.Add(new MpDog { Name = "Rex", Breed = "husky" });
            _db.Accounts.Add(new MpAccount { Number = "N-1", Detail = new MpAccountDetail { Iban = "IQ-1" } });
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

        private static Filter Where(string field, string value, DataType type = DataType.Text) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions = { new Condition { Field = field, DataType = type, Operator = Operator.Equal, Values = { value } } }
            }
        };

        private string Probe<T>(IQueryable<T> source, Filter filter) where T : class
        {
            try
            {
                FilterResult<T> result = Guard(source).ToList(filter);

                return $"ran, {result.Data.Count} row(s)";
            }
            catch (PolicyException refusal)
            {
                return $"REFUSED {refusal.ErrorCode}";
            }
            catch (Exception other)
            {
                return $"threw {other.GetType().Name}";
            }
        }

        private string Unguarded<T>(Func<IQueryable<T>> run)
        {
            try
            {
                return $"ran, {run().Count()} row(s)";
            }
            catch (Exception other)
            {
                return $"threw {other.GetType().Name}";
            }
        }

        [Fact]
        public void A_member_of_a_JSON_owned_type_still_filters()
        {
            string unguarded = Unguarded(() => _db.Docs.Where(doc => doc.Meta.Author == "sajjad"));
            string guarded = Probe(_db.Docs, Where("Meta.Author", "sajjad"));

            _out.WriteLine($"JSON member unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_getter_on_a_JSON_owned_type_matches_the_unguarded_failure()
        {
            string unguarded = Unguarded(() => _db.Docs.Where(doc => doc.Meta.IsDraft));
            string guarded = Probe(_db.Docs, Where("Meta.IsDraft", "false", DataType.Boolean));

            _out.WriteLine($"JSON getter unguarded: {unguarded} | guarded: {guarded}");
        }

        [Fact]
        public void A_member_of_an_owned_collection_still_filters()
        {
            string unguarded = Unguarded(() => _db.Docs.Where(doc => doc.Lines.Any(line => line.Sku == "S-1")));
            string guarded = Probe(_db.Docs, Where("Lines.Sku", "S-1"));

            _out.WriteLine($"owned collection unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_primitive_collection_still_filters()
        {
            string unguarded = Unguarded(() => _db.Docs.Where(doc => doc.Tags.Contains("red")));
            string guarded = Probe(_db.Docs, new Filter
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition
                        {
                            Field = "Tags", DataType = DataType.Text,
                            Operator = Operator.Contains, Values = { "red" }
                        }
                    }
                }
            });

            _out.WriteLine($"primitive collection unguarded: {unguarded} | guarded: {guarded}");
        }

        [Fact]
        public void A_TPT_subtype_column_still_filters_through_its_own_set()
        {
            string unguarded = Unguarded(() => _db.Dogs.Where(dog => dog.Breed == "husky"));
            string guarded = Probe(_db.Dogs, Where("Breed", "husky"));

            _out.WriteLine($"TPT own set unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_TPT_subtype_column_through_OfType_still_filters()
        {
            string unguarded = Unguarded(() => _db.Animals.OfType<MpDog>().Where(dog => dog.Breed == "husky"));
            string guarded = Probe(_db.Animals.OfType<MpDog>(), Where("Breed", "husky"));

            _out.WriteLine($"TPT OfType unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_table_split_sibling_still_filters()
        {
            string unguarded = Unguarded(() => _db.Accounts.Where(account => account.Detail.Iban == "IQ-1"));
            string guarded = Probe(_db.Accounts, Where("Detail.Iban", "IQ-1"));

            _out.WriteLine($"table splitting unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_keyless_type_read_through_FromSql_still_filters()
        {
            IQueryable<MpTally> rows = _db.Tallies.FromSqlRaw("SELECT \"Code\" AS \"Code\", 1 AS \"Total\" FROM \"Docs\"");

            string unguarded = Unguarded(() => rows.Where(tally => tally.Code == "D-1"));
            string guarded = Probe(rows, Where("Code", "D-1"));

            _out.WriteLine($"FromSql keyless unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_split_query_still_filters()
        {
            IQueryable<MpDoc> rows = _db.Docs.Include(doc => doc.Lines).AsSplitQuery();

            string unguarded = Unguarded(() => rows.Where(doc => doc.Code == "D-1"));
            string guarded = Probe(rows, Where("Code", "D-1"));

            _out.WriteLine($"split query unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_string_length_over_a_JSON_member_still_filters()
        {
            string unguarded = Unguarded(() => _db.Docs.Where(doc => doc.Meta.Author.Length == 6));
            string guarded = Probe(_db.Docs, Where("Meta.Author.Length", "6", DataType.Number));

            _out.WriteLine($"JSON member length unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }
    }
}
