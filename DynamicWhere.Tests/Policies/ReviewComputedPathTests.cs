using System.ComponentModel.DataAnnotations.Schema;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
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

namespace DynamicWhere.Tests.Policies
{
    // ---------------------------------------------------------------------------------------------
    // Probe model. EF Core 6 compatible on purpose, so the floor leg runs it too.
    // ---------------------------------------------------------------------------------------------

    public class ObMoney
    {
        public decimal Amount { get; set; }

        public string Currency { get; set; } = "USD";

        /// <summary>A getter over two owned columns: no database can answer it.</summary>
        public bool IsZero => Amount == 0m;
    }

    public class ObCustomer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Nickname { get; set; }

        public List<ObOrder> Orders { get; set; } = new();

        /// <summary>An unmapped getter one navigation away.</summary>
        [NotMapped]
        public string Handle => Name + "!";
    }

    public class ObOrder
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public DateTime PlacedAt { get; set; }

        public DateTime? ShippedAt { get; set; }

        public TimeSpan Window { get; set; }

        public int Quantity { get; set; }

        public int CustomerId { get; set; }

        public ObCustomer Customer { get; set; } = null!;

        public ObMoney Total { get; set; } = new();

        /// <summary>An unmapped getter over two mapped columns of the entity itself.</summary>
        [NotMapped]
        public string Slug => Code + "-" + Id;
    }

    /// <summary>A row a caller projects.</summary>
    public class ObOrderRow
    {
        public ObOrderRow()
        {
        }

        public ObOrderRow(int id, string code)
        {
            Id = id;
            Code = code;
        }

        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public ObMoney Total { get; set; } = new();

        public ObCustomer? Customer { get; set; }

        public string Label { get; set; } = string.Empty;

        public ObNest Nest { get; set; } = new();
    }

    public class ObNest
    {
        public ObNest? Inner { get; set; }

        public string Leaf { get; set; } = string.Empty;

        public ObMoney Money { get; set; } = new();
    }

    // ---- a hierarchy --------------------------------------------------------------------------

    public class ObParty
    {
        public int Id { get; set; }

        public string Kind { get; set; } = string.Empty;
    }

    public class ObMerchant : ObParty
    {
        public string? Licence { get; set; }
    }

    // ---- an alias -----------------------------------------------------------------------------

    public class ObAliased
    {
        public int Id { get; set; }

        [DwAlias("customer_name")]
        public string Name { get; set; } = string.Empty;

        [NotMapped]
        [DwAlias("computed_tag")]
        public string Tag => Name + "!";
    }

    public sealed class ObContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ObContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ObOrder> Orders => Set<ObOrder>();

        public DbSet<ObCustomer> Customers => Set<ObCustomer>();

        public DbSet<ObParty> Parties => Set<ObParty>();

        public DbSet<ObAliased> Aliased => Set<ObAliased>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<ObOrder>().OwnsOne(order => order.Total);
            model.Entity<ObOrder>().Ignore(order => order.Slug);
            model.Entity<ObOrder>().Property<string>("Tenant");
            model.Entity<ObParty>().HasDiscriminator<string>("Discriminator")
                .HasValue<ObParty>("party")
                .HasValue<ObMerchant>("merchant");
            model.Entity<ObAliased>().Ignore(row => row.Tag);
            model.Entity<ObCustomer>().Ignore(customer => customer.Handle);
        }
    }

    /// <summary>
    /// Probes for DW-17 over-blocking: a path the strict tier now refuses that an unguarded query
    /// would in fact have run. Every case checks the unguarded query first.
    /// </summary>
    public sealed class ReviewComputedPathTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ObContext _db;

        public ReviewComputedPathTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ObContext(_connection);
            _db.Database.EnsureCreated();

            ObCustomer customer = new() { Name = "Acme", Nickname = "A" };

            _db.Customers.Add(customer);
            _db.Orders.Add(new ObOrder
            {
                Code = "A-001",
                PlacedAt = new DateTime(2024, 3, 9, 10, 30, 0),
                ShippedAt = new DateTime(2024, 3, 10),
                Window = TimeSpan.FromHours(3),
                Quantity = 2,
                Customer = customer,
                Total = new ObMoney { Amount = 10m, Currency = "USD" }
            });
            _db.Parties.Add(new ObMerchant { Kind = "merchant", Licence = "L-1" });
            _db.Aliased.Add(new ObAliased { Name = "Acme" });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict)
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

        private static Filter Select(params string[] fields) => new() { Selects = fields.ToList() };

        private static Filter Order(string field) => new()
        {
            Orders = new List<OrderBy> { new() { Field = field, Direction = Direction.Ascending } }
        };

        /// <summary>Runs a guarded call and reports what came back: rows, a refusal, or a provider failure.</summary>
        private string Run<T>(IQueryable<T> source, Filter filter, DwTier tier = DwTier.Strict)
            where T : class
        {
            try
            {
                FilterResult<T> result = Guard(source, tier).ToList(filter);

                return $"OK({result.Data.Count})";
            }
            catch (PolicyException refusal)
            {
                return $"REFUSED({refusal.ErrorCode})";
            }
            catch (Exception failure)
            {
                return $"{failure.GetType().Name}: {Short(failure.Message)}";
            }
        }

        private static string Short(string message)
        {
            string one = message.Replace(Environment.NewLine, " ");

            return one.Length > 140 ? one[..140] : one;
        }

        /// <summary>Runs a raw LINQ query the way a caller without the guard would.</summary>
        private string Unguarded(Func<object> query)
        {
            try
            {
                object value = query();

                return value is System.Collections.ICollection rows ? $"OK({rows.Count})" : "OK";
            }
            catch (Exception failure)
            {
                return $"{failure.GetType().Name}: {Short(failure.Message)}";
            }
        }

        private void Report(string probe, string unguarded, string guarded) =>
            _out.WriteLine($"{probe,-52} unguarded={unguarded,-40} guarded={guarded}");

        // =========================================================================================
        // 1. Framework members the provider translates, on an entity source.
        // =========================================================================================

        [Fact]
        public void Framework_members_on_an_entity_are_not_refused()
        {
            (string Probe, string Field, DataType Type, Func<object> Raw)[] cases =
            {
                ("string.Length", "Code.Length", DataType.Number,
                    () => _db.Orders.Where(o => o.Code.Length == 5).ToList()),
                ("DateTime.Year", "PlacedAt.Year", DataType.Number,
                    () => _db.Orders.Where(o => o.PlacedAt.Year == 2024).ToList()),
                ("DateTime.Month", "PlacedAt.Month", DataType.Number,
                    () => _db.Orders.Where(o => o.PlacedAt.Month == 3).ToList()),
                ("DateTime.Day", "PlacedAt.Day", DataType.Number,
                    () => _db.Orders.Where(o => o.PlacedAt.Day == 9).ToList()),
                ("DateTime.Date", "PlacedAt.Date", DataType.DateTime,
                    () => _db.Orders.Where(o => o.PlacedAt.Date == new DateTime(2024, 3, 9)).ToList()),
                ("Nullable.HasValue", "ShippedAt.HasValue", DataType.Boolean,
                    () => _db.Orders.Where(o => o.ShippedAt.HasValue).ToList()),
                ("Nullable.Value.Year", "ShippedAt.Value.Year", DataType.Number,
                    () => _db.Orders.Where(o => o.ShippedAt!.Value.Year == 2024).ToList()),
                ("TimeSpan.Hours", "Window.Hours", DataType.Number,
                    () => _db.Orders.Where(o => o.Window.Hours == 3).ToList()),
                ("TimeSpan.TotalHours", "Window.TotalHours", DataType.Number,
                    () => _db.Orders.Where(o => o.Window.TotalHours == 3d).ToList()),
                ("nav then string.Length", "Customer.Name.Length", DataType.Number,
                    () => _db.Orders.Where(o => o.Customer.Name.Length == 4).ToList()),
                ("owned then string.Length", "Total.Currency.Length", DataType.Number,
                    () => _db.Orders.Where(o => o.Total.Currency.Length == 3).ToList()),
                // Ruled out: "Customer.Orders.Count" is refused on 3.2.0 too, by Validate<T>() --
                // the trace says "names nothing on ObOrder", not the DW-17 reason. Left out of the
                // assertion because it is a standing limit, not this branch's doing.
                ("owned then decimal member", "Total.Amount", DataType.Number,
                    () => _db.Orders.Where(o => o.Total.Amount == 10m).ToList())
            };

            List<string> wrong = new();

            foreach ((string probe, string field, DataType type, Func<object> raw) in cases)
            {
                string unguarded = Unguarded(raw);
                string guarded = Run(_db.Orders, Where(field, DefaultFor(type), type));

                Report(probe, unguarded, guarded);

                if (unguarded.StartsWith("OK") && guarded.StartsWith("REFUSED"))
                {
                    wrong.Add($"{probe}: unguarded {unguarded}, guarded {guarded}");
                }
            }

            Assert.True(wrong.Count == 0, string.Join(" || ", wrong));
        }

        private static string DefaultFor(DataType type) => type switch
        {
            DataType.Number => "3",
            DataType.Boolean => "true",
            DataType.DateTime => "2024-03-09",
            _ => "x"
        };

        // =========================================================================================
        // 2. An unmapped getter, in each clause, against what the unguarded query does.
        // =========================================================================================

        [Fact]
        public void An_unmapped_getter_in_a_where_matches_the_unguarded_failure()
        {
            string unguarded = Unguarded(() => _db.Orders.Where(o => o.Slug == "A-001-1").ToList());
            string guarded = Run(_db.Orders, Where("Slug", "A-001-1"));

            Report("WHERE on [NotMapped] getter", unguarded, guarded);

            Assert.StartsWith("InvalidOperationException", unguarded);
            Assert.StartsWith("REFUSED", guarded);
        }

        [Fact]
        public void An_unmapped_getter_in_an_order_matches_the_unguarded_failure()
        {
            string unguarded = Unguarded(() => _db.Orders.OrderBy(o => o.Slug).ToList());
            string guarded = Run(_db.Orders, Order("Slug"));

            Report("ORDER on [NotMapped] getter", unguarded, guarded);

            Assert.StartsWith("InvalidOperationException", unguarded);
            Assert.StartsWith("REFUSED", guarded);
        }

        /// <summary>
        /// The one EF Core evaluates on the client: the top-level projection. A caller who asked for
        /// the getter in <c>Selects</c> got rows on 3.2.0.
        /// </summary>
        [Fact]
        public void An_unmapped_getter_in_a_select_is_the_case_that_worked()
        {
            string unguarded = Unguarded(() => _db.Orders.Select(o => new { o.Id, o.Slug }).ToList());
            string guarded = Run(_db.Orders, Select("Id", "Slug"));
            string convenience = Run(_db.Orders, Select("Id", "Slug"), DwTier.Convenience);

            Report("SELECT on [NotMapped] getter", unguarded, $"strict={guarded} convenience={convenience}");

            // The finding, if any: unguarded runs and strict refuses.
            Assert.False(
                unguarded.StartsWith("OK") && guarded.StartsWith("REFUSED"),
                $"over-block: unguarded {unguarded}, convenience {convenience}, strict {guarded}");
        }

        // =========================================================================================
        // 3. The hierarchy, shadow state, includes, split queries, raw SQL.
        // =========================================================================================

        [Fact]
        public void A_hierarchy_and_a_loaded_source_are_not_refused()
        {
            List<string> wrong = new();

            void Probe(string name, string unguarded, string guarded)
            {
                Report(name, unguarded, guarded);

                if (unguarded.StartsWith("OK") && guarded.StartsWith("REFUSED"))
                {
                    wrong.Add($"{name}: unguarded {unguarded}, guarded {guarded}");
                }
            }

            Probe(
                "OfType<derived> then derived column",
                Unguarded(() => _db.Parties.OfType<ObMerchant>().Where(m => m.Licence == "L-1").ToList()),
                Run(_db.Parties.OfType<ObMerchant>(), Where("Licence", "L-1")));

            Probe(
                "Set<derived> then base column",
                Unguarded(() => _db.Set<ObMerchant>().Where(m => m.Kind == "merchant").ToList()),
                Run(_db.Set<ObMerchant>(), Where("Kind", "merchant")));

            Probe(
                "Include then a path through it",
                Unguarded(() => _db.Orders.Include(o => o.Customer).Where(o => o.Customer.Name == "Acme").ToList()),
                Run(_db.Orders.Include(o => o.Customer), Where("Customer.Name", "Acme")));

            Probe(
                "ThenInclude then a deeper path",
                Unguarded(() => _db.Customers.Include(c => c.Orders).ThenInclude(o => o.Total)
                    .Where(c => c.Name == "Acme").ToList()),
                Run(_db.Customers.Include(c => c.Orders).ThenInclude(o => o.Total), Where("Name", "Acme")));

            Probe(
                "AsSplitQuery",
                Unguarded(() => _db.Customers.Include(c => c.Orders).AsSplitQuery().Where(c => c.Name == "Acme").ToList()),
                Run(_db.Customers.Include(c => c.Orders).AsSplitQuery(), Where("Name", "Acme")));

            Probe(
                "FromSqlRaw",
                Unguarded(() => _db.Orders.FromSqlRaw("SELECT * FROM Orders").Where(o => o.Code == "A-001").ToList()),
                Run(_db.Orders.FromSqlRaw("SELECT * FROM Orders"), Where("Code", "A-001")));

            Probe(
                "AsNoTracking",
                Unguarded(() => _db.Orders.AsNoTracking().Where(o => o.Code == "A-001").ToList()),
                Run(_db.Orders.AsNoTracking(), Where("Code", "A-001")));

            Assert.True(wrong.Count == 0, string.Join(" || ", wrong));
        }

        // =========================================================================================
        // 4. Projections of every shape.
        // =========================================================================================

        [Fact]
        public void Projections_do_not_refuse_what_they_can_still_compute()
        {
            List<string> wrong = new();

            void Probe<TRow>(string name, IQueryable<TRow> rows, Filter filter, Func<object> raw)
                where TRow : class
            {
                string unguarded = Unguarded(raw);
                string guarded = Run(rows, filter);

                Report(name, unguarded, guarded);

                if (unguarded.StartsWith("OK") && guarded.StartsWith("REFUSED"))
                {
                    wrong.Add($"{name}: unguarded {unguarded}, guarded {guarded}");
                }
            }

            // a) member-by-member initializer, a framework member beneath a copied column
            Probe(
                "built row, Code.Length",
                _db.Orders.Select(o => new ObOrderRow { Id = o.Id, Code = o.Code }),
                Where("Code.Length", "5", DataType.Number),
                () => _db.Orders.Select(o => new ObOrderRow { Id = o.Id, Code = o.Code })
                    .Where(r => r.Code.Length == 5).ToList());

            // b) constructor with arguments
            Probe(
                "ctor row, Code",
                _db.Orders.Select(o => new ObOrderRow(o.Id, o.Code)),
                Where("Code", "A-001"),
                () => _db.Orders.Select(o => new ObOrderRow(o.Id, o.Code)).Where(r => r.Code == "A-001").ToList());

            // c) anonymous row
            Probe(
                "anonymous row, Code",
                _db.Orders.Select(o => new { o.Id, o.Code }),
                Where("Code", "A-001"),
                () => _db.Orders.Select(o => new { o.Id, o.Code }).Where(r => r.Code == "A-001").ToList());

            // d) two selects in a chain
            Probe(
                "two selects, Code",
                _db.Orders.Select(o => new ObOrderRow { Id = o.Id, Code = o.Code })
                    .Select(r => new ObOrderRow { Id = r.Id, Code = r.Code }),
                Where("Code", "A-001"),
                () => _db.Orders.Select(o => new ObOrderRow { Id = o.Id, Code = o.Code })
                    .Select(r => new ObOrderRow { Id = r.Id, Code = r.Code })
                    .Where(r => r.Code == "A-001").ToList());

            // e) SelectMany
            Probe(
                "SelectMany, Code",
                _db.Customers.SelectMany(c => c.Orders),
                Where("Code", "A-001"),
                () => _db.Customers.SelectMany(c => c.Orders).Where(o => o.Code == "A-001").ToList());

            // f) Join into a built row
            Probe(
                "Join, Code",
                _db.Orders.Join(_db.Customers, o => o.CustomerId, c => c.Id,
                    (o, c) => new ObOrderRow { Id = o.Id, Code = o.Code, Label = c.Name }),
                Where("Label", "Acme"),
                () => _db.Orders.Join(_db.Customers, o => o.CustomerId, c => c.Id,
                    (o, c) => new ObOrderRow { Id = o.Id, Code = o.Code, Label = c.Name })
                    .Where(r => r.Label == "Acme").ToList());

            // g) GroupBy then a built row
            Probe(
                "GroupBy, Label",
                _db.Orders.GroupBy(o => o.Code).Select(g => new ObOrderRow { Code = g.Key, Id = g.Count() }),
                Where("Code", "A-001"),
                () => _db.Orders.GroupBy(o => o.Code).Select(g => new ObOrderRow { Code = g.Key, Id = g.Count() })
                    .Where(r => r.Code == "A-001").ToList());

            // h) a member assigned from a method call
            Probe(
                "method-call member, Label.Length",
                _db.Orders.Select(o => new ObOrderRow { Id = o.Id, Label = o.Code.ToUpper() }),
                Where("Label.Length", "5", DataType.Number),
                () => _db.Orders.Select(o => new ObOrderRow { Id = o.Id, Label = o.Code.ToUpper() })
                    .Where(r => r.Label.Length == 5).ToList());

            // i) a member assigned from a conditional
            Probe(
                "conditional member, Label",
                _db.Orders.Select(o => new ObOrderRow { Id = o.Id, Label = o.Quantity > 1 ? o.Code : "none" }),
                Where("Label", "A-001"),
                () => _db.Orders.Select(o => new ObOrderRow { Id = o.Id, Label = o.Quantity > 1 ? o.Code : "none" })
                    .Where(r => r.Label == "A-001").ToList());

            // j) a member assigned from a captured variable
            string captured = "captured";

            Probe(
                "captured member, Label",
                _db.Orders.Select(o => new ObOrderRow { Id = o.Id, Label = captured }),
                Where("Label", "captured"),
                () => _db.Orders.Select(o => new ObOrderRow { Id = o.Id, Label = captured })
                    .Where(r => r.Label == "captured").ToList());

            // k) Select handing back an entity
            Probe(
                "Select(x => x.Navigation), Name",
                _db.Orders.Select(o => o.Customer),
                Where("Name", "Acme"),
                () => _db.Orders.Select(o => o.Customer).Where(c => c.Name == "Acme").ToList());

            // l) a copied owned member, then a mapped column beneath it
            Probe(
                "copied owned member, Total.Currency",
                _db.Orders.Select(o => new ObOrderRow { Id = o.Id, Total = o.Total }),
                Where("Total.Currency", "USD"),
                () => _db.Orders.Select(o => new ObOrderRow { Id = o.Id, Total = o.Total })
                    .Where(r => r.Total.Currency == "USD").ToList());

            // m) a nested initializer, a framework member two levels down
            Probe(
                "nested initializer, Nest.Leaf.Length",
                _db.Orders.Select(o => new ObOrderRow
                {
                    Id = o.Id,
                    Nest = new ObNest { Leaf = o.Code }
                }),
                Where("Nest.Leaf.Length", "5", DataType.Number),
                () => _db.Orders.Select(o => new ObOrderRow { Id = o.Id, Nest = new ObNest { Leaf = o.Code } })
                    .Where(r => r.Nest.Leaf.Length == 5).ToList());

            // n) a nested initializer as deep as DwCaps.MaxNavigationDepth (4) allows. The
            // ReadAssignments recursion cap is 8, so the navigation-depth cap refuses a path before
            // the recursion cap could ever be reached: a caller cannot get past it.
            Probe(
                "deep nest (4 levels), leaf",
                Deep(),
                Where("Nest.Inner.Inner.Leaf", "A-001"),
                () => Deep().Where(r => r.Nest.Inner!.Inner!.Leaf == "A-001").ToList());

            Assert.True(wrong.Count == 0, string.Join(" || ", wrong));
        }

        private IQueryable<ObOrderRow> Deep() =>
            _db.Orders.Select(o => new ObOrderRow
            {
                Id = o.Id,
                Nest = new ObNest
                {
                    Inner = new ObNest
                    {
                        Inner = new ObNest { Leaf = o.Code }
                    }
                }
            });

        // =========================================================================================
        // 5. A projection that leaves a member unassigned: refusal must match the unguarded failure.
        // =========================================================================================

        [Fact]
        public void An_unassigned_member_of_a_projection_matches_the_unguarded_failure()
        {
            IQueryable<ObOrderRow> rows = _db.Orders.Select(o => new ObOrderRow { Id = o.Id, Code = o.Code });

            string unguarded = Unguarded(() => _db.Orders
                .Select(o => new ObOrderRow { Id = o.Id, Code = o.Code })
                .Where(r => r.Total.Currency == "USD").ToList());
            string guarded = Run(rows, Where("Total.Currency", "USD"));

            Report("unassigned member of projection", unguarded, guarded);

            Assert.False(
                unguarded.StartsWith("OK") && guarded.StartsWith("REFUSED"),
                $"over-block: unguarded {unguarded}, guarded {guarded}");
        }

        // =========================================================================================
        // 6. A source the library cannot read: a wrapping provider that is not EF Core's.
        // =========================================================================================

        [Fact]
        public void A_wrapping_provider_is_left_alone()
        {
            IQueryable<ObOrder> wrapped = new WrappedQueryable<ObOrder>(_db.Orders);

            string guarded = Run(wrapped, Where("Code", "A-001"));
            string framework = Run(wrapped, Where("Code.Length", "5", DataType.Number));
            string getter = Run(wrapped, Where("Slug", "A-001-1"));

            Report("wrapping provider", "n/a", $"plain={guarded} framework={framework} getter={getter}");

            Assert.StartsWith("OK", guarded);
        }

        // =========================================================================================
        // 7. Aliases and spelling.
        // =========================================================================================

        [Fact]
        public void An_alias_target_that_is_a_column_is_not_refused()
        {
            string guarded = Run(_db.Aliased, Where("customer_name", "Acme"));
            string direct = Run(_db.Aliased, Where("Name", "Acme"));
            string cased = Run(_db.Aliased, Where("nAmE", "Acme"));
            string nested = Run(_db.Orders, Where("cUsToMeR.nAmE", "Acme"));
            string aliasOfGetter = Run(_db.Aliased, Where("computed_tag", "Acme!"));

            Report(
                "aliases and casing",
                Unguarded(() => _db.Aliased.Where(a => a.Name == "Acme").ToList()),
                $"alias={guarded} direct={direct} cased={cased} nestedCased={nested} aliasOfGetter={aliasOfGetter}");

            Assert.StartsWith("OK", guarded);
            Assert.StartsWith("OK", direct);
            Assert.StartsWith("OK", cased);
            Assert.StartsWith("OK", nested);
        }

        // =========================================================================================
        // 8. Every clause, on paths that must stay allowed.
        // =========================================================================================

        [Fact]
        public void Every_clause_still_takes_a_framework_member()
        {
            string order = Run(_db.Orders, Order("Code.Length"));
            string select = Run(_db.Orders, Select("Id", "Code.Length"));

            string group;

            try
            {
                Guard(_db.Orders).ToList(new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = new List<string> { "PlacedAt.Year" },
                        AggregateBy = new List<AggregateBy> { new() { Alias = "Total", Aggregator = Aggregator.Count } }
                    }
                });

                group = "OK";
            }
            catch (PolicyException refusal)
            {
                group = $"REFUSED({refusal.ErrorCode})";
            }
            catch (Exception failure)
            {
                group = $"{failure.GetType().Name}: {Short(failure.Message)}";
            }

            string aggregate;

            try
            {
                Guard(_db.Orders).ToList(new Summary
                {
                    GroupBy = new GroupBy
                    {
                        Fields = new List<string> { "Code" },
                        AggregateBy = new List<AggregateBy>
                        {
                            new() { Alias = "Longest", Field = "Code.Length", Aggregator = Aggregator.Maximum }
                        }
                    }
                });

                aggregate = "OK";
            }
            catch (PolicyException refusal)
            {
                aggregate = $"REFUSED({refusal.ErrorCode})";
            }
            catch (Exception failure)
            {
                aggregate = $"{failure.GetType().Name}: {Short(failure.Message)}";
            }

            string segment;

            try
            {
                Guard(_db.Orders).ToListAsync(new Segment
                {
                    ConditionSets = new List<ConditionSet>
                    {
                        new()
                        {
                            Sort = 0,
                            ConditionGroup = new ConditionGroup
                            {
                                Conditions =
                                {
                                    new Condition
                                    {
                                        Field = "Code.Length",
                                        DataType = DataType.Number,
                                        Operator = Operator.Equal,
                                        Values = { "5" }
                                    }
                                }
                            }
                        }
                    }
                }).GetAwaiter().GetResult();

                segment = "OK";
            }
            catch (PolicyException refusal)
            {
                segment = $"REFUSED({refusal.ErrorCode})";
            }
            catch (Exception failure)
            {
                segment = $"{failure.GetType().Name}: {Short(failure.Message)}";
            }

            Report("clauses on Code.Length / PlacedAt.Year", "n/a",
                $"order={order} select={select} group={group} aggregate={aggregate} segment={segment}");

            Assert.StartsWith("OK", order);
            Assert.DoesNotContain("REFUSED", group);
            Assert.DoesNotContain("REFUSED", aggregate);
            Assert.DoesNotContain("REFUSED", segment);
        }
    }

    /// <summary>A provider that is neither EF Core's nor an <c>EnumerableQuery</c>.</summary>
    internal sealed class WrappedQueryable<T> : IQueryable<T>, IOrderedQueryable<T>
    {
        private readonly IQueryable<T> _inner;

        internal WrappedQueryable(IQueryable<T> inner)
        {
            _inner = inner;
            Provider = new WrappedProvider(inner.Provider);
        }

        public Type ElementType => _inner.ElementType;

        public System.Linq.Expressions.Expression Expression => _inner.Expression;

        public IQueryProvider Provider { get; }

        public IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal sealed class WrappedProvider : IQueryProvider
    {
        private readonly IQueryProvider _inner;

        internal WrappedProvider(IQueryProvider inner) => _inner = inner;

        public IQueryable CreateQuery(System.Linq.Expressions.Expression expression) => _inner.CreateQuery(expression);

        public IQueryable<TElement> CreateQuery<TElement>(System.Linq.Expressions.Expression expression) =>
            _inner.CreateQuery<TElement>(expression);

        public object? Execute(System.Linq.Expressions.Expression expression) => _inner.Execute(expression);

        public TResult Execute<TResult>(System.Linq.Expressions.Expression expression) =>
            _inner.Execute<TResult>(expression);
    }
}
