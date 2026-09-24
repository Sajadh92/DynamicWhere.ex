using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
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

namespace DynamicWhere.Tests.Policies
{
    // ---- DCMP's shape: flat columns, the pair composed in C# only -----------------------------------------

    /// <summary>A shared kernel pair, as DCMP's LocalizedText is: a positional record struct.</summary>
    public readonly record struct ZcText(string Ar, string En)
    {
        public bool IsEmpty => string.IsNullOrWhiteSpace(Ar) && string.IsNullOrWhiteSpace(En);
    }

    /// <summary>The same pair as a class with a constructor, for a shape that is not a record.</summary>
    public sealed class ZcClassText
    {
        public ZcClassText(string ar, string en)
        {
            Ar = ar;
            En = en;
        }

        public string Ar { get; }

        public string En { get; }
    }

    /// <summary>A value built from one argument and one initializer binding.</summary>
    public sealed class ZcMoney
    {
        public ZcMoney()
        {
        }

        public ZcMoney(decimal amount) => Amount = amount;

        public decimal Amount { get; set; }

        public string Currency { get; set; } = string.Empty;
    }

    /// <summary>A type the model stores as a column, through a converter.</summary>
    public readonly record struct ZcTag(string Value);

    public class ZcTenant
    {
        public int Id { get; set; }

        public string NameAr { get; set; } = string.Empty;

        public string NameEn { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public string Currency { get; set; } = string.Empty;

        public int Year { get; set; }

        public ZcTag Tag { get; set; }

        /// <summary>Not mapped: composed over the two columns, as DCMP's entities compose it.</summary>
        public ZcText Name => new(NameAr, NameEn);
    }

    public class ZcRow
    {
        public int Id { get; set; }

        /// <summary>No attribute, so what refuses a path beneath it is the shape of the projection.</summary>
        public ZcText Name { get; set; }

        public ZcClassText? Label { get; set; }

        public ZcMoney? Money { get; set; }

        public DateTime Start { get; set; }

        public ZcTag Copy { get; set; }
    }

    /// <summary>DCMP's row as it ships: the composed pair denied for filtering and sorting.</summary>
    public class ZcDcmpRow
    {
        public int Id { get; set; }

        [DwNoWhere, DwNoOrder]
        public ZcText Name { get; set; }
    }

    /// <summary>A row that is itself built by a constructor, as a positional record row is.</summary>
    public sealed record ZcRecordRow(int Id, string NameAr);

