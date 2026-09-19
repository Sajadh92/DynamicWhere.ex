using System.Collections;
using System.Text.Json;
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
using Xunit.Abstractions;
using Zb.Geo;

// Probes for over-blocking in 6c9e171. Every test asserts what an ordinary query should get when
// nothing denied can reach the result (or what 3.1.0 / f7e1cc6 returned), so a failing test is a
// candidate over-block. Each test also writes its outcome, run with a detailed console logger.

namespace Zb.Geo
{
    /// <summary>
    /// Shaped like NetTopologySuite's Geometry: an application-namespace type with an object member
    /// (NTS has Geometry.UserData) and subtypes.
    /// </summary>
    public abstract class ZbGeometry
    {
        public int Srid { get; set; }

        public object? UserData { get; set; }
    }

    public class ZbPoint : ZbGeometry
    {
        public double X { get; set; }

        public double Y { get; set; }
    }

    public class ZbPolygon : ZbGeometry
    {
        public List<ZbPoint> Shell { get; set; } = new();
    }
}

namespace DynamicWhere.Tests.Policies
{
    // ------------------------------------------------------------ columns holding objects, no denials

    public class ZbTicket
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        public Dictionary<string, object> Extra { get; set; } = new();
    }

    public class ZbComment
    {
        public int Id { get; set; }

        public string Text { get; set; } = string.Empty;

        public int ZbTicketId { get; set; }

        public ZbTicket? Ticket { get; set; }
    }

    public class ZbSensor
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public BitArray Flags { get; set; } = new(4);
    }

    public class ZbSite
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public ZbPoint? Location { get; set; }
    }

    public class ZbVisit
    {
        public int Id { get; set; }

        public string Note { get; set; } = string.Empty;

        public int ZbSiteId { get; set; }

        public ZbSite? Site { get; set; }
    }

    public class ZbProfileHolder
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public ZbSettings Settings { get; set; } = new();
    }

    public class ZbSettings
    {
        public string Theme { get; set; } = string.Empty;

        public Dictionary<string, object> Prefs { get; set; } = new();
    }

    // ------------------------------------------------------------ reshaped chains

    public class ZbCustomer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<ZbCard> Cards { get; set; } = new();

        public List<ZbOrder> Orders { get; set; } = new();
    }

    public class ZbCard
    {
        public int Id { get; set; }

        public int ZbCustomerId { get; set; }

        public string Label { get; set; } = string.Empty;

        [DwDenied]
        public string? Pan { get; set; }
    }

    public class ZbOrder
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public int ZbCustomerId { get; set; }

        public ZbCustomer? Customer { get; set; }

        public List<ZbLine> Lines { get; set; } = new();
    }

    public class ZbLine
    {
        public int Id { get; set; }

        public int ZbOrderId { get; set; }

        public string Sku { get; set; } = string.Empty;

        public string? Cost { get; set; }
    }

    /// <summary>A constructor-bound entity: EF Core binds it, and it has no parameterless constructor.</summary>
    public class ZbMember
    {
        public ZbMember(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public int Id { get; private set; }

        public string Name { get; private set; }

        public List<ZbBadge> Badges { get; private set; } = new();
    }

    public class ZbBadge
    {
        public int Id { get; set; }

        public int ZbMemberId { get; set; }

        public string Label { get; set; } = string.Empty;

        [DwDenied]
        public string? Pin { get; set; }
    }

    public class ZbLink
    {
        public int Id { get; set; }

        public int ZbMemberId { get; set; }

        public ZbMember? Member { get; set; }
    }

    /// <summary>A shopper whose tier is auto-included and holds nothing denied; a denial sits beneath unloaded wallets.</summary>
    public class ZbShopper
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int ZbTierId { get; set; }

        public ZbTier? Tier { get; set; }

        public List<ZbWallet> Wallets { get; set; } = new();
    }

    public class ZbTier
    {
        public int Id { get; set; }

        public string Label { get; set; } = string.Empty;
    }

    public class ZbWallet
    {
        public int Id { get; set; }

        public int ZbShopperId { get; set; }

        [DwDenied]
        public string? Iban { get; set; }
    }

    public class ZbBasket
    {
        public int Id { get; set; }

        public int ZbShopperId { get; set; }

        public ZbShopper? Shopper { get; set; }
    }

    // ------------------------------------------------------------ hierarchies

    public abstract class ZbPayment
    {
        public int Id { get; set; }

        public long Cents { get; set; }
    }

    public class ZbCardPayment : ZbPayment
    {
        [DwDenied]
        public string? Pan { get; set; }
    }

    public class ZbCashPayment : ZbPayment
    {
        public string Till { get; set; } = string.Empty;
    }

    /// <summary>An abstract root; no denial anywhere, one derived type holds a JSON bag.</summary>
    public abstract class ZbEvent
    {
        public int Id { get; set; }

        public string Kind { get; set; } = string.Empty;
    }

    public class ZbClickEvent : ZbEvent
    {
        public Dictionary<string, object> Data { get; set; } = new();
    }

    public class ZbViewEvent : ZbEvent
    {
        public string Page { get; set; } = string.Empty;
    }

    /// <summary>A concrete root; no denial anywhere, one derived type holds a JSON bag.</summary>
    public class ZbDoc
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;
    }

    public class ZbRichDoc : ZbDoc
    {
        public Dictionary<string, object> Meta { get; set; } = new();
    }

    // ------------------------------------------------------------ rows

    public interface IZbContact
    {
        string Name { get; set; }
    }

    public class ZbContactRow : IZbContact
    {
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Another implementor of the interface, somewhere in the application, with a denial.</summary>
    public class ZbVipContact : IZbContact
    {
        public string Name { get; set; } = string.Empty;

        [DwDenied]
        public string? Phone { get; set; }
    }

    public class ZbCustomerRow
    {
        public int Id { get; set; }

        public IZbContact? Contact { get; set; }
    }

    public class ZbPersonDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public ZbAddressDto? Address { get; set; }
    }

    /// <summary>A subclass of the DTO elsewhere in the application, with a denial.</summary>
    public class ZbStaffDto : ZbPersonDto
    {
        [DwDenied]
        public string? Salary { get; set; }
    }

    public class ZbAddressDto
    {
        public string City { get; set; } = string.Empty;
    }

    public class ZbOrderRow
    {
        public ZbOrderRow()
        {
        }

        public ZbOrderRow(int id) => Id = id;

        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public List<ZbLineRow> Lines { get; set; } = new();
    }

    public class ZbLineRow
    {
        public string Sku { get; set; } = string.Empty;

        [DwDenied]
        public string? Cost { get; set; }
    }

    public class ZbPlaceDto
    {
        public string City { get; set; } = string.Empty;

        public string Display => City.ToUpperInvariant();
    }

    public class ZbPlaceRow
    {
        public int Id { get; set; }

        public ZbPlaceDto? Place { get; set; }
    }

    public class ZbZoneDto
    {
        public string Code { get; set; } = string.Empty;
    }

    /// <summary>A subclass with no denial at all.</summary>
    public class ZbSubZoneDto : ZbZoneDto
    {
        public string Parent { get; set; } = string.Empty;
    }

    public class ZbZoneRow
    {
        public int Id { get; set; }

        public ZbZoneDto? Zone { get; set; }
    }

    // ------------------------------------------------------------ the database

    public sealed class ZbProbeContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZbProbeContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZbTicket> Tickets => Set<ZbTicket>();

        public DbSet<ZbComment> Comments => Set<ZbComment>();

        public DbSet<ZbSensor> Sensors => Set<ZbSensor>();

        public DbSet<ZbSite> Sites => Set<ZbSite>();

        public DbSet<ZbVisit> Visits => Set<ZbVisit>();

        public DbSet<ZbProfileHolder> ProfileHolders => Set<ZbProfileHolder>();

        public DbSet<ZbCustomer> Customers => Set<ZbCustomer>();

        public DbSet<ZbOrder> Orders => Set<ZbOrder>();

        public DbSet<ZbMember> Members => Set<ZbMember>();

        public DbSet<ZbLink> Links => Set<ZbLink>();

        public DbSet<ZbPayment> Payments => Set<ZbPayment>();

        public DbSet<ZbEvent> Events => Set<ZbEvent>();

        public DbSet<ZbDoc> Docs => Set<ZbDoc>();

        public DbSet<ZbShopper> Shoppers => Set<ZbShopper>();

        public DbSet<ZbBasket> Baskets => Set<ZbBasket>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<ZbTicket>().Property(t => t.Extra).HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                text => JsonSerializer.Deserialize<Dictionary<string, object>>(text, (JsonSerializerOptions?)null)!);

            model.Entity<ZbSensor>().Property(s => s.Flags).HasConversion(
                value => string.Concat(value.Cast<bool>().Select(bit => bit ? '1' : '0')),
                text => new BitArray(text.Select(c => c == '1').ToArray()));

            model.Entity<ZbSite>().Property(s => s.Location).HasConversion(
                value => value == null ? null : $"{value.X};{value.Y}",
                text => text == null ? null : new ZbPoint { X = double.Parse(text.Split(';', StringSplitOptions.None)[0]), Y = double.Parse(text.Split(';', StringSplitOptions.None)[1]) });

            model.Entity<ZbProfileHolder>().OwnsOne(p => p.Settings, s => s.Property(x => x.Prefs).HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                text => JsonSerializer.Deserialize<Dictionary<string, object>>(text, (JsonSerializerOptions?)null)!));

            model.Entity<ZbMember>().HasMany(m => m.Badges).WithOne().HasForeignKey(b => b.ZbMemberId);

            model.Entity<ZbCardPayment>();
            model.Entity<ZbCashPayment>();

            model.Entity<ZbClickEvent>().Property(e => e.Data).HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                text => JsonSerializer.Deserialize<Dictionary<string, object>>(text, (JsonSerializerOptions?)null)!);
            model.Entity<ZbViewEvent>();

            model.Entity<ZbShopper>().Navigation(s => s.Tier).AutoInclude();

            model.Entity<ZbRichDoc>().Property(d => d.Meta).HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                text => JsonSerializer.Deserialize<Dictionary<string, object>>(text, (JsonSerializerOptions?)null)!);
        }
    }

    // ------------------------------------------------------------ the probes

    /// <summary>Appends a probe's outcome to a file named for the tree and the EF Core version it ran on.</summary>
    internal static class ZbProbeLog
    {
        private static readonly object Gate = new();

        internal static string Tag
        {
            get
            {
                string where = AppContext.BaseDirectory;
                string tree = where.Contains("base-8a8d7fd") ? "base-8a8d7fd"
                    : where.Contains("parent-f7e1cc6") ? "parent-f7e1cc6"
                    : where.Contains("head-fce9c16") ? "head-fce9c16"
                    : "6c9e171";

                return $"{tree}-ef{typeof(DbContext).Assembly.GetName().Version!.Major}";
            }
        }

        internal static void Write(string line)
        {
            string directory = "/private/tmp/claude-501/-Users-sajadh92-Developer-Project-DynamicWhere-ex/8f118b4d-a861-42ce-b1d4-baa9f4b933e3/scratchpad/probes-b3";

            if (!Directory.Exists(directory))
            {
                return;
            }

            lock (Gate)
            {
                File.AppendAllText(Path.Combine(directory, $"outcomes-{Tag}.txt"), line + Environment.NewLine);
            }
        }
    }

    public sealed class ReviewOverBlockTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZbProbeContext _db;

        public ReviewOverBlockTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _db = new ZbProbeContext(_connection);
            _db.Database.EnsureCreated();

            ZbTicket ticket = new() { Title = "T1", Extra = { ["colour"] = "red" } };

            _db.Tickets.Add(ticket);
            _db.Comments.Add(new ZbComment { Text = "c1", Ticket = ticket });
            _db.Sensors.Add(new ZbSensor { Name = "S1", Flags = new BitArray(new[] { true, false, true, true }) });

            ZbSite site = new() { Name = "Basra", Location = new ZbPoint { X = 30.5, Y = 47.8 } };

            _db.Sites.Add(site);
            _db.Visits.Add(new ZbVisit { Note = "v1", Site = site });
            _db.ProfileHolders.Add(new ZbProfileHolder { Name = "P1", Settings = new ZbSettings { Theme = "dark", Prefs = { ["lang"] = "ar" } } });

            ZbCustomer withOrders = new()
            {
                Name = "C1",
                Cards = { new ZbCard { Label = "visa", Pan = "pan-secret" } },
                Orders = { new ZbOrder { Code = "O1", Lines = { new ZbLine { Sku = "S1", Cost = "9" } } } }
            };

            _db.Customers.Add(withOrders);
            _db.Customers.Add(new ZbCustomer { Name = "C2-no-orders" });

            ZbMember member = new(0, "M1");

            member.Badges.Add(new ZbBadge { Label = "gold", Pin = "pin-secret" });
            _db.Members.Add(member);
            _db.Links.Add(new ZbLink { Member = member });

            _db.Payments.Add(new ZbCardPayment { Cents = 100, Pan = "payment-pan-secret" });
            _db.Payments.Add(new ZbCashPayment { Cents = 200, Till = "till-1" });

            _db.Events.Add(new ZbClickEvent { Kind = "click", Data = { ["x"] = 1 } });
            _db.Events.Add(new ZbViewEvent { Kind = "view", Page = "/home" });

            ZbShopper shopper = new() { Name = "S1", Tier = new ZbTier { Label = "gold" }, Wallets = { new ZbWallet { Iban = "iban-secret" } } };

            _db.Shoppers.Add(shopper);
            _db.Baskets.Add(new ZbBasket { Shopper = shopper });

            _db.Docs.Add(new ZbDoc { Title = "plain" });
            _db.Docs.Add(new ZbRichDoc { Title = "rich", Meta = { ["pages"] = 3 } });

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

        private static string Dropped<T>(PolicyQueryable<T> guarded) where T : class =>
            string.Join(" | ", guarded.LastTrace?.Decisions
                .Where(d => d.Action != PolicyAction.Allowed)
                .Select(d => $"{d.FieldPath} {d.Action}: {d.Reason}") ?? Array.Empty<string>());

        private static bool AnyDropped<T>(PolicyQueryable<T> guarded) where T : class =>
            guarded.LastTrace?.Decisions.Any(d => d.Action == PolicyAction.Dropped) ?? false;

        /// <summary>Runs a guarded call and describes what came back, or what it threw.</summary>
        private string Outcome<T>(string label, PolicyQueryable<T> guarded, Func<PolicyQueryable<T>, IEnumerable> run)
            where T : class
        {
            string line;

            try
            {
                List<object?> rows = run(guarded).Cast<object?>().ToList();

                line = $"{label}: OK rows={rows.Count} types=[{string.Join(",", rows.Select(r => r?.GetType().Name ?? "null"))}]"
                       + $" json={JsonSerializer.Serialize(rows)} decisions=[{Dropped(guarded)}]";
            }
            catch (Exception e)
            {
                string code = e is PolicyException refusal ? refusal.ErrorCode.ToString() : string.Empty;

                line = $"{label}: THREW {e.GetType().Name} {code} {e.Message.Split('\n')[0]} decisions=[{Dropped(guarded)}]";
            }

            _out.WriteLine(line);
            ZbProbeLog.Write(line);

            return line;
        }

        // ================================================================ A: columns that hold objects

        [Fact]
        public void Zb_A1_entity_with_an_object_bag_column_and_no_denial_is_returned_as_loaded()
        {
            PolicyQueryable<ZbTicket> guarded = Guard(_db.Tickets);

            Outcome("A1 typed", Guard(_db.Tickets), g => g.ToList(new Filter()).Data);

            ZbTicket ticket = guarded.ToList(new Filter()).Data.Single();

            Assert.False(AnyDropped(guarded), Dropped(guarded));
            Assert.True(ticket.Extra.ContainsKey("colour"));
        }

        [Fact]
        public void Zb_A2_entity_with_a_BitArray_column_and_no_denial_is_returned_as_loaded()
        {
            PolicyQueryable<ZbSensor> guarded = Guard(_db.Sensors);

            Outcome("A2 typed", Guard(_db.Sensors), g => g.ToList(new Filter()).Data);

            ZbSensor sensor = guarded.ToList(new Filter()).Data.Single();

            Assert.False(AnyDropped(guarded), Dropped(guarded));
            Assert.Equal(4, sensor.Flags.Length);
            Assert.True(sensor.Flags[3]);
        }

        [Fact]
        public void Zb_A3_entity_with_a_geometry_like_column_and_no_denial_is_returned_as_loaded()
        {
            PolicyQueryable<ZbSite> guarded = Guard(_db.Sites);

            Outcome("A3 typed", Guard(_db.Sites), g => g.ToList(new Filter()).Data);

            ZbSite site = guarded.ToList(new Filter()).Data.Single();

            Assert.False(AnyDropped(guarded), Dropped(guarded));
            Assert.NotNull(site.Location);
        }

        [Fact]
        public void Zb_A4_an_included_navigation_whose_entity_holds_an_object_bag_is_kept()
        {
            PolicyQueryable<ZbComment> guarded = Guard(_db.Comments.Include(c => c.Ticket));

            Outcome("A4 typed", Guard(_db.Comments.Include(c => c.Ticket)), g => g.ToList(new Filter()).Data);

            ZbComment comment = guarded.ToList(new Filter()).Data.Single();

            Assert.False(AnyDropped(guarded), Dropped(guarded));
            Assert.NotNull(comment.Ticket);
        }

        [Fact]
        public void Zb_A5_an_included_navigation_whose_entity_holds_a_geometry_is_kept()
        {
            PolicyQueryable<ZbVisit> guarded = Guard(_db.Visits.Include(v => v.Site));

            Outcome("A5 typed", Guard(_db.Visits.Include(v => v.Site)), g => g.ToList(new Filter()).Data);

            ZbVisit visit = guarded.ToList(new Filter()).Data.Single();

            Assert.False(AnyDropped(guarded), Dropped(guarded));
            Assert.NotNull(visit.Site);
        }

        [Fact]
        public void Zb_A6_an_owned_member_holding_an_object_bag_is_kept()
        {
            PolicyQueryable<ZbProfileHolder> guarded = Guard(_db.ProfileHolders);

            Outcome("A6 typed", Guard(_db.ProfileHolders), g => g.ToList(new Filter()).Data);

            ZbProfileHolder holder = guarded.ToList(new Filter()).Data.Single();

            Assert.False(AnyDropped(guarded), Dropped(guarded));
            Assert.True(holder.Settings.Prefs.ContainsKey("lang"));
        }

        // ================================================================ B: reshaped chains, nothing loaded beneath

        [Fact]
        public void Zb_B1_Select_to_a_navigation_with_nothing_loaded_beneath_is_not_projected()
        {
            IQueryable<ZbCustomer> source = _db.Orders.Select(o => o.Customer!);

            Outcome("B1 unguarded", Guard(_db.Customers.Where(c => false)), _ => source.ToList());
            Outcome("B1 typed", Guard(source), g => g.ToList(new Filter()).Data);
            _db.ChangeTracker.Clear();

            PolicyQueryable<ZbCustomer> guarded = Guard(source);
            ZbCustomer customer = guarded.ToList(new Filter()).Data.Single();

            Assert.Equal("C1", customer.Name);
            Assert.False(AnyDropped(guarded), Dropped(guarded));
        }

        [Fact]
        public void Zb_B2_SelectMany_with_nothing_loaded_beneath_is_not_projected()
        {
            IQueryable<ZbOrder> source = _db.Customers.SelectMany(c => c.Orders);

            Outcome("B2 typed", Guard(source), g => g.ToList(new Filter()).Data);
            _db.ChangeTracker.Clear();

            PolicyQueryable<ZbOrder> guarded = Guard(source);

            Assert.Single(guarded.ToList(new Filter()).Data);
            Assert.False(AnyDropped(guarded), Dropped(guarded));
        }

        [Fact]
        public void Zb_B3_Join_with_nothing_loaded_beneath_is_not_projected()
        {
            IQueryable<ZbCustomer> source = _db.Orders.Join(_db.Customers, o => o.ZbCustomerId, c => c.Id, (o, c) => c);

            Outcome("B3 typed", Guard(source), g => g.ToList(new Filter()).Data);

            PolicyQueryable<ZbCustomer> guarded = Guard(source);

            Assert.Single(guarded.ToList(new Filter()).Data);
            Assert.False(AnyDropped(guarded), Dropped(guarded));
        }

        [Fact]
        public void Zb_B4_GroupBy_First_with_nothing_loaded_beneath_runs_unprojected()
        {
            // EF Core 6 cannot count this shape, guarded or not; the core's count fails there before the policy has a say.
            if (typeof(DbContext).Assembly.GetName().Version!.Major < 7)
            {
                return;
            }

            IQueryable<ZbOrder> source = _db.Orders.GroupBy(o => o.ZbCustomerId).Select(g => g.OrderBy(o => o.Id).First());

            Outcome("B4 unguarded", Guard(_db.Orders.Where(o => false)), _ => source.ToList());
            Outcome("B4 typed", Guard(source), g => g.ToList(new Filter()).Data);
            Outcome("B4 typed paged", Guard(source), g => g.ToList(new Filter { Page = new PageBy { PageNumber = 1, PageSize = 10 } }).Data);

            PolicyQueryable<ZbOrder> guarded = Guard(source);

            Assert.Single(guarded.ToList(new Filter()).Data);
            Assert.False(AnyDropped(guarded), Dropped(guarded));
        }

        [Fact]
        public void Zb_B5_Select_to_a_constructor_bound_entity_with_nothing_loaded_beneath_runs()
        {
            IQueryable<ZbMember> source = _db.Links.Select(l => l.Member!);

            Outcome("B5 unguarded", Guard(_db.Members.Where(m => false)), _ => source.ToList());
            Outcome("B5 typed", Guard(source), g => g.ToList(new Filter()).Data);
            Outcome("B5 dynamic", Guard(source), g => g.ToListDynamic(new Filter()).Data);
            Outcome("B5 direct typed (no reshape)", Guard(_db.Members), g => g.ToList(new Filter()).Data);

            Assert.Equal("M1", Guard(source).ToList(new Filter()).Data.Single().Name);
        }

        [Fact]
        public void Zb_B6_Select_FirstOrDefault_with_a_missing_row_runs_as_it_does_unguarded()
        {
            IQueryable<ZbOrder> source = _db.Customers.OrderBy(c => c.Id).Select(c => c.Orders.OrderBy(o => o.Id).FirstOrDefault()!);

            Outcome("B6 unguarded", Guard(_db.Orders.Where(o => false)), _ => source.ToList());
            Outcome("B6 typed", Guard(source), g => g.ToList(new Filter()).Data);

            Assert.Equal(2, Guard(source).ToList(new Filter()).Data.Count);
        }

        [Fact]
        public void Zb_B7_left_join_through_DefaultIfEmpty_runs_as_it_does_unguarded()
        {
            IQueryable<ZbOrder> source = _db.Customers.SelectMany(c => c.Orders.DefaultIfEmpty(), (c, o) => o!);

            Outcome("B7 unguarded", Guard(_db.Orders.Where(o => false)), _ => source.ToList());
            Outcome("B7 typed", Guard(source), g => g.ToList(new Filter()).Data);

            Assert.Equal(2, Guard(source).ToList(new Filter()).Data.Count);
        }

        /// <summary>
        /// The needless projection over a reshaped chain drops an auto-included navigation that holds nothing
        /// denied: only the unloaded wallets hold a denial.
        /// </summary>
        [Fact]
        public void Zb_B9_Select_to_a_navigation_keeps_its_auto_included_navigation()
        {
            IQueryable<ZbShopper> source = _db.Baskets.Select(b => b.Shopper!);

            Outcome("B9 unguarded", Guard(_db.Shoppers.Where(s => false)), _ => source.AsNoTracking().ToList());
            Outcome("B9 typed", Guard(source), g => g.ToList(new Filter()).Data);
            Outcome("B9 direct (no reshape)", Guard(_db.Shoppers), g => g.ToList(new Filter()).Data);

            Assert.Equal("gold", Guard(_db.Shoppers).ToList(new Filter()).Data.Single().Tier?.Label);
            Assert.Equal("gold", Guard(source).ToList(new Filter()).Data.Single().Tier?.Label);
        }

        /// <summary>
        /// Informational: the same chains with a top-level denial, which forces the synthesized projection in
        /// every version. Shows whether the exceptions above are the projection's own fragility.
        /// </summary>
        [Fact]
        public void Zb_B8_the_same_chains_with_a_top_level_denial_in_every_version()
        {
            FakePolicyProvider code = new FakePolicyProvider().Add("Code", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicGlobal);

            Outcome("B8 FirstOrDefault + top-level denial", Guard(_db.Customers.OrderBy(c => c.Id).Select(c => c.Orders.OrderBy(o => o.Id).FirstOrDefault()!), DwTier.Strict, code), g => g.ToList(new Filter()).Data);
            Outcome("B8 DefaultIfEmpty + top-level denial", Guard(_db.Customers.SelectMany(c => c.Orders.DefaultIfEmpty(), (c, o) => o!), DwTier.Strict, code), g => g.ToList(new Filter()).Data);
            Outcome("B8 GroupBy First + top-level denial", Guard(_db.Orders.GroupBy(o => o.ZbCustomerId).Select(g => g.OrderBy(o => o.Id).First()), DwTier.Strict, code), g => g.ToList(new Filter()).Data);
        }

        // ================================================================ C: hierarchies

        [Fact]
        public void Zb_C1_an_abstract_root_with_no_denial_anywhere_runs()
        {
            Outcome("C1 typed", Guard(_db.Events), g => g.ToList(new Filter()).Data);
            Outcome("C1 dynamic", Guard(_db.Events), g => g.ToListDynamic(new Filter()).Data);

            List<ZbEvent> events = Guard(_db.Events).ToList(new Filter()).Data;

            Assert.Contains(events, e => e is ZbClickEvent click && click.Data.ContainsKey("x"));
        }

        [Fact]
        public void Zb_C2_a_concrete_root_with_no_denial_anywhere_keeps_its_derived_rows()
        {
            PolicyQueryable<ZbDoc> guarded = Guard(_db.Docs);

            Outcome("C2 typed", Guard(_db.Docs), g => g.ToList(new Filter()).Data);

            List<ZbDoc> docs = guarded.ToList(new Filter()).Data;

            Assert.False(AnyDropped(guarded), Dropped(guarded));
            Assert.Contains(docs, d => d is ZbRichDoc rich && rich.Meta.ContainsKey("pages"));
        }

        /// <summary>Informational: an abstract root whose derived type declares a denied field.</summary>
        [Fact]
        public void Zb_C3_an_abstract_root_whose_derived_type_has_a_denial()
        {
            string typed = Outcome("C3 typed", Guard(_db.Payments), g => g.ToList(new Filter()).Data);
            string dynamic = Outcome("C3 dynamic", Guard(_db.Payments), g => g.ToListDynamic(new Filter()).Data);
            string convenience = Outcome("C3 typed convenience", Guard(_db.Payments, DwTier.Convenience), g => g.ToList(new Filter()).Data);

            Assert.DoesNotContain("THREW", dynamic);
        }

        // ================================================================ D: interfaces and DTO base classes

        [Fact]
        public void Zb_D1_a_projected_member_declared_as_an_interface_keeps_the_value_it_was_built_with()
        {
            IQueryable<ZbCustomerRow> rows = _db.Customers.OrderBy(c => c.Id)
                .Select(c => new ZbCustomerRow { Id = c.Id, Contact = new ZbContactRow { Name = c.Name } });

            Outcome("D1 typed", Guard(rows), g => g.ToList(new Filter()).Data);

            PolicyQueryable<ZbCustomerRow> guarded = Guard(rows);
            ZbCustomerRow row = guarded.ToList(new Filter()).Data.First();

            Assert.Equal("C1", row.Contact?.Name);
            Assert.False(AnyDropped(guarded), Dropped(guarded));
        }

        [Fact]
        public void Zb_D2_an_in_memory_member_declared_as_an_interface_whose_other_implementation_is_denied_is_left_out()
        {
            ZbCustomerRow[] rows = { new() { Id = 1, Contact = new ZbContactRow { Name = "n1" } } };

            Outcome("D2 typed", Guard(rows.AsQueryable()), g => g.ToList(new Filter()).Data);

            PolicyQueryable<ZbCustomerRow> guarded = Guard(rows.AsQueryable());
            ZbCustomerRow row = guarded.ToList(new Filter()).Data.Single();

            // In memory the member can hold any implementation, one with a denied field among them, and an
            // object of a row in memory is never kept once a projection is needed.
            Assert.Null(row.Contact);
            Assert.True(AnyDropped(guarded), Dropped(guarded));
        }

        [Fact]
        public void Zb_D3_in_memory_rows_of_a_DTO_a_subclass_of_which_has_a_denial_are_projected()
        {
            ZbPersonDto[] rows = { new() { Id = 1, Name = "n1", Address = new ZbAddressDto { City = "Basra" } } };

            Outcome("D3 typed", Guard(rows.AsQueryable()), g => g.ToList(new Filter()).Data);

            PolicyQueryable<ZbPersonDto> guarded = Guard(rows.AsQueryable());
            ZbPersonDto row = guarded.ToList(new Filter()).Data.Single();

            // A loaded subclass declares a denied field, and a row in memory can be one: the rows are projected
            // to the declared type, which leaves their objects out.
            Assert.NotSame(rows[0], row);
            Assert.Null(row.Address);
            Assert.Equal("n1", row.Name);
        }

        // ================================================================ E: constructor plus initializer

        [Fact]
        public void Zb_E1_an_initializer_after_a_constructor_still_narrows_the_members_it_assigns()
        {
            IQueryable<ZbOrderRow> rows = _db.Orders.Select(o => new ZbOrderRow(o.Id)
            {
                Code = o.Code,
                Lines = o.Lines.Select(l => new ZbLineRow { Sku = l.Sku, Cost = l.Cost }).ToList()
            });

            Outcome("E1 typed", Guard(rows), g => g.ToList(new Filter()).Data);

            ZbOrderRow row = Guard(rows).ToList(new Filter()).Data.Single();

            Assert.Equal("O1", row.Code);
            Assert.Equal("S1", Assert.Single(row.Lines).Sku);
            Assert.Null(row.Lines[0].Cost);
        }

        // ================================================================ F: deny-by-default, every path allowed

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zb_F1_deny_by_default_allowing_every_path_keeps_an_in_memory_member_with_a_computed_property(DwTier tier)
        {
            ZbPlaceRow[] rows = { new() { Id = 1, Place = new ZbPlaceDto { City = "Basra" } } };
            FakePolicyProvider rules = DenyingAllBut("Id", "Place", "Place.City", "Place.Display");

            Outcome($"F1 {tier} named", Guard(rows.AsQueryable(), tier, rules), g => g.ToList(new Filter { Selects = new List<string> { "Id", "Place" } }).Data);
            Outcome($"F1 {tier} none", Guard(rows.AsQueryable(), tier, rules), g => g.ToList(new Filter()).Data);

            ZbPlaceRow row = Guard(rows.AsQueryable(), tier, rules).ToList(new Filter { Selects = new List<string> { "Id", "Place" } }).Data.Single();

            Assert.Equal("Basra", row.Place?.City);
        }

        [Fact]
        public void Zb_F2_deny_by_default_allowing_every_path_keeps_a_projected_member_with_a_computed_property()
        {
            IQueryable<ZbPlaceRow> rows = _db.Customers.OrderBy(c => c.Id).Select(c => new ZbPlaceRow { Id = c.Id, Place = new ZbPlaceDto { City = c.Name } });
            FakePolicyProvider rules = DenyingAllBut("Id", "Place", "Place.City", "Place.Display");

            Outcome("F2 none", Guard(rows, DwTier.Strict, rules), g => g.ToList(new Filter()).Data);
            Outcome("F2 named", Guard(rows, DwTier.Strict, rules), g => g.ToList(new Filter { Selects = new List<string> { "Id", "Place" } }).Data);

            PolicyQueryable<ZbPlaceRow> guarded = Guard(rows, DwTier.Strict, rules);
            ZbPlaceRow row = guarded.ToList(new Filter()).Data.First();

            Assert.Equal("C1", row.Place?.City);

            // Naming the member, every path of which the policy allows, is not refused.
            ZbPlaceRow named = Guard(rows, DwTier.Strict, rules).ToList(new Filter { Selects = new List<string> { "Id", "Place" } }).Data.First();

            Assert.Equal("C1", named.Place?.City);
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zb_F3_deny_by_default_keeps_a_member_only_when_every_subtype_path_is_named(DwTier tier)
        {
            ZbZoneRow[] rows = { new() { Id = 1, Zone = new ZbZoneDto { Code = "Z1" } } };
            FakePolicyProvider rules = DenyingAllBut("Id", "Zone", "Zone.Code");

            Outcome($"F3 {tier} named", Guard(rows.AsQueryable(), tier, rules), g => g.ToList(new Filter { Selects = new List<string> { "Id", "Zone" } }).Data);

            // The subclass declares Parent, which the policy does not name, and a row in memory can hold it.
            Assert.Throws<PolicyException>(() => Guard(rows.AsQueryable(), tier, rules).ToList(new Filter { Selects = new List<string> { "Id", "Zone" } }));

            // Named too, every path a Zone can hold is granted, and the member is kept whole.
            FakePolicyProvider named = DenyingAllBut("Id", "Zone", "Zone.Code", "Zone.Parent");
            ZbZoneRow row = Guard(rows.AsQueryable(), tier, named).ToList(new Filter { Selects = new List<string> { "Id", "Zone" } }).Data.Single();

            Assert.Equal("Z1", row.Zone?.Code);
        }
    }
}
