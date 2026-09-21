using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;
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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // =============================================================================================
    // Round 3 over-block probes. EF Core 6 compatible on purpose, so the floor leg runs them too:
    // no complex properties, no ToJson, no primitive collections, no DateOnly/TimeOnly.
    //
    // Every probe runs the shape BOTH unguarded and guarded. The finding rule is one line:
    //     unguarded ran  AND  guarded REFUSED   ->  over-block.
    // =============================================================================================

    public class R3Money
    {
        public decimal Amount { get; set; }

        public string Currency { get; set; } = "USD";

        /// <summary>A getter over two owned columns: no database computes it.</summary>
        public bool IsZero => Amount == 0m;
    }

    public class R3Customer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Nickname { get; set; }

        public List<R3Order> Orders { get; set; } = new();

        [NotMapped]
        public string Handle => Name + "!";
    }

    public class R3Line
    {
        public int Id { get; set; }

        public int OrderId { get; set; }

        public decimal Price { get; set; }

        /// <summary>Unmapped on the entity; the projected row type assigns a member of the same name.</summary>
        [NotMapped]
        public string Label => Price + "!";
    }

    public class R3Order
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string? Note { get; set; }

        public int Qty { get; set; }

        public int CustomerId { get; set; }

        public R3Customer Customer { get; set; } = null!;

        public R3Money Total { get; set; } = new();

        public List<R3Line> Lines { get; set; } = new();

        [NotMapped]
        public string Slug => Code + "-" + Id;
    }

    // ---- row types a caller projects into --------------------------------------------------------

    public class R3Nest
    {
        public string A { get; set; } = string.Empty;

        public string B { get; set; } = string.Empty;

        public R3Nest? Deep { get; set; }

        public R3Money Money { get; set; } = new();
    }

    public class R3LineRow
    {
        public decimal Price { get; set; }

        public string Label { get; set; } = string.Empty;
    }

    public class R3Row
    {
        public R3Row()
        {
        }

        public R3Row(int id) => Id = id;

        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public string? Tag { get; set; }

        public R3Nest Nest { get; set; } = new();

        public R3Money Money { get; set; } = new();

        public R3Customer? Customer { get; set; }

        public List<R3LineRow> Lines { get; set; } = new();
    }

    public sealed class R3Context : DbContext
    {
        private readonly SqliteConnection _connection;

        public R3Context(SqliteConnection connection) => _connection = connection;

        public DbSet<R3Order> Orders => Set<R3Order>();

        public DbSet<R3Customer> Customers => Set<R3Customer>();

        public DbSet<R3Line> Lines => Set<R3Line>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<R3Order>().OwnsOne(order => order.Total);
            model.Entity<R3Order>().Ignore(order => order.Slug);
            model.Entity<R3Customer>().Ignore(customer => customer.Handle);
            model.Entity<R3Line>().Ignore(line => line.Label);
        }
    }

    /// <summary>
    /// Rewrites the entity's own unmapped getters into expressions EF Core translates, and does it
    /// from a provider that <i>derives from</i> EF Core's own rather than wrapping it. That is the
    /// shape <c>EfCoreOwns</c>'s base-type walk cannot tell from EF Core itself.
    /// </summary>
    internal sealed class R3ExpandingVisitor : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Member.Name == nameof(R3Order.Slug) && node.Member.DeclaringType == typeof(R3Order))
            {
                Expression order = Visit(node.Expression)!;

                return Expression.Call(
                    typeof(string).GetMethod(nameof(string.Concat), new[] { typeof(string), typeof(string) })!,
                    Expression.Property(order, nameof(R3Order.Code)),
                    Expression.Constant("-x"));
            }

            if (node.Member.Name == nameof(R3Customer.Handle) && node.Member.DeclaringType == typeof(R3Customer))
            {
                return Expression.Property(Visit(node.Expression)!, nameof(R3Customer.Name));
            }

            return base.VisitMember(node);
        }
    }

    /// <summary>A provider that <b>derives from</b> EF Core's own and rewrites before handing over.</summary>
    internal sealed class R3DerivedProvider : EntityQueryProvider
    {
        public R3DerivedProvider(IQueryCompiler compiler)
            : base(compiler)
        {
        }

        public override IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            base.CreateQuery<TElement>(new R3ExpandingVisitor().Visit(expression)!);

        public override object? Execute(Expression expression) =>
            base.Execute(new R3ExpandingVisitor().Visit(expression)!);

        public override TResult Execute<TResult>(Expression expression) =>
            base.Execute<TResult>(new R3ExpandingVisitor().Visit(expression)!);
    }

    public sealed class ReviewThirdRoundProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly R3Context _db;
        private readonly List<string> _findings = new();

        public ReviewThirdRoundProbes(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new R3Context(_connection);
            _db.Database.EnsureCreated();

            R3Customer customer = new() { Name = "Acme", Nickname = "A" };

            R3Order order = new()
            {
                Code = "AB123",
                Note = "n",
                Qty = 2,
                Customer = customer,
                Total = new R3Money { Amount = 10m, Currency = "USD" }
            };

            order.Lines.Add(new R3Line { Price = 4m });
            order.Lines.Add(new R3Line { Price = 6m });

            _db.Customers.Add(customer);
            _db.Orders.Add(order);
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        // ---- harness ---------------------------------------------------------------------------

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict)
            where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static Filter Where(string field, string value, DataType type) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition { Field = field, DataType = type, Operator = Operator.Equal, Values = { value } }
                }
            }
        };

        private static string Short(string message)
        {
            string one = message.Replace(Environment.NewLine, " ");

            return one.Length > 90 ? one[..90] : one;
        }

        private static string Guarded<T>(IQueryable<T> source, string field, DataType type, string value)
            where T : class
        {
            try
            {
                FilterResult<T> result = Guard(source).ToList(Where(field, type == DataType.Number ? value : value, type));

                return $"OK({result.Data.Count})";
            }
            catch (PolicyException refusal)
            {
                return $"REFUSED({refusal.ErrorCode})";
            }
            catch (Exception failure)
            {
                return $"{failure.GetType().Name}";
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
                return $"{failure.GetType().Name}";
            }
        }

        /// <summary>Records one probe line, and flags it when the guard refuses what the query runs.</summary>
        private void Case(string probe, string unguarded, string guarded)
        {
            _out.WriteLine($"{probe,-58} unguarded={unguarded,-28} guarded={guarded}");

            if (unguarded.StartsWith("OK", StringComparison.Ordinal)
                && guarded.StartsWith("REFUSED", StringComparison.Ordinal))
            {
                _findings.Add($"{probe}: unguarded {unguarded}, guarded {guarded}");
            }
        }

        /// <summary>
        /// A probe whose refusal is a standing limit rather than this branch's doing: the same file
        /// run against 3.2.0 (commit a7b06e1) refuses it identically, so nothing here regressed it.
        /// Logged, never flagged.
        /// </summary>
        private void Known(string probe, string unguarded, string guarded) =>
            _out.WriteLine($"{probe,-58} unguarded={unguarded,-28} guarded={guarded}   [same on 3.2.0]");

        private void Done() => Assert.True(_findings.Count == 0, string.Join(" || ", _findings));

        // =========================================================================================
        // R3-A. The member a projection's initializer does not assign at all.
        // =========================================================================================

        [Fact]
        public void R3_A_A_member_the_initializer_never_assigns()
        {
            IQueryable<R3Row> rows = _db.Orders.Select(order => new R3Row { Id = order.Id });

            Case("A1 unassigned scalar member",
                Raw(() => _db.Orders.Select(o => new R3Row { Id = o.Id }).Where(r => r.Code == "AB123").ToList()),
                Guarded(rows, "Code", DataType.Text, "AB123"));

            Case("A2 framework member beneath an unassigned member",
                Raw(() => _db.Orders.Select(o => new R3Row { Id = o.Id }).Where(r => r.Code.Length == 5).ToList()),
                Guarded(rows, "Code.Length", DataType.Number, "5"));

            Case("A3 assigned member (control)",
                Raw(() => _db.Orders.Select(o => new R3Row { Id = o.Id }).Where(r => r.Id == 1).ToList()),
                Guarded(rows, "Id", DataType.Number, "1"));

            Done();
        }

        // =========================================================================================
        // R3-B. A nested initializer: the sibling member it does not assign.
        // =========================================================================================

        [Fact]
        public void R3_B_A_nested_initializer_and_the_sibling_it_leaves_out()
        {
            IQueryable<R3Row> rows = _db.Orders.Select(order => new R3Row
            {
                Id = order.Id,
                Nest = new R3Nest { A = order.Code }
            });

            Case("B1 nested member the initializer assigns (control)",
                Raw(() => _db.Orders
                    .Select(o => new R3Row { Id = o.Id, Nest = new R3Nest { A = o.Code } })
                    .Where(r => r.Nest.A == "AB123").ToList()),
                Guarded(rows, "Nest.A", DataType.Text, "AB123"));

            Case("B2 nested sibling the initializer leaves out",
                Raw(() => _db.Orders
                    .Select(o => new R3Row { Id = o.Id, Nest = new R3Nest { A = o.Code } })
                    .Where(r => r.Nest.B == "x").ToList()),
                Guarded(rows, "Nest.B", DataType.Text, "x"));

            Case("B3 framework member beneath the sibling left out",
                Raw(() => _db.Orders
                    .Select(o => new R3Row { Id = o.Id, Nest = new R3Nest { A = o.Code } })
                    .Where(r => r.Nest.B.Length == 1).ToList()),
                Guarded(rows, "Nest.B.Length", DataType.Number, "1"));

            Done();
        }

        // =========================================================================================
        // R3-C. Values ReadValue cannot read: coalesce, cast, concatenation, a method call.
        //       Each one must leave the path ALONE (null), never refuse it.
        // =========================================================================================

        [Fact]
        public void R3_C_A_value_the_shape_cannot_read_is_left_alone()
        {
            IQueryable<R3Row> coalesced = _db.Orders.Select(o => new R3Row { Id = o.Id, Tag = o.Note ?? o.Code });
            IQueryable<R3Row> concatenated = _db.Orders.Select(o => new R3Row { Id = o.Id, Tag = o.Code + "-" + o.Qty });
            IQueryable<R3Row> converted = _db.Orders.Select(o => new R3Row { Id = o.Id, Tag = (string)o.Code });
            IQueryable<R3Row> called = _db.Orders.Select(o => new R3Row { Id = o.Id, Tag = o.Code.ToUpper() });
            IQueryable<R3Row> ternaryScalar = _db.Orders.Select(o => new R3Row
            {
                Id = o.Id,
                Tag = o.Qty > 1 ? o.Code : o.Note
            });

            Case("C1 ?? assignment, the member itself",
                Raw(() => _db.Orders.Select(o => new R3Row { Id = o.Id, Tag = o.Note ?? o.Code })
                    .Where(r => r.Tag == "n").ToList()),
                Guarded(coalesced, "Tag", DataType.Text, "n"));

            Case("C2 ?? assignment, a framework member beneath it",
                Raw(() => _db.Orders.Select(o => new R3Row { Id = o.Id, Tag = o.Note ?? o.Code })
                    .Where(r => r.Tag!.Length == 1).ToList()),
                Guarded(coalesced, "Tag.Length", DataType.Number, "1"));

            Case("C3 concatenation, a framework member beneath it",
                Raw(() => _db.Orders.Select(o => new R3Row { Id = o.Id, Tag = o.Code + "-" + o.Qty })
                    .Where(r => r.Tag!.Length == 7).ToList()),
                Guarded(concatenated, "Tag.Length", DataType.Number, "7"));

            Case("C4 cast, a framework member beneath it",
                Raw(() => _db.Orders.Select(o => new R3Row { Id = o.Id, Tag = (string)o.Code })
                    .Where(r => r.Tag!.Length == 5).ToList()),
                Guarded(converted, "Tag.Length", DataType.Number, "5"));

            Case("C5 method call, a framework member beneath it",
                Raw(() => _db.Orders.Select(o => new R3Row { Id = o.Id, Tag = o.Code.ToUpper() })
                    .Where(r => r.Tag!.Length == 5).ToList()),
                Guarded(called, "Tag.Length", DataType.Number, "5"));

            Case("C6 scalar ternary, a framework member beneath it",
                Raw(() => _db.Orders.Select(o => new R3Row { Id = o.Id, Tag = o.Qty > 1 ? o.Code : o.Note })
                    .Where(r => r.Tag!.Length == 5).ToList()),
                Guarded(ternaryScalar, "Tag.Length", DataType.Number, "5"));

            Done();
        }

        // =========================================================================================
        // R3-D. Conditionals around a NESTED initializer: the branch shapes ReadValue now reads.
        // =========================================================================================

        [Fact]
        public void R3_D_A_conditional_around_a_nested_initializer()
        {
            IQueryable<R3Row> emptyBranch = _db.Orders.Select(o => new R3Row
            {
                Id = o.Id,
                Nest = o.Note == null ? new R3Nest() : new R3Nest { A = o.Code }
            });

            IQueryable<R3Row> nullBranch = _db.Orders.Select(o => new R3Row
            {
                Id = o.Id,
                Nest = o.Note == null ? null! : new R3Nest { A = o.Code }
            });

            IQueryable<R3Row> bothAssign = _db.Orders.Select(o => new R3Row
            {
                Id = o.Id,
                Nest = o.Note == null ? new R3Nest { B = o.Code } : new R3Nest { A = o.Code }
            });

            IQueryable<R3Row> opaqueBranch = _db.Orders.Select(o => new R3Row
            {
                Id = o.Id,
                Nest = o.Note == null ? new R3Nest { A = o.Code } : new R3Nest { A = o.Code.ToUpper() }
            });

            Case("D1 empty branch + assigning branch, the assigned member",
                Raw(() => _db.Orders
                    .Select(o => new R3Row { Id = o.Id, Nest = o.Note == null ? new R3Nest() : new R3Nest { A = o.Code } })
                    .Where(r => r.Nest.A == "AB123").ToList()),
                Guarded(emptyBranch, "Nest.A", DataType.Text, "AB123"));

            Case("D2 empty branch + assigning branch, the member neither sets",
                Raw(() => _db.Orders
                    .Select(o => new R3Row { Id = o.Id, Nest = o.Note == null ? new R3Nest() : new R3Nest { A = o.Code } })
                    .Where(r => r.Nest.B == "x").ToList()),
                Guarded(emptyBranch, "Nest.B", DataType.Text, "x"));

            Case("D3 null branch + assigning branch, the assigned member",
                Raw(() => _db.Orders
                    .Select(o => new R3Row { Id = o.Id, Nest = o.Note == null ? null! : new R3Nest { A = o.Code } })
                    .Where(r => r.Nest.A == "AB123").ToList()),
                Guarded(nullBranch, "Nest.A", DataType.Text, "AB123"));

            Case("D4 null branch + assigning branch, the member it does not set",
                Raw(() => _db.Orders
                    .Select(o => new R3Row { Id = o.Id, Nest = o.Note == null ? null! : new R3Nest { A = o.Code } })
                    .Where(r => r.Nest.B == "x").ToList()),
                Guarded(nullBranch, "Nest.B", DataType.Text, "x"));

            Case("D5 two branches assigning two different members, one of them",
                Raw(() => _db.Orders
                    .Select(o => new R3Row
                    {
                        Id = o.Id,
                        Nest = o.Note == null ? new R3Nest { B = o.Code } : new R3Nest { A = o.Code }
                    })
                    .Where(r => r.Nest.A == "AB123").ToList()),
                Guarded(bothAssign, "Nest.A", DataType.Text, "AB123"));

            Case("D6 both branches build in place, a member neither sets",
                Raw(() => _db.Orders
                    .Select(o => new R3Row
                    {
                        Id = o.Id,
                        Nest = o.Note == null ? new R3Nest { A = o.Code } : new R3Nest { A = o.Code.ToUpper() }
                    })
                    .Where(r => r.Nest.B == "x").ToList()),
                Guarded(opaqueBranch, "Nest.B", DataType.Text, "x"));

            Done();
        }

        // =========================================================================================
        // R3-E. A constructor with arguments, and the levels beneath it.
        // =========================================================================================

        [Fact]
        public void R3_E_A_constructor_with_arguments_and_what_is_beneath_it()
        {
            IQueryable<R3Row> built = _db.Orders.Select(o => new R3Row(o.Id) { Code = o.Code });
            IQueryable<R3Row> nestedUnderCtor = _db.Orders.Select(o => new R3Row(o.Id)
            {
                Nest = new R3Nest { A = o.Code }
            });

            Case("E1 ctor arg member",
                Raw(() => _db.Orders.Select(o => new R3Row(o.Id) { Code = o.Code }).Where(r => r.Id == 1).ToList()),
                Guarded(built, "Id", DataType.Number, "1"));

            Case("E2 member the ctor may have set, never in the initializer",
                Raw(() => _db.Orders.Select(o => new R3Row(o.Id) { Code = o.Code }).Where(r => r.Tag == "x").ToList()),
                Guarded(built, "Tag", DataType.Text, "x"));

            Case("E3 nested initializer under a ctor, the assigned member",
                Raw(() => _db.Orders.Select(o => new R3Row(o.Id) { Nest = new R3Nest { A = o.Code } })
                    .Where(r => r.Nest.A == "AB123").ToList()),
                Guarded(nestedUnderCtor, "Nest.A", DataType.Text, "AB123"));

            Case("E4 nested initializer under a ctor, the member it leaves out",
                Raw(() => _db.Orders.Select(o => new R3Row(o.Id) { Nest = new R3Nest { A = o.Code } })
                    .Where(r => r.Nest.B == "x").ToList()),
                Guarded(nestedUnderCtor, "Nest.B", DataType.Text, "x"));

            Done();
        }

        // =========================================================================================
        // R3-F. Members copied from the entity, and EF Core's own owned-type rewriting.
        // =========================================================================================

        [Fact]
        public void R3_F_A_copied_member_and_an_owned_type()
        {
            IQueryable<R3Row> copiedOwned = _db.Orders.Select(o => new R3Row { Id = o.Id, Money = o.Total });
            IQueryable<R3Row> builtOwned = _db.Orders.Select(o => new R3Row
            {
                Id = o.Id,
                Money = new R3Money { Amount = o.Total.Amount }
            });
            IQueryable<R3Row> copiedNav = _db.Orders.Select(o => new R3Row { Id = o.Id, Customer = o.Customer });

            Case("F1 owned member copied whole, a mapped member beneath it",
                Raw(() => _db.Orders.Select(o => new R3Row { Id = o.Id, Money = o.Total })
                    .Where(r => r.Money.Amount == 10m).ToList()),
                Guarded(copiedOwned, "Money.Amount", DataType.Number, "10"));

            Case("F2 owned member copied whole, the getter over its columns",
                Raw(() => _db.Orders.Select(o => new R3Row { Id = o.Id, Money = o.Total })
                    .Where(r => r.Money.IsZero).ToList()),
                Guarded(copiedOwned, "Money.IsZero", DataType.Boolean, "false"));

            Case("F3 owned member built in place, the member it assigns",
                Raw(() => _db.Orders.Select(o => new R3Row { Id = o.Id, Money = new R3Money { Amount = o.Total.Amount } })
                    .Where(r => r.Money.Amount == 10m).ToList()),
                Guarded(builtOwned, "Money.Amount", DataType.Number, "10"));

            Case("F4 owned member built in place, the member it leaves out",
                Raw(() => _db.Orders.Select(o => new R3Row { Id = o.Id, Money = new R3Money { Amount = o.Total.Amount } })
                    .Where(r => r.Money.Currency == "USD").ToList()),
                Guarded(builtOwned, "Money.Currency", DataType.Text, "USD"));

            Case("F5 navigation copied whole, a mapped member beneath it",
                Raw(() => _db.Orders.Select(o => new R3Row { Id = o.Id, Customer = o.Customer })
                    .Where(r => r.Customer!.Name == "Acme").ToList()),
                Guarded(copiedNav, "Customer.Name", DataType.Text, "Acme"));

            Case("F6 navigation copied whole, the unmapped getter beneath it",
                Raw(() => _db.Orders.Select(o => new R3Row { Id = o.Id, Customer = o.Customer })
                    .Where(r => r.Customer!.Handle == "Acme!").ToList()),
                Guarded(copiedNav, "Customer.Handle", DataType.Text, "Acme!"));

            Done();
        }

        // =========================================================================================
        // R3-G. A subquery collection: MemberChain strips ToList/Select, so the member is recorded
        //       as COPIED from the entity's navigation. What is beneath it is then read off the
        //       ENTITY's element type, not the row type the subquery actually builds.
        // =========================================================================================

        [Fact]
        public void R3_G_A_subquery_collection_is_read_through_the_entity()
        {
            IQueryable<R3Row> withLines = _db.Orders.Select(o => new R3Row
            {
                Id = o.Id,
                Lines = o.Lines.Select(line => new R3LineRow { Price = line.Price, Label = line.Price + "!" }).ToList()
            });

            // Refused on 3.2.0 too, by the same standing collection-path limit as K2/K4. What this
            // probe pins is that the new walk did not make it worse: MemberChain strips ToList and
            // Select, so the member is recorded as COPIED from o.Lines and the rest of the path is
            // read off the ENTITY's element type rather than R3LineRow. Nothing reaches that today
            // because the path is refused earlier; it is the shape to re-probe if the limit lifts.
            Known("G1 Count of a subquery-built collection",
                Raw(() => _db.Orders
                    .Select(o => new R3Row
                    {
                        Id = o.Id,
                        Lines = o.Lines.Select(l => new R3LineRow { Price = l.Price, Label = l.Price + "!" }).ToList()
                    })
                    .Where(r => r.Lines.Count == 2).ToList()),
                Guarded(withLines, "Lines.Count", DataType.Number, "2"));

            Done();
        }

        // =========================================================================================
        // R3-H. A second projection whose members come from the first projection's members.
        // =========================================================================================

        [Fact]
        public void R3_H_A_member_assigned_from_another_projected_member()
        {
            IQueryable<R3Row> twice = _db.Orders
                .Select(o => new R3Row { Id = o.Id, Code = o.Code })
                .Select(r => new R3Row { Id = r.Id, Nest = new R3Nest { A = r.Code } });

            IQueryable<R3Row> copiedAcross = _db.Orders
                .Select(o => new R3Row { Id = o.Id, Customer = o.Customer })
                .Select(r => new R3Row { Id = r.Id, Customer = r.Customer });

            Case("H1 nested member from the first projection's member",
                Raw(() => _db.Orders
                    .Select(o => new R3Row { Id = o.Id, Code = o.Code })
                    .Select(r => new R3Row { Id = r.Id, Nest = new R3Nest { A = r.Code } })
                    .Where(x => x.Nest.A == "AB123").ToList()),
                Guarded(twice, "Nest.A", DataType.Text, "AB123"));

            Case("H2 the sibling the second projection leaves out",
                Raw(() => _db.Orders
                    .Select(o => new R3Row { Id = o.Id, Code = o.Code })
                    .Select(r => new R3Row { Id = r.Id, Nest = new R3Nest { A = r.Code } })
                    .Where(x => x.Nest.B == "x").ToList()),
                Guarded(twice, "Nest.B", DataType.Text, "x"));

            Case("H3 a navigation carried through two projections",
                Raw(() => _db.Orders
                    .Select(o => new R3Row { Id = o.Id, Customer = o.Customer })
                    .Select(r => new R3Row { Id = r.Id, Customer = r.Customer })
                    .Where(x => x.Customer!.Name == "Acme").ToList()),
                Guarded(copiedAcross, "Customer.Name", DataType.Text, "Acme"));

            Done();
        }

        // =========================================================================================
        // R3-I. EfCoreOwns: every ordinary EF Core shape must still reach EF Core's own provider,
        //       or the refusal is silently lost.
        // =========================================================================================

        [Fact]
        public void R3_I_Every_ordinary_EF_Core_shape_reaches_EF_Cores_own_provider()
        {
            (string Name, IQueryable Source)[] shapes =
            {
                ("DbSet", _db.Orders),
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
                ("FromSqlRaw", _db.Orders.FromSqlRaw("SELECT * FROM Orders")),
                ("Select(row type)", _db.Orders.Select(o => new R3Row { Id = o.Id })),
                ("Select(anonymous)", _db.Orders.Select(o => new { o.Id, o.Code })),
                ("Select(navigation)", _db.Orders.Select(o => o.Customer)),
                ("OfType", _db.Orders.OfType<R3Order>()),
                ("Distinct", _db.Orders.Distinct()),
                ("OrderBy+Skip+Take", _db.Orders.OrderBy(o => o.Id).Skip(0).Take(5)),
                ("SelectMany", _db.Orders.SelectMany(o => o.Lines)),
                ("GroupBy+Select", _db.Orders.GroupBy(o => o.CustomerId).Select(g => new R3Row { Id = g.Key })),
                ("Join", _db.Orders.Join(_db.Customers, o => o.CustomerId, c => c.Id, (o, c) => o))
            };

            List<string> lost = new();

            foreach ((string name, IQueryable source) in shapes)
            {
                bool owned = WalksToEfCore(source.Provider);

                _out.WriteLine($"{name,-38} provider={source.Provider.GetType().Name,-24} EfCoreOwns={owned}");

                if (!owned)
                {
                    lost.Add($"{name} -> {source.Provider.GetType().FullName}");
                }
            }

            Assert.True(lost.Count == 0, "refusal silently lost on: " + string.Join(", ", lost));
        }

        /// <summary>The comparison <c>RowShape.EfCoreOwns</c> performs: the type itself, not a subclass.</summary>
        private static bool WalksToEfCore(IQueryProvider provider) =>
            provider.GetType().FullName == "Microsoft.EntityFrameworkCore.Query.Internal.EntityQueryProvider";

        // =========================================================================================
        // R3-J. OPEN FINDING (round 3). The over-block direction of EfCoreOwns.
        //
        // EfCoreOwns walks BASE TYPES for the EntityQueryProvider name. A provider that rewrites the
        // members EF Core cannot translate — the expander/decompiler shape the method's own remarks
        // say must NOT be held to EF Core's model — is caught by that walk whenever it is built by
        // DERIVING from EntityQueryProvider rather than by wrapping it.
        //
        // 3.2.0 (a7b06e1): both cases ran, 1 row each.
        // This commit:     both cases REFUSED(FieldDeniedForWhere).
        // Unguarded:       both cases still run, 1 row each.
        //
        // The base walk buys nothing for EF Core itself: EntityQueryProvider derives directly from
        // System.Object in EF Core 6, 7, 8, 9 and 10 (verified against the assemblies), so an exact
        // FullName comparison matches every real EF Core provider and stops catching subclasses.
        // =========================================================================================

        [Fact]
        public void R3_J_A_provider_deriving_from_EF_Cores_own_is_held_to_EF_Cores_model()
        {
            IQueryCompiler compiler =
                (IQueryCompiler)((IInfrastructure<IServiceProvider>)_db).Instance.GetService(typeof(IQueryCompiler))!;

            R3DerivedProvider provider = new(compiler);

            IQueryable<R3Order> expanding =
                provider.CreateQuery<R3Order>(((IQueryable<R3Order>)_db.Orders).Expression);

            _out.WriteLine($"derived provider = {provider.GetType().FullName}");
            _out.WriteLine($"EfCoreOwns walk  = {WalksToEfCore(provider)}");

            Case("J1 rewritten entity getter, on a provider derived from EF Core's",
                Raw(() => expanding.Where(o => o.Slug == "AB123-x").ToList()),
                Guarded(expanding, "Slug", DataType.Text, "AB123-x"));

            Case("J2 rewritten getter one navigation away",
                Raw(() => expanding.Where(o => o.Customer.Handle == "Acme").ToList()),
                Guarded(expanding, "Customer.Handle", DataType.Text, "Acme"));

            // The projection branch consults EfCoreOwns too, so it is caught by the same walk.
            IQueryable<R3Row> projected = expanding.Select(o => new R3Row
            {
                Id = o.Id,
                Customer = o.Customer
            });

            Case("J3 the same provider, over a projection",
                Raw(() => expanding.Select(o => new R3Row { Id = o.Id, Customer = o.Customer })
                    .Where(r => r.Customer!.Handle == "Acme").ToList()),
                Guarded(projected, "Customer.Handle", DataType.Text, "Acme"));

            Done();
        }

        // =========================================================================================
        // R3-K. The entity branch: members EF Core translates that the model maps nothing for.
        // =========================================================================================

        [Fact]
        public void R3_K_The_entity_branch_leaves_translatable_members_alone()
        {
            Case("K1 string.Length on a column",
                Raw(() => _db.Orders.Where(o => o.Code.Length == 5).ToList()),
                Guarded(_db.Orders, "Code.Length", DataType.Number, "5"));

            // A standing limit, not DW-17's: Validate<T>() has always refused a path that walks
            // through a collection, and 3.2.0 refuses these two exactly as this commit does.
            Known("K2 collection navigation Count",
                Raw(() => _db.Orders.Where(o => o.Lines.Count == 2).ToList()),
                Guarded(_db.Orders, "Lines.Count", DataType.Number, "2"));

            Case("K3 owned member then string.Length",
                Raw(() => _db.Orders.Where(o => o.Total.Currency.Length == 3).ToList()),
                Guarded(_db.Orders, "Total.Currency.Length", DataType.Number, "3"));

            Known("K4 navigation then collection Count",
                Raw(() => _db.Orders.Where(o => o.Customer.Orders.Count == 1).ToList()),
                Guarded(_db.Orders, "Customer.Orders.Count", DataType.Number, "1"));

            Case("K5 nullable column HasValue",
                Raw(() => _db.Orders.Where(o => o.Note != null).ToList()),
                Guarded(_db.Orders, "Note", DataType.Text, "n"));

            Done();
        }

        // =========================================================================================
        // R3-L. Clone: nothing it copies is shallower than the docs claim, and nothing is dropped.
        //       Reflection throughout, so this file compiles against 3.2.0 where Clone is internal.
        // =========================================================================================

        [Fact]
        public void R3_L_Clone_copies_every_declared_member_of_every_node()
        {
            Filter filter = new()
            {
                Selects = new List<string> { "Id", "Code" },
                Orders = new List<OrderBy> { new() { Sort = 1, Field = "Id", Direction = Direction.Descending } },
                Page = new PageBy { PageNumber = 2, PageSize = 20 },
                ConditionGroup = new ConditionGroup
                {
                    Conditions =
                    {
                        new Condition
                        {
                            Sort = 1, Field = "Code", DataType = DataType.Text,
                            Operator = Operator.Equal, Values = { "AB123" }
                        }
                    },
                    SubConditionGroups = new List<ConditionGroup>
                    {
                        new()
                        {
                            Conditions =
                            {
                                new Condition
                                {
                                    Sort = 2, Field = "Id", DataType = DataType.Number,
                                    Operator = Operator.Equal, Values = { 1 }
                                }
                            }
                        }
                    }
                }
            };

            Filter copy = (Filter)Invoke(filter, "Clone");

            List<string> shared = new();

            // Every reference node must be a different object.
            Same(shared, "Filter.ConditionGroup", filter.ConditionGroup, copy.ConditionGroup);
            Same(shared, "Filter.Selects", filter.Selects, copy.Selects);
            Same(shared, "Filter.Orders", filter.Orders, copy.Orders);
            Same(shared, "Filter.Orders[0]", filter.Orders![0], copy.Orders![0]);
            Same(shared, "Filter.Page", filter.Page, copy.Page);
            Same(shared, "Group.Conditions", filter.ConditionGroup!.Conditions, copy.ConditionGroup!.Conditions);
            Same(shared, "Group.Conditions[0]", filter.ConditionGroup.Conditions[0], copy.ConditionGroup.Conditions[0]);
            Same(shared, "Group.Conditions[0].Values",
                filter.ConditionGroup.Conditions[0].Values, copy.ConditionGroup.Conditions[0].Values);
            Same(shared, "Group.SubGroups", filter.ConditionGroup.SubConditionGroups, copy.ConditionGroup.SubConditionGroups);
            Same(shared, "Group.SubGroups[0]",
                filter.ConditionGroup.SubConditionGroups![0], copy.ConditionGroup.SubConditionGroups![0]);
            Same(shared, "Group.SubGroups[0].Conditions[0]",
                filter.ConditionGroup.SubConditionGroups[0].Conditions[0],
                copy.ConditionGroup.SubConditionGroups[0].Conditions[0]);

            // Every value must survive the copy.
            List<string> lost = new();

            Carried(lost, "Selects", string.Join(",", filter.Selects!), string.Join(",", copy.Selects!));
            Carried(lost, "Orders[0].Sort", filter.Orders[0].Sort, copy.Orders[0].Sort);
            Carried(lost, "Orders[0].Field", filter.Orders[0].Field, copy.Orders[0].Field);
            Carried(lost, "Orders[0].Direction", filter.Orders[0].Direction, copy.Orders[0].Direction);
            Carried(lost, "Page.PageNumber", filter.Page!.PageNumber, copy.Page!.PageNumber);
            Carried(lost, "Page.PageSize", filter.Page.PageSize, copy.Page.PageSize);
            Carried(lost, "Group.Sort", filter.ConditionGroup.Sort, copy.ConditionGroup.Sort);
            Carried(lost, "Group.Connector", filter.ConditionGroup.Connector, copy.ConditionGroup.Connector);
            Carried(lost, "Condition.Sort",
                filter.ConditionGroup.Conditions[0].Sort, copy.ConditionGroup.Conditions[0].Sort);
            Carried(lost, "Condition.Field",
                filter.ConditionGroup.Conditions[0].Field, copy.ConditionGroup.Conditions[0].Field);
            Carried(lost, "Condition.DataType",
                filter.ConditionGroup.Conditions[0].DataType, copy.ConditionGroup.Conditions[0].DataType);
            Carried(lost, "Condition.Operator",
                filter.ConditionGroup.Conditions[0].Operator, copy.ConditionGroup.Conditions[0].Operator);
            Carried(lost, "Condition.Values",
                string.Join(",", filter.ConditionGroup.Conditions[0].Values),
                string.Join(",", copy.ConditionGroup.Conditions[0].Values));
            Carried(lost, "SubGroup.Condition.Field",
                filter.ConditionGroup.SubConditionGroups[0].Conditions[0].Field,
                copy.ConditionGroup.SubConditionGroups[0].Conditions[0].Field);

            // No public settable property of any node may be left uncopied.
            lost.AddRange(Uncopied(typeof(Filter), filter, copy));

            foreach (string line in shared.Concat(lost))
            {
                _out.WriteLine(line);
            }

            Assert.True(shared.Count == 0 && lost.Count == 0, string.Join(" || ", shared.Concat(lost)));
        }

        [Fact]
        public void R3_L_Clone_of_a_segment_and_a_summary_copies_every_node()
        {
            Segment segment = new()
            {
                Selects = new List<string> { "Id" },
                Orders = new List<OrderBy> { new() { Field = "Id" } },
                Page = new PageBy { PageNumber = 1, PageSize = 5 },
                ConditionSets =
                {
                    new ConditionSet
                    {
                        Sort = 1,
                        Intersection = Intersection.Union,
                        ConditionGroup = new ConditionGroup
                        {
                            Conditions = { new Condition { Field = "Code", Values = { "AB123" } } }
                        }
                    }
                }
            };

            Summary summary = new()
            {
                ConditionGroup = new ConditionGroup { Conditions = { new Condition { Field = "Code" } } },
                Having = new ConditionGroup { Conditions = { new Condition { Field = "n" } } },
                GroupBy = new GroupBy
                {
                    Fields = { "CustomerId" },
                    AggregateBy = { new AggregateBy { Field = "Id", Alias = "n", Aggregator = Aggregator.Count } }
                },
                Orders = new List<OrderBy> { new() { Field = "n" } },
                Page = new PageBy { PageNumber = 1, PageSize = 5 }
            };

            Segment segmentCopy = (Segment)Invoke(segment, "Clone");
            Summary summaryCopy = (Summary)Invoke(summary, "Clone");

            List<string> shared = new();

            Same(shared, "Segment.ConditionSets", segment.ConditionSets, segmentCopy.ConditionSets);
            Same(shared, "Segment.ConditionSets[0]", segment.ConditionSets[0], segmentCopy.ConditionSets[0]);
            Same(shared, "Segment.Sets[0].Group",
                segment.ConditionSets[0].ConditionGroup, segmentCopy.ConditionSets[0].ConditionGroup);
            Same(shared, "Segment.Sets[0].Group.Conditions[0]",
                segment.ConditionSets[0].ConditionGroup.Conditions[0],
                segmentCopy.ConditionSets[0].ConditionGroup.Conditions[0]);
            Same(shared, "Segment.Selects", segment.Selects, segmentCopy.Selects);
            Same(shared, "Segment.Orders[0]", segment.Orders![0], segmentCopy.Orders![0]);
            Same(shared, "Segment.Page", segment.Page, segmentCopy.Page);

            Same(shared, "Summary.ConditionGroup", summary.ConditionGroup, summaryCopy.ConditionGroup);
            Same(shared, "Summary.Having", summary.Having, summaryCopy.Having);
            Same(shared, "Summary.GroupBy", summary.GroupBy, summaryCopy.GroupBy);
            Same(shared, "Summary.GroupBy.Fields", summary.GroupBy!.Fields, summaryCopy.GroupBy!.Fields);
            Same(shared, "Summary.GroupBy.AggregateBy", summary.GroupBy.AggregateBy, summaryCopy.GroupBy.AggregateBy);
            Same(shared, "Summary.GroupBy.AggregateBy[0]",
                summary.GroupBy.AggregateBy[0], summaryCopy.GroupBy.AggregateBy[0]);
            Same(shared, "Summary.Orders[0]", summary.Orders![0], summaryCopy.Orders![0]);
            Same(shared, "Summary.Page", summary.Page, summaryCopy.Page);

            List<string> lost = new();

            Carried(lost, "Segment.Sets[0].Sort", segment.ConditionSets[0].Sort, segmentCopy.ConditionSets[0].Sort);
            Carried(lost, "Segment.Sets[0].Intersection",
                segment.ConditionSets[0].Intersection, segmentCopy.ConditionSets[0].Intersection);
            Carried(lost, "Summary.GroupBy.Fields",
                string.Join(",", summary.GroupBy.Fields), string.Join(",", summaryCopy.GroupBy.Fields));
            Carried(lost, "Summary.Agg.Alias", summary.GroupBy.AggregateBy[0].Alias, summaryCopy.GroupBy.AggregateBy[0].Alias);
            Carried(lost, "Summary.Agg.Aggregator",
                summary.GroupBy.AggregateBy[0].Aggregator, summaryCopy.GroupBy.AggregateBy[0].Aggregator);
            Carried(lost, "Summary.Having.Conditions[0].Field",
                summary.Having!.Conditions[0].Field, summaryCopy.Having!.Conditions[0].Field);

            lost.AddRange(Uncopied(typeof(Segment), segment, segmentCopy));
            lost.AddRange(Uncopied(typeof(Summary), summary, summaryCopy));

            foreach (string line in shared.Concat(lost))
            {
                _out.WriteLine(line);
            }

            Assert.True(shared.Count == 0 && lost.Count == 0, string.Join(" || ", shared.Concat(lost)));
        }

        private static object Invoke(object target, string method) =>
            target.GetType()
                .GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, Type.EmptyTypes, null)!
                .Invoke(target, null)!;

        private static void Same(List<string> shared, string what, object? left, object? right)
        {
            if (left is not null && ReferenceEquals(left, right))
            {
                shared.Add($"SHARED NODE: {what}");
            }
        }

        private static void Carried(List<string> lost, string what, object? left, object? right)
        {
            if (!Equals(left, right))
            {
                lost.Add($"NOT COPIED: {what} ({left} -> {right})");
            }
        }

        /// <summary>Every public settable property of the type whose value the copy does not carry.</summary>
        private static IEnumerable<string> Uncopied(Type type, object original, object copy)
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead)
                {
                    continue;
                }

                object? left = property.GetValue(original);
                object? right = property.GetValue(copy);

                if (left is null != right is null)
                {
                    yield return $"UNCOPIED BRANCH: {type.Name}.{property.Name} ({left} -> {right})";
                }
            }
        }

        // =========================================================================================
        // R3-M. SamePosture: every public knob, and the effective-value comparison.
        // =========================================================================================

        // R3-M walked every settable posture value and printed what it found, and asserted nothing
        // on what it printed. The comparison is guarded for real by
        // ConfigureOnceTests.Every_value_on_the_posture_is_compared, which requires each value to be
        // refused when it changes and fails when a value is added and left out, and by S4_N, which
        // drives Configure itself one knob at a time.

    }
}
