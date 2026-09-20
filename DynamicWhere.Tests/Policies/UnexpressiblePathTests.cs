using System.Linq.Expressions;
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
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ---- a shared kernel type: two columns and a getter over them ------------------------------------------

    public class ZyLocalizedText
    {
        public string Ar { get; set; } = string.Empty;

        public string En { get; set; } = string.Empty;

        /// <summary>Computed from the two columns, so no database can answer it.</summary>
        public bool IsEmpty => string.IsNullOrWhiteSpace(Ar) && string.IsNullOrWhiteSpace(En);
    }

    public class ZyRole
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        [DwNoWhere, DwNoOrder]
        public ZyLocalizedText Name { get; set; } = new();

        /// <summary>An unmapped getter over two mapped columns of the entity itself.</summary>
        public string Display => $"{Code}:{Id}";
    }

    /// <summary>A hierarchy, so a column only one subtype maps is still a column.</summary>
    public class ZyParty
    {
        public int Id { get; set; }

        public string Kind { get; set; } = string.Empty;

        /// <summary>Declared on the base, mapped only on the subtype, which is where the rows carry it.</summary>
        public string? Licence { get; set; }
    }

    public class ZyMerchant : ZyParty
    {
        public string? Rating { get; set; }
    }

    /// <summary>The row a caller projects before the guard sees it, which is DCMP's shape.</summary>
    public class ZyRoleRow
    {
        public int Id { get; set; }

        public ZyLocalizedText Name { get; set; } = new();

        public string Code { get; set; } = string.Empty;
    }

    public sealed class ZyContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZyContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZyRole> Roles => Set<ZyRole>();

        public DbSet<ZyParty> Parties => Set<ZyParty>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            // Owned rather than complex, so the EF Core 6.0.22 floor builds this model too.
            model.Entity<ZyRole>().OwnsOne(role => role.Name);
            model.Entity<ZyRole>().Ignore(role => role.Display);
            model.Entity<ZyParty>().HasDiscriminator<string>("Discriminator")
                .HasValue<ZyParty>("party")
                .HasValue<ZyMerchant>("merchant");

            // Ignored where it is declared and mapped where the rows have it, so the model maps the
            // member on the derived type alone while the queried type still carries the CLR property.
            model.Entity<ZyParty>().Ignore(party => party.Licence);
            model.Entity<ZyMerchant>().Property(merchant => merchant.Licence);
        }
    }

    /// <summary>A provider of its own: not EF Core's, and not the one rows in memory carry.</summary>
    public sealed class ZyOwnProvider<T> : IQueryable<T>, IQueryProvider
    {
        private readonly IQueryable<T> _inner;

        public ZyOwnProvider(IQueryable<T> inner)
        {
            _inner = inner;
            Expression = inner.Expression;
        }

        public Type ElementType => typeof(T);

        public Expression Expression { get; }

        public IQueryProvider Provider => this;

        public IEnumerator<T> GetEnumerator() => _inner.Provider.CreateQuery<T>(Expression).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public IQueryable CreateQuery(Expression expression) => _inner.Provider.CreateQuery(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            _inner.Provider.CreateQuery<TElement>(expression);

        public object? Execute(Expression expression) => _inner.Provider.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => _inner.Provider.Execute<TResult>(expression);
    }

    /// <summary>
    /// A path beneath a member the policy has no fragment for, whose leaf no database can compute.
    /// Until this, the package accepted the path and the provider threw, which is a five-hundred
    /// where the strict tier promises a refusal.
    /// </summary>
    public sealed class UnexpressiblePathTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZyContext _db;

        public UnexpressiblePathTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZyContext(_connection);
            _db.Database.EnsureCreated();
            _db.Roles.Add(new ZyRole { Code = "admin", Name = new ZyLocalizedText { Ar = "مدير", En = "Admin" } });
            _db.Parties.Add(new ZyMerchant { Kind = "merchant", Licence = "L-1", Rating = "A" });
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

        private static Filter Where(string field, string value, DataType type = DataType.Text) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions = { new Condition { Field = field, DataType = type, Operator = Operator.Equal, Values = { value } } }
            }
        };

        /// <summary>The rows a caller projects before the guard sees them, built member by member.</summary>
        private IQueryable<ZyRoleRow> Built() =>
            _db.Roles.Select(role => new ZyRoleRow
            {
                Id = role.Id,
                Code = role.Code,
                Name = new ZyLocalizedText { Ar = role.Name.Ar, En = role.Name.En }
            });

        /// <summary>The same rows, with the shared type copied whole from the entity.</summary>
        private IQueryable<ZyRoleRow> Copied() =>
            _db.Roles.Select(role => new ZyRoleRow { Id = role.Id, Code = role.Code, Name = role.Name });

        // ---- the entity itself ----------------------------------------------------------------------------

        [Fact]
        public void A_computed_member_beneath_an_undecorated_member_is_refused_rather_than_run()
        {
            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(_db.Roles, DwTier.Strict).ToList(Where("Name.IsEmpty", "false", DataType.Boolean)));

            _out.WriteLine($"entity: {refusal.ErrorCode}");

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Fact]
        public void The_mapped_members_beneath_it_still_answer()
        {
            FilterResult<ZyRole> result = Guard(_db.Roles, DwTier.Strict).ToList(Where("Name.En", "Admin"));

            Assert.Single(result.Data);
        }

        [Fact]
        public void An_unmapped_getter_on_the_entity_is_refused()
        {
            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(_db.Roles, DwTier.Strict).ToList(Where("Display", "admin:1")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Fact]
        public void A_column_a_subtype_maps_is_still_a_column()
        {
            FilterResult<ZyMerchant> result = Guard(_db.Set<ZyMerchant>(), DwTier.Strict)
                .ToList(Where("Licence", "L-1"));

            Assert.Single(result.Data);
        }

        [Fact]
        public void A_column_only_the_subtype_maps_is_refused_through_the_base_type()
        {
            // The queried type declares the member and the model maps it one level down. EF Core
            // translates against the type the query is over, so this is the failure the refusal is
            // for: "Translation of member 'Licence' on entity type 'ZyParty' failed."
            Assert.NotNull(_db.Model.FindEntityType(typeof(ZyMerchant))!.FindProperty("Licence"));
            Assert.Null(_db.Model.FindEntityType(typeof(ZyParty))!.FindProperty("Licence"));

            Exception? unguarded = Record.Exception(() => _db.Parties.Where(party => party.Licence == "L-1").ToList());

            Assert.IsType<InvalidOperationException>(unguarded);

            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(_db.Parties, DwTier.Strict).ToList(Where("Licence", "L-1")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Fact]
        public void A_framework_member_the_provider_translates_is_left_alone()
        {
            FilterResult<ZyRole> result = Guard(_db.Roles, DwTier.Strict)
                .ToList(Where("Code.Length", "5", DataType.Number));

            Assert.Single(result.Data);
        }

        // ---- the projected row ----------------------------------------------------------------------------

        [Fact]
        public void A_projection_that_builds_the_shared_type_refuses_the_computed_member()
        {
            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(Built(), DwTier.Strict).ToList(Where("Name.IsEmpty", "false", DataType.Boolean)));

            _out.WriteLine($"built: {refusal.ErrorCode}");

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Fact]
        public void A_projection_that_copies_the_shared_type_refuses_it_too()
        {
            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(Copied(), DwTier.Strict).ToList(Where("Name.IsEmpty", "false", DataType.Boolean)));

            _out.WriteLine($"copied: {refusal.ErrorCode}");

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Fact]
        public void What_the_projection_does_assign_still_answers()
        {
            Assert.Single(Guard(Built(), DwTier.Strict).ToList(Where("Name.Ar", "مدير")).Data);
            Assert.Single(Guard(Copied(), DwTier.Strict).ToList(Where("Name.En", "Admin")).Data);
        }

        // ---- every clause, one choke point ----------------------------------------------------------------

        [Fact]
        public void An_order_on_it_is_refused()
        {
            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(Built(), DwTier.Strict).ToList(new Filter
                {
                    Orders = new List<OrderBy> { new() { Field = "Name.IsEmpty", Direction = Direction.Ascending } }
                }));

            Assert.Equal(PolicyErrorCode.FieldDeniedForOrder, refusal.ErrorCode);
        }

        [Fact]
        public void A_projection_naming_it_is_refused()
        {
            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(Built(), DwTier.Strict).ToList(new Filter
                {
                    Selects = new List<string> { "Id", "Name.IsEmpty" }
                }));

            Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, refusal.ErrorCode);
        }

        [Fact]
        public void A_clause_composed_on_the_handle_is_refused_too()
        {
            // The composed pipeline reads the same source, so it answers as the terminal does.
            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(Built(), DwTier.Strict).Where(new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition
                        {
                            Field = "Name.IsEmpty", DataType = DataType.Boolean,
                            Operator = Operator.Equal, Values = { "false" }
                        }
                    }
                }));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Fact]
        public async Task A_segment_naming_it_is_refused_and_leaves_its_own_trace()
        {
            PolicyQueryable<ZyRoleRow> guarded = Guard(Built(), DwTier.Strict);

            // A first request, so a trace left over from it would be visible if the refusal skipped
            // the assignment.
            await guarded.ToListAsync(new Segment
            {
                ConditionSets =
                {
                    new ConditionSet
                    {
                        Sort = 1,
                        ConditionGroup = new ConditionGroup
                        {
                            Conditions = { new Condition { Field = "Code", DataType = DataType.Text, Operator = Operator.Equal, Values = { "admin" } } }
                        }
                    }
                }
            });

            PolicyTrace? first = guarded.LastTrace;

            PolicyException refusal = await Assert.ThrowsAnyAsync<PolicyException>(
                () => guarded.ToListAsync(new Segment
                {
                    ConditionSets =
                    {
                        new ConditionSet
                        {
                            Sort = 1,
                            ConditionGroup = new ConditionGroup
                            {
                                Conditions = { new Condition { Field = "Name.IsEmpty", DataType = DataType.Boolean, Operator = Operator.Equal, Values = { "false" } } }
                            }
                        }
                    }
                }));

            Assert.Equal(PolicyErrorCode.FieldDeniedForSegment, refusal.ErrorCode);
            Assert.NotSame(first, guarded.LastTrace);
            Assert.Contains(
                guarded.LastTrace!.Decisions,
                decision => decision.Reason is not null && decision.Reason.Contains("cannot compute"));
        }

        [Fact]
        public void A_summary_grouping_on_it_is_refused()
        {
            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(Built(), DwTier.Strict).ToList(new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = new List<string> { "Name.IsEmpty" },
                        AggregateBy = new List<AggregateBy> { new() { Alias = "Total", Aggregator = Aggregator.Count } }
                    }
                }));

            Assert.Equal(PolicyErrorCode.FieldDeniedForGroup, refusal.ErrorCode);
        }

        // ---- what is left alone ---------------------------------------------------------------------------

        [Fact]
        public void A_member_the_projection_never_assigns_fails_unguarded_and_is_refused_guarded()
        {
            // Unguarded first: what the provider does with it decides whether refusing is right.
            IQueryable<ZyRoleRow> rows = _db.Roles.Select(role => new ZyRoleRow { Id = role.Id, Code = role.Code });

            Exception? unguarded = Record.Exception(() => rows.Where(row => row.Name.Ar == "مدير").ToList());

            _out.WriteLine($"unguarded: {unguarded?.GetType().Name ?? "ran"}");

            Assert.NotNull(unguarded);

            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(rows, DwTier.Strict).ToList(Where("Name.Ar", "مدير")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
        }

        [Fact]
        public void A_provider_that_is_not_EF_Core_s_decides_for_itself()
        {
            // Its rules are its own: it may evaluate the getter in memory, as rows in memory do, so
            // nothing here is refused on EF Core's behalf.
            ZyRole[] rows = { new() { Id = 1, Code = "admin", Name = new ZyLocalizedText { Ar = "مدير", En = "Admin" } } };

            IQueryable<ZyRoleRow> built = new ZyOwnProvider<ZyRoleRow>(rows.AsQueryable().Select(role => new ZyRoleRow
            {
                Id = role.Id,
                Code = role.Code,
                Name = new ZyLocalizedText { Ar = role.Name.Ar, En = role.Name.En }
            }));

            FilterResult<ZyRoleRow> result = Guard(built, DwTier.Strict)
                .ToList(Where("Name.IsEmpty", "false", DataType.Boolean));

            Assert.Single(result.Data);
        }

        [Fact]
        public void Rows_in_memory_run_the_getter_as_they_always_did()
        {
            ZyRole[] rows = { new() { Id = 1, Code = "admin", Name = new ZyLocalizedText { Ar = "مدير", En = "Admin" } } };

            FilterResult<ZyRole> result = Guard(rows.AsQueryable(), DwTier.Strict)
                .ToList(Where("Name.IsEmpty", "false", DataType.Boolean));

            Assert.Single(result.Data);
        }

        [Fact]
        public void The_convenience_tier_fails_exactly_as_it_does_unguarded()
        {
            // Not a refusal: the tier's promise is the error an unguarded query gives, and unguarded
            // this is the provider's own failure to translate.
            Assert.ThrowsAny<InvalidOperationException>(
                () => Guard(_db.Roles, DwTier.Convenience).ToList(Where("Name.IsEmpty", "false", DataType.Boolean)));
        }

        [Fact]
        public void A_dry_run_refuses_nothing()
        {
            PolicyQueryable<ZyRole> guarded = _db.Roles.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = DwTier.Strict, DryRun = true },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

            Assert.ThrowsAny<InvalidOperationException>(
                () => guarded.ToList(Where("Name.IsEmpty", "false", DataType.Boolean)));
        }

        // ---- the trace says which of the two it was -------------------------------------------------------

        [Fact]
        public void The_trace_records_why_the_path_was_refused()
        {
            PolicyQueryable<ZyRole> guarded = Guard(_db.Roles, DwTier.Strict);

            Assert.ThrowsAny<PolicyException>(
                () => guarded.ToList(Where("Name.IsEmpty", "false", DataType.Boolean)));

            PolicyTrace? trace = guarded.LastTrace;

            Assert.NotNull(trace);
            Assert.Contains(
                trace!.Decisions,
                decision => decision.Reason is not null && decision.Reason.Contains("cannot compute"));

            _out.WriteLine(string.Join(
                " | ",
                trace.Decisions.Select(decision => $"{decision.FieldPath} {decision.Action} {decision.Reason}")));
        }
    }
}
