using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

// What an ordinary reshaped EF Core query gets when the only denial (ZyrCard.Pan) sits beneath a navigation nothing
// loads: no projection, the auto-included Tier kept, the converted Meta bag kept. Specifications, repositories,
// FromSql, a context's Set through an interface, composite keys and query-syntax joins, lets and left joins read no
// more than a plain chain does. QueryRoot's internals are reached by reflection.

namespace DynamicWhere.Tests.Policies
{
    public class ZyrTier
    {
        public int Id { get; set; }

        public string Label { get; set; } = string.Empty;
    }

    public class ZyrCustomer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Region { get; set; } = string.Empty;

        public int ZyrTierId { get; set; }

        /// <summary>Automatically included; nothing beneath it is denied.</summary>
        public ZyrTier? Tier { get; set; }

        /// <summary>A converted JSON bag, the pre-EF7 way to store one.</summary>
        public Dictionary<string, object> Meta { get; set; } = new();

        /// <summary>Never loaded by any probe; the only denial is beneath it.</summary>
        public List<ZyrCard> Cards { get; set; } = new();

        public List<ZyrOrder> Orders { get; set; } = new();
    }

    public class ZyrCard
    {
        public int Id { get; set; }

        public int ZyrCustomerId { get; set; }

        public string Label { get; set; } = string.Empty;

        [DwDenied]
        public string? Pan { get; set; }
    }

    public class ZyrChannel
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class ZyrOrder
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Region { get; set; } = string.Empty;

        public int ZyrCustomerId { get; set; }

        /// <summary>Never loaded; Customer.Cards.Pan is the denial beneath it.</summary>
        public ZyrCustomer? Customer { get; set; }

        public int ZyrChannelId { get; set; }

        /// <summary>Automatically included; nothing beneath it is denied.</summary>
        public ZyrChannel? Channel { get; set; }

        /// <summary>A converted JSON bag.</summary>
        public Dictionary<string, object> Meta { get; set; } = new();
    }

    /// <summary>A context exposed through an interface, as clean-architecture templates do.</summary>
    public interface IZyrContext
    {
        DbSet<TEntity> Set<TEntity>() where TEntity : class;
    }

    /// <summary>What a repository layer looks like: a method and a property handing out a plain set.</summary>
    public sealed class ZyrRepository
    {
        private readonly ZyrContext _db;

        public ZyrRepository(ZyrContext db) => _db = db;

        public IQueryable<ZyrCustomer> Customers() => _db.Customers;

        public IQueryable<ZyrCustomer> CustomerSet => _db.Customers;
    }

    /// <summary>Reusable predicates, the specification pattern.</summary>
    public static class ZyrSpecs
    {
        public static readonly Expression<Func<ZyrOrder, bool>> Open = o => o.Code != string.Empty;

        public static Expression<Func<ZyrOrder, bool>> InRegion(string region) => o => o.Region == region;

        public static readonly Expression<Func<ZyrCustomer, bool>> Named = c => c.Name != string.Empty;
    }

    public readonly record struct ZyrRegionId(int Value);

    /// <summary>A request object a controller captures into a query.</summary>
    public sealed class ZyrFilter
    {
        public string Region { get; set; } = string.Empty;

        public List<string> Codes { get; set; } = new();
    }

    public sealed class ZyrContext : DbContext, IZyrContext
    {
        private readonly SqliteConnection _connection;

        public ZyrContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZyrCustomer> Customers => Set<ZyrCustomer>();

        public DbSet<ZyrOrder> Orders => Set<ZyrOrder>();

        public DbSet<ZyrTier> Tiers => Set<ZyrTier>();

        public DbSet<ZyrChannel> Channels => Set<ZyrChannel>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<ZyrCustomer>().ToTable("ZyrCustomers");
            model.Entity<ZyrOrder>().ToTable("ZyrOrders");
            model.Entity<ZyrCustomer>().Navigation(c => c.Tier).AutoInclude();
            model.Entity<ZyrOrder>().Navigation(o => o.Channel).AutoInclude();
            model.Entity<ZyrCustomer>().Property(c => c.Meta).HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                text => JsonSerializer.Deserialize<Dictionary<string, object>>(text, (JsonSerializerOptions?)null)!);
            model.Entity<ZyrOrder>().Property(o => o.Meta).HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                text => JsonSerializer.Deserialize<Dictionary<string, object>>(text, (JsonSerializerOptions?)null)!);
        }
    }

    /// <summary>Writes a test's outcome to its output, and reads the query's shape.</summary>
    internal static class ZyLog
    {
        internal static void Write(ITestOutputHelper output, string line) => output.WriteLine(line);

        /// <summary>QueryRoot.Reshapes / Builds, by reflection, where the tree has them.</summary>
        internal static string Shape(Expression expression)
        {
            Type? root = typeof(Filter).Assembly.GetType("DynamicWhere.ex.Source.QueryRoot");

            string Call(string name)
            {
                MethodInfo? method = root?.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(Expression) }, null);

                if (method is null)
                {
                    return "n/a";
                }

                try
                {
                    return method.Invoke(null, new object[] { expression })!.ToString()!;
                }
                catch (TargetInvocationException e)
                {
                    return "threw " + e.InnerException?.GetType().Name;
                }
            }

            return $"reshapes={Call("Reshapes")} builds={Call("Builds")}";
        }

        internal static string Decisions<T>(PolicyQueryable<T> guarded) where T : class =>
            string.Join(" | ", guarded.LastTrace?.Decisions
                .Where(d => d.Action != PolicyAction.Allowed)
                .Select(d => $"{d.FieldPath} {d.Action}: {d.Reason}") ?? Array.Empty<string>());

        internal static bool AnyDropped<T>(PolicyQueryable<T> guarded) where T : class =>
            guarded.LastTrace?.Decisions.Any(d => d.Action == PolicyAction.Dropped) ?? false;
    }

    public sealed class ReviewReshapePrecisionTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZyrContext _db;
        private readonly ZyrRepository _repo;

        public ReviewReshapePrecisionTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZyrContext(_connection);
            _db.Database.EnsureCreated();
            _repo = new ZyrRepository(_db);

            ZyrTier gold = new() { Label = "gold" };
            ZyrChannel web = new() { Name = "web" };

            ZyrCustomer c1 = new()
            {
                Name = "C1",
                Region = "north",
                Tier = gold,
                Meta = { ["k"] = "v1" },
                Cards = { new ZyrCard { Label = "visa", Pan = "zyr-pan-secret" } }
            };
            ZyrCustomer c2 = new() { Name = "C2", Region = "south", Tier = gold, Meta = { ["k"] = "v2" } };

            c1.Orders.Add(new ZyrOrder { Code = "O1", Region = "north", Channel = web, Meta = { ["k"] = "o1" } });
            c1.Orders.Add(new ZyrOrder { Code = "O2", Region = "south", Channel = web, Meta = { ["k"] = "o2" } });
            c2.Orders.Add(new ZyrOrder { Code = "O3", Region = "south", Channel = web, Meta = { ["k"] = "o3" } });

            _db.Customers.AddRange(c1, c2);
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        /// <summary>
        /// Runs the unguarded query (to show EF Core translates it) and the guarded one, logs both, and returns the
        /// guarded rows with the handle; null rows when the unguarded query itself does not translate.
        /// </summary>
        private (List<T>? Rows, PolicyQueryable<T> Guarded) Run<T>(string label, IQueryable<T> source) where T : class
        {
            string shape = ZyLog.Shape(source.Expression);
            PolicyQueryable<T> guarded = Guard(source);

            try
            {
                int count = source.AsNoTracking().ToList().Count;

                ZyLog.Write(_out, $"R {label}: unguarded OK rows={count} {shape}");
            }
            catch (Exception e)
            {
                ZyLog.Write(_out, $"R {label}: unguarded THREW {e.GetType().Name} {e.Message.Split('\n')[0]} {shape}");

                return (null, guarded);
            }

            _db.ChangeTracker.Clear();

            try
            {
                List<T> rows = guarded.ToList(new Filter()).Data;
                int tracked = _db.ChangeTracker.Entries().Count();

                ZyLog.Write(_out, $"R {label}: guarded OK rows={rows.Count} tracked={tracked} dropped=[{ZyLog.Decisions(guarded)}]");

                return (rows, guarded);
            }
            catch (Exception e)
            {
                string code = e is PolicyException refusal ? refusal.ErrorCode.ToString() : string.Empty;

                ZyLog.Write(_out, $"R {label}: guarded THREW {e.GetType().Name} {code} {e.Message.Split('\n')[0]} dropped=[{ZyLog.Decisions(guarded)}]");

                throw;
            }
        }

        private void CustomersAsLoaded(string label, IQueryable<ZyrCustomer> source, int count)
        {
            (List<ZyrCustomer>? rows, PolicyQueryable<ZyrCustomer> guarded) = Run(label, source);

            if (rows is null)
            {
                return;
            }

            Assert.Equal(count, rows.Count);
            Assert.False(ZyLog.AnyDropped(guarded), ZyLog.Decisions(guarded));
            Assert.All(rows, row => Assert.Equal("gold", row.Tier?.Label));
            Assert.All(rows, row => Assert.True(row.Meta.ContainsKey("k"), "Meta left out"));
        }

        private void OrdersAsLoaded(string label, IQueryable<ZyrOrder> source, int count)
        {
            (List<ZyrOrder>? rows, PolicyQueryable<ZyrOrder> guarded) = Run(label, source);

            if (rows is null)
            {
                return;
            }

            Assert.Equal(count, rows.Count);
            Assert.False(ZyLog.AnyDropped(guarded), ZyLog.Decisions(guarded));
            Assert.All(rows, row => Assert.Equal("web", row.Channel?.Name));
            Assert.All(rows, row => Assert.True(row.Meta.ContainsKey("k"), "Meta left out"));
        }

        // ------------------------------------------------------------------ controls: expected green everywhere

        [Fact]
        public void Zy_R00_control_Select_to_a_navigation()
        {
            CustomersAsLoaded("R00 Select(o => o.Customer)", _db.Orders.Select(o => o.Customer!), 3);
        }

        [Fact]
        public void Zy_R01_control_SelectMany_over_a_collection()
        {
            OrdersAsLoaded("R01 SelectMany(c => c.Orders)", _db.Customers.SelectMany(c => c.Orders), 3);
        }

        [Fact]
        public void Zy_R02_control_correlated_subquery_over_a_captured_set_property()
        {
            CustomersAsLoaded(
                "R02 SelectMany(o => _db.Customers.Where(..))",
                _db.Orders.SelectMany(o => _db.Customers.Where(c => c.Id == o.ZyrCustomerId)),
                3);
        }

        [Fact]
        public void Zy_R03_control_correlated_subquery_over_DbContext_Set()
        {
            CustomersAsLoaded(
                "R03 SelectMany(o => _db.Set<C>().Where(..))",
                _db.Orders.SelectMany(o => _db.Set<ZyrCustomer>().Where(c => c.Id == o.ZyrCustomerId)),
                3);
        }

        [Fact]
        public void Zy_R04_control_correlated_subquery_over_a_repository_property()
        {
            CustomersAsLoaded(
                "R04 SelectMany(o => _repo.CustomerSet.Where(..))",
                _db.Orders.SelectMany(o => _repo.CustomerSet.Where(c => c.Id == o.ZyrCustomerId)),
                3);
        }

        [Fact]
        public void Zy_R05_control_captured_plain_query_variable()
        {
            IQueryable<ZyrCustomer> named = _db.Customers.Where(c => c.Name != string.Empty).AsNoTracking();

            CustomersAsLoaded(
                "R05 SelectMany(o => named.Where(..))",
                _db.Orders.SelectMany(o => named.Where(c => c.Id == o.ZyrCustomerId)),
                3);
        }

        [Fact]
        public void Zy_R06_control_captured_values_in_a_nested_predicate()
        {
            List<string> regions = new() { "north", "south" };
            ZyrRegionId region = new(1);
            DateTime cutoff = new(2000, 1, 1);

            OrdersAsLoaded(
                "R06 nested predicate: list.Contains, struct id, DateTime, new DateTime",
                _db.Customers.SelectMany(c => c.Orders.Where(o => regions.Contains(o.Region)
                                                                   && region.Value > 0
                                                                   && new DateTime(2100, 1, 1) > cutoff)),
                3);
        }

        [Fact]
        public void Zy_R07_control_Join_on_a_single_key()
        {
            CustomersAsLoaded(
                "R07 Join single key",
                _db.Orders.Join(_db.Customers, o => o.ZyrCustomerId, c => c.Id, (o, c) => c),
                3);
        }

        // ------------------------------------------------------------------ what a lambda reads without building

        [Fact]
        public void Zy_R10_a_specification_variable_inside_a_correlated_subquery()
        {
            Expression<Func<ZyrCustomer, bool>> named = c => c.Name != string.Empty;

            CustomersAsLoaded(
                "R10 SelectMany(o => _db.Customers.Where(specVariable).Where(..))",
                _db.Orders.SelectMany(o => _db.Customers.Where(named).Where(c => c.Id == o.ZyrCustomerId)),
                3);
        }

        [Fact]
        public void Zy_R11_a_static_specification_on_a_navigation_collection()
        {
            OrdersAsLoaded(
                "R11 SelectMany(c => c.Orders.AsQueryable().Where(ZyrSpecs.Open))",
                _db.Customers.SelectMany(c => c.Orders.AsQueryable().Where(ZyrSpecs.Open)),
                3);
        }

        [Fact]
        public void Zy_R12_a_specification_factory_method_on_a_navigation_collection()
        {
            OrdersAsLoaded(
                "R12 SelectMany(c => c.Orders.AsQueryable().Where(ZyrSpecs.InRegion(\"south\")))",
                _db.Customers.SelectMany(c => c.Orders.AsQueryable().Where(ZyrSpecs.InRegion("south"))),
                2);
        }

        [Fact]
        public void Zy_R13_a_static_specification_inside_a_correlated_subquery_over_a_set()
        {
            CustomersAsLoaded(
                "R13 SelectMany(o => _db.Customers.Where(ZyrSpecs.Named).Where(..))",
                _db.Orders.SelectMany(o => _db.Customers.Where(ZyrSpecs.Named).Where(c => c.Id == o.ZyrCustomerId)),
                3);
        }

        [Fact]
        public void Zy_R14_a_repository_method_inside_a_correlated_subquery()
        {
            CustomersAsLoaded(
                "R14 SelectMany(o => _repo.Customers().Where(..))",
                _db.Orders.SelectMany(o => _repo.Customers().Where(c => c.Id == o.ZyrCustomerId)),
                3);
        }

        [Fact]
        public void Zy_R15_Set_through_a_context_interface_inside_a_correlated_subquery()
        {
            IZyrContext context = _db;

            CustomersAsLoaded(
                "R15 SelectMany(o => iface.Set<C>().Where(..))",
                _db.Orders.SelectMany(o => context.Set<ZyrCustomer>().Where(c => c.Id == o.ZyrCustomerId)),
                3);
        }

        [Fact]
        public void Zy_R16_FromSqlRaw_inside_a_correlated_subquery()
        {
            CustomersAsLoaded(
                "R16 SelectMany(o => _db.Customers.FromSqlRaw(..).Where(..))",
                _db.Orders.SelectMany(o => _db.Customers.FromSqlRaw("SELECT * FROM ZyrCustomers").Where(c => c.Id == o.ZyrCustomerId)),
                3);
        }

        [Fact]
        public void Zy_R17_a_specification_in_query_syntax()
        {
            Expression<Func<ZyrOrder, bool>> open = o => o.Code != string.Empty;

            IQueryable<ZyrOrder> source =
                from c in _db.Customers
                from o in c.Orders.AsQueryable().Where(open)
                select o;

            OrdersAsLoaded("R17 from c .. from o in c.Orders.AsQueryable().Where(spec) select o", source, 3);
        }

        // ------------------------------------------------------------------ 3.2.0-wide: anonymous keys and carriers

        [Fact]
        public void Zy_R20_Join_on_a_composite_key()
        {
            CustomersAsLoaded(
                "R20 Join composite key",
                _db.Orders.Join(_db.Customers, o => new { Id = o.ZyrCustomerId, o.Region }, c => new { c.Id, c.Region }, (o, c) => c),
                2);
        }

        [Fact]
        public void Zy_R21_left_join_in_query_syntax()
        {
            IQueryable<ZyrCustomer> source =
                from o in _db.Orders
                join c in _db.Customers on o.ZyrCustomerId equals c.Id into cs
                from c in cs.DefaultIfEmpty()
                select c;

            CustomersAsLoaded("R21 left join (GroupJoin + DefaultIfEmpty)", source, 3);
        }

        [Fact]
        public void Zy_R22_two_from_clauses_with_a_where()
        {
            IQueryable<ZyrOrder> source =
                from c in _db.Customers
                from o in c.Orders
                where o.Code != string.Empty
                select o;

            OrdersAsLoaded("R22 from c from o in c.Orders where .. select o", source, 3);
        }

        [Fact]
        public void Zy_R23_GroupBy_on_a_composite_key_then_First()
        {
            if (typeof(DbContext).Assembly.GetName().Version!.Major < 7)
            {
                return;
            }

            OrdersAsLoaded(
                "R23 GroupBy(new { .. }).Select(g => g.OrderBy(..).First())",
                _db.Orders.GroupBy(o => new { o.ZyrCustomerId, o.Region }).Select(g => g.OrderBy(o => o.Id).First()),
                3);
        }

        [Fact]
        public void Zy_R08_control_a_conditional_with_null()
        {
            CustomersAsLoaded(
                "R08 Select(o => o.ZyrCustomerId > 0 ? o.Customer : null)",
                _db.Orders.Select(o => o.ZyrCustomerId > 0 ? o.Customer! : null!),
                3);
        }

        [Fact]
        public void Zy_R09_control_captured_filter_object_in_a_nested_predicate()
        {
            ZyrFilter filter = new() { Region = "south", Codes = new List<string> { "O2", "O3" } };

            OrdersAsLoaded(
                "R09 nested predicate over a captured filter object's members",
                _db.Customers.SelectMany(c => c.Orders.Where(o => o.Region == filter.Region && filter.Codes.Contains(o.Code))),
                2);
        }

        [Fact]
        public void Zy_R18_control_two_from_clauses_without_a_where()
        {
            IQueryable<ZyrOrder> source =
                from c in _db.Customers
                from o in c.Orders
                select o;

            OrdersAsLoaded("R18 from c from o in c.Orders select o", source, 3);
        }

        [Fact]
        public void Zy_R19_control_join_in_query_syntax()
        {
            IQueryable<ZyrCustomer> source =
                from o in _db.Orders
                join c in _db.Customers on o.ZyrCustomerId equals c.Id
                select c;

            CustomersAsLoaded("R19 from o join c .. select c", source, 3);
        }

        [Fact]
        public void Zy_R25_join_then_where_in_query_syntax()
        {
            IQueryable<ZyrCustomer> source =
                from o in _db.Orders
                join c in _db.Customers on o.ZyrCustomerId equals c.Id
                where o.Code != string.Empty
                select c;

            CustomersAsLoaded("R25 from o join c .. where o.Code .. select c", source, 3);
        }

        /// <summary>Shape only: value-typed calls in predicates and keys (string.Format, interpolation, Convert, ToUpper, Nullable).</summary>
        [Fact]
        public void Zy_R30_control_value_calls_in_predicates_and_keys_are_not_read_as_building()
        {
            (string Label, System.Linq.Expressions.Expression Expression)[] shapes =
            {
                ("string.Format in a nested predicate", _db.Customers.SelectMany(c => c.Orders.Where(o => string.Format("{0}", o.Code) == "O1")).Expression),
                ("interpolation in a nested predicate", _db.Customers.SelectMany(c => c.Orders.Where(o => $"{o.Code}-{o.Region}" != string.Empty)).Expression),
                ("Convert in a nested predicate", _db.Customers.SelectMany(c => c.Orders.Where(o => Convert.ToString(o.ZyrCustomerId) != string.Empty)).Expression),
                ("ToUpper key", _db.Orders.GroupBy(o => o.Region.ToUpper()).Select(g => g.OrderBy(o => o.Id).First()).Expression),
                ("Nullable key", _db.Orders.GroupBy(o => (int?)o.ZyrCustomerId).Select(g => g.OrderBy(o => o.Id).First()).Expression),
                ("EF.Functions in a nested predicate", _db.Customers.SelectMany(c => c.Orders.Where(o => EF.Functions.Like(o.Code, "O%"))).Expression)
            };

            List<string> building = new();

            foreach ((string label, System.Linq.Expressions.Expression expression) in shapes)
            {
                string shape = ZyLog.Shape(expression);

                ZyLog.Write(_out, $"R R30 {label}: {shape}");

                if (shape.Contains("builds=True"))
                {
                    building.Add(label);
                }
            }

            Assert.Empty(building);
        }

        [Fact]
        public void Zy_R24_let_clause()
        {
            IQueryable<ZyrCustomer> source =
                from o in _db.Orders
                let c = o.Customer
                where c!.Name != string.Empty
                select c;

            CustomersAsLoaded("R24 let c = o.Customer", source, 3);
        }
    }
}
