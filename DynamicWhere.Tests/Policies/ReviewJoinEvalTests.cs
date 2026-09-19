using System.Collections;
using System.Data.Common;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using DynamicWhere.ex.Classes.Complex;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

// Joins on a filtered, tagged or untracked set, and anonymous carriers: a query the tree holds inline is read where it
// stands, not evaluated, so these read as plain chains.

namespace DynamicWhere.Tests.Policies
{
    public class ZwTier
    {
        public int Id { get; set; }

        public string Label { get; set; } = string.Empty;
    }

    public class ZwCustomer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Region { get; set; } = string.Empty;

        public int ZwTierId { get; set; }

        /// <summary>Automatically included; nothing beneath it is denied.</summary>
        public ZwTier? Tier { get; set; }

        /// <summary>A converted JSON bag.</summary>
        public Dictionary<string, object> Meta { get; set; } = new();

        /// <summary>The only denial is beneath it; nothing includes it.</summary>
        public List<ZwCard> Cards { get; set; } = new();

        public List<ZwOrder> Orders { get; set; } = new();
    }

    public class ZwCard
    {
        public int Id { get; set; }

        public int ZwCustomerId { get; set; }

        public string Label { get; set; } = string.Empty;

        [DwDenied]
        public string? Pan { get; set; }
    }

    public class ZwChannel
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class ZwOrder
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Region { get; set; } = string.Empty;

        public int ZwCustomerId { get; set; }

        public ZwCustomer? Customer { get; set; }

        public int ZwChannelId { get; set; }

        /// <summary>Automatically included.</summary>
        public ZwChannel? Channel { get; set; }

        public Dictionary<string, object> Meta { get; set; } = new();
    }

    /// <summary>Counts the commands a context sends.</summary>
    public sealed class ZwCommandCounter : DbCommandInterceptor
    {
        public int Readers;

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref Readers);

            return result;
        }
    }

    public sealed class ZwContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZwContext(SqliteConnection connection, ZwCommandCounter? counter = null)
        {
            _connection = connection;
            Counter = counter;
        }

        public ZwCommandCounter? Counter { get; }

        public DbSet<ZwCustomer> Customers => Set<ZwCustomer>();

        public DbSet<ZwOrder> Orders => Set<ZwOrder>();

        public DbSet<ZwCard> Cards => Set<ZwCard>();

        /// <summary>An ordinary method on the context (not a mapped function) that returns a query with an include.</summary>
        public IQueryable<ZwCustomer> CustomersWithCardsNamed(string name) => Customers.Include(c => c.Cards).Where(c => c.Name == name);

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseSqlite(_connection);

            if (Counter is not null)
            {
                options.AddInterceptors(Counter);
            }
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<ZwCustomer>().ToTable("ZwCustomers");
            model.Entity<ZwOrder>().ToTable("ZwOrders");
            model.Entity<ZwCustomer>().Navigation(c => c.Tier).AutoInclude();
            model.Entity<ZwOrder>().Navigation(o => o.Channel).AutoInclude();
            model.Entity<ZwCustomer>().Property(c => c.Meta).HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                text => JsonSerializer.Deserialize<Dictionary<string, object>>(text, (JsonSerializerOptions?)null)!);
            model.Entity<ZwOrder>().Property(o => o.Meta).HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                text => JsonSerializer.Deserialize<Dictionary<string, object>>(text, (JsonSerializerOptions?)null)!);
        }
    }

    /// <summary>A repository whose methods the guard now evaluates.</summary>
    public sealed class ZwRepository
    {
        private readonly ZwContext _db;
        private IQueryable<ZwCustomer>? _level2;

        public ZwRepository(ZwContext db) => _db = db;

        public int Calls;

        public int FlipCalls;

        /// <summary>Counts its calls (a log line, a metric, an audit record).</summary>
        public IQueryable<ZwCustomer> Customers()
        {
            Interlocked.Increment(ref Calls);

            return _db.Customers;
        }

        /// <summary>Resolves which customers the caller may see with a query of its own, then hands back a query.</summary>
        public IQueryable<ZwCustomer> Visible()
        {
            Interlocked.Increment(ref Calls);

            List<int> ids = _db.Customers.AsNoTracking().Where(c => c.Region != string.Empty).Select(c => c.Id).ToList();

            return _db.Customers.Where(c => ids.Contains(c.Id));
        }

        /// <summary>A stateful method: the first call hands back the plain set, every later one includes the cards.</summary>
        public IQueryable<ZwCustomer> Flip()
        {
            int call = Interlocked.Increment(ref FlipCalls);

            return call == 1 ? _db.Customers : _db.Customers.Include(c => c.Cards);
        }

        /// <summary>The same as a property getter.</summary>
        public IQueryable<ZwCustomer> FlipSet
        {
            get
            {
                int call = Interlocked.Increment(ref FlipCalls);

                return call == 1 ? _db.Customers : _db.Customers.Include(c => c.Cards);
            }
        }

        /// <summary>A query that captures a built query two levels down.</summary>
        public IQueryable<ZwCustomer> Level1()
        {
            _level2 ??= _db.Customers.Select(c => new ZwCustomer { Id = c.Id, Name = c.Name, Region = c.Region, Cards = c.Cards });

            IQueryable<ZwCustomer> level2 = _level2;

            return _db.Customers.Where(c => c.Name != string.Empty).SelectMany(c => level2.Where(x => x.Id == c.Id));
        }

        /// <summary>Customers already in memory, handed out as a query.</summary>
        public IQueryable<ZwCustomer> InMemory(List<ZwCustomer> held) => held.AsQueryable();
    }

    internal static class ZwKit
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { ReferenceHandler = ReferenceHandler.IgnoreCycles };

        internal static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        internal static string Shape(Expression expression)
        {
            string Try(Func<bool> read)
            {
                try
                {
                    return read().ToString();
                }
                catch (Exception e)
                {
                    return "threw " + e.GetType().Name;
                }
            }

            return $"reshapes={Try(() => QueryRoot.Reshapes(expression))} builds={Try(() => QueryRoot.Builds(expression))}";
        }

        internal static string Trace<T>(PolicyQueryable<T> guarded) where T : class =>
            string.Join(" | ", guarded.LastTrace?.Decisions
                .Where(d => d.Action != PolicyAction.Allowed)
                .Select(d => $"{d.FieldPath} {d.Action}: {d.Reason}") ?? Array.Empty<string>());

        internal static bool AnyDropped<T>(PolicyQueryable<T> guarded) where T : class =>
            guarded.LastTrace?.Decisions.Any(d => d.Action == PolicyAction.Dropped) ?? false;

        internal static string Json(object? value)
        {
            try
            {
                return JsonSerializer.Serialize(value, JsonOptions);
            }
            catch (Exception e)
            {
                return $"<unserializable: {e.GetType().Name}>";
            }
        }

        internal static string Code(Action run)
        {
            try
            {
                run();

                return "ran";
            }
            catch (PolicyException refusal)
            {
                return $"{refusal.ErrorCode}({refusal.FieldPath}; {refusal.SourceOrigin})";
            }
            catch (Exception other)
            {
                return $"{other.GetType().Name}: {other.Message.Split('\n')[0]}";
            }
        }

        internal static object? SafeRead(Func<object?> read)
        {
            try
            {
                return read();
            }
            catch (PolicyException)
            {
                return null;
            }
            catch (LogicException)
            {
                return null;
            }
        }

        /// <summary>Everything reachable from a value, read by runtime type (public and non-public properties and fields).</summary>
        internal static bool Holds(object? value, string text)
        {
            HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
            Stack<object?> pending = new();

            pending.Push(value);

            while (pending.Count > 0)
            {
                object? current = pending.Pop();

                if (current is null || current is MemberInfo || current is Delegate
                    || (current.GetType().Namespace ?? string.Empty).StartsWith("Microsoft.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (current is string held)
                {
                    if (held.Contains(text, StringComparison.Ordinal))
                    {
                        return true;
                    }

                    continue;
                }

                if (current.GetType().IsPrimitive || current is decimal || current is DateTime || current is Guid)
                {
                    continue;
                }

                if (!current.GetType().IsValueType && !seen.Add(current))
                {
                    continue;
                }

                if (current is IDictionary map)
                {
                    foreach (DictionaryEntry item in map)
                    {
                        pending.Push(item.Key);
                        pending.Push(item.Value);
                    }
                }
                else if (current is IEnumerable items)
                {
                    try
                    {
                        foreach (object? item in items)
                        {
                            pending.Push(item);
                        }
                    }
                    catch
                    {
                        // Unreadable.
                    }
                }

                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                foreach (PropertyInfo property in current.GetType().GetProperties(all))
                {
                    if (property.GetIndexParameters().Length != 0 || !property.CanRead)
                    {
                        continue;
                    }

                    try
                    {
                        pending.Push(property.GetValue(current));
                    }
                    catch
                    {
                        // Unreadable.
                    }
                }

                for (Type? type = current.GetType(); type is not null && type != typeof(object); type = type.BaseType)
                {
                    if (type.Namespace is { } ns && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal)
                                                     || ns.StartsWith("Microsoft.", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    foreach (FieldInfo field in type.GetFields(all | BindingFlags.DeclaredOnly))
                    {
                        try
                        {
                            pending.Push(field.GetValue(current));
                        }
                        catch
                        {
                            // Unreadable.
                        }
                    }
                }
            }

            return false;
        }
    }

    public sealed class ReviewJoinEvalTests : IDisposable
    {
        private const string Secret = "zw-pan-secret";

        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZwCommandCounter _counter = new();
        private readonly ZwContext _db;
        private readonly ZwRepository _repo;

        public ReviewJoinEvalTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZwContext(_connection, _counter);
            _db.Database.EnsureCreated();
            _repo = new ZwRepository(_db);

            ZwTier gold = new() { Label = "gold" };
            ZwChannel web = new() { Name = "web" };

            ZwCustomer c1 = new()
            {
                Name = "C1",
                Region = "north",
                Tier = gold,
                Meta = { ["k"] = "v1" },
                Cards = { new ZwCard { Label = "visa", Pan = Secret } }
            };
            ZwCustomer c2 = new() { Name = "C2", Region = "south", Tier = gold, Meta = { ["k"] = "v2" } };

            c1.Orders.Add(new ZwOrder { Code = "O1", Region = "north", Channel = web, Meta = { ["k"] = "o1" } });
            c1.Orders.Add(new ZwOrder { Code = "O2", Region = "south", Channel = web, Meta = { ["k"] = "o2" } });
            c2.Orders.Add(new ZwOrder { Code = "O3", Region = "south", Channel = web, Meta = { ["k"] = "o3" } });

            _db.Customers.AddRange(c1, c2);
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private (List<T>? Rows, PolicyQueryable<T> Guarded) Run<T>(string label, IQueryable<T> source) where T : class
        {
            string shape = ZwKit.Shape(source.Expression);
            PolicyQueryable<T> guarded = ZwKit.Guard(source);

            try
            {
                int count = source.AsNoTracking().ToList().Count;

                _out.WriteLine($"{label}: unguarded OK rows={count} {shape}");
            }
            catch (Exception e)
            {
                _out.WriteLine($"{label}: unguarded THREW {e.GetType().Name} {e.Message.Split('\n')[0]} {shape}");

                return (null, guarded);
            }

            _db.ChangeTracker.Clear();

            List<T> rows = guarded.ToList(new Filter()).Data;

            _out.WriteLine($"{label}: guarded rows={rows.Count} trace=[{ZwKit.Trace(guarded)}]");

            return (rows, guarded);
        }

        private void CustomersAsLoaded(string label, IQueryable<ZwCustomer> source, int count)
        {
            (List<ZwCustomer>? rows, PolicyQueryable<ZwCustomer> guarded) = Run(label, source);

            Assert.NotNull(rows);
            Assert.Equal(count, rows!.Count);
            Assert.False(ZwKit.AnyDropped(guarded), ZwKit.Trace(guarded));
            Assert.All(rows, row => Assert.Equal("gold", row.Tier?.Label));
            Assert.All(rows, row => Assert.True(row.Meta.ContainsKey("k"), "Meta left out"));
        }

        private void OrdersAsLoaded(string label, IQueryable<ZwOrder> source, int count)
        {
            (List<ZwOrder>? rows, PolicyQueryable<ZwOrder> guarded) = Run(label, source);

            Assert.NotNull(rows);
            Assert.Equal(count, rows!.Count);
            Assert.False(ZwKit.AnyDropped(guarded), ZwKit.Trace(guarded));
            Assert.All(rows, row => Assert.Equal("web", row.Channel?.Name));
            Assert.All(rows, row => Assert.True(row.Meta.ContainsKey("k"), "Meta left out"));
        }

        // ------------------------------------------------------------------ J: joins whose inner sequence is not a bare set

        [Fact]
        public void Zw_J0_control_join_on_a_bare_set()
        {
            CustomersAsLoaded("J0 Join(_db.Customers)", _db.Orders.Join(_db.Customers, o => o.ZwCustomerId, c => c.Id, (o, c) => c), 3);
        }

        [Fact]
        public void Zw_J1_join_on_a_filtered_set()
        {
            CustomersAsLoaded(
                "J1 Join(_db.Customers.Where(..))",
                _db.Orders.Join(_db.Customers.Where(c => c.Region != string.Empty), o => o.ZwCustomerId, c => c.Id, (o, c) => c),
                3);
        }

        [Fact]
        public void Zw_J2_query_syntax_join_on_an_untracked_set()
        {
            IQueryable<ZwCustomer> source =
                from o in _db.Orders
                join c in _db.Customers.AsNoTracking() on o.ZwCustomerId equals c.Id
                select c;

            CustomersAsLoaded("J2 join c in _db.Customers.AsNoTracking()", source, 3);
        }

        [Fact]
        public void Zw_J3_left_join_on_a_filtered_set()
        {
            IQueryable<ZwCustomer> source =
                from o in _db.Orders
                join c in _db.Customers.Where(x => x.Region != string.Empty) on o.ZwCustomerId equals c.Id into cs
                from c in cs.DefaultIfEmpty()
                select c;

            CustomersAsLoaded("J3 left join on a filtered set", source, 3);
        }

        [Fact]
        public void Zw_J4_join_on_a_filtered_set_returning_the_outer_rows()
        {
            OrdersAsLoaded(
                "J4 Join(_db.Customers.Where(..), (o, c) => o)",
                _db.Orders.Join(_db.Customers.Where(c => c.Region != string.Empty), o => o.ZwCustomerId, c => c.Id, (o, c) => o),
                3);
        }

        [Fact]
        public void Zw_J5_join_on_a_tagged_set()
        {
            CustomersAsLoaded(
                "J5 Join(_db.Customers.TagWith(..))",
                _db.Orders.Join(_db.Customers.TagWith("lookup"), o => o.ZwCustomerId, c => c.Id, (o, c) => c),
                3);
        }

        [Fact]
        public void Zw_J6_where_then_join_on_a_filtered_order_set()
        {
            OrdersAsLoaded(
                "J6 _db.Orders.Where(..).Join(_db.Orders.Where(..), (a, b) => a)",
                _db.Orders.Where(o => o.Code != string.Empty)
                    .Join(_db.Orders.Where(o => o.Region != string.Empty), a => a.Id, b => b.Id, (a, b) => a),
                3);
        }

        // ------------------------------------------------------------------ E: what the evaluation of user code costs

        [Fact]
        public void Zw_E4_a_repository_query_capturing_a_built_query_two_levels_down()
        {
            IQueryable<ZwCustomer> source = _db.Orders.SelectMany(o => _repo.Level1().Where(c => c.Id == o.ZwCustomerId));
            string unguarded;

            try
            {
                unguarded = ZwKit.Holds(source.AsNoTracking().ToList(), Secret) ? "HOLDS PAN" : "no pan";
            }
            catch (Exception e)
            {
                unguarded = $"THREW {e.GetType().Name}: {e.Message.Split('\n')[0]}";
            }

            PolicyQueryable<ZwCustomer> guarded = ZwKit.Guard(source);
            object? data;

            try
            {
                data = ZwKit.SafeRead(() => guarded.ToList(new Filter()).Data);
            }
            catch (InvalidOperationException) when (typeof(DbContext).Assembly.GetName().Version!.Major < 7)
            {
                // EF Core 6 cannot translate the projection this shape needs, so the guarded read fails closed.
                return;
            }

            _out.WriteLine($"E4 {ZwKit.Shape(source.Expression)} unguarded={unguarded} sent={ZwKit.Json(data)} trace=[{ZwKit.Trace(guarded)}]");

            Assert.False(ZwKit.Holds(data, Secret));
        }

        [Fact]
        public void Zw_E5_a_repository_method_handing_out_rows_in_memory()
        {
            List<ZwCustomer> held = _db.Customers.Include(c => c.Cards).AsNoTracking().ToList();
            IQueryable<ZwCustomer> source = _db.Orders.SelectMany(o => _repo.InMemory(held).Where(c => c.Id == o.ZwCustomerId));

            string code = ZwKit.Code(() => source.AsNoTracking().ToList());
            PolicyQueryable<ZwCustomer> guarded = ZwKit.Guard(source);
            object? data = null;
            string guardedCode = ZwKit.Code(() => data = guarded.ToList(new Filter()).Data);

            _out.WriteLine($"E5 {ZwKit.Shape(source.Expression)} unguarded={code} guarded={guardedCode} sent={ZwKit.Json(data)}");

            Assert.False(ZwKit.Holds(data, Secret));
        }

        [Fact]
        public void Zw_E6_a_context_method_that_includes_read_through_a_correlated_argument()
        {
            IQueryable<ZwCustomer> source = _db.Orders.SelectMany(o => _db.CustomersWithCardsNamed(o.Code));

            string code = ZwKit.Code(() => source.AsNoTracking().ToList());
            PolicyQueryable<ZwCustomer> guarded = ZwKit.Guard(source);
            object? data = null;
            string guardedCode = ZwKit.Code(() => data = guarded.ToList(new Filter()).Data);

            _out.WriteLine($"E6 {ZwKit.Shape(source.Expression)} unguarded={code} guarded={guardedCode} sent={ZwKit.Json(data)}");

            Assert.False(ZwKit.Holds(data, Secret));
        }

        // ------------------------------------------------------------------ A: anonymous carriers under the caller's tracking

        [Theory]
        [InlineData("AsTracking")]
        [InlineData("IdentityResolution")]
        [InlineData("None")]
        public void Zw_A1_an_anonymous_carrier_with_the_cards_in_a_context_that_tracks_them(string tracking)
        {
            // Earlier in the unit of work the application read the cards with tracking.
            _ = _db.Cards.ToList();

            IQueryable<ZwOrder> orders = tracking switch
            {
                "AsTracking" => _db.Orders.AsTracking(),
                "IdentityResolution" => _db.Orders.AsNoTrackingWithIdentityResolution(),
                _ => _db.Orders
            };

            IQueryable<ZwCustomer> source = orders
                .Select(o => new { o.Customer, Cards = o.Customer!.Cards.ToList() })
                .Select(x => x.Customer!);

            PolicyQueryable<ZwCustomer> guarded = ZwKit.Guard(source);
            object? data = ZwKit.SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine($"A1 {tracking} {ZwKit.Shape(source.Expression)} tracked={_db.ChangeTracker.Entries().Count()} sent={ZwKit.Json(data)} trace=[{ZwKit.Trace(guarded)}]");

            Assert.False(ZwKit.Holds(data, Secret));
        }

        [Theory]
        [InlineData("AsTracking")]
        [InlineData("IdentityResolution")]
        public void Zw_A2_the_carrier_is_the_last_projection_before_a_member_read(string tracking)
        {
            _ = _db.Cards.ToList();

            IQueryable<ZwOrder> orders = tracking == "AsTracking" ? _db.Orders.AsTracking() : _db.Orders.AsNoTrackingWithIdentityResolution();

            IQueryable<ZwCustomer> source = orders
                .Select(o => new { Order = o, Owner = o.Customer, o.Customer!.Cards })
                .Where(x => x.Cards.Count >= 0)
                .Select(x => x.Owner!);

            PolicyQueryable<ZwCustomer> guarded = ZwKit.Guard(source);
            object? data = ZwKit.SafeRead(() => guarded.ToList(new Filter()).Data);

            _out.WriteLine($"A2 {tracking} {ZwKit.Shape(source.Expression)} sent={ZwKit.Json(data)} trace=[{ZwKit.Trace(guarded)}]");

            Assert.False(ZwKit.Holds(data, Secret));
        }

        [Fact]
        public void Zw_A3_control_the_anonymous_carrier_query_reads_as_it_did()
        {
            CustomersAsLoaded(
                "A3 Select(o => new { o.Customer, o.Code }).Select(x => x.Customer)",
                _db.Orders.Select(o => new { o.Customer, o.Code }).Select(x => x.Customer!),
                3);
        }
    }
}
