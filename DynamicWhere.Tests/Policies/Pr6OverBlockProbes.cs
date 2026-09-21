using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ---- entity shapes ----------------------------------------------------------------------------------

    /// <summary>Owned, with a getter over two columns that no database can compute.</summary>
    public class P6Text
    {
        public string Ar { get; set; } = string.Empty;

        public string En { get; set; } = string.Empty;

        public bool IsEmpty => string.IsNullOrWhiteSpace(Ar) && string.IsNullOrWhiteSpace(En);
    }

    public class P6Customer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Unmapped getter.</summary>
        public string Handle => Name.ToLowerInvariant();

        /// <summary>Table splitting: this lives in the customer's own table.</summary>
        public P6Detail Detail { get; set; } = new();

        public List<P6Order> Orders { get; set; } = new();
    }

    public class P6Detail
    {
        public int Id { get; set; }

        public string Notes { get; set; } = string.Empty;
    }

    public class P6Line
    {
        public int Id { get; set; }

        public int OrderId { get; set; }

        public P6Order Order { get; set; } = null!;

        public string Sku { get; set; } = string.Empty;

        public int Qty { get; set; }

        /// <summary>Unmapped getter whose name a projected element row also declares.</summary>
        public string Label => $"{Sku}#{Qty}";
    }

    public class P6Order
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        public int? Bonus { get; set; }

        /// <summary>A computed column.</summary>
        public decimal Doubled { get; set; }

        /// <summary>Owned.</summary>
        public P6Text Title { get; set; } = new();

        /// <summary>A column a value converter builds.</summary>
        public P6Text Badge { get; set; } = new();

        public int CustomerId { get; set; }

        public P6Customer Customer { get; set; } = null!;

        public List<P6Line> Lines { get; set; } = new();

        /// <summary>Unmapped getter.</summary>
        public string Display => $"{Code}/{Id}";
    }

    /// <summary>TPH.</summary>
    public class P6Party
    {
        public int Id { get; set; }

        public string Kind { get; set; } = string.Empty;
    }

    public class P6Vendor : P6Party
    {
        public string? Vat { get; set; }
    }

    /// <summary>TPT.</summary>
    public class P6Animal
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    public class P6Dog : P6Animal
    {
        public string? Breed { get; set; }
    }

    /// <summary>Keyless.</summary>
    public class P6Stat
    {
        public string Bucket { get; set; } = string.Empty;

        public int Total { get; set; }
    }

    // ---- the rows a caller projects ---------------------------------------------------------------------

    public class P6Nest
    {
        public string A { get; set; } = string.Empty;

        public string B { get; set; } = string.Empty;
    }

    public class P6LineRow
    {
        /// <summary>The entity declares this name and maps nothing for it.</summary>
        public string Label { get; set; } = string.Empty;

        public int Qty { get; set; }

        /// <summary>A name the entity does not declare at all.</summary>
        public string Note { get; set; } = string.Empty;
    }

    public class P6Row
    {
        public P6Row()
        {
        }

        public P6Row(int id, string code)
        {
            Id = id;
            Code = code;
        }

        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Tag { get; set; } = string.Empty;

        public string Flag { get; set; } = string.Empty;

        public int Count { get; set; }

        public decimal Total { get; set; }

        public long Wide { get; set; }

        public P6Text Title { get; set; } = new();

        public P6Nest Nest { get; set; } = new();

        /// <summary>A projected collection of projected rows.</summary>
        public List<P6LineRow> Lines { get; set; } = new();

        /// <summary>The entity's own rows, copied whole.</summary>
        public List<P6Line> Raw { get; set; } = new();

        public P6Customer? Customer { get; set; }

        public P6Line? One { get; set; }
    }

    public sealed class P6Context : DbContext
    {
        private readonly SqliteConnection _connection;

        public P6Context(SqliteConnection connection) => _connection = connection;

        public DbSet<P6Order> Orders => Set<P6Order>();

        public DbSet<P6Customer> Customers => Set<P6Customer>();

        public DbSet<P6Line> Lines => Set<P6Line>();

        public DbSet<P6Party> Parties => Set<P6Party>();

        public DbSet<P6Animal> Animals => Set<P6Animal>();

        public DbSet<P6Stat> Stats => Set<P6Stat>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<P6Order>().OwnsOne(order => order.Title);
            model.Entity<P6Order>().Ignore(order => order.Display);
            model.Entity<P6Order>().Property(order => order.Doubled).HasComputedColumnSql("\"Amount\" * 2");
            model.Entity<P6Order>().Property<string>("Tenant");
            model.Entity<P6Order>().Property(order => order.Badge).HasConversion(
                new ValueConverter<P6Text, string>(
                    text => text.En,
                    stored => new P6Text { En = stored, Ar = stored }));

            model.Entity<P6Customer>().Ignore(customer => customer.Handle);

            // Table splitting: the detail shares the customer's table.
            model.Entity<P6Customer>().ToTable("P6Customers");
            model.Entity<P6Customer>().HasOne(customer => customer.Detail).WithOne()
                .HasForeignKey<P6Detail>(detail => detail.Id);
            model.Entity<P6Detail>().ToTable("P6Customers");

            model.Entity<P6Line>().Ignore(line => line.Label);

            model.Entity<P6Party>().HasDiscriminator<string>("Discriminator")
                .HasValue<P6Party>("party")
                .HasValue<P6Vendor>("vendor");

            // TPT: each type its own table.
            model.Entity<P6Animal>().ToTable("P6Animals");
            model.Entity<P6Dog>().ToTable("P6Dogs");

            model.Entity<P6Stat>().HasNoKey().ToTable("P6Stats");
        }
    }

    /// <summary>
    /// Round 6 of the over-blocking hunt: every entity shape and every projection shape, run guarded
    /// and unguarded, so a query that ran in 3.2.0 and still runs unguarded is visible when the guard
    /// refuses it.
    /// </summary>
    public sealed class Pr6OverBlockProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly P6Context _db;
        private readonly List<string> _findings = new();

        public Pr6OverBlockProbes(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new P6Context(_connection);
            _db.Database.EnsureCreated();

            P6Customer customer = new()
            {
                Name = "Acme",
                Detail = new P6Detail { Notes = "vip" }
            };

            P6Order order = new()
            {
                Customer = customer,
                Code = "AB123",
                Amount = 10m,
                Bonus = 3,
                Title = new P6Text { Ar = "AR", En = "EN" },
                Badge = new P6Text { En = "gold" }
            };

            order.Lines.Add(new P6Line { Sku = "S-1", Qty = 2 });
            order.Lines.Add(new P6Line { Sku = "S-2", Qty = 4 });

            _db.Customers.Add(customer);
            _db.Orders.Add(order);
            _db.Entry(order).Property("Tenant").CurrentValue = "t1";
            _db.Parties.Add(new P6Vendor { Kind = "vendor", Vat = "V-1" });
            _db.Animals.Add(new P6Dog { Name = "Rex", Breed = "collie" });
            _db.SaveChanges();

            // Keyless rows cannot be tracked, so the one row goes in directly.
            _db.Database.ExecuteSqlRaw("INSERT INTO P6Stats (Bucket, Total) VALUES ('b1', 7)");
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        // ---- harness -----------------------------------------------------------------------------

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = DwTier.Strict, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static Filter Where(string field, DataType type, string value) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition { Field = field, DataType = type, Operator = Operator.Equal, Values = { value } }
                }
            }
        };

        private static string Guarded<T>(IQueryable<T> source, string field, DataType type, string value)
            where T : class
        {
            try
            {
                FilterResult<T> result = Guard(source).ToList(Where(field, type, value));

                return $"OK({result.Data.Count})";
            }
            catch (PolicyException refusal)
            {
                return $"REFUSED({refusal.ErrorCode})";
            }
            catch (Exception failure)
            {
                return failure.GetType().Name;
            }
        }

        /// <summary>A projection of the named field, which the tier exempts from the computed check.</summary>
        private static string GuardedSelect<T>(IQueryable<T> source, string field) where T : class
        {
            try
            {
                FilterResult<dynamic> result = Guard(source)
                    .ToListDynamic(new Filter { Selects = new List<string> { field } });

                return $"OK({result.Data.Count})";
            }
            catch (PolicyException refusal)
            {
                return $"REFUSED({refusal.ErrorCode})";
            }
            catch (Exception failure)
            {
                return failure.GetType().Name;
            }
        }

        /// <summary>The same request through the library with no policy attached at all.</summary>
        private static string Unguarded<T>(IQueryable<T> source, string field, DataType type, string value)
            where T : class
        {
            try
            {
                FilterResult<T> result = source.ToList(Where(field, type, value));

                return $"OK({result.Data.Count})";
            }
            catch (Exception failure)
            {
                return failure.GetType().Name;
            }
        }

        private static string Raw(Func<object> query)
        {
            try
            {
                object value = query();

                return value is System.Collections.ICollection rows ? $"OK({rows.Count})" : "OK";
            }
            catch (Exception failure)
            {
                return failure.GetType().Name;
            }
        }

        private void Case(string probe, string unguarded, string guarded)
        {
            _out.WriteLine($"{probe,-64} unguarded={unguarded,-24} guarded={guarded}");

            if (unguarded.StartsWith("OK", StringComparison.Ordinal)
                && guarded.StartsWith("REFUSED", StringComparison.Ordinal))
            {
                _findings.Add($"{probe}: unguarded {unguarded}, guarded {guarded}");
            }
        }

        private void Done() => Assert.True(_findings.Count == 0, string.Join(" || ", _findings));

        // =========================================================================================
        // Pr6-D. Entity shapes.
        // =========================================================================================

        [Fact]
        public void Pr6_D_Every_mapped_entity_shape_is_left_alone()
        {
            Case("D01 plain column",
                Raw(() => _db.Orders.Where(o => o.Code == "AB123").ToList()),
                Guarded(_db.Orders, "Code", DataType.Text, "AB123"));

            Case("D02 owned type's column",
                Raw(() => _db.Orders.Where(o => o.Title.En == "EN").ToList()),
                Guarded(_db.Orders, "Title.En", DataType.Text, "EN"));

            Case("D03 getter on an owned type (refusal is right)",
                Raw(() => _db.Orders.Where(o => o.Title.IsEmpty).ToList()),
                Guarded(_db.Orders, "Title.IsEmpty", DataType.Boolean, "false"));

            Case("D04 computed column",
                Raw(() => _db.Orders.Where(o => o.Doubled == 20m).ToList()),
                Guarded(_db.Orders, "Doubled", DataType.Number, "20"));

            Case("D05 a column a converter builds",
                Raw(() => _db.Orders.Where(o => o.Badge.En == "gold").ToList()),
                Guarded(_db.Orders, "Badge.En", DataType.Text, "gold"));

            Case("D06 nullable column",
                Raw(() => _db.Orders.Where(o => o.Bonus == 3).ToList()),
                Guarded(_db.Orders, "Bonus", DataType.Number, "3"));

            Case("D07 navigation to the principal",
                Raw(() => _db.Orders.Where(o => o.Customer.Name == "Acme").ToList()),
                Guarded(_db.Orders, "Customer.Name", DataType.Text, "Acme"));

            Case("D08 a framework member of a column",
                Raw(() => _db.Orders.Where(o => o.Code.Length == 5).ToList()),
                Guarded(_db.Orders, "Code.Length", DataType.Number, "5"));

            Case("D09 table splitting: the dependent's column",
                Raw(() => _db.Customers.Where(c => c.Detail.Notes == "vip").ToList()),
                Guarded(_db.Customers, "Detail.Notes", DataType.Text, "vip"));

            Case("D10 TPH: the base type's column",
                Raw(() => _db.Parties.Where(p => p.Kind == "vendor").ToList()),
                Guarded(_db.Parties, "Kind", DataType.Text, "vendor"));

            Case("D11 TPH: a subtype's own column, queried on the subtype",
                Raw(() => _db.Parties.OfType<P6Vendor>().Where(v => v.Vat == "V-1").ToList()),
                Guarded(_db.Parties.OfType<P6Vendor>(), "Vat", DataType.Text, "V-1"));

            Case("D12 TPT: the base type's column",
                Raw(() => _db.Animals.Where(a => a.Name == "Rex").ToList()),
                Guarded(_db.Animals, "Name", DataType.Text, "Rex"));

            Case("D13 TPT: a subtype's own column, queried on the subtype",
                Raw(() => _db.Animals.OfType<P6Dog>().Where(d => d.Breed == "collie").ToList()),
                Guarded(_db.Animals.OfType<P6Dog>(), "Breed", DataType.Text, "collie"));

            Case("D14 keyless entity",
                Raw(() => _db.Stats.Where(s => s.Bucket == "b1").ToList()),
                Guarded(_db.Stats, "Bucket", DataType.Text, "b1"));

            // Printed, not flagged: EF.Property is not something a Filter can express, so the
            // comparison that means anything is D19 below.
            _out.WriteLine("D15 shadow property, hand-written LINQ  unguarded="
                + Raw(() => _db.Orders.Where(o => EF.Property<string>(o, "Tenant") == "t1").ToList())
                + " guarded=" + Guarded(_db.Orders, "Tenant", DataType.Text, "t1"));

            Case("D16 an unmapped getter on the entity (refusal is right)",
                Raw(() => _db.Orders.Where(o => o.Display == "AB123/1").ToList()),
                Guarded(_db.Orders, "Display", DataType.Text, "AB123/1"));

            Case("D17 a collection's element column",
                Raw(() => _db.Orders.Where(o => o.Lines.Any(l => l.Sku == "S-1")).ToList()),
                Guarded(_db.Lines, "Sku", DataType.Text, "S-1"));

            Case("D18 a navigation back to the principal from the dependent",
                Raw(() => _db.Lines.Where(l => l.Order.Code == "AB123").ToList()),
                Guarded(_db.Lines, "Order.Code", DataType.Text, "AB123"));

            // The control that matters for a shadow property: no Filter a caller writes can name it,
            // guarded or not, because it is not a member of the type.
            Case("D19 the shadow property through the library, unguarded",
                Unguarded(_db.Orders, "Tenant", DataType.Text, "t1"),
                Guarded(_db.Orders, "Tenant", DataType.Text, "t1"));

            Case("D20 a mapped column through the library, unguarded",
                Unguarded(_db.Orders, "Code", DataType.Text, "AB123"),
                Guarded(_db.Orders, "Code", DataType.Text, "AB123"));

            Done();
        }

        // =========================================================================================
        // Pr6-E. Projection shapes: a member of a projected row is read out of the row, and the
        //        projection clause itself is exempt from what the provider can compute.
        // =========================================================================================

        [Fact]
        public void Pr6_E_A_row_built_by_a_subquery_of_every_operator()
        {
            IQueryable<P6Row> rows = _db.Orders.Select(order => new P6Row
            {
                Id = order.Id,
                Code = order.Code,
                Count = order.Lines.Count(),
                Total = order.Lines.Sum(line => line.Qty),
                Wide = order.Lines.Max(line => line.Qty),
                Tag = order.Lines.OrderBy(line => line.Id).Select(line => line.Sku).FirstOrDefault() ?? "none",
                Flag = order.Lines.Any(line => line.Qty > 3) ? "big" : "small",
                Lines = order.Lines
                    .Where(line => line.Qty > 0)
                    .OrderBy(line => line.Id)
                    .Select(line => new P6LineRow { Label = line.Sku, Qty = line.Qty, Note = "n" })
                    .ToList(),
                Raw = order.Lines.ToList(),
                One = order.Lines.OrderBy(line => line.Id).FirstOrDefault(),
                Customer = order.Customer
            });

            Case("E01 Count() subquery",
                Raw(() => Shape().Where(r => r.Count == 2).ToList()),
                Guarded(rows, "Count", DataType.Number, "2"));

            Case("E02 Sum() subquery",
                Raw(() => Shape().Where(r => r.Total == 6m).ToList()),
                Guarded(rows, "Total", DataType.Number, "6"));

            Case("E03 Max() subquery",
                Raw(() => Shape().Where(r => r.Wide == 4L).ToList()),
                Guarded(rows, "Wide", DataType.Number, "4"));

            Case("E04 OrderBy/Select/FirstOrDefault with ??",
                Raw(() => Shape().Where(r => r.Tag == "S-1").ToList()),
                Guarded(rows, "Tag", DataType.Text, "S-1"));

            Case("E05 Any() inside a conditional",
                Raw(() => Shape().Where(r => r.Flag == "big").ToList()),
                Guarded(rows, "Flag", DataType.Text, "big"));

            Case("E06 a member of a projected collection of projected rows",
                Raw(() => Shape().Where(r => r.Lines.Any(l => l.Qty == 2)).ToList()),
                Guarded(rows, "Lines.Qty", DataType.Number, "2"));

            Case("E07 a member the element row assigns and the entity only computes",
                Raw(() => Shape().Where(r => r.Lines.Any(l => l.Label == "S-1")).ToList()),
                Guarded(rows, "Lines.Label", DataType.Text, "S-1"));

            Case("E08 a member only the element row declares",
                Raw(() => Shape().Where(r => r.Lines.Any(l => l.Note == "n")).ToList()),
                Guarded(rows, "Lines.Note", DataType.Text, "n"));

            Case("E09 the entity's own rows copied whole",
                Raw(() => Shape().Where(r => r.Raw.Any(l => l.Sku == "S-1")).ToList()),
                Guarded(rows, "Raw.Sku", DataType.Text, "S-1"));

            Case("E10 a single entity read out of a collection",
                Raw(() => Shape().Where(r => r.One!.Sku == "S-1").ToList()),
                Guarded(rows, "One.Sku", DataType.Text, "S-1"));

            Case("E11 a navigation copied whole",
                Raw(() => Shape().Where(r => r.Customer!.Name == "Acme").ToList()),
                Guarded(rows, "Customer.Name", DataType.Text, "Acme"));

            Case("E12 a plain copied column",
                Raw(() => Shape().Where(r => r.Code == "AB123").ToList()),
                Guarded(rows, "Code", DataType.Text, "AB123"));

            Done();

            IQueryable<P6Row> Shape() => _db.Orders.Select(order => new P6Row
            {
                Id = order.Id,
                Code = order.Code,
                Count = order.Lines.Count(),
                Total = order.Lines.Sum(line => line.Qty),
                Wide = order.Lines.Max(line => line.Qty),
                Tag = order.Lines.OrderBy(line => line.Id).Select(line => line.Sku).FirstOrDefault() ?? "none",
                Flag = order.Lines.Any(line => line.Qty > 3) ? "big" : "small",
                Lines = order.Lines
                    .Where(line => line.Qty > 0)
                    .OrderBy(line => line.Id)
                    .Select(line => new P6LineRow { Label = line.Sku, Qty = line.Qty, Note = "n" })
                    .ToList(),
                Raw = order.Lines.ToList(),
                One = order.Lines.OrderBy(line => line.Id).FirstOrDefault(),
                Customer = order.Customer
            });
        }

        [Fact]
        public void Pr6_E_Casts_conditionals_method_calls_and_nested_initializers()
        {
            IQueryable<P6Row> rows = _db.Orders.Select(order => new P6Row
            {
                Id = order.Id,
                Code = order.Code.ToUpper(),
                Tag = order.Code.Substring(0, 2),
                Flag = order.Bonus == null ? "none" : "some",
                Total = (decimal)order.Amount,
                Wide = (long)order.Id,
                Title = new P6Text { Ar = order.Title.Ar, En = order.Title.En },
                Nest = new P6Nest { A = order.Code, B = order.Customer.Name }
            });

            Case("E20 a method call on a column",
                Raw(() => Shape().Where(r => r.Code == "AB123").ToList()),
                Guarded(rows, "Code", DataType.Text, "AB123"));

            Case("E21 Substring",
                Raw(() => Shape().Where(r => r.Tag == "AB").ToList()),
                Guarded(rows, "Tag", DataType.Text, "AB"));

            Case("E22 a conditional over a null check",
                Raw(() => Shape().Where(r => r.Flag == "some").ToList()),
                Guarded(rows, "Flag", DataType.Text, "some"));

            Case("E23 a cast",
                Raw(() => Shape().Where(r => r.Total == 10m).ToList()),
                Guarded(rows, "Total", DataType.Number, "10"));

            Case("E24 a widening cast",
                Raw(() => Shape().Where(r => r.Wide == 1L).ToList()),
                Guarded(rows, "Wide", DataType.Number, "1"));

            Case("E25 a nested initializer rebuilt member by member",
                Raw(() => Shape().Where(r => r.Title.En == "EN").ToList()),
                Guarded(rows, "Title.En", DataType.Text, "EN"));

            Case("E26 a nested initializer of a type the entity has no member for",
                Raw(() => Shape().Where(r => r.Nest.A == "AB123").ToList()),
                Guarded(rows, "Nest.A", DataType.Text, "AB123"));

            Case("E27 a nested member read from a navigation",
                Raw(() => Shape().Where(r => r.Nest.B == "Acme").ToList()),
                Guarded(rows, "Nest.B", DataType.Text, "Acme"));

            Done();

            IQueryable<P6Row> Shape() => _db.Orders.Select(order => new P6Row
            {
                Id = order.Id,
                Code = order.Code.ToUpper(),
                Tag = order.Code.Substring(0, 2),
                Flag = order.Bonus == null ? "none" : "some",
                Total = (decimal)order.Amount,
                Wide = (long)order.Id,
                Title = new P6Text { Ar = order.Title.Ar, En = order.Title.En },
                Nest = new P6Nest { A = order.Code, B = order.Customer.Name }
            });
        }

        [Fact]
        public void Pr6_E_A_constructor_with_arguments_and_a_second_projection_over_the_first()
        {
            IQueryable<P6Row> built = _db.Orders.Select(order => new P6Row(order.Id, order.Code)
            {
                Tag = order.Title.En
            });

            Case("E30 a member the constructor sets",
                Raw(() => _db.Orders.Select(o => new P6Row(o.Id, o.Code) { Tag = o.Title.En })
                    .Where(r => r.Code == "AB123").ToList()),
                Guarded(built, "Code", DataType.Text, "AB123"));

            Case("E31 a binding beside the constructor",
                Raw(() => _db.Orders.Select(o => new P6Row(o.Id, o.Code) { Tag = o.Title.En })
                    .Where(r => r.Tag == "EN").ToList()),
                Guarded(built, "Tag", DataType.Text, "EN"));

            // Members of one projected row read from another projected row's members.
            IQueryable<P6Row> twice = _db.Orders
                .Select(order => new P6Row { Id = order.Id, Code = order.Code, Tag = order.Title.En })
                .Select(row => new P6Row { Id = row.Id, Code = row.Tag, Tag = row.Code });

            Case("E32 a second projection over the first",
                Raw(() => _db.Orders
                    .Select(o => new P6Row { Id = o.Id, Code = o.Code, Tag = o.Title.En })
                    .Select(r => new P6Row { Id = r.Id, Code = r.Tag, Tag = r.Code })
                    .Where(r => r.Code == "EN").ToList()),
                Guarded(twice, "Code", DataType.Text, "EN"));

            Case("E33 the swapped member of the second projection",
                Raw(() => _db.Orders
                    .Select(o => new P6Row { Id = o.Id, Code = o.Code, Tag = o.Title.En })
                    .Select(r => new P6Row { Id = r.Id, Code = r.Tag, Tag = r.Code })
                    .Where(r => r.Tag == "AB123").ToList()),
                Guarded(twice, "Tag", DataType.Text, "AB123"));

            // An anonymous row: nothing can say which member each value sets.
            var anonymous = _db.Orders.Select(order => new { order.Id, order.Code, Name = order.Customer.Name });

            Case("E34 an anonymous row",
                Raw(() => _db.Orders.Select(o => new { o.Id, o.Code, Name = o.Customer.Name })
                    .Where(r => r.Code == "AB123").ToList()),
                Guarded(anonymous, "Code", DataType.Text, "AB123"));

            Case("E35 an anonymous row's renamed member",
                Raw(() => _db.Orders.Select(o => new { o.Id, o.Code, Name = o.Customer.Name })
                    .Where(r => r.Name == "Acme").ToList()),
                Guarded(anonymous, "Name", DataType.Text, "Acme"));

            Done();
        }

        [Fact]
        public void Pr6_E_A_projection_clause_still_returns_what_no_database_can_compute()
        {
            Case("E40 select a getter over two owned columns",
                Raw(() => _db.Orders.Select(o => new { o.Title.IsEmpty }).ToList()),
                GuardedSelect(_db.Orders, "Title.IsEmpty"));

            Case("E41 select an unmapped getter on the entity",
                Raw(() => _db.Orders.Select(o => new { o.Display }).ToList()),
                GuardedSelect(_db.Orders, "Display"));

            Case("E42 select a plain column",
                Raw(() => _db.Orders.Select(o => new { o.Code }).ToList()),
                GuardedSelect(_db.Orders, "Code"));

            Case("E43 select a computed column",
                Raw(() => _db.Orders.Select(o => new { o.Doubled }).ToList()),
                GuardedSelect(_db.Orders, "Doubled"));

            Case("E44 select through a navigation",
                Raw(() => _db.Orders.Select(o => new { o.Customer.Name }).ToList()),
                GuardedSelect(_db.Orders, "Customer.Name"));

            Done();
        }

        [Fact]
        public void Pr6_E_A_source_composed_before_the_guard()
        {
            Case("E50 a filtered source",
                Raw(() => _db.Orders.Where(o => o.Amount > 0m).Where(o => o.Code == "AB123").ToList()),
                Guarded(_db.Orders.Where(o => o.Amount > 0m), "Code", DataType.Text, "AB123"));

            Case("E51 an ordered source",
                Raw(() => _db.Orders.OrderBy(o => o.Id).Where(o => o.Code == "AB123").ToList()),
                Guarded(_db.Orders.OrderBy(o => o.Id), "Code", DataType.Text, "AB123"));

            Case("E52 a source that took a page",
                Raw(() => _db.Orders.OrderBy(o => o.Id).Take(10).Where(o => o.Code == "AB123").ToList()),
                Guarded(_db.Orders.OrderBy(o => o.Id).Take(10), "Code", DataType.Text, "AB123"));

            Case("E53 a distinct source",
                Raw(() => _db.Orders.Distinct().Where(o => o.Code == "AB123").ToList()),
                Guarded(_db.Orders.Distinct(), "Code", DataType.Text, "AB123"));

            Case("E54 a set operation",
                Raw(() => _db.Orders.Where(o => o.Id > 0).Union(_db.Orders.Where(o => o.Id < 0))
                    .Where(o => o.Code == "AB123").ToList()),
                Guarded(
                    _db.Orders.Where(o => o.Id > 0).Union(_db.Orders.Where(o => o.Id < 0)),
                    "Code", DataType.Text, "AB123"));

            Case("E55 SelectMany over a collection",
                Raw(() => _db.Orders.SelectMany(o => o.Lines).Where(l => l.Sku == "S-1").ToList()),
                Guarded(_db.Orders.SelectMany(o => o.Lines), "Sku", DataType.Text, "S-1"));

            Case("E56 an included navigation",
                Raw(() => _db.Orders.Include(o => o.Lines).Where(o => o.Code == "AB123").ToList()),
                Guarded(_db.Orders.Include(o => o.Lines), "Code", DataType.Text, "AB123"));

            Done();
        }
    }
}
