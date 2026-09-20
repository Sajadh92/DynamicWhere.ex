using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>The same model on EF Core's own in-memory provider.</summary>
    public sealed class Rv3MemoryContext : DbContext
    {
        private readonly string _name;

        public Rv3MemoryContext(string name) => _name = name;

        public DbSet<Rv3Role> Roles => Set<Rv3Role>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseInMemoryDatabase(_name);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Rv3Role>().OwnsOne(role => role.Name);
            model.Entity<Rv3Role>().OwnsOne(role => role.Other);
            model.Entity<Rv3Role>().Ignore(role => role.Display);
        }
    }

    /// <summary>
    /// REVIEW PROBE ONLY (round 3), and a ruling-out rather than a finding.
    /// </summary>
    /// <remarks>
    /// The provider test behind the 3.3.0 refusal asks "is this EF Core's own provider", not "does
    /// this provider have to translate the path". The strongest candidate for a provider that is EF
    /// Core's own and yet answers a member the model does not map is EF Core's in-memory provider,
    /// which has no SQL to generate. It does not: it runs the same translating visitor and refuses
    /// the same members a relational provider refuses, so holding its rows to EF Core's model
    /// refuses nothing that would have run.
    /// <para>
    /// This suite is the reason the project references <c>Microsoft.EntityFrameworkCore.InMemory</c>.
    /// Drop both together if the ruling-out is not worth keeping.
    /// </para>
    /// </remarks>
    public sealed class Rv3InMemoryProviderProbe : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly Rv3MemoryContext _db;

        public Rv3InMemoryProviderProbe(ITestOutputHelper output)
        {
            _out = output;
            _db = new Rv3MemoryContext(Guid.NewGuid().ToString("N"));
            _db.Roles.Add(new Rv3Role
            {
                Id = 1,
                Code = "admin",
                Secret = "S-TOP",
                Cost = "C-9",
                Name = new Rv3Text { Ar = "AR", En = "Admin" },
                Other = new Rv3Text { Ar = "ar2", En = "Other" }
            });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose() => _db.Dispose();

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
                Conditions =
                {
                    new Condition { Field = field, DataType = type, Operator = Operator.Equal, Values = { value } }
                }
            }
        };

        /// <summary>
        /// The provider with no SQL to write still refuses a getter over two columns, an unmapped
        /// getter on the entity, and an order by one. So the shape's answer for it is right.
        /// </summary>
        [Fact]
        public void G1_The_in_memory_provider_refuses_the_same_members_a_relational_one_does()
        {
            string Try(Func<int> read)
            {
                try
                {
                    return read().ToString();
                }
                catch (Exception failure)
                {
                    return failure.GetType().Name;
                }
            }

            string owned = Try(() => _db.Roles.Where(role => !role.Name.IsEmpty).Count());
            string entity = Try(() => _db.Roles.Where(role => role.Display == "admin:1").Count());
            string ordered = Try(() => _db.Roles.OrderBy(role => role.Display).Count());

            _out.WriteLine($"G1 unguarded owned={owned} entity={entity} ordered={ordered}");

            Assert.Equal(nameof(InvalidOperationException), owned);
            Assert.Equal(nameof(InvalidOperationException), entity);
            Assert.Equal(nameof(InvalidOperationException), ordered);
        }

        /// <summary>
        /// So the strict tier's refusal replaces a failure rather than taking away an answer, which
        /// is the whole premise of the check.
        /// </summary>
        [Fact]
        public void G2_The_refusal_replaces_a_failure_rather_than_an_answer()
        {
            Exception? owned = Record.Exception(
                () => Guard(_db.Roles, DwTier.Strict).ToList(Where("Name.IsEmpty", "false", DataType.Boolean)));

            Exception? entity = Record.Exception(
                () => Guard(_db.Roles, DwTier.Strict).ToList(Where("Display", "admin:1")));

            Exception? order = Record.Exception(
                () => Guard(_db.Roles, DwTier.Strict).ToList(new Filter
                {
                    Orders = new List<OrderBy> { new() { Field = "Display", Direction = Direction.Ascending } }
                }));

            _out.WriteLine($"G2 owned={owned?.GetType().Name} entity={entity?.GetType().Name} "
                           + $"order={order?.GetType().Name}");

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, Assert.IsAssignableFrom<PolicyException>(owned).ErrorCode);
            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, Assert.IsAssignableFrom<PolicyException>(entity).ErrorCode);
            Assert.Equal(PolicyErrorCode.FieldDeniedForOrder, Assert.IsAssignableFrom<PolicyException>(order).ErrorCode);
        }

        /// <summary>The mapped members still answer, so nothing usable was taken away.</summary>
        [Fact]
        public void G3_The_mapped_members_still_answer()
        {
            Assert.Single(Guard(_db.Roles, DwTier.Strict).ToList(Where("Code", "admin")).Data);
            Assert.Single(Guard(_db.Roles, DwTier.Strict).ToList(Where("Name.En", "Admin")).Data);
        }

        /// <summary>A denied field is refused and withheld here exactly as anywhere else.</summary>
        [Fact]
        public void G4_Denials_are_unaffected_on_the_in_memory_provider()
        {
            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(_db.Roles, DwTier.Strict).ToList(Where("Secret", "S-TOP")));

            Rv3Role row = Assert.Single(Guard(_db.Roles, DwTier.Strict).ToList(new Filter()).Data);

            _out.WriteLine($"G4 {refusal.ErrorCode} secret='{row.Secret}' cost='{row.Cost}'");

            Assert.True(string.IsNullOrEmpty(row.Secret), $"Secret leaked: '{row.Secret}'");
            Assert.True(string.IsNullOrEmpty(row.Cost), $"Cost leaked: '{row.Cost}'");
        }
    }
}