    public sealed class ZcContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZcContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZcTenant> Tenants => Set<ZcTenant>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<ZcTenant>().Ignore(tenant => tenant.Name);
            model.Entity<ZcTenant>().Property(tenant => tenant.Tag)
                .HasConversion(tag => tag.Value, value => new ZcTag(value));
        }
    }

    /// <summary>
    /// A member a projection builds by calling a constructor with arguments. EF Core follows a member
    /// only through an initializer's binding, so a clause on a path through such a member failed inside
    /// the provider: a five-hundred where the strict tier promises a refusal. DCMP-314.
    /// </summary>
    public sealed class ConstructedMemberTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ZcContext _db;

        public ConstructedMemberTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZcContext(_connection);
            _db.Database.EnsureCreated();
            _db.Tenants.Add(new ZcTenant
            {
                NameAr = "مالية", NameEn = "Finance", Amount = 10, Currency = "IQD", Year = 2026, Tag = new ZcTag("a")
            });
            _db.Tenants.Add(new ZcTenant
            {
                NameAr = "صحة", NameEn = "Health", Amount = 20, Currency = "USD", Year = 2025, Tag = new ZcTag("b")
            });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier)
            where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static Filter Where(string field, string value, Operator op = Operator.Equal, DataType type = DataType.Text)
        {
            Condition condition = new() { Field = field, DataType = type, Operator = op };
            condition.Values.Add(value);

            return new Filter { ConditionGroup = new ConditionGroup { Conditions = { condition } } };
        }

        private static Filter OrderBy(string field) => new() { Orders = new List<OrderBy> { new() { Field = field } } };

        /// <summary>DCMP's projection as it is written today.</summary>
        private IQueryable<ZcRow> Constructed() =>
            _db.Tenants.Select(t => new ZcRow { Id = t.Id, Name = new ZcText(t.NameAr, t.NameEn) });

        /// <summary>The same projection written with an initializer, which EF Core follows.</summary>
        private IQueryable<ZcRow> Initialized() =>
            _db.Tenants.Select(t => new ZcRow { Id = t.Id, Name = new ZcText { Ar = t.NameAr, En = t.NameEn } });

        // ---- the refusal ---------------------------------------------------------------------------------

        [Theory]
        [InlineData("Name.Ar", Operator.Equal, "صحة")]
        [InlineData("Name.En", Operator.IContains, "fin")]
        public void A_filter_through_a_constructed_member_is_refused_under_the_strict_tier(
            string field, Operator op, string value)
        {
            PolicyException refusal = Assert.Throws<PolicyException>(
                () => Guard(Constructed(), DwTier.Strict).ToList(Where(field, value, op)));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
            Assert.Equal("*", refusal.FieldPath);
        }

        [Fact]
        public void A_sort_through_a_constructed_member_is_refused_under_the_strict_tier()
        {
            PolicyException refusal = Assert.Throws<PolicyException>(
                () => Guard(Constructed(), DwTier.Strict).ToList(OrderBy("Name.En")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForOrder, refusal.ErrorCode);
        }

        [Fact]
        public void A_getter_through_a_constructed_member_is_refused_as_it_is_through_an_initializer()
        {
            PolicyException refusal = Assert.Throws<PolicyException>(
                () => Guard(Constructed(), DwTier.Strict).ToList(Where("Name.IsEmpty", "true", type: DataType.Boolean)));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Fact]
        public void The_trace_says_why_the_path_was_refused()
        {
            PolicyQueryable<ZcRow> guarded = Guard(Constructed(), DwTier.Strict);

            Assert.Throws<PolicyException>(() => guarded.ToList(Where("Name.Ar", "x")));

            Assert.Contains(guarded.LastTrace!.Decisions, decision =>
                decision.FieldPath == "Name.Ar"
                && decision.Reason is { } reason
                && reason.Contains("cannot compute", StringComparison.Ordinal));
        }

        [Fact]
        public void A_class_built_by_its_constructor_is_refused_as_a_record_struct_is()
        {
            IQueryable<ZcRow> rows = _db.Tenants.Select(t => new ZcRow { Id = t.Id, Label = new ZcClassText(t.NameAr, t.NameEn) });

            PolicyException refusal = Assert.Throws<PolicyException>(
                () => Guard(rows, DwTier.Strict).ToList(Where("Label.Ar", "x")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Fact]
        public void A_group_and_a_segment_through_a_constructed_member_are_refused()
        {
            Summary summary = new() { GroupBy = new GroupBy { Fields = { "Name.Ar" } } };

            PolicyException grouped = Assert.Throws<PolicyException>(
                () => Guard(Constructed(), DwTier.Strict).ToList(summary));

            Assert.Equal(PolicyErrorCode.FieldDeniedForGroup, grouped.ErrorCode);

            Segment segment = new()
            {
                ConditionSets =
                {
                    new ConditionSet { Sort = 1, ConditionGroup = Where("Name.Ar", "x").ConditionGroup! }
                }
            };

            PolicyException segmented = Assert.ThrowsAny<PolicyException>(
                () => Guard(Constructed(), DwTier.Strict).ToListAsync(segment).GetAwaiter().GetResult());

            Assert.Equal(PolicyErrorCode.FieldDeniedForSegment, segmented.ErrorCode);
        }

        [Fact]
        public void A_row_built_by_its_own_constructor_refuses_every_member_a_clause_names()
        {
            // A positional record as the row: EF Core cannot filter new ZcRecordRow(t.Id, t.NameAr) on
            // NameAr either, and every clause on it was a five-hundred.
            IQueryable<ZcRecordRow> rows = _db.Tenants.Select(t => new ZcRecordRow(t.Id, t.NameAr));

            PolicyException refusal = Assert.Throws<PolicyException>(
                () => Guard(rows, DwTier.Strict).ToList(Where("NameAr", "x")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);

            // Nothing is refused where nothing is named: the count and the read need no member.
            FilterResult<ZcRecordRow> all = Guard(rows, DwTier.Strict).ToList(new Filter());

            Assert.Equal(2, all.Data.Count);
        }

        [Fact]
        public void A_member_an_initializer_beside_the_constructor_binds_stays_filterable()
        {
            IQueryable<ZcRow> rows = _db.Tenants.Select(t => new ZcRow
            {
                Id = t.Id,
                Money = new ZcMoney(t.Amount) { Currency = t.Currency }
            });

            FilterResult<ZcRow> bound = Guard(rows, DwTier.Strict).ToList(Where("Money.Currency", "USD"));

            Assert.Single(bound.Data);

            PolicyException refusal = Assert.Throws<PolicyException>(
                () => Guard(rows, DwTier.Strict).ToList(Where("Money.Amount", "20", type: DataType.Number)));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Fact]
        public void A_member_one_constructing_branch_leaves_to_its_constructor_is_out_of_reach()
        {
            // Bound in one branch and not in the other: EF Core reads the member through both, so the
            // branch that leaves it to the constructor decides.
            IQueryable<ZcRow> rows = _db.Tenants.Select(t => new ZcRow
            {
                Id = t.Id,
                Money = t.Year > 2025 ? new ZcMoney(t.Amount) { Currency = t.Currency } : new ZcMoney(t.Amount)
            });

            PolicyException refusal = Assert.Throws<PolicyException>(
                () => Guard(rows, DwTier.Strict).ToList(Where("Money.Currency", "USD")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Fact]
        public void A_constructor_in_one_branch_refuses_the_member_whatever_the_other_branch_binds()
        {
            IQueryable<ZcRow> rows = _db.Tenants.Select(t => new ZcRow
            {
                Id = t.Id,
                Name = t.Year > 2025 ? new ZcText(t.NameAr, t.NameEn) : new ZcText { Ar = t.NameAr, En = t.NameEn }
            });

            PolicyException refusal = Assert.Throws<PolicyException>(
                () => Guard(rows, DwTier.Strict).ToList(Where("Name.Ar", "x")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        // ---- what is left alone --------------------------------------------------------------------------

        [Fact]
        public void The_convenience_tier_is_left_as_the_unguarded_query_leaves_it()
        {
            // The shape-aware refusal is the strict tier's promise, as it has been since 3.3.0.
            Exception thrown = Assert.ThrowsAny<Exception>(
                () => Guard(Constructed(), DwTier.Convenience).ToList(Where("Name.Ar", "x")));

            Assert.IsNotType<PolicyException>(thrown);
        }

        [Fact]
        public void A_framework_type_built_by_its_constructor_is_left_to_the_provider()
        {
            // new DateTime(y, 1, 1) is one a provider may translate, constructor and members alike.
            IQueryable<ZcRow> rows = _db.Tenants.Select(t => new ZcRow { Id = t.Id, Start = new DateTime(t.Year, 1, 1) });

            try
            {
                Guard(rows, DwTier.Strict).ToList(Where("Start.Year", "2026", type: DataType.Number));
            }
            catch (Exception thrown)
            {
                Assert.IsNotType<PolicyException>(thrown);
            }
        }

        [Fact]
        public void A_type_the_model_stores_as_a_column_is_left_to_the_provider()
        {
            IQueryable<ZcRow> rows = _db.Tenants.Select(t => new ZcRow { Id = t.Id, Copy = new ZcTag(t.NameEn) });

            try
            {
                Guard(rows, DwTier.Strict).ToList(Where("Copy.Value", "Health"));
            }
            catch (Exception thrown)
            {
                Assert.IsNotType<PolicyException>(thrown);
            }
        }

        [Fact]
        public void Selecting_a_path_through_a_constructed_member_still_works()
        {
            // EF Core evaluates the last projection on the client, so the row is built and read there.
            FilterResult<ZcRow> result = Guard(Constructed(), DwTier.Strict)
                .ToList(new Filter { Selects = new List<string> { "Name.Ar" }, Orders = new List<OrderBy> { new() { Field = "Id" } } });

            Assert.Equal(new[] { "مالية", "صحة" }, result.Data.Select(row => row.Name.Ar));
        }

        // ---- DCMP's row, unchanged --------------------------------------------------------------------

        private IQueryable<ZcDcmpRow> Dcmp() =>
            _db.Tenants.Select(t => new ZcDcmpRow { Id = t.Id, Name = new ZcText(t.NameAr, t.NameEn) });

        [Theory]
        [InlineData(DwTier.Strict, "Name")]
        [InlineData(DwTier.Strict, "Name.Ar")]
        [InlineData(DwTier.Strict, "Name.En")]
        [InlineData(DwTier.Convenience, "Name.Ar")]
        [InlineData(DwTier.Convenience, "Name.En")]
        public void The_pair_and_its_halves_are_refused_as_filters_in_both_tiers(DwTier tier, string field)
        {
            // The halves take the pair's [DwNoWhere]: DCMP-314's 400, with no change to DCMP's row.
            PolicyException refusal = Assert.Throws<PolicyException>(() => Guard(Dcmp(), tier).ToList(Where(field, "x")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Fact]
        public void The_halves_are_refused_as_sorts_under_the_strict_tier()
        {
            PolicyException refusal = Assert.Throws<PolicyException>(() => Guard(Dcmp(), DwTier.Strict).ToList(OrderBy("Name.Ar")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForOrder, refusal.ErrorCode);
        }

        [Fact]
        public void The_halves_are_still_read_as_columns()
        {
            FilterResult<ZcDcmpRow> result = Guard(Dcmp(), DwTier.Strict)
                .ToList(new Filter { Selects = new List<string> { "Name.Ar", "Name.En" }, Orders = new List<OrderBy> { new() { Field = "Id" } } });

            Assert.Equal(new[] { "Finance", "Health" }, result.Data.Select(row => row.Name.En));
            Assert.Equal(new[] { "مالية", "صحة" }, result.Data.Select(row => row.Name.Ar));
        }

        // ---- the form EF Core follows --------------------------------------------------------------------

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Written_with_an_initializer_the_same_paths_filter_and_sort_on_the_columns(DwTier tier)
        {
            FilterResult<ZcRow> ar = Guard(Initialized(), tier).ToList(Where("Name.Ar", "صحة"));
            FilterResult<ZcRow> en = Guard(Initialized(), tier).ToList(Where("Name.En", "fin", Operator.IContains));
            FilterResult<ZcRow> sorted = Guard(Initialized(), tier).ToList(OrderBy("Name.En"));

            Assert.Equal("Health", Assert.Single(ar.Data).Name.En);
            Assert.Equal("Finance", Assert.Single(en.Data).Name.En);
            Assert.Equal(new[] { "Finance", "Health" }, sorted.Data.Select(row => row.Name.En));
        }

        [Fact]
        public void Written_with_an_initializer_the_filter_reads_the_column_itself()
        {
            FilterResult<ZcRow> result = Guard(Initialized(), DwTier.Convenience)
                .ToList(Where("Name.En", "fin", Operator.IContains), getQueryString: true);

            Assert.Contains("\"NameEn\"", result.QueryString, StringComparison.Ordinal);
        }
    }
}
