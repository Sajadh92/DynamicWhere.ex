using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Metadata;
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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ---- model -------------------------------------------------------------------------------------------

    /// <summary>A shared kernel type: two columns and a getter over them.</summary>
    public class OvbText
    {
        public string Ar { get; set; } = string.Empty;

        public string En { get; set; } = string.Empty;

        public bool IsEmpty => string.IsNullOrWhiteSpace(Ar) && string.IsNullOrWhiteSpace(En);
    }

    public class OvbCustomer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>An unmapped getter on the customer.</summary>
        public string Handle => Name.ToLowerInvariant();

        public List<OvbOrder> Orders { get; set; } = new();
    }

    public class OvbLine
    {
        public int Id { get; set; }

        public int OrderId { get; set; }

        public OvbOrder Order { get; set; } = null!;

        public string Sku { get; set; } = string.Empty;

        public int Qty { get; set; }

        /// <summary>
        /// An unmapped getter on the LINE ENTITY whose NAME a projected element row also declares.
        /// The projected row assigns it from a column, so the query computes it; the entity cannot.
        /// </summary>
        public string Label => $"{Sku}#{Qty}";

        /// <summary>An unmapped getter whose name a projected row also uses for a customer.</summary>
        public string Name => Sku;
    }

    public class OvbOrder
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public decimal Amount { get; set; }

        /// <summary>Mapped as a computed column.</summary>
        public decimal Doubled { get; set; }

        /// <summary>Owned; its getter is the member no database can compute.</summary>
        public OvbText Title { get; set; } = new();

        /// <summary>A column whose value a converter builds, so what is beneath it is the converter's.</summary>
        public OvbText Badge { get; set; } = new();

        public int CustomerId { get; set; }

        public OvbCustomer Customer { get; set; } = null!;

        public List<OvbLine> Lines { get; set; } = new();

        /// <summary>Unmapped getter on the order.</summary>
        public string Display => $"{Code}/{Id}";
    }

    /// <summary>TPH.</summary>
    public class OvbParty
    {
        public int Id { get; set; }

        public string Kind { get; set; } = string.Empty;
    }

    public class OvbVendor : OvbParty
    {
        public string? Vat { get; set; }
    }

    // ---- the rows a caller projects ----------------------------------------------------------------------

    /// <summary>The element of a projected collection. <c>Label</c> collides with the entity's getter.</summary>
    public class OvbLineRow
    {
        /// <summary>The entity declares this name and maps nothing for it.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>The entity maps a column of this name.</summary>
        public int Qty { get; set; }

        /// <summary>The entity does not declare this name at all.</summary>
        public string Note { get; set; } = string.Empty;
    }

    public class OvbNest
    {
        public string A { get; set; } = string.Empty;

        public string B { get; set; } = string.Empty;
    }

    public class OvbRow
    {
        public OvbRow()
        {
        }

        public OvbRow(int id) => Id = id;

        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string Tag { get; set; } = string.Empty;

        public OvbText Title { get; set; } = new();

        public OvbNest Nest { get; set; } = new();

        /// <summary>A projected collection of projected rows.</summary>
        public List<OvbLineRow> Lines { get; set; } = new();

        /// <summary>The entity's own rows, copied whole.</summary>
        public List<OvbLine> Raw { get; set; } = new();

        /// <summary>A customer read out of a collection, so the recorded source is the collection.</summary>
        public OvbCustomer? Customer { get; set; }
    }

    public sealed class OvbContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public OvbContext(SqliteConnection connection) => _connection = connection;

        public DbSet<OvbOrder> Orders => Set<OvbOrder>();

        public DbSet<OvbCustomer> Customers => Set<OvbCustomer>();

        public DbSet<OvbLine> Lines => Set<OvbLine>();

        public DbSet<OvbParty> Parties => Set<OvbParty>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseSqlite(_connection).AddInterceptors(new OvbInterceptor());

        protected override void OnModelCreating(ModelBuilder model)
        {
            // Owned rather than complex, so the EF Core 6.0.22 floor builds this model too.
            model.Entity<OvbOrder>().OwnsOne(order => order.Title);
            model.Entity<OvbOrder>().Ignore(order => order.Display);
            model.Entity<OvbOrder>().Property(order => order.Doubled).HasComputedColumnSql("\"Amount\" * 2");
            model.Entity<OvbOrder>().Property<string>("Tenant");

            // A column a converter builds: what is beneath it is the application's code to decide.
            model.Entity<OvbOrder>().Property(order => order.Badge).HasConversion(
                new ValueConverter<OvbText, string>(
                    text => text.En,
                    stored => new OvbText { En = stored, Ar = stored }));

            model.Entity<OvbCustomer>().Ignore(customer => customer.Handle);

            model.Entity<OvbLine>().Ignore(line => line.Label);
            model.Entity<OvbLine>().Ignore(line => line.Name);

            model.Entity<OvbParty>().HasDiscriminator<string>("Discriminator")
                .HasValue<OvbParty>("party")
                .HasValue<OvbVendor>("vendor");
        }
    }

    /// <summary>An interceptor, to confirm one does not change which provider the queryable carries.</summary>
    public sealed class OvbInterceptor : DbCommandInterceptor
    {
    }

    /// <summary>
    /// Round 4 of the over-blocking review of 3.3.0's strict refusal: each shape run guarded and
    /// unguarded, so a query that ran in 3.2.0 and still runs unguarded is visible when it is refused.
    /// </summary>
    public sealed class Ob4OverBlockProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly OvbContext _db;
        private readonly List<string> _findings = new();

        public Ob4OverBlockProbes(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new OvbContext(_connection);
            _db.Database.EnsureCreated();

            OvbCustomer customer = new() { Name = "Acme" };

            OvbOrder order = new()
            {
                Customer = customer,
                Code = "AB123",
                Amount = 10m,
                Title = new OvbText { Ar = "AR", En = "EN" },
                Badge = new OvbText { En = "gold" }
            };

            order.Lines.Add(new OvbLine { Sku = "S-1", Qty = 2 });
            order.Lines.Add(new OvbLine { Sku = "S-2", Qty = 4 });

            _db.Customers.Add(customer);
            _db.Orders.Add(order);
            _db.Entry(order).Property("Tenant").CurrentValue = "t1";
            _db.Parties.Add(new OvbVendor { Kind = "vendor", Vat = "V-1" });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        // ---- harness ---------------------------------------------------------------------------

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = DwTier.Strict },
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

        /// <summary>Records a probe, and flags it when the guard refuses what the same query runs.</summary>
        private void Case(string probe, string unguarded, string guarded)
        {
            _out.WriteLine($"{probe,-62} unguarded={unguarded,-26} guarded={guarded}");

            if (unguarded.StartsWith("OK", StringComparison.Ordinal)
                && guarded.StartsWith("REFUSED", StringComparison.Ordinal))
            {
                _findings.Add($"{probe}: unguarded {unguarded}, guarded {guarded}");
            }
        }

        private void Done() => Assert.True(_findings.Count == 0, string.Join(" || ", _findings));

        // =========================================================================================
        // Ob4-A. EfCoreOwns is an EXACT type-name comparison now. Every ordinary EF Core shape must
        //        still carry exactly that provider, or its refusal is silently lost.
        // =========================================================================================

        private const string EfCoreProvider = "Microsoft.EntityFrameworkCore.Query.Internal.EntityQueryProvider";

        /// <summary>The exact comparison the shipped <c>EfCoreOwns</c> performs.</summary>
        private static bool ExactlyEfCore(IQueryProvider provider) => provider.GetType().FullName == EfCoreProvider;

        [Fact]
        public void Ob4_A_Every_ordinary_EF_Core_shape_carries_exactly_EF_Cores_provider()
        {
            (string Name, IQueryable Source)[] shapes =
            {
                ("DbSet", _db.Orders),
                ("Set<T>()", _db.Set<OvbOrder>()),
                ("Where", _db.Orders.Where(o => o.Id > 0)),
                ("AsNoTracking", _db.Orders.AsNoTracking()),
                ("AsNoTrackingWithIdentityResolution", _db.Orders.AsNoTrackingWithIdentityResolution()),
                ("AsTracking", _db.Orders.AsTracking()),
                ("Include", _db.Orders.Include(o => o.Customer)),
                ("Include+ThenInclude", _db.Customers.Include(c => c.Orders).ThenInclude(o => o.Lines)),
                ("AsSplitQuery", _db.Orders.Include(o => o.Lines).AsSplitQuery()),
                ("AsSingleQuery", _db.Orders.Include(o => o.Lines).AsSingleQuery()),
                ("IgnoreQueryFilters", _db.Orders.IgnoreQueryFilters()),
                ("IgnoreAutoIncludes", _db.Orders.IgnoreAutoIncludes()),
                ("TagWith", _db.Orders.TagWith("t")),
                ("TagWithCallSite", _db.Orders.TagWithCallSite()),
                ("FromSqlRaw", _db.Orders.FromSqlRaw("SELECT * FROM \"Orders\"")),
                ("FromSqlInterpolated", _db.Orders.FromSqlInterpolated($"SELECT * FROM \"Orders\"")),
                ("Select(row type)", _db.Orders.Select(o => new OvbRow { Id = o.Id })),
                ("Select(anonymous)", _db.Orders.Select(o => new { o.Id, o.Code })),
                ("Select(navigation)", _db.Orders.Select(o => o.Customer)),
                ("OfType", _db.Parties.OfType<OvbVendor>()),
                ("Cast", _db.Parties.Cast<OvbParty>()),
                ("Distinct", _db.Orders.Distinct()),
                ("OrderBy+Skip+Take", _db.Orders.OrderBy(o => o.Id).Skip(0).Take(5)),
                ("SelectMany", _db.Orders.SelectMany(o => o.Lines)),
                ("GroupBy+Select", _db.Orders.GroupBy(o => o.CustomerId).Select(g => new OvbRow { Id = g.Key })),
                ("Join", _db.Orders.Join(_db.Customers, o => o.CustomerId, c => c.Id, (o, c) => o)),
                ("Concat", _db.Orders.Concat(_db.Orders)),
                ("Union", _db.Orders.Union(_db.Orders))
            };

            List<string> lost = new();

            foreach ((string name, IQueryable source) in shapes)
            {
                bool exact = ExactlyEfCore(source.Provider);

                _out.WriteLine($"{name,-38} {source.Provider.GetType().FullName} exact={exact}");

                if (!exact)
                {
                    lost.Add($"{name} -> {source.Provider.GetType().FullName}");
                }
            }

            Assert.True(lost.Count == 0, "refusal silently lost on: " + string.Join(", ", lost));
        }

        [Fact]
        public void Ob4_A_A_pooled_context_a_factory_and_an_interceptor_carry_it_too()
        {
            // A pooled context and one built by a factory are ordinary DbContexts with a different
            // lifetime; an interceptor sits under the provider, not in front of it. Each must still
            // hand out exactly EF Core's own provider, or every query on them loses the refusal.
            ServiceCollectionStandIn services = new(_connection);

            List<string> lost = new();

            foreach ((string name, IQueryProvider provider) in services.Providers())
            {
                bool exact = ExactlyEfCore(provider);

                _out.WriteLine($"{name,-38} {provider.GetType().FullName} exact={exact}");

                if (!exact)
                {
                    lost.Add($"{name} -> {provider.GetType().FullName}");
                }
            }

            Assert.True(lost.Count == 0, "refusal silently lost on: " + string.Join(", ", lost));
        }

        /// <summary>Builds the context shapes a host builds, without needing a service container.</summary>
        private sealed class ServiceCollectionStandIn
        {
            private readonly SqliteConnection _connection;

            internal ServiceCollectionStandIn(SqliteConnection connection) => _connection = connection;

            internal IEnumerable<(string Name, IQueryProvider Provider)> Providers()
            {
                using OvbContext plain = new(_connection);

                yield return ("plain context", ((IQueryable<OvbOrder>)plain.Orders).Provider);

                // A second context over the same connection is what a factory hands out.
                using OvbContext fromFactory = new(_connection);

                yield return ("factory-built context", ((IQueryable<OvbOrder>)fromFactory.Orders).Provider);

                // The interceptor registered in OnConfiguring is already in force on both.
                yield return ("intercepted context", plain.Orders.AsNoTracking().Provider);

                using OvbInMemoryContext inMemory = new();

                yield return ("EF Core in-memory provider", ((IQueryable<OvbOrder>)inMemory.Orders).Provider);
            }
        }

        [Fact]
        public void Ob4_A_EF_Cores_provider_is_that_exact_type_in_every_version_it_supports()
        {
            // The exact comparison only holds if no EF Core version renames the type, namespaces it
            // differently, or hands out a subclass. Read straight from the assemblies.
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages", "microsoft.entityframeworkcore");

            if (!Directory.Exists(root))
            {
                _out.WriteLine("no package cache on this machine; nothing to read.");

                return;
            }

            List<string> wrong = new();
            int seen = 0;

            foreach (string version in Directory.GetDirectories(root).OrderBy(name => name))
            {
                string? assembly = Directory
                    .GetFiles(version, "Microsoft.EntityFrameworkCore.dll", SearchOption.AllDirectories)
                    .FirstOrDefault(path => path.Contains("net", StringComparison.OrdinalIgnoreCase));

                if (assembly is null)
                {
                    continue;
                }

                try
                {
                    using FileStream file = File.OpenRead(assembly);
                    using System.Reflection.PortableExecutable.PEReader reader = new(file);

                    System.Reflection.Metadata.MetadataReader metadata = reader.GetMetadataReader();

                    bool found = false;

                    foreach (System.Reflection.Metadata.TypeDefinitionHandle handle in metadata.TypeDefinitions)
                    {
                        System.Reflection.Metadata.TypeDefinition definition = metadata.GetTypeDefinition(handle);

                        if (metadata.GetString(definition.Name) != "EntityQueryProvider")
                        {
                            continue;
                        }

                        found = true;
                        seen++;

                        string space = metadata.GetString(definition.Namespace);
                        string baseName = definition.BaseType.Kind switch
                        {
                            System.Reflection.Metadata.HandleKind.TypeReference => metadata.GetString(
                                metadata.GetTypeReference(
                                    (System.Reflection.Metadata.TypeReferenceHandle)definition.BaseType).Name),
                            System.Reflection.Metadata.HandleKind.TypeDefinition => metadata.GetString(
                                metadata.GetTypeDefinition(
                                    (System.Reflection.Metadata.TypeDefinitionHandle)definition.BaseType).Name),
                            _ => "?"
                        };

                        _out.WriteLine(
                            $"{Path.GetFileName(version),-10} {space}.EntityQueryProvider : {baseName}");

                        if ($"{space}.EntityQueryProvider" != EfCoreProvider)
                        {
                            wrong.Add($"{version}: {space}.EntityQueryProvider");
                        }

                        if (baseName != "Object")
                        {
                            wrong.Add($"{version}: derives from {baseName}, so an exact match is not enough");
                        }
                    }

                    if (!found)
                    {
                        _out.WriteLine($"{Path.GetFileName(version),-10} no EntityQueryProvider in this package");
                    }
                }
                catch (Exception failure)
                {
                    _out.WriteLine($"{Path.GetFileName(version),-10} unreadable: {failure.GetType().Name}");
                }
            }

            _out.WriteLine($"versions read: {seen}");

            Assert.True(wrong.Count == 0, string.Join(" || ", wrong));
        }

        // =========================================================================================
        // Ob4-B. A projected collection of projected rows. MemberChain strips ToList/Where/Select,
        //        so the shape records the member as COPIED FROM THE ENTITY NAVIGATION and then reads
        //        the rest of the path out of the ENTITY's model — not out of the row the subquery
        //        actually builds.
        // =========================================================================================

        [Fact]
        public void Ob4_B_A_projected_collection_is_read_out_of_the_entity_not_the_projected_row()
        {
            IQueryable<OvbRow> projected = _db.Orders.Select(order => new OvbRow
            {
                Id = order.Id,
                Lines = order.Lines.Select(line => new OvbLineRow { Label = line.Sku, Qty = line.Qty }).ToList()
            });

            Case("B1 element member the subquery assigns, name the entity also declares",
                Raw(() => _db.Orders
                    .Select(o => new OvbRow
                    {
                        Id = o.Id,
                        Lines = o.Lines.Select(l => new OvbLineRow { Label = l.Sku, Qty = l.Qty }).ToList()
                    })
                    .Where(r => r.Lines.Any(l => l.Label == "S-1")).ToList()),
                Guarded(projected, "Lines.Label", DataType.Text, "S-1"));

            Case("B2 element member both the row and the entity map (control)",
                Raw(() => _db.Orders
                    .Select(o => new OvbRow
                    {
                        Id = o.Id,
                        Lines = o.Lines.Select(l => new OvbLineRow { Label = l.Sku, Qty = l.Qty }).ToList()
                    })
                    .Where(r => r.Lines.Any(l => l.Qty == 2)).ToList()),
                Guarded(projected, "Lines.Qty", DataType.Number, "2"));

            // The third case of the triangulation. The only thing that changes between B1, B2 and
            // B2b is whether the ENTITY's element type declares the name and whether it maps it:
            //   maps it            -> allowed   (B2, Qty)
            //   declares, unmapped -> REFUSED   (B1, Label)
            //   does not declare   -> allowed   (B2b, Note)
            // which is the model of OvbLine being read, not the OvbLineRow the subquery builds.
            IQueryable<OvbRow> noted = _db.Orders.Select(order => new OvbRow
            {
                Id = order.Id,
                Lines = order.Lines.Select(line => new OvbLineRow { Note = line.Sku, Qty = line.Qty }).ToList()
            });

            Case("B2b element member the entity does not declare at all",
                Raw(() => _db.Orders
                    .Select(o => new OvbRow
                    {
                        Id = o.Id,
                        Lines = o.Lines.Select(l => new OvbLineRow { Note = l.Sku, Qty = l.Qty }).ToList()
                    })
                    .Where(r => r.Lines.Any(l => l.Note == "S-1")).ToList()),
                Guarded(noted, "Lines.Note", DataType.Text, "S-1"));

            // The same shape with a filtered include's operators in front of the Select, which is how
            // a caller writes it when the collection is narrowed.
            IQueryable<OvbRow> filtered = _db.Orders.Select(order => new OvbRow
            {
                Id = order.Id,
                Lines = order.Lines
                    .Where(line => line.Qty > 0)
                    .Select(line => new OvbLineRow { Label = line.Sku, Qty = line.Qty })
                    .ToList()
            });

            Case("B3 the same, with the subquery filtered first",
                Raw(() => _db.Orders
                    .Select(o => new OvbRow
                    {
                        Id = o.Id,
                        Lines = o.Lines.Where(l => l.Qty > 0)
                            .Select(l => new OvbLineRow { Label = l.Sku, Qty = l.Qty }).ToList()
                    })
                    .Where(r => r.Lines.Any(l => l.Label == "S-1")).ToList()),
                Guarded(filtered, "Lines.Label", DataType.Text, "S-1"));

            // The entity's own rows copied whole: here the entity IS what the member holds, so the
            // refusal is right and the unguarded query fails the same way.
            IQueryable<OvbRow> copiedWhole = _db.Orders.Select(order => new OvbRow
            {
                Id = order.Id,
                Raw = order.Lines.ToList()
            });

            Case("B4 the entity's own rows copied whole (the refusal is right here)",
                Raw(() => _db.Orders.Select(o => new OvbRow { Id = o.Id, Raw = o.Lines.ToList() })
                    .Where(r => r.Raw.Any(l => l.Label == "S-1")).ToList()),
                Guarded(copiedWhole, "Raw.Label", DataType.Text, "S-1"));

            Done();
        }

        [Fact]
        public void Ob4_B_A_member_read_out_of_a_collection_is_read_against_the_elements_model()
        {
            // Customer is assigned from a subquery over Lines, so MemberChain records ("Customer",
            // Order, "Lines") and the rest of the path is walked in OvbLine — a type that has nothing
            // to do with what the member holds.
            IQueryable<OvbRow> projected = _db.Orders.Select(order => new OvbRow
            {
                Id = order.Id,
                Customer = order.Lines.Select(line => line.Order.Customer).FirstOrDefault()
            });

            Case("B5 customer read out of a line subquery, filtered on a column of the customer",
                Raw(() => _db.Orders
                    .Select(o => new OvbRow { Id = o.Id, Customer = o.Lines.Select(l => l.Order.Customer).FirstOrDefault() })
                    .Where(r => r.Customer!.Name == "Acme").ToList()),
                Guarded(projected, "Customer.Name", DataType.Text, "Acme"));

            Done();
        }

        // =========================================================================================
        // Ob4-C. Binding shapes the initializer reader skips: a member-member binding and a list
        //        binding record the NAME but nothing beneath it.
        // =========================================================================================

        [Fact]
        public void Ob4_C_A_member_member_binding_is_left_alone()
        {
            IQueryable<OvbRow> nested = _db.Orders.Select(order => new OvbRow
            {
                Id = order.Id,
                Nest = { A = order.Code }
            });

            Case("C1 member-member binding, the member it sets",
                Raw(() => _db.Orders.Select(o => new OvbRow { Id = o.Id, Nest = { A = o.Code } })
                    .Where(r => r.Nest.A == "AB123").ToList()),
                Guarded(nested, "Nest.A", DataType.Text, "AB123"));

            Case("C2 member-member binding, the sibling it does not set",
                Raw(() => _db.Orders.Select(o => new OvbRow { Id = o.Id, Nest = { A = o.Code } })
                    .Where(r => r.Nest.B == "x").ToList()),
                Guarded(nested, "Nest.B", DataType.Text, "x"));

            Done();
        }

        // =========================================================================================
        // Ob4-D. The entity branch's remaining mapping shapes, on both EF Core legs.
        // =========================================================================================

        [Fact]
        public void Ob4_D_The_entity_branch_leaves_every_mapped_shape_alone()
        {
            Case("D1 computed column",
                Raw(() => _db.Orders.Where(o => o.Doubled == 20m).ToList()),
                Guarded(_db.Orders, "Doubled", DataType.Number, "20"));

            Case("D2 a member beneath a converted column",
                Raw(() => _db.Orders.Where(o => o.Badge.En == "gold").ToList()),
                Guarded(_db.Orders, "Badge.En", DataType.Text, "gold"));

            Case("D3 a getter beneath a converted column",
                Raw(() => _db.Orders.Where(o => o.Badge.IsEmpty).ToList()),
                Guarded(_db.Orders, "Badge.IsEmpty", DataType.Boolean, "false"));

            Case("D4 a column of an owned type",
                Raw(() => _db.Orders.Where(o => o.Title.En == "EN").ToList()),
                Guarded(_db.Orders, "Title.En", DataType.Text, "EN"));

            Case("D5 a framework member of a column",
                Raw(() => _db.Orders.Where(o => o.Code.Length == 5).ToList()),
                Guarded(_db.Orders, "Code.Length", DataType.Number, "5"));

            Case("D6 a column across a reference navigation",
                Raw(() => _db.Orders.Where(o => o.Customer.Name == "Acme").ToList()),
                Guarded(_db.Orders, "Customer.Name", DataType.Text, "Acme"));

            Case("D7 a column across a collection navigation",
                Raw(() => _db.Orders.Where(o => o.Lines.Any(l => l.Sku == "S-1")).ToList()),
                Guarded(_db.Orders, "Lines.Sku", DataType.Text, "S-1"));

            Case("D8 a TPH subtype column through its own set",
                Raw(() => _db.Parties.OfType<OvbVendor>().Where(v => v.Vat == "V-1").ToList()),
                Guarded(_db.Parties.OfType<OvbVendor>(), "Vat", DataType.Text, "V-1"));

            Case("D9 a mapped column of the root (control)",
                Raw(() => _db.Orders.Where(o => o.Code == "AB123").ToList()),
                Guarded(_db.Orders, "Code", DataType.Text, "AB123"));

            // A shadow property has no CLR member, so the name never reaches the new walk: it is an
            // unknown name, refused the way it was before this branch. The unguarded form has to go
            // through EF.Property, which is not a path a caller can write.
            string shadow = Guarded(_db.Orders, "Tenant", DataType.Text, "t1");

            _out.WriteLine($"D14 a shadow property, named directly       unguarded=(EF.Property only) guarded={shadow}");

            // Documented limit, and no regression: a caller's path names CLR members, so a shadow
            // property is a name that matches nothing, refused as one, on this branch and before it.
            // The unguarded form has to go through EF.Property, which is not a path a caller writes.
            Assert.StartsWith("REFUSED", shadow);

            Done();
        }

        [Fact]
        public void Ob4_D_The_entity_branch_over_a_set_operation_and_a_reshape()
        {
            Case("D10 Concat of two entity queries",
                Raw(() => _db.Orders.Concat(_db.Orders).Where(o => o.Code == "AB123").ToList()),
                Guarded(_db.Orders.Concat(_db.Orders), "Code", DataType.Text, "AB123"));

            Case("D11 rows reached through a navigation",
                Raw(() => _db.Orders.Select(o => o.Customer).Where(c => c.Name == "Acme").ToList()),
                Guarded(_db.Orders.Select(o => o.Customer), "Name", DataType.Text, "Acme"));

            Case("D12 rows reached by SelectMany",
                Raw(() => _db.Orders.SelectMany(o => o.Lines).Where(l => l.Sku == "S-1").ToList()),
                Guarded(_db.Orders.SelectMany(o => o.Lines), "Sku", DataType.Text, "S-1"));

            Case("D13 a getter on rows reached by SelectMany",
                Raw(() => _db.Orders.SelectMany(o => o.Lines).Where(l => l.Label == "S-1#2").ToList()),
                Guarded(_db.Orders.SelectMany(o => o.Lines), "Label", DataType.Text, "S-1#2"));

            Done();
        }

        // =========================================================================================
        // Ob4-E. Members of one projected row read from another projected row's members.
        // =========================================================================================

        [Fact]
        public void Ob4_E_A_second_projection_over_the_first()
        {
            IQueryable<OvbRow> once = _db.Orders.Select(order => new OvbRow { Id = order.Id, Code = order.Code });
            IQueryable<OvbRow> twice = once.Select(row => new OvbRow { Id = row.Id, Tag = row.Code });

            Case("E1 member of the second projection, assigned from the first",
                Raw(() => _db.Orders.Select(o => new OvbRow { Id = o.Id, Code = o.Code })
                    .Select(r => new OvbRow { Id = r.Id, Tag = r.Code })
                    .Where(x => x.Tag == "AB123").ToList()),
                Guarded(twice, "Tag", DataType.Text, "AB123"));

            Case("E2 a framework member beneath it",
                Raw(() => _db.Orders.Select(o => new OvbRow { Id = o.Id, Code = o.Code })
                    .Select(r => new OvbRow { Id = r.Id, Tag = r.Code })
                    .Where(x => x.Tag.Length == 5).ToList()),
                Guarded(twice, "Tag.Length", DataType.Number, "5"));

            Case("E3 a member the second projection leaves out",
                Raw(() => _db.Orders.Select(o => new OvbRow { Id = o.Id, Code = o.Code })
                    .Select(r => new OvbRow { Id = r.Id, Tag = r.Code })
                    .Where(x => x.Code == "AB123").ToList()),
                Guarded(twice, "Code", DataType.Text, "AB123"));

            Done();
        }

        // =========================================================================================
        // Ob4-F. Anonymous rows and a constructor with arguments: neither knows which member each
        //        value sets, so neither may refuse anything.
        // =========================================================================================

        [Fact]
        public void Ob4_F_A_constructor_with_arguments_and_the_bindings_beside_it()
        {
            IQueryable<OvbRow> built = _db.Orders.Select(order => new OvbRow(order.Id) { Code = order.Code });

            Case("F1 member the initializer beside the constructor sets",
                Raw(() => _db.Orders.Select(o => new OvbRow(o.Id) { Code = o.Code })
                    .Where(r => r.Code == "AB123").ToList()),
                Guarded(built, "Code", DataType.Text, "AB123"));

            Case("F2 member only the constructor could have set",
                Raw(() => _db.Orders.Select(o => new OvbRow(o.Id) { Code = o.Code })
                    .Where(r => r.Id == 1).ToList()),
                Guarded(built, "Id", DataType.Number, "1"));

            Case("F3 member neither sets",
                Raw(() => _db.Orders.Select(o => new OvbRow(o.Id) { Code = o.Code })
                    .Where(r => r.Tag == "x").ToList()),
                Guarded(built, "Tag", DataType.Text, "x"));

            Done();
        }

        // =========================================================================================
        // Ob4-G. The projection clause is exempt, so nothing above takes back a Select that worked.
        // =========================================================================================

        [Fact]
        public void Ob4_G_A_projection_naming_the_refused_path_still_returns_it()
        {
            IQueryable<OvbRow> projected = _db.Orders.Select(order => new OvbRow
            {
                Id = order.Id,
                Lines = order.Lines.Select(line => new OvbLineRow { Label = line.Sku, Qty = line.Qty }).ToList()
            });

            string selected;

            try
            {
                FilterResult<OvbRow> result = Guard(projected).ToList(new Filter
                {
                    Selects = new List<string> { "Id", "Lines.Label" }
                });

                selected = $"OK({result.Data.Count})";
            }
            catch (PolicyException refusal)
            {
                selected = $"REFUSED({refusal.ErrorCode})";
            }
            catch (Exception failure)
            {
                selected = failure.GetType().Name;
            }

            Case("G1 Selects naming the path Where refuses",
                Raw(() => _db.Orders
                    .Select(o => new OvbRow
                    {
                        Id = o.Id,
                        Lines = o.Lines.Select(l => new OvbLineRow { Label = l.Sku, Qty = l.Qty }).ToList()
                    }).ToList()),
                selected);

            Done();
        }
    }

    /// <summary>EF Core's own in-memory provider, which is still EF Core's provider.</summary>
    public sealed class OvbInMemoryContext : DbContext
    {
        public DbSet<OvbOrder> Orders => Set<OvbOrder>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseInMemoryDatabase("ob4-" + Guid.NewGuid().ToString("N"));

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<OvbOrder>().OwnsOne(order => order.Title);
            model.Entity<OvbOrder>().Ignore(order => order.Display);
            model.Entity<OvbOrder>().Ignore(order => order.Badge);
            model.Entity<OvbOrder>().Ignore(order => order.Doubled);
            model.Entity<OvbCustomer>().Ignore(customer => customer.Handle);
            model.Entity<OvbLine>().Ignore(line => line.Label);
            model.Entity<OvbLine>().Ignore(line => line.Name);
        }
    }
}
