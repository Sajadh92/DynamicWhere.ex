using System.Collections;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
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

namespace DynamicWhere.Tests.Policies
{
    // --------------------------------------------------------------------------------- the database

    public class RcCustomer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<RcCard> Cards { get; set; } = new();

        public List<RcOrder> Orders { get; set; } = new();
    }

    public class RcCard
    {
        public int Id { get; set; }

        public int RcCustomerId { get; set; }

        public string Label { get; set; } = string.Empty;

        [DwDenied]
        public string? Pan { get; set; }
    }

    public class RcOrder
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public int RcCustomerId { get; set; }

        public RcCustomer? Customer { get; set; }

        public List<RcLine> Lines { get; set; } = new();
    }

    public class RcLine
    {
        public int Id { get; set; }

        public int RcOrderId { get; set; }

        public string Sku { get; set; } = string.Empty;

        [DwDenied]
        public string? Cost { get; set; }
    }

    public class RcPerson
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        [DwDenied]
        public string? TaxId { get; set; }
    }

    public class RcAccount
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int RcPersonId { get; set; }

        public RcPerson? Person { get; set; }
    }

    /// <summary>The root of a hierarchy whose derived type declares a denied field.</summary>
    public class RcParty
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class RcCompany : RcParty
    {
        [DwDenied]
        public string? TaxSecret { get; set; }
    }

    public class RcDeal
    {
        public int Id { get; set; }

        public string Note { get; set; } = string.Empty;

        public int RcPartyId { get; set; }

        public RcParty? Party { get; set; }
    }

    /// <summary>An abstract root, which no projection can build.</summary>
    public abstract class RcAsset
    {
        public int Id { get; set; }

        public string Label { get; set; } = string.Empty;
    }

    public class RcVault : RcAsset
    {
        [DwDenied]
        public string? Combination { get; set; }
    }

    /// <summary>A value EF Core converts to text, holding a type with a denied field.</summary>
    public class RcCharge
    {
        public string Sku { get; set; } = string.Empty;

        [DwDenied]
        public string? Amount { get; set; }
    }

    public class RcClient
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public Dictionary<string, RcCharge> Charges { get; set; } = new();
    }

    public class RcPurchase
    {
        public int Id { get; set; }

        public int RcClientId { get; set; }

        public RcClient? Client { get; set; }
    }

    /// <summary>A navigation whose entity owns a chain deeper than the walker goes.</summary>
    public class RcHolding
    {
        public int Id { get; set; }

        public int RcKeeperId { get; set; }

        public RcKeeper? Keeper { get; set; }
    }

    public class RcKeeper
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public RcProfile Profile { get; set; } = new();
    }

    public class RcProfile
    {
        public string A { get; set; } = string.Empty;

        public RcDetail Detail { get; set; } = new();
    }

    public class RcDetail
    {
        public string B { get; set; } = string.Empty;

        public RcInner Inner { get; set; } = new();
    }

    public class RcInner
    {
        public string C { get; set; } = string.Empty;

        [DwDenied]
        public string? Secret { get; set; }
    }

    /// <summary>An automatically included navigation with a denied field.</summary>
    public class RcBadgeHolder
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int RcBadgeId { get; set; }

        public RcBadge? Badge { get; set; }
    }

    public class RcBadge
    {
        public int Id { get; set; }

        public string Label { get; set; } = string.Empty;

        [DwDenied]
        public string? Pin { get; set; }
    }

    /// <summary>A member EF Core does not map that can hold an object of any type.</summary>
    public class RcBag
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int RcBagOwnerId { get; set; }

        public RcBagOwner? Owner { get; set; }

        [NotMapped]
        public object? Extra { get; set; }
    }

    public class RcBagOwner
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>An owned member with a mapped property that has no setter.</summary>
    public class RcShop
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Notes { get; set; }

        public RcAddress Address { get; set; } = new();
    }

    public class RcAddress
    {
        public RcAddress()
        {
        }

        public RcAddress(string city, string? zip)
        {
            City = city;
            Zip = zip;
        }

        public string City { get; set; } = string.Empty;

        public string? Zip { get; }
    }

    /// <summary>An owned member every path of which the walk asks about, and an included navigation.</summary>
    public class RcStore
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public RcPlace Place { get; set; } = new();

        public int RcStoreKeeperId { get; set; }

        public RcStoreKeeper? Keeper { get; set; }
    }

    public class RcPlace
    {
        public string City { get; set; } = string.Empty;

        public string Zone { get; set; } = string.Empty;
    }

    public class RcStoreKeeper
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>An owned chain whose last owned type exposes a mapped field through a getter the model does not map.</summary>
    public class RcRoom
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public RcShelf Shelf { get; set; } = new();
    }

    public class RcShelf
    {
        public string Label { get; set; } = string.Empty;

        public RcBox Box { get; set; } = new();
    }

    public class RcBox
    {
        public string Label { get; set; } = string.Empty;

        public RcLid Lid { get; set; } = new();
    }

    public class RcLid
    {
        private string? _code;

        public string Colour { get; set; } = string.Empty;

        [NotMapped]
        public RcCodeView Code => new() { Value = _code };

        public void Seal(string code) => _code = code;
    }

    public class RcCodeView
    {
        [DwDenied]
        public string? Value { get; set; }
    }

    public sealed class ProjectionReachContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ProjectionReachContext(SqliteConnection connection) => _connection = connection;

        public DbSet<RcCustomer> Customers => Set<RcCustomer>();

        public DbSet<RcOrder> Orders => Set<RcOrder>();

        public DbSet<RcPerson> People => Set<RcPerson>();

        public DbSet<RcAccount> Accounts => Set<RcAccount>();

        public DbSet<RcParty> Parties => Set<RcParty>();

        public DbSet<RcDeal> Deals => Set<RcDeal>();

        public DbSet<RcAsset> Assets => Set<RcAsset>();

        public DbSet<RcPurchase> Purchases => Set<RcPurchase>();

        public DbSet<RcHolding> Holdings => Set<RcHolding>();

        public DbSet<RcBadgeHolder> BadgeHolders => Set<RcBadgeHolder>();

        public DbSet<RcBag> Bags => Set<RcBag>();

        public DbSet<RcShop> Shops => Set<RcShop>();

        public DbSet<RcStore> Stores => Set<RcStore>();

        public DbSet<RcRoom> Rooms => Set<RcRoom>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<RcCompany>();
            model.Entity<RcVault>();
            model.Entity<RcClient>().Property(c => c.Charges).HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                text => JsonSerializer.Deserialize<Dictionary<string, RcCharge>>(text, (JsonSerializerOptions?)null)!);
            model.Entity<RcKeeper>().OwnsOne(
                k => k.Profile, p => p.OwnsOne(x => x.Detail, d => d.OwnsOne(y => y.Inner)));
            model.Entity<RcBadgeHolder>().Navigation(h => h.Badge).AutoInclude();
            model.Entity<RcShop>().OwnsOne(s => s.Address, a => a.Property(x => x.Zip));
            model.Entity<RcStore>().OwnsOne(s => s.Place);
            model.Entity<RcRoom>().OwnsOne(
                r => r.Shelf, s => s.OwnsOne(x => x.Box, b => b.OwnsOne(y => y.Lid, l => l.Property<string?>("_code"))));
        }
    }

    // --------------------------------------------------------------------------------- lazy loaders

    /// <summary>A lazy loader delegate the constructor takes and keeps in a field.</summary>
    public class RcFieldBlog
    {
        private readonly Action<object, string>? _loader;
        private List<RcFieldPost>? _posts;

        public RcFieldBlog()
        {
        }

        private RcFieldBlog(Action<object, string> lazyLoader) => _loader = lazyLoader;

        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<RcFieldPost> Posts
        {
            get
            {
                _loader?.Invoke(this, nameof(Posts));

                return _posts ??= new List<RcFieldPost>();
            }
            set => _posts = value;
        }
    }

    public class RcFieldPost
    {
        public int Id { get; set; }

        public int RcFieldBlogId { get; set; }

        public string Title { get; set; } = string.Empty;

        [DwDenied]
        public string? Draft { get; set; }
    }

    /// <summary>A lazy loader delegate kept in a property not named LazyLoader.</summary>
    public class RcNamedBlog
    {
        private List<RcNamedPost>? _posts;

        public RcNamedBlog()
        {
        }

        private RcNamedBlog(Action<object, string> lazyLoader) => Loader = lazyLoader;

        private Action<object, string>? Loader { get; set; }

        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<RcNamedPost> Posts
        {
            get
            {
                Loader?.Invoke(this, nameof(Posts));

                return _posts ??= new List<RcNamedPost>();
            }
            set => _posts = value;
        }
    }

    public class RcNamedPost
    {
        public int Id { get; set; }

        public int RcNamedBlogId { get; set; }

        public string Title { get; set; } = string.Empty;

        [DwDenied]
        public string? Draft { get; set; }
    }

    public sealed class ProjectionReachLazyContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ProjectionReachLazyContext(SqliteConnection connection) => _connection = connection;

        public DbSet<RcFieldBlog> FieldBlogs => Set<RcFieldBlog>();

        public DbSet<RcNamedBlog> NamedBlogs => Set<RcNamedBlog>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<RcFieldBlog>().Navigation(b => b.Posts).HasField("_posts");
            model.Entity<RcNamedBlog>().Navigation(b => b.Posts).HasField("_posts");
        }
    }

    // --------------------------------------------------------------------------------- rows

    /// <summary>A row whose member a constructor sets, before its initializer runs.</summary>
    public class RcHolder
    {
        public RcHolder()
        {
        }

        public RcHolder(RcPerson person) => Person = person;

        public int Id { get; set; }

        public RcPerson? Person { get; set; }
    }

    public class RcDealRow
    {
        public int Id { get; set; }

        [DwDenied]
        public string? Note { get; set; }

        public RcParty? Party { get; set; }
    }

    public sealed class RcDeepRow
    {
        public int Id { get; set; }

        public string? Tenant { get; set; }

        public RcL1? First { get; set; }
    }

    public sealed class RcL1
    {
        public RcL2? Next { get; set; }
    }

    public sealed class RcL2
    {
        public RcL3? Next { get; set; }
    }

    public sealed class RcL3
    {
        public string Name { get; set; } = string.Empty;

        public RcL4? Next { get; set; }
    }

    public sealed class RcL4
    {
        public string? Secret { get; set; }
    }

    /// <summary>A row holding policed objects through a collection that is not generic.</summary>
    public sealed class RcBagRow
    {
        public int Id { get; set; }

        [DwDenied]
        public string? Note { get; set; }

        public IEnumerable? Items { get; set; }
    }

    public sealed class RcNode
    {
        public string Name { get; set; } = string.Empty;

        public RcNode? Next { get; set; }
    }

    public sealed class RcLink
    {
        public int Id { get; set; }

        public RcNode? Head { get; set; }
    }

    /// <summary>A base type whose subtype's field a rule denies, no attribute anywhere.</summary>
    public class RcPet
    {
        public string Name { get; set; } = string.Empty;
    }

    public class RcHamster : RcPet
    {
        public string? Tag { get; set; }
    }

    public class RcCage
    {
        public int Id { get; set; }

        public RcPet? Pet { get; set; }
    }

    /// <summary>A row in memory holding an object of any type, with nothing denied anywhere.</summary>
    public class RcNote
    {
        public int Id { get; set; }

        public object? Payload { get; set; }
    }

    /// <summary>Rows in memory whose subtype declares a denied field.</summary>
    public class RcAnimal
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class RcDog : RcAnimal
    {
        [DwDenied]
        public string? Chip { get; set; }
    }

    // --------------------------------------------------------------------------------- the tests

    /// <summary>
    /// What a denied value can reach, read from what the source actually loads, and what a member can
    /// hold that no path names: a derived type's field, a path a policy denying by default never names,
    /// a collection that is not generic. Each test asserts the safe outcome, so a failing one is a leak.
    /// </summary>
    public sealed class ProjectionReachTests : IDisposable
    {
        private static readonly JsonSerializerOptions Json = new() { ReferenceHandler = ReferenceHandler.IgnoreCycles };

        private readonly SqliteConnection _connection;
        private readonly ProjectionReachContext _db;

        public ProjectionReachTests()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _db = new ProjectionReachContext(_connection);
            _db.Database.EnsureCreated();

            _db.Customers.Add(new RcCustomer
            {
                Name = "C1",
                Cards = { new RcCard { Label = "visa", Pan = "pan-secret" } },
                Orders = { new RcOrder { Code = "O1", Lines = { new RcLine { Sku = "S1", Cost = "cost-secret" } } } }
            });
            _db.Accounts.Add(new RcAccount { Name = "A1", Person = new RcPerson { Name = "P1", TaxId = "tax-secret" } });
            _db.Deals.Add(new RcDeal { Note = "note", Party = new RcCompany { Name = "Acme", TaxSecret = "company-secret" } });
            _db.Assets.Add(new RcVault { Label = "V1", Combination = "combination-secret" });
            _db.Purchases.Add(new RcPurchase
            {
                Client = new RcClient { Name = "K1", Charges = { ["a"] = new RcCharge { Sku = "S1", Amount = "amount-secret" } } }
            });
            _db.Holdings.Add(new RcHolding
            {
                Keeper = new RcKeeper
                {
                    Name = "O1",
                    Profile = new RcProfile { A = "a", Detail = new RcDetail { B = "b", Inner = new RcInner { C = "c", Secret = "deep-secret" } } }
                }
            });
            _db.BadgeHolders.Add(new RcBadgeHolder { Name = "H1", Badge = new RcBadge { Label = "gold", Pin = "pin-secret" } });
            _db.Bags.Add(new RcBag { Name = "B1", Owner = new RcBagOwner { Name = "W1" } });
            _db.Shops.Add(new RcShop { Name = "S1", Notes = "notes", Address = new RcAddress("Basra", "zip-secret") });
            _db.Stores.Add(new RcStore { Name = "T1", Place = new RcPlace { City = "Basra", Zone = "Z1" }, Keeper = new RcStoreKeeper { Name = "K" } });

            RcRoom room = new() { Name = "R1", Shelf = new RcShelf { Label = "s", Box = new RcBox { Label = "b", Lid = new RcLid { Colour = "red" } } } };
            room.Shelf.Box.Lid.Seal("lid-secret");
            _db.Rooms.Add(room);

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

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict, params IDwPolicyProvider[] more)
            where T : class =>
            source.ApplyPolicy(
                Caller(),
                new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }.Concat(more).ToArray()));

        private static Filter Selecting(params string[] fields) => new() { Selects = fields.ToList() };

        /// <summary>A policy that denies Select on every field except those it names.</summary>
        private static FakePolicyProvider DenyingAllBut(params string[] allowed)
        {
            FakePolicyProvider rules = new FakePolicyProvider()
                .Add("*", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);

            foreach (string path in allowed)
            {
                rules.Add(path, PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicGlobal);
            }

            return rules;
        }

        /// <summary>Everything a row holds, as a serializer would send it.</summary>
        private static string Sent(object? rows) => JsonSerializer.Serialize(rows, Json);

        /// <summary>
        /// True when anything reachable from a value holds the text, read by each object's runtime type: a
        /// serializer that writes the declared type would miss a derived type's field.
        /// </summary>
        private static bool Holds(object? value, string text)
        {
            HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
            Stack<object?> pending = new();

            pending.Push(value);

            while (pending.Count > 0)
            {
                object? current = pending.Pop();

                if (current is null || current is ValueType)
                {
                    continue;
                }

                if (current is string held)
                {
                    if (held == text)
                    {
                        return true;
                    }

                    continue;
                }

                if (!seen.Add(current))
                {
                    continue;
                }

                if (current is IEnumerable items)
                {
                    foreach (object? item in items)
                    {
                        pending.Push(item);
                    }

                    continue;
                }

                foreach (System.Reflection.PropertyInfo property in current.GetType().GetProperties())
                {
                    if (property.GetIndexParameters().Length == 0 && property.CanRead)
                    {
                        pending.Push(property.GetValue(current));
                    }
                }
            }

            return false;
        }

        private static void Refused(Action query)
        {
            PolicyException refusal = Assert.Throws<PolicyException>(query);

            Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, refusal.ErrorCode);
        }

        // ---------------------------------------------------------------- includes read from another root

        /// <summary>
        /// A later operator that reaches the rows through a navigation keeps what the includes loaded, named
        /// from the query's root: <c>Select(o =&gt; o.Customer)</c> after an include of the customer's cards.
        /// </summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public async Task An_include_named_from_another_root_counts_as_loading_every_navigation(DwTier tier)
        {
            IQueryable<RcCustomer> source = _db.Orders
                .Include(o => o.Customer).ThenInclude(c => c!.Cards)
                .Select(o => o.Customer!);

            Assert.Contains("pan-secret", Sent(source.AsNoTracking().ToList()));

            Assert.DoesNotContain("pan-secret", Sent(Guard(source, tier).ToList(new Filter()).Data));
            Assert.DoesNotContain("pan-secret", Sent(Guard(source, tier).ToListDynamic(new Filter()).Data));
            Assert.DoesNotContain("pan-secret", Sent((await Guard(source, tier).ToListAsync(new Filter(), CancellationToken.None)).Data));
            Assert.DoesNotContain("pan-secret", Sent((await Guard(source, tier).ToListAsync(new Segment(), CancellationToken.None)).Data));

            IQueryable<RcCustomer> named = _db.Orders.Include("Customer.Cards").Select(o => o.Customer!);

            Assert.DoesNotContain("pan-secret", Sent(Guard(named, tier).ToList(new Filter()).Data));
        }

        [Fact]
        public void An_include_named_from_another_root_is_read_through_SelectMany_and_Join()
        {
            IQueryable<RcOrder> flattened = _db.Customers
                .Include(c => c.Orders).ThenInclude(o => o.Lines)
                .SelectMany(c => c.Orders);
            IQueryable<RcOrder> selected = _db.Customers
                .Include(c => c.Orders).ThenInclude(o => o.Lines)
                .SelectMany(c => c.Orders, (c, o) => o);
            IQueryable<RcCustomer> joined = _db.Orders
                .Include(o => o.Customer).ThenInclude(c => c!.Cards)
                .Join(_db.Accounts, o => o.Id, a => a.Id, (o, a) => o.Customer!);

            Assert.Contains("cost-secret", Sent(flattened.AsNoTracking().ToList()));

            Assert.DoesNotContain("cost-secret", Sent(Guard(flattened).ToList(new Filter()).Data));
            Assert.DoesNotContain("cost-secret", Sent(Guard(selected).ToList(new Filter()).Data));
            Assert.DoesNotContain("pan-secret", Sent(Guard(joined).ToList(new Filter()).Data));
        }

        // ---------------------------------------------------------------- projections the outer Select hides

        /// <summary>
        /// A projection behind a <c>Select</c> that builds nothing, a member of an anonymous row or a
        /// conditional still loads what it assigns, although the rows are the entity's type.
        /// </summary>
        [Fact]
        public void A_projection_behind_another_Select_is_not_read_as_an_entity_query()
        {
            IQueryable<RcAccount> identity = _db.Accounts
                .Select(a => new RcAccount { Id = a.Id, Name = a.Name, Person = a.Person })
                .Select(x => x);
            IQueryable<RcAccount> member = _db.Accounts
                .Select(a => new { Row = new RcAccount { Id = a.Id, Name = a.Name, Person = a.Person } })
                .Select(x => x.Row);
            IQueryable<RcAccount> conditional = _db.Accounts
                .Select(a => a.Id > 0
                    ? new RcAccount { Id = a.Id, Name = a.Name, Person = a.Person }
                    : new RcAccount { Id = a.Id, Name = a.Name });

            Assert.Contains("tax-secret", Sent(identity.ToList()));

            Assert.DoesNotContain("tax-secret", Sent(Guard(identity).ToList(new Filter()).Data));
            Assert.DoesNotContain("tax-secret", Sent(Guard(member).ToList(new Filter()).Data));
            Assert.DoesNotContain("tax-secret", Sent(Guard(conditional).ToList(new Filter()).Data));
        }

        /// <summary>
        /// A projection beneath a <c>Join</c> does not make the join's rows: they are the result selector's,
        /// which here puts the person back. The rows are read as the entity they are, every navigation loaded.
        /// </summary>
        [Fact]
        public void A_projection_beneath_a_Join_is_not_the_one_that_makes_the_rows()
        {
            IQueryable<RcAccount> rows = _db.Accounts
                .Select(a => new RcAccount { Id = a.Id, RcPersonId = a.RcPersonId })
                .Join(_db.People, a => a.RcPersonId, p => p.Id, (a, p) => new RcAccount { Id = a.Id, Person = p });

            RowShape shape = RowShape.Of(rows);

            Assert.Equal(RowKind.Entity, shape.Kind);
            Assert.True(shape.Materializes("Person.TaxId"));
            Assert.Contains("tax-secret", Sent(rows.ToList()));

            // EF Core may refuse to read a member the result selector did not assign; it never returns the value.
            Exception? refused = Record.Exception(() => Assert.DoesNotContain("tax-secret", Sent(Guard(rows).ToList(new Filter()).Data)));

            Assert.True(refused is null or InvalidOperationException, refused?.ToString());
        }

        /// <summary>A constructor with arguments sets members its initializer never names.</summary>
        [Fact]
        public void An_initializer_after_a_constructor_with_arguments_counts_every_member_as_assigned()
        {
            IQueryable<RcHolder> rows = _db.People.Select(p => new RcHolder(p) { Id = p.Id });

            Assert.Contains("tax-secret", Sent(rows.ToList()));
            Assert.DoesNotContain("tax-secret", Sent(Guard(rows).ToList(new Filter()).Data));
        }

        // ---------------------------------------------------------------- lazy loaders the model keeps no record of

        /// <summary>
        /// A lazy loader delegate the constructor takes gets no service property, whether the class keeps it
        /// in a field or in a property of another name; it still fills the navigation after the query.
        /// </summary>
        [Fact]
        public void A_lazy_loader_kept_where_the_model_has_no_record_of_it_counts_as_loading()
        {
            using SqliteConnection connection = new("DataSource=:memory:");
            connection.Open();

            using ProjectionReachLazyContext db = new(connection);
            db.Database.EnsureCreated();
            db.FieldBlogs.Add(new RcFieldBlog { Name = "B1", Posts = { new RcFieldPost { Title = "T", Draft = "field-draft" } } });
            db.NamedBlogs.Add(new RcNamedBlog { Name = "B2", Posts = { new RcNamedPost { Title = "T", Draft = "named-draft" } } });
            db.SaveChanges();
            db.ChangeTracker.Clear();

            RcFieldBlog field = Guard(db.FieldBlogs).ToList(new Filter()).Data.Single();
            RcNamedBlog named = Guard(db.NamedBlogs).ToList(new Filter()).Data.Single();

            Assert.DoesNotContain(field.Posts, post => post.Draft == "field-draft");
            Assert.DoesNotContain(named.Posts, post => post.Draft == "named-draft");
        }

        // ---------------------------------------------------------------- a rule in another letter case

        /// <summary>
        /// A rule spelled in another letter case, on a path deeper than the walk, reaches a projected row's
        /// assigned member: whether or not anything else is denied, it asks for the projection.
        /// </summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_rule_in_another_letter_case_reaches_an_assigned_member(bool somethingElseDenied)
        {
            IQueryable<RcDeepRow> rows = _db.Customers.Select(c => new RcDeepRow
            {
                Id = c.Id,
                Tenant = c.Name,
                First = new RcL1 { Next = new RcL2 { Next = new RcL3 { Name = c.Name, Next = new RcL4 { Secret = "camel-secret" } } } }
            });

            FakePolicyProvider rules = new FakePolicyProvider()
                .Add("first.next.next.next.secret", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);

            if (somethingElseDenied)
            {
                rules.Add("tenant", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);
            }

            RcDeepRow row = Guard(rows, DwTier.Strict, rules).ToList(new Filter()).Data.Single();

            Assert.Null(row.First?.Next?.Next?.Next?.Secret);
            Assert.Equal("C1", row.First?.Next?.Next?.Name);
        }

        // ---------------------------------------------------------------- what a member can hold

        /// <summary>A collection that is not generic holds objects, whatever they carry, so it is never clean.</summary>
        [Fact]
        public void A_collection_that_is_not_generic_is_never_kept_whole()
        {
            IQueryable<RcBagRow> rows = _db.Customers.Select(c => new RcBagRow { Id = c.Id, Note = c.Name, Items = c.Cards.ToList() });

            PolicyQueryable<RcBagRow> guarded = Guard(rows);
            RcBagRow row = guarded.ToList(new Filter()).Data.Single();

            Assert.Null(row.Items);
            Assert.Contains(guarded.LastTrace!.Decisions, d => d.FieldPath == "Items" && d.Action == PolicyAction.Dropped);
        }

        /// <summary>
        /// A member declared as a base type holds a derived entity, whose own denied field the base type
        /// never names: a projected row narrows it to the base type rather than keeping it whole.
        /// </summary>
        [Fact]
        public void A_member_declared_as_a_base_type_is_narrowed_to_it()
        {
            IQueryable<RcDealRow> rows = _db.Deals.Select(d => new RcDealRow { Id = d.Id, Note = d.Note, Party = d.Party });

            Assert.True(Holds(rows.ToList(), "company-secret"));

            RcDealRow row = Guard(rows).ToList(new Filter()).Data.Single();

            Assert.False(Holds(row, "company-secret"));
            Assert.IsNotType<RcCompany>(row.Party);
            Assert.Equal("Acme", row.Party!.Name);
        }

        /// <summary>
        /// A query over the root of a hierarchy returns each row as its derived type. A derived type's denied
        /// field asks for the projection, which builds the root type.
        /// </summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_denied_field_of_a_derived_type_projects_the_root(DwTier tier)
        {
            PolicyQueryable<RcParty> guarded = Guard(_db.Parties, tier);
            RcParty party = guarded.ToList(new Filter()).Data.Single();

            Assert.IsNotType<RcCompany>(party);
            Assert.Equal("Acme", party.Name);
            Assert.Contains(guarded.LastTrace!.Decisions, d => d.FieldPath == "TaxSecret" && d.Action == PolicyAction.Dropped);
        }

        /// <summary>
        /// An abstract root cannot be built, so the typed terminal refuses the projection a derived type's denial
        /// asks for, as it refuses any type with no public parameterless constructor. The dynamic terminal builds
        /// its own class and returns the root's allowed members.
        /// </summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void An_abstract_root_that_needs_a_projection_is_never_returned_whole(DwTier tier)
        {
            LogicException refusal = Assert.Throws<LogicException>(() => Guard(_db.Assets, tier).ToList(new Filter()));

            Assert.Equal(ErrorCode.SelectTypeMustHaveParameterlessConstructor, refusal.Message);

            List<dynamic> rows = Guard(_db.Assets, tier).ToListDynamic(new Filter()).Data!;

            Assert.Equal("V1", (string)Assert.Single(rows).Label);
            Assert.False(Holds(rows, "combination-secret"));

            // Its concrete type can be queried, and is projected like any other.
            Assert.Null(Guard(_db.Assets.OfType<RcVault>(), tier).ToList(new Filter()).Data.Single().Combination);
        }

        /// <summary>Rows in memory can be any loaded subtype, and are projected to the rows' type.</summary>
        [Fact]
        public void Rows_in_memory_of_a_subtype_with_a_denied_field_are_projected()
        {
            RcAnimal[] rows = { new RcDog { Id = 1, Name = "Rex", Chip = "chip-secret" } };

            RcAnimal row = Guard(rows.AsQueryable()).ToList(new Filter()).Data.Single();

            Assert.IsNotType<RcDog>(row);
            Assert.Equal("Rex", row.Name);
        }

        /// <summary>
        /// A navigation named in Selects, or included, declared as the root of a hierarchy, holds the
        /// derived type's denied field. Strict refuses naming it; Convenience narrows it to the root type.
        /// An included one is left out of the projection it asks for.
        /// </summary>
        [Fact]
        public void A_navigation_declared_as_a_base_type_carries_the_derived_type()
        {
            Refused(() => Guard(_db.Deals, DwTier.Strict).ToList(Selecting("Id", "Party")));

            RcDeal named = Guard(_db.Deals, DwTier.Convenience).ToList(Selecting("Id", "Party")).Data.Single();
            RcDeal included = Guard(_db.Deals.Include(d => d.Party)).ToList(new Filter()).Data.Single();

            Assert.True(Holds(_db.Deals.Include(d => d.Party).AsNoTracking().ToList(), "company-secret"));
            Assert.Equal("Acme", named.Party!.Name);
            Assert.False(Holds(named, "company-secret"));
            Assert.False(Holds(included, "company-secret"));
        }

        // ---------------------------------------------------------------- a policy that denies what it does not name

        /// <summary>
        /// Under a <c>"*"</c> deny, a path the walk never asks about is denied: one around a cycle, one
        /// deeper than the walk, and a property with no setter.
        /// </summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Under_a_deny_by_default_policy_a_member_named_is_never_kept_whole(DwTier tier)
        {
            RcLink[] links = { new() { Id = 1, Head = new RcNode { Name = "n1", Next = new RcNode { Name = "n2-secret" } } } };

            // In memory a member cannot be narrowed, so it is refused in both tiers.
            Refused(() => Guard(links.AsQueryable(), tier, DenyingAllBut("Id", "Head", "Head.Name")).ToList(Selecting("Id", "Head")));

            FakePolicyProvider shop = DenyingAllBut("Id", "Name", "Address", "Address.City");

            if (tier == DwTier.Strict)
            {
                Refused(() => Guard(_db.Shops, tier, shop).ToList(Selecting("Id", "Address")));
            }
            else
            {
                RcShop row = Guard(_db.Shops, tier, shop).ToList(Selecting("Id", "Address")).Data.Single();

                Assert.Equal(("Basra", (string?)null), (row.Address.City, row.Address.Zip));
            }
        }

        [Fact]
        public void Under_a_deny_by_default_policy_a_synthesized_projection_narrows_what_the_walk_misses()
        {
            IQueryable<RcDeepRow> deep = _db.Customers.Select(c => new RcDeepRow
            {
                Id = c.Id,
                Tenant = c.Name,
                First = new RcL1 { Next = new RcL2 { Next = new RcL3 { Name = c.Name, Next = new RcL4 { Secret = "deep-secret" } } } }
            });

            RcDeepRow row = Guard(deep, DwTier.Strict, DenyingAllBut("Id", "First", "First.Next", "First.Next.Next", "First.Next.Next.Name"))
                .ToList(new Filter()).Data.Single();

            Assert.Null(row.First?.Next?.Next?.Next?.Secret);
            Assert.Equal("C1", row.First?.Next?.Next?.Name);

            // Notes denied, and with nothing else denied: the owned address is narrowed either way.
            foreach (FakePolicyProvider rules in new[]
                     {
                         DenyingAllBut("Id", "Name", "Address", "Address.City"),
                         DenyingAllBut("Id", "Name", "Notes", "Address", "Address.City")
                     })
            {
                RcShop shop = Guard(_db.Shops, DwTier.Strict, rules).ToList(new Filter()).Data.Single();

                Assert.Equal(("Basra", (string?)null), (shop.Address.City, shop.Address.Zip));
            }
        }

        /// <summary>
        /// A deny-by-default policy that names every path of an owned member asks for nothing: the entity is
        /// read as loaded, its included navigation with it.
        /// </summary>
        [Fact]
        public void Under_a_deny_by_default_policy_a_member_the_walk_covers_is_kept()
        {
            FakePolicyProvider rules = DenyingAllBut(
                "Id", "Name", "Place", "Place.City", "Place.Zone", "RcStoreKeeperId", "Keeper", "Keeper.Id", "Keeper.Name");

            PolicyQueryable<RcStore> guarded = Guard(_db.Stores.Include(s => s.Keeper), DwTier.Strict, rules);
            RcStore store = guarded.ToList(new Filter()).Data.Single();

            Assert.Equal(("Z1", "K"), (store.Place.Zone, store.Keeper?.Name));
            Assert.DoesNotContain(guarded.LastTrace!.Decisions, d => d.Action == PolicyAction.Dropped);
        }

        // ---------------------------------------------------------------- what a navigation's entity loads

        /// <summary>
        /// Naming a navigation loads its entity whole, its owned chain included, however deep. Strict refuses
        /// it; Convenience narrows it to the paths the walk can name.
        /// </summary>
        [Fact]
        public void A_navigation_named_whose_owned_chain_holds_a_denial_past_the_walk_is_not_returned_whole()
        {
            Refused(() => Guard(_db.Holdings, DwTier.Strict).ToList(Selecting("Id", "Keeper")));

            RcHolding holding = Guard(_db.Holdings, DwTier.Convenience).ToList(Selecting("Id", "Keeper")).Data.Single();

            Assert.Equal("O1", holding.Keeper!.Name);
            Assert.DoesNotContain("deep-secret", Sent(holding));
        }

        /// <summary>A column of a navigation's entity holding a framework collection of a policed type.</summary>
        [Fact]
        public void A_navigation_named_whose_column_holds_a_policed_type_is_not_returned_whole()
        {
            Assert.Contains("amount-secret", Sent(_db.Purchases.Include(p => p.Client).ToList()));

            Refused(() => Guard(_db.Purchases, DwTier.Strict).ToList(Selecting("Id", "Client")));
            Refused(() => Guard(_db.Purchases, DwTier.Strict).ToList(Selecting("Id", "Client.Charges")));

            // Convenience narrows the navigation around the column, and the column itself to nothing.
            RcPurchase purchase = Guard(_db.Purchases, DwTier.Convenience).ToList(Selecting("Id", "Client")).Data.Single();
            PolicyQueryable<RcPurchase> guarded = Guard(_db.Purchases, DwTier.Convenience);
            RcPurchase column = guarded.ToList(Selecting("Id", "Client.Charges")).Data.Single();

            Assert.Equal("K1", purchase.Client!.Name);
            Assert.False(Holds(purchase, "amount-secret"));
            Assert.False(Holds(column, "amount-secret"));
            Assert.Contains(guarded.LastTrace!.Decisions, d => d.FieldPath == "Client.Charges" && d.Action == PolicyAction.Dropped);
        }

        /// <summary>
        /// An included navigation whose own navigation holds the denial, unloaded, asks for nothing: the
        /// entity comes back as loaded, the included navigation with it.
        /// </summary>
        [Fact]
        public void An_included_navigation_with_a_denial_only_beneath_what_it_does_not_load_is_kept()
        {
            PolicyQueryable<RcOrder> guarded = Guard(_db.Orders.Include(o => o.Customer));
            RcOrder order = guarded.ToList(new Filter()).Data.Single();

            Assert.Equal("C1", order.Customer?.Name);
            Assert.DoesNotContain(guarded.LastTrace!.Decisions, d => d.Action == PolicyAction.Dropped);
        }

        /// <summary>An automatically included navigation loads with every query, and so does its denied field.</summary>
        [Fact]
        public void An_automatically_included_navigation_counts_as_loaded()
        {
            Assert.Contains("pin-secret", Sent(_db.BadgeHolders.ToList()));

            PolicyQueryable<RcBadgeHolder> guarded = Guard(_db.BadgeHolders);
            RcBadgeHolder holder = guarded.ToList(new Filter()).Data.Single();

            Assert.DoesNotContain("pin-secret", Sent(holder));
            Assert.Contains(guarded.LastTrace!.Decisions, d => d.FieldPath == "Badge.Pin" && d.Action == PolicyAction.Dropped);
        }

        /// <summary>
        /// A getter the model does not map, past the walker's four segments in an owned chain, hands out a
        /// mapped field: what it returns counts as loaded, so its denied field asks for the projection.
        /// </summary>
        [Fact]
        public void A_getter_the_model_does_not_map_past_the_walk_counts_as_loaded()
        {
            Assert.True(Holds(_db.Rooms.AsNoTracking().ToList(), "lid-secret"));

            PolicyQueryable<RcRoom> guarded = Guard(_db.Rooms);
            RcRoom room = guarded.ToList(new Filter()).Data.Single();

            Assert.False(Holds(room, "lid-secret"));
            Assert.Equal("red", room.Shelf.Box.Lid.Colour);
        }

        /// <summary>
        /// A rule on a field only a subtype declares, reached through a member declared as the base type, is
        /// enforced like an attribute there would be: named or not, the field does not come back.
        /// </summary>
        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void A_rule_on_a_subtype_field_through_a_base_typed_member_is_enforced(DwTier tier)
        {
            RcCage[] rows = { new() { Id = 1, Pet = new RcHamster { Name = "Ham", Tag = "tag-by-rule" } } };
            FakePolicyProvider rules = new FakePolicyProvider()
                .Add("Pet.Tag", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);

            Assert.False(Holds(Guard(rows.AsQueryable(), tier, rules).ToList(new Filter()).Data, "tag-by-rule"));

            Exception? named = Record.Exception(() =>
                Assert.False(Holds(Guard(rows.AsQueryable(), tier, rules).ToList(Selecting("Id", "Pet")).Data, "tag-by-rule")));

            Assert.True(named is null or PolicyException, named?.ToString());
        }

        /// <summary>
        /// With nothing denied, a row in memory holding an object of any type is returned as it is: such a
        /// member asks for no projection on its own.
        /// </summary>
        [Fact]
        public void A_row_in_memory_holding_an_object_with_nothing_denied_is_returned_as_it_is()
        {
            RcNote[] rows = { new() { Id = 1, Payload = new Dictionary<string, object> { ["a"] = 1 } } };

            PolicyQueryable<RcNote> guarded = Guard(rows.AsQueryable());

            Assert.Same(rows[0], guarded.ToList(new Filter()).Data.Single());
            Assert.DoesNotContain(guarded.LastTrace!.Decisions, d => d.Action == PolicyAction.Dropped);
        }

        /// <summary>
        /// A member that can hold an object of any type asks for no projection: the policy cannot see into it
        /// whether or not one is built. The entity comes back as loaded, its included navigation with it.
        /// </summary>
        [Fact]
        public void A_member_that_can_hold_anything_asks_for_nothing()
        {
            PolicyQueryable<RcBag> guarded = Guard(_db.Bags.Include(b => b.Owner));
            RcBag bag = guarded.ToList(new Filter()).Data.Single();

            Assert.Equal("W1", bag.Owner?.Name);
            Assert.DoesNotContain(guarded.LastTrace!.Decisions, d => d.Action == PolicyAction.Dropped);
        }
    }
}
