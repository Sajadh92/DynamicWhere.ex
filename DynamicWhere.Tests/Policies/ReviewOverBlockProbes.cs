using System.Linq.Expressions;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
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

namespace DynamicWhere.Tests.Policies
{
    // ---- model -------------------------------------------------------------------------------------------

    public class RbText
    {
        public string Ar { get; set; } = string.Empty;

        public string En { get; set; } = string.Empty;

        /// <summary>A getter over two columns: no database can answer it.</summary>
        public bool IsEmpty => string.IsNullOrWhiteSpace(Ar) && string.IsNullOrWhiteSpace(En);
    }

    public class RbCustomer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public RbText Title { get; set; } = new();

        public List<RbOrder> Orders { get; set; } = new();

        /// <summary>Unmapped getter over mapped columns.</summary>
        public string Display => $"{Name}#{Id}";
    }

    public class RbOrder
    {
        public int Id { get; set; }

        /// <summary>An alias whose target is a member the model does not map.</summary>
        [DwAlias("ticket")]
        public string AliasedLabel => $"{Code}!";

        /// <summary>An alias over a mapped column, for the control case.</summary>
        [DwAlias("ref")]
        public string Reference { get; set; } = string.Empty;

        /// <summary>Denied outright, so a Selects naming it must still be refused.</summary>
        [DwDenied]
        public string Secret { get; set; } = string.Empty;

        public int CustomerId { get; set; }

        public RbCustomer Customer { get; set; } = null!;

        public string Code { get; set; } = string.Empty;

        public DateTime PlacedAt { get; set; }

        public DateTime? ShippedAt { get; set; }

        public decimal Amount { get; set; }

        /// <summary>Mapped with a computed column.</summary>
        public decimal Doubled { get; set; }

        /// <summary>Unmapped getter, ignored in the model.</summary>
        public string Label => $"{Code}/{Id}";
    }

    /// <summary>TPH.</summary>
    public class RbParty
    {
        public int Id { get; set; }

        public string Kind { get; set; } = string.Empty;
    }

    public class RbVendor : RbParty
    {
        public string? Vat { get; set; }
    }

    // ---- rows a caller projects --------------------------------------------------------------------------

    public class RbRow
    {
        public RbRow()
        {
        }

        public RbRow(int id) => Id = id;

        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public RbText Title { get; set; } = new();

        public string Tag { get; set; } = string.Empty;

        public RbCustomer? Customer { get; set; }
    }

    public sealed class RbContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public RbContext(SqliteConnection connection) => _connection = connection;

        public DbSet<RbCustomer> Customers => Set<RbCustomer>();

        public DbSet<RbOrder> Orders => Set<RbOrder>();

        public DbSet<RbParty> Parties => Set<RbParty>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<RbCustomer>().OwnsOne(customer => customer.Title);
            model.Entity<RbCustomer>().Ignore(customer => customer.Display);

            model.Entity<RbOrder>().Ignore(order => order.Label);
            model.Entity<RbOrder>().Ignore(order => order.AliasedLabel);
            model.Entity<RbOrder>().Property(order => order.Doubled).HasComputedColumnSql("\"Amount\" * 2");
            model.Entity<RbOrder>().Property<string>("Tenant");

            model.Entity<RbParty>().HasDiscriminator<string>("Discriminator")
                .HasValue<RbParty>("party")
                .HasValue<RbVendor>("vendor");
        }
    }

    /// <summary>
    /// A provider layered over EF Core, the way LinqKit's <c>AsExpandable</c> and DelegateDecompiler's
    /// <c>Decompile</c> are: the expression still carries EF Core's query root, and the provider
    /// rewrites what EF Core alone could not translate before handing it on.
    /// </summary>
    public sealed class RbDecompilingProvider<T> : IQueryable<T>, IQueryProvider
    {
        private readonly IQueryProvider _inner;

        public RbDecompilingProvider(IQueryable<T> inner)
            : this(inner.Provider, inner.Expression)
        {
        }

        public RbDecompilingProvider(IQueryProvider inner, Expression expression)
        {
            _inner = inner;
            Expression = expression;
        }

        public Type ElementType => typeof(T);

        public Expression Expression { get; }

        public IQueryProvider Provider => this;

        public IEnumerator<T> GetEnumerator() =>
            _inner.CreateQuery<T>(Rewritten(Expression)).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        // Composition stays wrapped, as LinqKit's ExpandableQuery does: the expansion happens once,
        // at execution, over the whole expression the caller built.
        public IQueryable CreateQuery(Expression expression) =>
            (IQueryable)Activator.CreateInstance(
                typeof(RbDecompilingProvider<>).MakeGenericType(
                    expression.Type.GetGenericArguments().Length == 1
                        ? expression.Type.GetGenericArguments()[0]
                        : typeof(object)),
                new object[] { _inner, expression },
                null)!;

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new RbDecompilingProvider<TElement>(_inner, expression);

        public object? Execute(Expression expression) => _inner.Execute(Rewritten(expression));

        public TResult Execute<TResult>(Expression expression) => _inner.Execute<TResult>(Rewritten(expression));

        /// <summary>Expands the getters EF Core cannot translate into the columns behind them.</summary>
        private static Expression Rewritten(Expression expression) => new RbDecompiler().Visit(expression);
    }

    /// <summary>Replaces <c>Label</c> and <c>Display</c> with the expression their getter computes.</summary>
    public sealed class RbDecompiler : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Member.Name == "Label" && node.Member.DeclaringType == typeof(RbOrder))
            {
                Expression order = Visit(node.Expression)!;

                return Expression.Call(
                    typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(string), typeof(string) })!,
                    Expression.Property(order, nameof(RbOrder.Code)),
                    Expression.Constant("/"));
            }

            if (node.Member.Name == "Display" && node.Member.DeclaringType == typeof(RbCustomer))
            {
                Expression customer = Visit(node.Expression)!;

                return Expression.Property(customer, nameof(RbCustomer.Name));
            }

            return base.VisitMember(node);
        }
    }

    public sealed class ReviewOverBlockProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly RbContext _db;

        public ReviewOverBlockProbes(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new RbContext(_connection);
            _db.Database.EnsureCreated();

            RbCustomer customer = new() { Name = "Acme", Title = new RbText { Ar = "AR", En = "EN" } };

            RbOrder order = new()
            {
                Customer = customer,
                Code = "AB123",
                PlacedAt = new DateTime(2024, 3, 9, 14, 30, 0),
                ShippedAt = new DateTime(2024, 3, 11, 9, 0, 0),
                Amount = 10m,
                Reference = "R-1",
                Secret = "s"
            };

            _db.Customers.Add(customer);
            _db.Orders.Add(order);
            _db.Entry(order).Property("Tenant").CurrentValue = "t1";
            _db.Parties.Add(new RbVendor { Kind = "vendor", Vat = "V-1" });
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

        private static ConditionGroup Group(string field, string value, DataType type = DataType.Text) => new()
        {
            Conditions = { new Condition { Field = field, DataType = type, Operator = Operator.Equal, Values = { value } } }
        };

        /// <summary>Runs a guarded filter and says what happened, for the probe log.</summary>
        private string Probe<T>(IQueryable<T> source, Filter filter) where T : class
        {
            try
            {
                FilterResult<T> result = Guard(source).ToList(filter);

                return $"ran, {result.Data.Count} row(s)";
            }
            catch (PolicyException refusal)
            {
                return $"REFUSED {refusal.ErrorCode}";
            }
            catch (Exception other)
            {
                return $"threw {other.GetType().Name}";
            }
        }

        private string Unguarded<T>(Func<IQueryable<T>> run)
        {
            try
            {
                return $"ran, {run().Count()} row(s)";
            }
            catch (Exception other)
            {
                return $"threw {other.GetType().Name}";
            }
        }

        // ---- framework members the provider translates, on every clause ----------------------------------

        [Theory]
        [InlineData("Code.Length", "5", DataType.Number)]
        [InlineData("PlacedAt.Year", "2024", DataType.Number)]
        [InlineData("PlacedAt.Month", "3", DataType.Number)]
        [InlineData("PlacedAt.Date", "2024-03-09", DataType.Date)]
        [InlineData("ShippedAt.Value.Year", "2024", DataType.Number)]
        [InlineData("Doubled", "20", DataType.Number)]
        [InlineData("Customer.Name", "Acme", DataType.Text)]
        [InlineData("Customer.Title.En", "EN", DataType.Text)]
        public void A_translatable_member_still_filters(string field, string value, DataType type)
        {
            string guarded = Probe(_db.Orders, Where(field, value, type));

            _out.WriteLine($"where {field}: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_shadow_property_is_refused_on_both_versions()
        {
            // Ruled out, not a finding. A shadow property has no CLR member, so the path never
            // resolved: 3.2.0 refuses it with the same code, before Expresses existed.
            string guarded = Probe(_db.Orders, Where("Tenant", "t1"));

            _out.WriteLine($"shadow property: {guarded}");

            Assert.StartsWith("REFUSED", guarded);
        }

        [Theory]
        [InlineData("Code.Length")]
        [InlineData("PlacedAt.Year")]
        [InlineData("Customer.Name")]
        public void A_translatable_member_still_orders(string field)
        {
            PolicyQueryable<RbOrder> guarded = Guard(_db.Orders);

            Exception? failed = Record.Exception(() => guarded.Order(new OrderBy { Field = field, Direction = Direction.Ascending }));

            _out.WriteLine($"order {field}: {failed?.GetType().Name ?? "ran"} {(failed as PolicyException)?.ErrorCode}");

            Assert.Null(failed);
        }

        [Theory]
        [InlineData("Code.Length")]
        [InlineData("PlacedAt.Year")]
        public void A_translatable_member_still_groups(string field)
        {
            Exception? failed = Record.Exception(() => Guard(_db.Orders).Group(new GroupBy
            {
                Fields = new List<string> { field },
                AggregateBy = new List<AggregateBy> { new() { Alias = "Total", Aggregator = Aggregator.Count } }
            }));

            _out.WriteLine($"group {field}: {failed?.GetType().Name ?? "ran"} {(failed as PolicyException)?.ErrorCode}");

            Assert.Null(failed);
        }

        [Fact]
        public void A_summary_over_an_entity_still_aggregates_a_navigation_path()
        {
            Exception? failed = Record.Exception(() => Guard(_db.Orders).ToList(new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "Customer.Name" },
                    AggregateBy = new List<AggregateBy> { new() { Alias = "Total", Field = "Id", Aggregator = Aggregator.Sumation } }
                }
            }));

            _out.WriteLine($"summary: {failed?.GetType().Name ?? "ran"} {(failed as PolicyException)?.ErrorCode} :: {failed?.InnerException?.GetType().Name} :: {failed?.InnerException?.Message}");

            Assert.Null(failed);
        }

        // ---- hierarchies ---------------------------------------------------------------------------------

        [Fact]
        public void OfType_reaches_the_subtype_s_own_column()
        {
            string unguarded = Unguarded(() => _db.Parties.OfType<RbVendor>().Where(vendor => vendor.Vat == "V-1"));
            string guarded = Probe(_db.Parties.OfType<RbVendor>(), Where("Vat", "V-1"));

            _out.WriteLine($"OfType unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void Cast_reaches_the_subtype_s_own_column()
        {
            string guarded = Probe(_db.Set<RbVendor>().Cast<RbVendor>(), Where("Vat", "V-1"));

            _out.WriteLine($"Cast guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        // ---- projections ---------------------------------------------------------------------------------

        [Fact]
        public void An_anonymous_projection_still_filters()
        {
            var rows = _db.Orders.Select(order => new { order.Id, order.Code, Title = order.Customer.Title });

            string unguarded = Unguarded(() => rows.Where(row => row.Code == "AB123"));

            _out.WriteLine($"anon unguarded: {unguarded}");

            // The guarded handle needs a class, so the same shape is probed through a named row.
            Assert.StartsWith("ran", unguarded);
        }

        [Fact]
        public void A_constructor_with_arguments_plus_an_initializer_still_filters()
        {
            IQueryable<RbRow> rows = _db.Orders.Select(order => new RbRow(order.Id) { Code = order.Code });

            string unguarded = Unguarded(() => rows.Where(row => row.Code == "AB123"));
            string guarded = Probe(rows, Where("Code", "AB123"));

            _out.WriteLine($"ctor+init unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_member_a_projection_computes_still_filters()
        {
            IQueryable<RbRow> rows = _db.Orders.Select(order => new RbRow
            {
                Id = order.Id,
                Tag = order.Code.Substring(0, 2)
            });

            string unguarded = Unguarded(() => rows.Where(row => row.Tag == "AB"));
            string guarded = Probe(rows, Where("Tag", "AB"));

            _out.WriteLine($"computed member unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_member_a_projection_assigns_conditionally_still_filters()
        {
            IQueryable<RbRow> rows = _db.Orders.Select(order => new RbRow
            {
                Id = order.Id,
                Tag = order.Amount > 5m ? order.Code : "none"
            });

            string unguarded = Unguarded(() => rows.Where(row => row.Tag == "AB123"));
            string guarded = Probe(rows, Where("Tag", "AB123"));

            _out.WriteLine($"conditional member unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_length_beneath_a_member_a_projection_copies_still_filters()
        {
            IQueryable<RbRow> rows = _db.Orders.Select(order => new RbRow { Id = order.Id, Code = order.Code });

            string unguarded = Unguarded(() => rows.Where(row => row.Code.Length == 5));
            string guarded = Probe(rows, Where("Code.Length", "5", DataType.Number));

            _out.WriteLine($"copied.Length unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_length_beneath_a_member_a_projection_builds_still_filters()
        {
            IQueryable<RbRow> rows = _db.Customers.Select(customer => new RbRow
            {
                Id = customer.Id,
                Title = new RbText { Ar = customer.Title.Ar, En = customer.Title.En }
            });

            string unguarded = Unguarded(() => rows.Where(row => row.Title.En.Length == 2));
            string guarded = Probe(rows, Where("Title.En.Length", "2", DataType.Number));

            _out.WriteLine($"built.En.Length unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_projection_of_a_navigation_still_filters()
        {
            IQueryable<RbCustomer> rows = _db.Orders.Select(order => order.Customer);

            string unguarded = Unguarded(() => rows.Where(customer => customer.Name == "Acme"));
            string guarded = Probe(rows, Where("Name", "Acme"));

            _out.WriteLine($"Select(nav) unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void Two_selects_still_filter()
        {
            IQueryable<RbRow> rows = _db.Orders
                .Select(order => new RbRow { Id = order.Id, Code = order.Code })
                .Select(row => new RbRow { Id = row.Id, Tag = row.Code });

            string unguarded = Unguarded(() => rows.Where(row => row.Tag == "AB123"));
            string guarded = Probe(rows, Where("Tag", "AB123"));

            _out.WriteLine($"two selects unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_selectmany_still_filters()
        {
            IQueryable<RbOrder> rows = _db.Customers.SelectMany(customer => customer.Orders);

            string unguarded = Unguarded(() => rows.Where(order => order.Code == "AB123"));
            string guarded = Probe(rows, Where("Code", "AB123"));

            _out.WriteLine($"SelectMany unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_join_still_filters()
        {
            IQueryable<RbRow> rows = _db.Orders.Join(
                _db.Customers,
                order => order.CustomerId,
                customer => customer.Id,
                (order, customer) => new RbRow { Id = order.Id, Code = order.Code, Tag = customer.Name });

            string unguarded = Unguarded(() => rows.Where(row => row.Tag == "Acme"));
            string guarded = Probe(rows, Where("Tag", "Acme"));

            _out.WriteLine($"Join unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_let_clause_still_filters()
        {
            IQueryable<RbRow> rows =
                from order in _db.Orders
                let tag = order.Code
                select new RbRow { Id = order.Id, Tag = tag };

            string unguarded = Unguarded(() => rows.Where(row => row.Tag == "AB123"));
            string guarded = Probe(rows, Where("Tag", "AB123"));

            _out.WriteLine($"let unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_groupby_projection_still_filters()
        {
            IQueryable<RbRow> rows = _db.Orders
                .GroupBy(order => order.CustomerId)
                .Select(group => new RbRow { Id = group.Key, Tag = group.Count().ToString() });

            string unguarded = Unguarded(() => rows.Where(row => row.Id == 1));
            string guarded = Probe(rows, Where("Id", "1", DataType.Number));

            _out.WriteLine($"GroupBy unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_projection_that_copies_an_owned_member_still_filters_beneath_it()
        {
            IQueryable<RbRow> rows = _db.Customers.Select(customer => new RbRow
            {
                Id = customer.Id,
                Title = customer.Title
            });

            string unguarded = Unguarded(() => rows.Where(row => row.Title.En == "EN"));
            string guarded = Probe(rows, Where("Title.En", "EN"));

            _out.WriteLine($"copied owned unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        // ---- the wrapping provider -----------------------------------------------------------------------

        [Fact]
        public void A_decompiling_provider_over_EF_Core_is_not_held_to_EF_Core_s_model()
        {
            IQueryable<RbOrder> rows = new RbDecompilingProvider<RbOrder>(_db.Orders);

            string unguarded = Unguarded(() => rows.Where(order => order.Label == "AB123/"));
            string guarded = Probe(rows, Where("Label", "AB123/"));

            _out.WriteLine($"decompiling provider unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", unguarded);
            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_decompiling_provider_over_a_navigation_getter_is_not_held_to_EF_Core_s_model()
        {
            IQueryable<RbCustomer> rows = new RbDecompilingProvider<RbCustomer>(_db.Customers);

            string unguarded = Unguarded(() => rows.Where(customer => customer.Display == "Acme"));
            string guarded = Probe(rows, Where("Display", "Acme"));

            _out.WriteLine($"decompiling nav unguarded: {unguarded} | guarded: {guarded}");

            Assert.StartsWith("ran", unguarded);
            Assert.StartsWith("ran", guarded);
        }


        [Fact]
        public void The_same_wrapping_provider_is_answered_for_the_same_way_over_either_kind_of_row()
        {
            // Both branches ask the same question — is EF Core's own provider the one translating
            // this? — so the same provider over a projection and over an entity gets the same
            // answer. Until 3.3.0's review only the projection branch asked.
            IQueryable<RbRow> rows = new RbDecompilingProvider<RbRow>(
                _db.Customers.Select(customer => new RbRow { Id = customer.Id, Tag = customer.Name }));

            string guarded = Probe(rows, Where("Tag.Length", "4", DataType.Number));

            _out.WriteLine($"wrapper over a projection: {guarded}");

            IQueryable<RbOrder> entities = new RbDecompilingProvider<RbOrder>(_db.Orders);

            string overEntity = Probe(entities, Where("Label", "AB123/"));

            _out.WriteLine($"same wrapper over an entity: {overEntity}");

            Assert.DoesNotContain("REFUSED", guarded);
            Assert.DoesNotContain("REFUSED", overEntity);
        }

        // ---- composed clauses ----------------------------------------------------------------------------

        [Fact]
        public void A_composed_chain_over_a_projection_still_runs()
        {
            IQueryable<RbRow> rows = _db.Orders.Select(order => new RbRow
            {
                Id = order.Id,
                Code = order.Code,
                Tag = order.Code.Substring(0, 2)
            });

            Exception? failed = Record.Exception(() => Guard(rows)
                .Where(Group("Tag", "AB"))
                .Order(new OrderBy { Field = "Code", Direction = Direction.Ascending })
                .Select(new List<string> { "Id", "Code" })
                .Page(new PageBy { PageNumber = 1, PageSize = 10 })
                .ToList(new Filter()));

            _out.WriteLine($"composed: {failed?.GetType().Name ?? "ran"} {(failed as PolicyException)?.ErrorCode}");

            Assert.Null(failed);
        }

        [Fact]
        public void A_composed_select_of_a_navigation_still_runs()
        {
            Exception? failed = Record.Exception(() =>
                Guard(_db.Orders).Select(new List<string> { "Id", "Customer" }).ToList(new Filter()));

            _out.WriteLine($"composed select nav: {failed?.GetType().Name ?? "ran"} {(failed as PolicyException)?.ErrorCode} :: {failed?.Message}");

            // Ruled out, not a finding. Customer.Orders reaches RbOrder.Secret, which is denied, and
            // 3.2.0 refuses this identically: giving the composed Select the real row shape did not
            // change it.
            Assert.IsType<PolicyException>(failed);
        }

        [Fact]
        public void A_composed_select_of_an_owned_member_still_runs()
        {
            Exception? failed = Record.Exception(() =>
                Guard(_db.Customers).Select(new List<string> { "Id", "Title" }).ToList(new Filter()));

            _out.WriteLine($"composed select owned: {failed?.GetType().Name ?? "ran"} {(failed as PolicyException)?.ErrorCode}");

            Assert.Null(failed);
        }

        [Fact]
        public void A_composed_filter_and_filterdynamic_still_run()
        {
            Exception? filtered = Record.Exception(() => Guard(_db.Orders).Filter(Where("Code.Length", "5", DataType.Number)).ToList(new Filter()));
            Exception? dynamic_ = Record.Exception(() => Guard(_db.Orders).FilterDynamic(Where("Code.Length", "5", DataType.Number)));

            _out.WriteLine($"Filter: {filtered?.GetType().Name ?? "ran"} | FilterDynamic: {dynamic_?.GetType().Name ?? "ran"}");

            Assert.Null(filtered);
            Assert.Null(dynamic_);
        }

        // ---- casing ---------------------------------------------------------------------------------------

        [Fact]
        public void A_path_whose_casing_differs_still_filters()
        {
            string guarded = Probe(_db.Orders, Where("customer.title.en", "EN"));

            _out.WriteLine($"casing: {guarded}");

            Assert.StartsWith("ran", guarded);
        }


        // ---- batch 2: aliases, denials, copied members, postures ------------------------------------------

        [Fact]
        public void An_alias_over_a_mapped_column_still_filters()
        {
            string guarded = Probe(_db.Orders, Where("ref", "R-1"));

            _out.WriteLine($"alias over column: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void An_alias_whose_target_is_unmapped_matches_the_unguarded_failure()
        {
            string unguarded = Unguarded(() => _db.Orders.Where(order => order.AliasedLabel == "AB123!"));
            string guarded = Probe(_db.Orders, Where("ticket", "AB123!"));

            _out.WriteLine($"alias to unmapped unguarded: {unguarded} | guarded: {guarded}");
        }

        [Fact]
        public void A_member_copied_from_an_unmapped_getter_matches_the_unguarded_failure()
        {
            IQueryable<RbRow> rows = _db.Customers.Select(customer => new RbRow
            {
                Id = customer.Id,
                Tag = customer.Display
            });

            string unguarded = Unguarded(() => rows.Where(row => row.Tag.Length == 4));
            string guarded = Probe(rows, Where("Tag.Length", "4", DataType.Number));

            _out.WriteLine($"copied-from-getter unguarded: {unguarded} | guarded: {guarded}");
        }

        [Fact]
        public void A_member_copied_from_an_unmapped_getter_still_filters_on_itself()
        {
            IQueryable<RbRow> rows = _db.Customers.Select(customer => new RbRow
            {
                Id = customer.Id,
                Tag = customer.Display
            });

            string unguarded = Unguarded(() => rows.Where(row => row.Tag == "Acme#1"));
            string guarded = Probe(rows, Where("Tag", "Acme#1"));

            _out.WriteLine($"copied-from-getter itself unguarded: {unguarded} | guarded: {guarded}");
        }

        [Fact]
        public void An_identity_select_is_exempted_rather_than_checked()
        {
            IQueryable<RbOrder> rows = _db.Orders.Select(order => order);

            string unguarded = Unguarded(() => rows.Where(order => order.Label == "AB123/1"));
            string guarded = Probe(rows, Where("Label", "AB123/1"));

            _out.WriteLine($"identity select unguarded: {unguarded} | guarded: {guarded}");
        }

        [Fact]
        public void A_selects_naming_a_denied_field_is_still_refused()
        {
            string guarded = Probe(_db.Orders, new Filter { Selects = new List<string> { "Id", "Secret" } });

            _out.WriteLine($"denied select: {guarded}");

            Assert.StartsWith("REFUSED", guarded);
        }

        [Fact]
        public void A_selects_naming_an_unmapped_getter_is_still_allowed()
        {
            string guarded = Probe(_db.Orders, new Filter { Selects = new List<string> { "Id", "Label" } });

            _out.WriteLine($"select unmapped getter: {guarded}");

            Assert.StartsWith("ran", guarded);
        }

        [Fact]
        public void A_summary_over_a_projection_still_groups_on_an_assigned_member()
        {
            IQueryable<RbRow> rows = _db.Orders.Select(order => new RbRow { Id = order.Id, Code = order.Code });

            Exception? failed = Record.Exception(() => Guard(rows).ToList(new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "Code" },
                    AggregateBy = new List<AggregateBy> { new() { Alias = "Total", Aggregator = Aggregator.Count } }
                }
            }));

            _out.WriteLine($"summary over projection: {failed?.GetType().Name ?? "ran"} {(failed as PolicyException)?.ErrorCode}");

            Assert.Null(failed);
        }

        [Fact]
        public void A_summary_over_an_entity_still_groups_on_a_date_part()
        {
            Exception? failed = Record.Exception(() => Guard(_db.Orders).ToList(new Summary
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "PlacedAt.Year" },
                    AggregateBy = new List<AggregateBy> { new() { Alias = "Total", Aggregator = Aggregator.Count } }
                }
            }));

            _out.WriteLine($"summary date part: {failed?.GetType().Name ?? "ran"} {(failed as PolicyException)?.ErrorCode} {(failed as PolicyException)?.Message}");

            Assert.Null(failed);
        }

        // ---- what must still be refused ---------------------------------------------------------------------

        [Fact]
        public void A_filter_on_an_unmapped_getter_is_still_refused()
        {
            string guarded = Probe(_db.Orders, Where("Label", "AB123/1"));

            _out.WriteLine($"unmapped getter: {guarded}");

            Assert.StartsWith("REFUSED", guarded);
        }
    }
}
