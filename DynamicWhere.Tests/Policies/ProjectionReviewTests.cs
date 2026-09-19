using System.ComponentModel.DataAnnotations.Schema;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
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
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SystemsCorp.Payroll
{
    /// <summary>An application type whose namespace only starts with the word System.</summary>
    public sealed class RvPay
    {
        public string? Grade { get; set; }

        [DwDenied]
        public string? Salary { get; set; }
    }
}

namespace DynamicWhere.Tests.Policies
{
    // --------------------------------------------------------------------------------- the database

    public class RvRole
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public int? KeeperId { get; set; }

        public RvKeeper? Keeper { get; set; }
    }

    public class RvKeeper
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwDenied]
        public string? Ssn { get; set; }
    }

    /// <summary>An entity with values EF Core maps through converters, and a denial only beneath a navigation.</summary>
    public class RvAsset
    {
        public int Id { get; set; }

        public Uri? Home { get; set; }

        public RvMoney Price { get; set; }

        [NotMapped]
        public List<string> Tags { get; set; } = new();

        public int? KeeperId { get; set; }

        public RvKeeper? Keeper { get; set; }
    }

    public readonly record struct RvMoney(decimal Amount);

    /// <summary>The same shape with a denied column, so a projection is always needed.</summary>
    public class RvSealedAsset
    {
        public int Id { get; set; }

        [DwDenied]
        public string? Serial { get; set; }

        public Uri? Home { get; set; }

        public RvMoney Price { get; set; }

        [NotMapped]
        public string Display => Serial ?? "none";

        [NotMapped]
        public List<string> Tags { get; set; } = new();
    }

    /// <summary>A table-per-hierarchy base with a denial only beneath a navigation it does not load.</summary>
    public abstract class RvVehicle
    {
        public int Id { get; set; }

        public string Plate { get; set; } = string.Empty;

        public int? KeeperId { get; set; }

        public RvKeeper? Keeper { get; set; }
    }

    public class RvTruck : RvVehicle
    {
        public int Axles { get; set; }
    }

    /// <summary>An entity whose owned member has a denied property with no setter.</summary>
    public class RvShop
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public RvAddress Address { get; set; } = new();
    }

    public class RvAddress
    {
        public RvAddress()
        {
        }

        public RvAddress(string city, string? zip)
        {
            City = city;
            Zip = zip;
        }

        public string City { get; set; } = string.Empty;

        [DwDenied]
        public string? Zip { get; }
    }

    /// <summary>An entity whose denied members are not simple values: nothing else is denied.</summary>
    public class RvVault
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwDenied]
        public byte[] Blob { get; set; } = Array.Empty<byte>();

        [DwDenied]
        public RvVaultPlace? Place { get; set; }
    }

    public class RvVaultPlace
    {
        public string Aisle { get; set; } = string.Empty;
    }

    /// <summary>An entity whose child's key no caller may see, reached through another navigation.</summary>
    public class RvHouse
    {
        public int Id { get; set; }

        public int? MainId { get; set; }

        public RvGroup? Main { get; set; }
    }

    public class RvGroup
    {
        [DwDenied]
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int? LeadId { get; set; }

        public RvLead? Lead { get; set; }
    }

    public class RvLead
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>An entity whose children are scoped to a tenant: the scope filters parents, not children.</summary>
    public class RvFamily
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<RvKid> Kids { get; set; } = new();
    }

    public class RvKid
    {
        public int Id { get; set; }

        public int RvFamilyId { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwForceWhere(Operator.Equal, ContextValue = "TenantId")]
        public int TenantId { get; set; }
    }

    /// <summary>An entity whose posts arrive through an injected lazy loader.</summary>
    public class RvBlog
    {
        private List<RvPost>? _posts;

        public RvBlog()
        {
        }

        private RvBlog(ILazyLoader lazyLoader) => LazyLoader = lazyLoader;

        private ILazyLoader? LazyLoader { get; set; }

        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<RvPost> Posts
        {
            get
            {
                if (LazyLoader is not null)
                {
                    LazyLoader.Load(this, ref _posts);
                }

                return _posts ??= new List<RvPost>();
            }
            set => _posts = value;
        }
    }

    public class RvPost
    {
        public int Id { get; set; }

        public int RvBlogId { get; set; }

        public string Title { get; set; } = string.Empty;

        [DwDenied]
        public string? Draft { get; set; }
    }

    public sealed class ProjectionReviewContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ProjectionReviewContext(SqliteConnection connection) => _connection = connection;

        public DbSet<RvRole> Roles => Set<RvRole>();

        public DbSet<RvAsset> Assets => Set<RvAsset>();

        public DbSet<RvSealedAsset> SealedAssets => Set<RvSealedAsset>();

        public DbSet<RvVehicle> Vehicles => Set<RvVehicle>();

        public DbSet<RvShop> Shops => Set<RvShop>();

        public DbSet<RvVault> Vaults => Set<RvVault>();

        public DbSet<RvHouse> Houses => Set<RvHouse>();

        public DbSet<RvFamily> Families => Set<RvFamily>();

        public DbSet<RvBlog> Blogs => Set<RvBlog>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<RvAsset>().Property(a => a.Price).HasConversion(m => m.Amount, a => new RvMoney(a));
            model.Entity<RvSealedAsset>().Property(a => a.Price).HasConversion(m => m.Amount, a => new RvMoney(a));
            model.Entity<RvTruck>();
            model.Entity<RvShop>().OwnsOne(s => s.Address, a => a.Property(x => x.Zip));
            model.Entity<RvVault>().OwnsOne(v => v.Place);
            model.Entity<RvBlog>().Navigation(b => b.Posts).HasField("_posts");
        }
    }

    // --------------------------------------------------------------------------------- the rows

    [DwEntity(RequirePolicy = true)]
    public sealed class RvRoleRow
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        [DwDenied]
        public string? Tenant { get; set; }

        public RvKeeper? Keeper { get; set; }
    }

    public sealed class RvContact
    {
        public RvContact()
        {
        }

        public RvContact(string email) => Email = email;

        public string Phone { get; set; } = string.Empty;

        [DwDenied]
        public string? Email { get; }
    }

    [DwEntity(RequirePolicy = true)]
    public sealed class RvContactRow
    {
        public int Id { get; set; }

        [DwDenied]
        public string? Tenant { get; set; }

        public RvContact? Contact { get; set; }
    }

    public sealed class RvLine
    {
        public string Sku { get; set; } = string.Empty;

        [DwDenied]
        public string? Cost { get; set; }
    }

    /// <summary>Framework types holding a policed type: the walker does not enter them.</summary>
    [DwEntity(RequirePolicy = true)]
    public sealed class RvKeyedRow
    {
        public int Id { get; set; }

        public Dictionary<string, RvLine> BySku { get; set; } = new();

        public object? Payload { get; set; }
    }

    public sealed class RvLevel1
    {
        public RvLevel2? Next { get; set; }
    }

    public sealed class RvLevel2
    {
        public RvLevel3? Next { get; set; }
    }

    public sealed class RvLevel3
    {
        public string Name { get; set; } = string.Empty;

        public RvLevel4? Next { get; set; }
    }

    public sealed class RvLevel4
    {
        [DwDenied]
        public string? Secret { get; set; }
    }

    /// <summary>A denial five segments down, deeper than the walker gives a fragment.</summary>
    [DwEntity(RequirePolicy = true)]
    public sealed class RvDeepRow
    {
        public int Id { get; set; }

        [DwDenied]
        public string? Tenant { get; set; }

        public RvLevel1? First { get; set; }
    }

    public sealed class RvStaffRow
    {
        public int Id { get; set; }

        public SystemsCorp.Payroll.RvPay? Pay { get; set; }
    }

    [DwEntity(DefaultOrder = "Label desc")]
    public sealed class RvLabelRow
    {
        public int Id { get; set; }

        public string Label { get; set; } = string.Empty;
    }

    [DwEntity(RequirePolicy = true)]
    public sealed class RvReservedRow
    {
        public int Id { get; set; }

        [DwDenied]
        public string? Tenant { get; set; }

        public List<string> Cast { get; set; } = new();
    }

    public sealed class RvItem
    {
        public string Name { get; set; } = string.Empty;

        [DwDenied]
        public string? Hidden { get; set; }
    }

    [DwEntity(RequirePolicy = true)]
    public sealed class RvArrayRow
    {
        public int Id { get; set; }

        public RvItem[] Items { get; set; } = Array.Empty<RvItem>();
    }

    /// <summary>
    /// What the 3.2.0 review found in the first cut of the synthesized projection, and the fixes for
    /// it: denials the gate could not see, projections EF Core cannot translate, and members the first
    /// cut lost that 3.1.0 returned.
    /// </summary>
    public sealed class ProjectionReviewTests : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ProjectionReviewContext _db;

        public ProjectionReviewTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _db = new ProjectionReviewContext(_connection);
            _db.Database.EnsureCreated();

            RvKeeper keeper = new() { Name = "K", Ssn = "ssn-secret" };

            _db.Roles.Add(new RvRole { Code = "R1", Keeper = keeper });
            _db.Assets.Add(new RvAsset { Home = new Uri("https://example.test/"), Price = new RvMoney(12.5m), Keeper = keeper });
            _db.SealedAssets.Add(new RvSealedAsset { Serial = "serial-secret", Home = new Uri("https://example.test/"), Price = new RvMoney(7m) });
            _db.Vehicles.Add(new RvTruck { Plate = "T42", Axles = 3, Keeper = keeper });
            _db.Shops.Add(new RvShop { Name = "S1", Address = new RvAddress("Basra", "zip-secret") });
            _db.Vaults.Add(new RvVault { Name = "V1", Blob = new byte[] { 9 }, Place = new RvVaultPlace { Aisle = "A1" } });
            _db.Houses.Add(new RvHouse { Main = new RvGroup { Name = "G", Lead = new RvLead { Name = "L" } } });
            _db.Families.Add(new RvFamily
            {
                Name = "F1",
                Kids = { new RvKid { Name = "own", TenantId = 1 }, new RvKid { Name = "other", TenantId = 2 } }
            });
            _db.Blogs.Add(new RvBlog { Name = "B1", Posts = { new RvPost { Title = "P1", Draft = "draft-secret" } } });

            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static DwPolicyContext Caller() =>
            new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1").WithValue("TenantId", 1);

        private static PolicyResolver Resolver(params IDwPolicyProvider[] more) =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() }.Concat(more).ToArray());

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict, params IDwPolicyProvider[] more)
            where T : class =>
            source.ApplyPolicy(Caller(), new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } }, Resolver(more));

        private static Filter Selecting(params string[] fields) => new() { Selects = fields.ToList() };

        // ---------------------------------------------------------------- entity queries keep 3.1.0's shape

        /// <summary>
        /// A denial beneath a navigation the query does not load reaches nothing, so it needs no
        /// projection: the entity comes back as EF Core loads it, converted members and all.
        /// </summary>
        [Fact]
        public void A_denial_beneath_a_navigation_nothing_loads_leaves_the_entity_as_loaded()
        {
            PolicyQueryable<RvAsset> guarded = Guard(_db.Assets);
            RvAsset asset = guarded.ToList(new Filter()).Data.Single();

            Assert.Equal(("https://example.test/", 12.5m), (asset.Home!.ToString(), asset.Price.Amount));
            Assert.DoesNotContain(guarded.LastTrace!.Decisions, decision => decision.Action == PolicyAction.Dropped);
        }

        /// <summary>A hierarchy keeps its runtime types and its abstract base, as it did in 3.1.0.</summary>
        [Fact]
        public void A_hierarchy_is_still_read_as_entities()
        {
            RvVehicle vehicle = Guard(_db.Vehicles).ToList(new Filter()).Data.Single();

            Assert.Equal(3, Assert.IsType<RvTruck>(vehicle).Axles);
        }

        /// <summary>
        /// When a projection is needed, an entity keeps every mapped column, converted ones included, and
        /// leaves out a member EF Core does not map, which would make it read the denied column.
        /// </summary>
        [Fact]
        public void A_needed_projection_keeps_converted_columns_and_skips_unmapped_members()
        {
            PolicyQueryable<RvSealedAsset> guarded = Guard(_db.SealedAssets, DwTier.Convenience);
            FilterResultOf<RvSealedAsset> result = new(guarded.ToList(new Filter(), getQueryString: true));

            Assert.Equal(("https://example.test/", 7m, "none"), (result.Row.Home!.ToString(), result.Row.Price.Amount, result.Row.Display));
            Assert.DoesNotContain("Serial", result.Sql, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A denied member that holds no simple value, a blob or an owned object, used to be passed over,
        /// so with nothing else denied the whole entity came back with it.
        /// </summary>
        [Fact]
        public void A_denied_member_that_is_not_a_simple_value_is_withheld()
        {
            RvVault vault = Guard(_db.Vaults).ToList(new Filter()).Data.Single();

            Assert.Equal("V1", vault.Name);
            Assert.Empty(vault.Blob);
            Assert.Null(vault.Place);
        }

        /// <summary>A denied owned property with no setter is left out of the owned member, which is kept.</summary>
        [Theory]
        [InlineData(DwTier.Convenience)]
        [InlineData(DwTier.Strict)]
        public void A_denied_property_with_no_setter_beneath_an_owned_member_is_left_out(DwTier tier)
        {
            RvShop shop = Guard(_db.Shops, tier).ToList(new Filter()).Data.Single();

            Assert.Equal(("Basra", (string?)null), (shop.Address.City, shop.Address.Zip));

            if (tier == DwTier.Strict)
            {
                Assert.Throws<PolicyException>(() => Guard(_db.Shops, tier).ToList(Selecting("Id", "Address")));
            }
            else
            {
                RvAddress named = Guard(_db.Shops, tier).ToList(Selecting("Id", "Address")).Data.Single().Address;

                Assert.Equal(("Basra", (string?)null), (named.City, named.Zip));
            }
        }

        /// <summary>
        /// A navigation a lazy loader can fill carries its denied values out after the query, so its
        /// entity needs the projection, whose rows have no loader.
        /// </summary>
        [Fact]
        public void A_navigation_a_lazy_loader_can_fill_needs_the_projection()
        {
            PolicyQueryable<RvBlog> guarded = Guard(_db.Blogs);
            RvBlog blog = guarded.ToList(new Filter()).Data.Single();

            Assert.Equal("B1", blog.Name);
            Assert.Empty(blog.Posts);
            Assert.Contains(guarded.LastTrace!.Decisions, decision => decision.FieldPath == "Posts.Draft");
        }

        /// <summary>
        /// A forced scope on a list's elements filters the rows that hold the list, never the elements,
        /// so a list kept whole would carry every tenant's children. A projection that is needed anyway
        /// leaves it out whole; the scope alone asks for none, as it never did.
        /// </summary>
        [Fact]
        public void A_list_whose_elements_carry_a_forced_scope_is_left_out_of_a_needed_projection()
        {
            PolicyQueryable<RvFamilyRow> guarded = Guard(_db.Families.Select(f => new RvFamilyRow
            {
                Id = f.Id,
                Tenant = f.Name,
                Kids = f.Kids.Select(k => new RvKidRow { Name = k.Name, TenantId = k.TenantId }).ToList()
            }));

            RvFamilyRow row = guarded.ToList(new Filter()).Data.Single();

            Assert.Empty(row.Kids);
            Assert.Contains(guarded.LastTrace!.Decisions, decision => decision.FieldPath == "Kids"
                && decision.Reason == "left out whole: a scope forced beneath it cannot be applied to what it holds");
        }

        /// <summary>
        /// A navigation named through another keeps the key of each node it passes through, and a
        /// denied one refuses the projection, as naming a sibling of the key always did.
        /// </summary>
        [Theory]
        [InlineData(DwTier.Convenience)]
        [InlineData(DwTier.Strict)]
        public void A_navigation_named_through_one_whose_key_is_denied_is_refused(DwTier tier)
        {
            PolicyException refused = Assert.Throws<PolicyException>(
                () => Guard(_db.Houses, tier).ToList(Selecting("Id", "Main.Lead")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, refused.ErrorCode);
        }

        // ---------------------------------------------------------------- projected rows

        /// <summary>
        /// A member the projection never assigns holds its default and is not carried: narrowing it
        /// would read it through EF.Property, which EF Core cannot translate for an unbound member.
        /// </summary>
        [Fact]
        public void A_member_the_projection_does_not_assign_is_not_narrowed()
        {
            RvRoleRow row = Guard(_db.Roles.Select(r => new RvRoleRow { Id = r.Id, Code = r.Code }))
                .ToList(new Filter()).Data.Single();

            Assert.Equal(("R1", (RvKeeper?)null), (row.Code, row.Keeper));
        }

        /// <summary>A composed projection feeds the terminal only what it selected.</summary>
        [Fact]
        public void A_composed_projection_then_a_terminal_runs()
        {
            Assert.Equal("R1", Guard(_db.Roles).Filter(new Filter()).ToList(new Filter()).Data.Single().Code);
            Assert.Equal("R1", Guard(_db.Roles).Select(new List<string> { "Id", "Code" }).ToList(new Filter()).Data.Single().Code);
        }

        /// <summary>
        /// A property with no setter is not one the gate can narrow a member around, and a member built
        /// by a constructor is not one the core can narrow: such a member is left out whole.
        /// </summary>
        [Fact]
        public void A_member_built_by_a_constructor_with_a_hidden_denial_is_left_out()
        {
            PolicyQueryable<RvContactRow> guarded = Guard(
                _db.Roles.Select(r => new RvContactRow { Id = r.Id, Contact = new RvContact(r.Code) }));

            Assert.Null(guarded.ToList(new Filter()).Data.Single().Contact);
            Assert.Contains(guarded.LastTrace!.Decisions, decision => decision.FieldPath == "Contact"
                && decision.Reason == "left out whole: the projection builds it in a way the core cannot narrow");
        }

        /// <summary>
        /// A denial five segments down has no fragment, since the walker stops at four. A projection
        /// carrying the member whole used to return it; the member is now narrowed to what the policy
        /// can speak about.
        /// </summary>
        [Fact]
        public void A_denial_deeper_than_the_walker_goes_is_not_carried()
        {
            RvDeepRow row = Guard(_db.Roles.Select(r => new RvDeepRow
            {
                Id = r.Id,
                First = new RvLevel1 { Next = new RvLevel2 { Next = new RvLevel3 { Name = r.Code, Next = new RvLevel4 { Secret = "deep-secret" } } } }
            })).ToList(new Filter()).Data.Single();

            Assert.Equal("R1", row.First!.Next!.Next!.Name);
            Assert.Null(row.First.Next.Next.Next?.Secret);
        }

        /// <summary>
        /// The same member named in Selects: the strict tier refuses a denial it cannot name, and the
        /// convenience tier narrows it away.
        /// </summary>
        [Theory]
        [InlineData(DwTier.Convenience)]
        [InlineData(DwTier.Strict)]
        public void Naming_a_member_with_a_denial_deeper_than_the_walker_goes(DwTier tier)
        {
            IQueryable<RvDeepRow> rows = _db.Roles.Select(r => new RvDeepRow
            {
                Id = r.Id,
                First = new RvLevel1 { Next = new RvLevel2 { Next = new RvLevel3 { Name = r.Code, Next = new RvLevel4 { Secret = "deep-secret" } } } }
            });

            if (tier == DwTier.Strict)
            {
                Assert.Throws<PolicyException>(() => Guard(rows, tier).ToList(Selecting("Id", "First")));

                return;
            }

            RvDeepRow row = Guard(rows, tier).ToList(Selecting("Id", "First")).Data.Single();

            Assert.Null(row.First!.Next!.Next!.Next?.Secret);
        }

        /// <summary>A default order computed on the client cannot be translated, so it is not applied.</summary>
        [Fact]
        public void A_default_computed_by_an_application_method_is_not_applied()
        {
            IQueryable<RvLabelRow> rows = _db.Roles.Select(r => new RvLabelRow { Id = r.Id, Label = Decorate(r.Code) });

            Assert.Equal("[R1]", Guard(rows).ToList(new Filter()).Data.Single().Label);
        }

        private static string Decorate(string code) => "[" + code + "]";

        /// <summary>A member named with a word the parser keeps cannot be projected, so it is skipped.</summary>
        [Fact]
        public void A_member_named_with_a_parser_word_is_skipped()
        {
            RvReservedRow row = Guard(_db.Roles.Select(r => new RvReservedRow
                {
                    Id = r.Id,
                    Tenant = r.Code,
                    Cast = _db.Roles.Where(x => x.Id == r.Id).Select(x => x.Code).ToList()
                }))
                .ToList(new Filter()).Data.Single();

            Assert.Equal((1, (string?)null), (row.Id, row.Tenant));
        }

        /// <summary>An array needing narrowing is left out, since the core can only bind a list.</summary>
        [Fact]
        public void An_array_that_needs_narrowing_is_left_out()
        {
            PolicyQueryable<RvArrayRow> guarded = Guard(_db.Roles.Select(r => new RvArrayRow
            {
                Id = r.Id,
                Items = _db.Roles.Where(x => x.Id == r.Id).Select(x => new RvItem { Name = x.Code, Hidden = "hidden" }).ToArray()
            }));

            Assert.Empty(guarded.ToList(new Filter()).Data.Single().Items);
            Assert.Contains(guarded.LastTrace!.Decisions, decision => decision.FieldPath == "Items"
                && decision.Reason!.StartsWith("left out whole", StringComparison.Ordinal));
        }

        // ---------------------------------------------------------------- what the policy cannot see

        /// <summary>
        /// A framework collection of a policed type, and a member typed object, are not entered by the
        /// walker. Rows in memory leave them out once a projection is needed, which a denial inside them
        /// now asks for; naming one is refused.
        /// </summary>
        [Theory]
        [InlineData(DwTier.Convenience)]
        [InlineData(DwTier.Strict)]
        public void A_framework_collection_of_a_policed_type_is_not_returned(DwTier tier)
        {
            RvKeyedRow[] rows =
            {
                new() { Id = 1, BySku = { ["a"] = new RvLine { Sku = "a", Cost = "cost-secret" } }, Payload = new RvLine { Cost = "payload-secret" } }
            };

            RvKeyedRow row = Guard(rows.AsQueryable(), tier).ToList(new Filter()).Data.Single();

            Assert.Empty(row.BySku);
            Assert.Null(row.Payload);
            Assert.Throws<PolicyException>(() => Guard(rows.AsQueryable(), tier).ToList(Selecting("Id", "BySku")));
        }

        /// <summary>
        /// A namespace that only starts with the word System is the application's. The walker read it
        /// as the framework's and put no fragment beneath its types, so a denied field there was
        /// returned, filtered on and ordered by.
        /// </summary>
        [Theory]
        [InlineData(DwTier.Convenience)]
        [InlineData(DwTier.Strict)]
        public void A_namespace_starting_with_System_is_policed(DwTier tier)
        {
            RvStaffRow[] rows = { new() { Id = 1, Pay = new SystemsCorp.Payroll.RvPay { Grade = "G1", Salary = "salary-secret" } } };

            Filter where = new()
            {
                ConditionGroup = new ConditionGroup
                {
                    Conditions = { new Condition { Field = "Pay.Salary", DataType = DataType.Text, Operator = Operator.Equal, Values = { "x" } } }
                }
            };

            Assert.Throws<PolicyException>(() => Guard(rows.AsQueryable(), tier).ToList(where));
            Assert.Contains(
                new AttributePolicyProvider().GetFragments(typeof(RvStaffRow), Caller()),
                fragment => fragment.FieldPath == "Pay.Salary");
        }

        /// <summary>A runtime rule on a path reached through a cycle is found beneath a member, as its fragment names it.</summary>
        [Fact]
        public void A_rule_on_a_path_through_a_cycle_is_seen_beneath_a_member()
        {
            RvLink[] rows = { new() { Id = 1, Head = new RvNode { Name = "n1", Next = new RvNode { Name = "n2-secret" } } } };
            FakePolicyProvider rule = new FakePolicyProvider().Add(
                "Head.Next.Name", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);

            PolicyQueryable<RvLink> guarded = Guard(rows.AsQueryable(), DwTier.Convenience, rule);

            Assert.Null(guarded.ToList(new Filter()).Data.Single().Head);
            Assert.Throws<PolicyException>(() => Guard(rows.AsQueryable(), DwTier.Convenience, rule).ToList(Selecting("Id", "Head")));
        }

        /// <summary>
        /// A rule on a path the type does not have, one left behind by a member since removed, holds nothing
        /// a projection could carry, so it does not stop a caller naming the member above it.
        /// </summary>
        [Fact]
        public void A_rule_on_a_path_that_does_not_exist_beneath_a_member_changes_nothing()
        {
            RvLink[] rows = { new() { Id = 1, Head = new RvNode { Name = "n1" } } };
            FakePolicyProvider stale = new FakePolicyProvider().Add(
                "Head.Fax", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);

            RvLink row = Guard(rows.AsQueryable(), DwTier.Strict, stale).ToList(Selecting("Id", "Head")).Data.Single();

            Assert.Equal("n1", row.Head!.Name);
        }

        /// <summary>
        /// A navigation EF Core's lazy-loading proxies can fill counts as loaded as well, and the rows the
        /// projection returns are plain objects, with no proxy left to load it.
        /// </summary>
        [Fact]
        public void A_navigation_a_lazy_loading_proxy_can_fill_needs_the_projection()
        {
            using SqliteConnection connection = new("DataSource=:memory:");
            connection.Open();

            using ProxyContext proxies = new(connection);

            proxies.Database.EnsureCreated();
            proxies.Blogs.Add(new PxBlog { Name = "X1", Posts = { new PxPost { Title = "T", Draft = "proxy-draft" } } });
            proxies.SaveChanges();
            proxies.ChangeTracker.Clear();

            PolicyQueryable<PxBlog> guarded = Guard(proxies.Blogs);
            PxBlog blog = guarded.ToList(new Filter()).Data.Single();

            Assert.Equal("X1", blog.Name);
            Assert.Empty(blog.Posts);
            Assert.Equal(typeof(PxBlog), blog.GetType());
            Assert.Contains(guarded.LastTrace!.Decisions, decision => decision.FieldPath == "Posts.Draft");
        }

        /// <summary>
        /// An include on a join's inner source loads a navigation of the rows the join returns, and it is
        /// not on the chain the paths are read from, so every navigation counts as loaded.
        /// </summary>
        [Fact]
        public void An_include_anywhere_in_the_query_counts()
        {
            IQueryable<RvRole> rows = _db.Assets.Join(_db.Roles.Include(r => r.Keeper), a => a.Id, r => r.Id, (a, r) => r);

            PolicyQueryable<RvRole> guarded = Guard(rows);

            Assert.Null(guarded.ToList(new Filter()).Data.Single().Keeper);
            Assert.Contains(guarded.LastTrace!.Decisions, decision => decision.FieldPath == "Keeper.Ssn");
        }

        /// <summary>Holds a reference to a copy of the result and its SQL, to keep a test to one statement per line.</summary>
        private sealed class FilterResultOf<T>
            where T : class
        {
            internal FilterResultOf(DynamicWhere.ex.Classes.Result.FilterResult<T> result)
            {
                Row = result.Data.Single();
                Sql = result.QueryString ?? string.Empty;
            }

            internal T Row { get; }

            internal string Sql { get; }
        }
    }

    public sealed class RvKidRow
    {
        public string Name { get; set; } = string.Empty;

        [DwForceWhere(Operator.Equal, ContextValue = "TenantId")]
        public int TenantId { get; set; }
    }

    [DwEntity(RequirePolicy = true)]
    public sealed class RvFamilyRow
    {
        public int Id { get; set; }

        [DwDenied]
        public string? Tenant { get; set; }

        public List<RvKidRow> Kids { get; set; } = new();
    }

    public class PxBlog
    {
        public virtual int Id { get; set; }

        public virtual string Name { get; set; } = string.Empty;

        public virtual List<PxPost> Posts { get; set; } = new();
    }

    public class PxPost
    {
        public virtual int Id { get; set; }

        public virtual int PxBlogId { get; set; }

        public virtual string Title { get; set; } = string.Empty;

        [DwDenied]
        public virtual string? Draft { get; set; }
    }

    public sealed class ProxyContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ProxyContext(SqliteConnection connection) => _connection = connection;

        public DbSet<PxBlog> Blogs => Set<PxBlog>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseSqlite(_connection).UseLazyLoadingProxies();
    }

    public sealed class RvNode
    {
        public string Name { get; set; } = string.Empty;

        public RvNode? Next { get; set; }
    }

    [DwEntity(RequirePolicy = true)]
    public sealed class RvLink
    {
        public int Id { get; set; }

        public RvNode? Head { get; set; }
    }
}
