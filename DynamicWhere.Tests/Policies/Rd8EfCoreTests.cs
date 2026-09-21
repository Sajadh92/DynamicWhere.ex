using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests.Policies
{
    public class Rd8Pet
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwDenied]
        public decimal? Price { get; set; }

        public decimal? Weight { get; set; }
    }

    public class Rd8Hound : Rd8Pet
    {
        [DwMask(MaskStrategy.Full)]
        public string? Chip { get; set; }
    }

    public class Rd8N1 { public int Id { get; set; } public Rd8N2? B { get; set; } }

    public class Rd8N2 { public int Id { get; set; } public Rd8N3? C { get; set; } }

    public class Rd8N3 { public int Id { get; set; } public Rd8N4? D { get; set; } }

    public class Rd8N4 { public int Id { get; set; } public Rd8N5? E { get; set; } }

    public class Rd8N5
    {
        public int Id { get; set; }

        [DwAudit]
        [DwMask(MaskStrategy.Full)]
        public string? Card { get; set; }
    }

    public sealed class Rd8Context : DbContext
    {
        private readonly SqliteConnection _connection;

        public Rd8Context(SqliteConnection connection) => _connection = connection;

        public DbSet<Rd8Pet> Pets => Set<Rd8Pet>();

        public DbSet<Rd8N1> Roots => Set<Rd8N1>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model) => model.Entity<Rd8Hound>();
    }

    /// <summary>
    /// Round 8's findings on a database rather than on rows in memory: what a provider translates, and
    /// what EF Core materializes, is where each of them was reachable in a deployment.
    /// </summary>
    public sealed class Rd8EfCoreTests : IDisposable
    {
        private readonly SqliteConnection _connection = new("DataSource=:memory:");
        private readonly Rd8Context _db;

        public Rd8EfCoreTests()
        {
            _connection.Open();
            _db = new Rd8Context(_connection);
            _db.Database.EnsureCreated();

            _db.Pets.Add(new Rd8Hound { Id = 1, Name = "rex", Price = 5100m, Weight = 30m, Chip = "CHIP-123" });
            _db.Pets.Add(new Rd8Pet { Id = 2, Name = "tom", Price = 900m, Weight = 4m });
            _db.Roots.Add(new Rd8N1 { Id = 1, B = new() { Id = 2, C = new() { Id = 3, D = new() { Id = 4, E = new() { Id = 5, Card = "4111111111111111" } } } } });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> query, DwTier tier) where T : class =>
            query.ApplyPolicy(
                Caller(),
                new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static Filter Where(string field, object value) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = new() { new Condition { Sort = 0, Field = field, DataType = DataType.Number, Operator = Operator.GreaterThan, Values = new() { value } } }
            },
            Selects = new() { "Id" }
        };

        /// <summary>The provider translates it, which is what made it a way round the denial.</summary>
        [Fact]
        public void The_database_answers_a_path_beneath_a_nullable_column()
        {
            Assert.Equal(new[] { 1 }, _db.Pets.ToList(Where("Price.Value", 1000)).Data!.Select(pet => pet.Id));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Beneath_a_denied_column_it_is_refused_and_beneath_an_open_one_it_runs(DwTier tier)
        {
            Assert.Equal(
                PolicyErrorCode.FieldDeniedForWhere,
                Assert.Throws<PolicyException>(() => Guard(_db.Pets, tier).ToList(Where("Price.Value", 1000))).ErrorCode);

            Assert.Equal(new[] { 1 }, Guard(_db.Pets, tier).ToList(Where("Weight.Value", 10)).Data!.Select(pet => pet.Id));
        }

        /// <summary>A hierarchy's rows are the types the database says they are, with the members those declare.</summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_masked_column_only_a_derived_entity_maps_is_masked(DwTier tier)
        {
            List<Rd8Pet> pets = Guard(_db.Pets.OrderBy(pet => pet.Id), tier).ToList(new Filter { Selects = new() { "Id", "Name" } }).Data!;

            Assert.Equal(new[] { "rex", "tom" }, pets.Select(pet => pet.Name));

            List<Rd8Hound> hounds = Guard(_db.Set<Rd8Hound>(), tier).ToList(new Filter()).Data!;

            Assert.Equal("********", Assert.Single(hounds).Chip);
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_masked_column_five_segments_down_an_included_graph_is_masked(DwTier tier)
        {
            IQueryable<Rd8N1> graph = _db.Roots
                .Include(root => root.B!).ThenInclude(b => b.C!).ThenInclude(c => c.D!).ThenInclude(d => d.E);

            Rd8N1 unguarded = graph.AsNoTracking().Single();

            Assert.Equal("4111111111111111", unguarded.B!.C!.D!.E!.Card);

            // The navigation is kept whole. Read as a denial, the masked column five segments down had the
            // projection gate leave the whole included graph out, which withholds what the policy allows.
            Rd8N1 row = Guard(graph, tier).ToList(new Filter()).Data!.Single();

            Assert.NotNull(row.B?.C?.D?.E);
            Assert.Equal("****************", row.B!.C!.D!.E!.Card);
        }

        /// <summary>
        /// A typed projection builds the real types, so the rows show the audited member whether the
        /// projection named it or not. Named, the gate was asked about it and recorded it; left out, the
        /// row built for the projection holds nothing in it and nothing was read.
        /// </summary>
        [Fact]
        public void An_audited_column_past_the_walk_is_recorded_once_when_named_and_not_when_left_out()
        {
            IQueryable<Rd8N1> graph = _db.Roots;

            PolicyQueryable<Rd8N1> Deep(DwPolicyContext caller) => graph.ApplyPolicy(
                caller,
                new DwPolicyOptions { Tier = DwTier.Strict, Caps = { MaxNavigationDepth = 6 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

            DwPolicyContext named = Caller();

            Rd8N1 row = Deep(named).ToList(new Filter { Selects = new() { "Id", "B.C.D.E.Card" } }).Data!.Single();

            Assert.Equal("****************", row.B!.C!.D!.E!.Card);
            Assert.Single(named.PendingAuditEvents, read => read.FieldPath == "B.C.D.E.Card");

            DwPolicyContext leftOut = Caller();

            Deep(leftOut).ToList(new Filter { Selects = new() { "Id", "B.C.D.E.Id" } });

            Assert.DoesNotContain(leftOut.PendingAuditEvents, read => read.FieldPath == "B.C.D.E.Card");
        }
    }
}
