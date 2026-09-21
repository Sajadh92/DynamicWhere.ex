using System.Globalization;
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
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Xunit.Abstractions;

// Round four of the docs-against-code probe for 3.3.0. Every fact here is read by running the
// library, never by reading it: each test prints what happened so the report can quote an outcome.
// Nothing here fixes anything, and nothing here drives DwPolicy's static fields.

namespace DynamicWhere.Tests.Policies
{
    // ---- models -------------------------------------------------------------------------------

    /// <summary>An entity with two owned members of the same type, so a conditional can copy two.</summary>
    public class Rd4Role
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public ZyLocalizedText Name { get; set; } = new();

        public ZyLocalizedText Alt { get; set; } = new();
    }

    /// <summary>The row a caller projects before the guard sees it.</summary>
    public class Rd4Row
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public ZyLocalizedText? Name { get; set; }
    }

    /// <summary>A chain, so an initializer can be nested to any depth the probe asks for.</summary>
    public class Rd4Node
    {
        public string Value { get; set; } = string.Empty;

        public Rd4Node? Next { get; set; }

        /// <summary>A getter over the level's own column: no database computes it.</summary>
        public bool IsEmpty => Value.Length == 0;
    }

    /// <summary>A class stored in one column through a value converter.</summary>
    public class Rd4Money
    {
        public decimal Amount { get; set; }

        public string Currency { get; set; } = "IQD";

        public bool IsZero => Amount == 0m;
    }

    public class Rd4Order
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        /// <summary>One column, converted. Beneath it the converter decides.</summary>
        public Rd4Money Total { get; set; } = new();
    }

    public sealed class Rd4Context : DbContext
    {
        private readonly SqliteConnection _connection;

        public Rd4Context(SqliteConnection connection) => _connection = connection;

        public DbSet<Rd4Role> Roles => Set<Rd4Role>();

        public DbSet<Rd4Order> Orders => Set<Rd4Order>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);

        protected override void OnModelCreating(ModelBuilder model)
        {
            // Owned rather than complex, so the EF Core 6.0.22 floor builds this model too.
            model.Entity<Rd4Role>().OwnsOne(role => role.Name);
            model.Entity<Rd4Role>().OwnsOne(role => role.Alt);

            model.Entity<Rd4Order>().Property(order => order.Total).HasConversion(
                money => money.Amount.ToString(CultureInfo.InvariantCulture),
                text => new Rd4Money { Amount = decimal.Parse(text, CultureInfo.InvariantCulture) });
        }
    }

    // ---- the two providers the corrected paragraph tells apart ----------------------------------

    /// <summary>Rewrites <c>ZyLocalizedText.IsEmpty</c> into the two columns beneath it.</summary>
    /// <remarks>
    /// The rewrite a member-translator plugin or a replaced query preprocessor performs, expressed
    /// where a test can drive it: whatever runs it, the member becomes something SQLite computes.
    /// </remarks>
    internal sealed class Rd4EmptyRewriter : ExpressionVisitor
    {
        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Member.Name == nameof(ZyLocalizedText.IsEmpty)
                && node.Member.DeclaringType == typeof(ZyLocalizedText)
                && node.Expression is not null)
            {
                Expression instance = Visit(node.Expression)!;

                return Expression.AndAlso(
                    Expression.Equal(
                        Expression.Property(instance, nameof(ZyLocalizedText.Ar)),
                        Expression.Constant(string.Empty)),
                    Expression.Equal(
                        Expression.Property(instance, nameof(ZyLocalizedText.En)),
                        Expression.Constant(string.Empty)));
            }

            return base.VisitMember(node);
        }
    }

    /// <summary>
    /// A rewrite <b>inside</b> EF Core's own pipeline: the provider handed to the query is
    /// <c>EntityQueryProvider</c> itself, and the rewrite happens under it.
    /// </summary>
    /// <remarks>
    /// This is what a member-translator plugin and a replaced query preprocessor look like from
    /// outside: EF Core's own provider is still the one on the queryable, so the library's name
    /// comparison still matches and the member is still refused — even though the query now runs.
    /// </remarks>
    internal sealed class Rd4RewritingCompiler : IQueryCompiler
    {
        private readonly IQueryCompiler _inner;

        public Rd4RewritingCompiler(IQueryCompiler inner) => _inner = inner;

        public TResult Execute<TResult>(Expression query) =>
            _inner.Execute<TResult>(new Rd4EmptyRewriter().Visit(query)!);

        public TResult ExecuteAsync<TResult>(Expression query, CancellationToken cancellationToken = default) =>
            _inner.ExecuteAsync<TResult>(new Rd4EmptyRewriter().Visit(query)!, cancellationToken);

        public Func<QueryContext, TResult> CreateCompiledQuery<TResult>(Expression query) =>
            _inner.CreateCompiledQuery<TResult>(new Rd4EmptyRewriter().Visit(query)!);

        public Func<QueryContext, TResult> CreateCompiledAsyncQuery<TResult>(Expression query) =>
            _inner.CreateCompiledAsyncQuery<TResult>(new Rd4EmptyRewriter().Visit(query)!);
    }

    /// <summary>A provider built by <b>deriving from</b> EF Core's own, rewriting the same member.</summary>
    internal sealed class Rd4DerivedProvider : EntityQueryProvider
    {
        public Rd4DerivedProvider(IQueryCompiler compiler) : base(compiler)
        {
        }

        public override IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            base.CreateQuery<TElement>(new Rd4EmptyRewriter().Visit(expression)!);

        public override object? Execute(Expression expression) =>
            base.Execute(new Rd4EmptyRewriter().Visit(expression)!);

        public override TResult Execute<TResult>(Expression expression) =>
            base.Execute<TResult>(new Rd4EmptyRewriter().Visit(expression)!);
    }

    // ---- shared plumbing ------------------------------------------------------------------------

    internal static class Rd4
    {
        private static readonly MethodInfo OfMethod = typeof(RowShape)
            .GetMethod("Of", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

        private static readonly MethodInfo ExpressesMethod = typeof(RowShape)
            .GetMethod("Expresses", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly MethodInfo OwnsMethod = typeof(RowShape)
            .GetMethod("EfCoreOwns", BindingFlags.Static | BindingFlags.NonPublic)!;

        /// <summary>What the shape of a source says about a path: true, false, or "cannot say".</summary>
        internal static bool? Ask(IQueryable source, string path)
        {
            object shape = OfMethod.MakeGenericMethod(source.ElementType).Invoke(null, new object[] { source })!;

            return (bool?)ExpressesMethod.Invoke(shape, new object[] { path });
        }

        internal static bool EfCoreOwns(IQueryProvider provider) =>
            (bool)OwnsMethod.Invoke(null, new object[] { provider })!;

        internal static string Show(bool? answer) => answer switch
        {
            null => "null  (left alone)",
            true => "true  (nothing to refuse)",
            _ => "false (refused)"
        };

        /// <summary>A guarded handle that reads no process-wide state.</summary>
        internal static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict)
            where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        internal static Filter Where(string field, string value, DataType type) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition { Field = field, DataType = type, Operator = Operator.Equal, Values = { value } }
                }
            }
        };

        /// <summary>What a call did: the rows it returned, or the exception it raised.</summary>
        internal static string Outcome(Func<int> call)
        {
            try
            {
                return $"ran, {call()} row(s)";
            }
            catch (PolicyException refusal)
            {
                return $"REFUSED  PolicyException({refusal.Message})";
            }
            catch (LogicException invalid)
            {
                return $"invalid  LogicException({invalid.Message})";
            }
            catch (Exception other)
            {
                return $"threw    {other.GetType().Name}";
            }
        }
    }

    // =============================================================================================
    // 1. Which provider the rule belongs to: EF Core's own TYPE, and a rewrite under it.
    // =============================================================================================

    public sealed class Rd4ProviderProbe : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly Rd4Context _db;
        private readonly IQueryCompiler _compiler;

        public Rd4ProviderProbe(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new Rd4Context(_connection);
            _db.Database.EnsureCreated();
            _db.Roles.Add(new Rd4Role
            {
                Code = "admin",
                Name = new ZyLocalizedText { Ar = "مدير", En = "Admin" },
                Alt = new ZyLocalizedText { Ar = string.Empty, En = string.Empty }
            });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();

            _compiler = (IQueryCompiler)((IInfrastructure<IServiceProvider>)_db).Instance
                .GetService(typeof(IQueryCompiler))!;
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        /// <summary>
        /// "A rewrite <i>inside</i> EF Core's own pipeline — a member-translator plugin, a replaced
        /// query preprocessor — leaves EF Core's own provider in place, so a member it computes
        /// without a mapping is refused with the rest."
        /// </summary>
        [Fact]
        public void A_rewrite_inside_EF_Cores_own_pipeline_leaves_the_provider_in_place_and_is_refused()
        {
            EntityQueryProvider provider = new(new Rd4RewritingCompiler(_compiler));

            IQueryable<Rd4Role> rewritten =
                provider.CreateQuery<Rd4Role>(((IQueryable<Rd4Role>)_db.Roles).Expression);

            _out.WriteLine($"provider type   = {rewritten.Provider.GetType().FullName}");
            _out.WriteLine($"EfCoreOwns      = {Rd4.EfCoreOwns(rewritten.Provider)}");
            _out.WriteLine($"Expresses(\"Name.IsEmpty\") = {Rd4.Show(Rd4.Ask(rewritten, "Name.IsEmpty"))}");

            // The rewrite makes the member something SQLite computes, so the unguarded query runs.
            string unguarded = Rd4.Outcome(() => rewritten.Where(role => role.Name.IsEmpty).ToList().Count);
            string alt = Rd4.Outcome(() => rewritten.Where(role => role.Alt.IsEmpty).ToList().Count);

            string guarded = Rd4.Outcome(
                () => Rd4.Guard(rewritten).ToList(Rd4.Where("Name.IsEmpty", "false", DataType.Boolean)).Data.Count);

            _out.WriteLine($"  unguarded Name.IsEmpty -> {unguarded}");
            _out.WriteLine($"  unguarded Alt.IsEmpty  -> {alt}");
            _out.WriteLine($"  guarded   Name.IsEmpty -> {guarded}");

            Assert.True(Rd4.EfCoreOwns(rewritten.Provider));
            Assert.False(Rd4.Ask(rewritten, "Name.IsEmpty"));
            Assert.StartsWith("ran,", unguarded);
            Assert.StartsWith("REFUSED", guarded);
        }

        /// <summary>
        /// "one built by deriving from EF Core's provider rewrites in the same way and is left alone
        /// too" — over an entity.
        /// </summary>
        [Fact]
        public void A_provider_deriving_from_EF_Cores_own_is_left_alone_over_an_entity()
        {
            Rd4DerivedProvider provider = new(_compiler);

            IQueryable<Rd4Role> derived =
                provider.CreateQuery<Rd4Role>(((IQueryable<Rd4Role>)_db.Roles).Expression);

            _out.WriteLine($"provider type   = {derived.Provider.GetType().FullName}");
            _out.WriteLine($"derives from    = {derived.Provider.GetType().BaseType?.FullName}");
            _out.WriteLine($"EntityQueryProvider itself derives from {typeof(EntityQueryProvider).BaseType?.FullName} "
                           + $"(EF Core {typeof(EntityQueryProvider).Assembly.GetName().Version})");
            _out.WriteLine($"EfCoreOwns      = {Rd4.EfCoreOwns(derived.Provider)}");
            _out.WriteLine($"Expresses(\"Name.IsEmpty\") = {Rd4.Show(Rd4.Ask(derived, "Name.IsEmpty"))}");

            string guarded = Rd4.Outcome(
                () => Rd4.Guard(derived).ToList(Rd4.Where("Name.IsEmpty", "false", DataType.Boolean)).Data.Count);

            _out.WriteLine($"  guarded Name.IsEmpty -> {guarded}");

            Assert.Equal(typeof(EntityQueryProvider), derived.Provider.GetType().BaseType);

            // "EF Core's own derives from object in every version, so nothing real is lost by the
            // exact test" — read from the assembly this leg runs against, both legs.
            Assert.Equal(typeof(object), typeof(EntityQueryProvider).BaseType);

            Assert.False(Rd4.EfCoreOwns(derived.Provider));
            Assert.Null(Rd4.Ask(derived, "Name.IsEmpty"));
            Assert.DoesNotContain("REFUSED", guarded);
        }

        /// <summary>The same sentence, over a projection.</summary>
        [Fact]
        public void A_provider_deriving_from_EF_Cores_own_is_left_alone_over_a_projection()
        {
            Rd4DerivedProvider provider = new(_compiler);

            IQueryable<Rd4Role> derived =
                provider.CreateQuery<Rd4Role>(((IQueryable<Rd4Role>)_db.Roles).Expression);

            IQueryable<Rd4Row> builtOnEfCore = _db.Roles.Select(role => new Rd4Row
            {
                Id = role.Id,
                Code = role.Code,
                Name = new ZyLocalizedText { Ar = role.Name.Ar, En = role.Name.En }
            });

            IQueryable<Rd4Row> builtOnDerived = derived.Select(role => new Rd4Row
            {
                Id = role.Id,
                Code = role.Code,
                Name = new ZyLocalizedText { Ar = role.Name.Ar, En = role.Name.En }
            });

            _out.WriteLine($"EF Core's own provider, projection  -> {Rd4.Show(Rd4.Ask(builtOnEfCore, "Name.IsEmpty"))}");
            _out.WriteLine($"derived provider, same projection   -> {Rd4.Show(Rd4.Ask(builtOnDerived, "Name.IsEmpty"))}");
            _out.WriteLine($"  the derived one's provider = {builtOnDerived.Provider.GetType().FullName}");

            Assert.False(Rd4.Ask(builtOnEfCore, "Name.IsEmpty"));
            Assert.Null(Rd4.Ask(builtOnDerived, "Name.IsEmpty"));
        }

        /// <summary>The wrapping half of the same sentence, over an entity and over a projection.</summary>
        [Fact]
        public void A_provider_wrapping_EF_Core_is_left_alone_over_both()
        {
            IQueryable<Rd4Role> entity = new ZyOwnProvider<Rd4Role>(_db.Roles);

            IQueryable<Rd4Row> projection = new ZyOwnProvider<Rd4Row>(
                _db.Roles.Select(role => new Rd4Row
                {
                    Id = role.Id,
                    Code = role.Code,
                    Name = new ZyLocalizedText { Ar = role.Name.Ar, En = role.Name.En }
                }));

            _out.WriteLine($"wrapper over an entity     -> {Rd4.Show(Rd4.Ask(entity, "Name.IsEmpty"))}");
            _out.WriteLine($"wrapper over a projection  -> {Rd4.Show(Rd4.Ask(projection, "Name.IsEmpty"))}");

            Assert.Null(Rd4.Ask(entity, "Name.IsEmpty"));
            Assert.Null(Rd4.Ask(projection, "Name.IsEmpty"));
        }

        /// <summary>
        /// "<c>[DbFunction]</c> was removed as an example because it maps a method, which no field
        /// path can name." A path is resolved through properties, so a method never reaches the
        /// refusal at all — it fails name resolution first, in both tiers.
        /// </summary>
        [Fact]
        public void No_field_path_can_name_a_method()
        {
            // Three methods on the row's own types: an instance method, a method on a member's type,
            // and a static one of the kind [DbFunction] maps.
            foreach (string path in new[] { "ToString", "Name.ToString", "Code.Trim", "Name.GetHashCode" })
            {
                string strict = Rd4.Outcome(
                    () => Rd4.Guard(_db.Roles).ToList(Rd4.Where(path, "x", DataType.Text)).Data.Count);

                string convenience = Rd4.Outcome(
                    () => Rd4.Guard(_db.Roles, DwTier.Convenience)
                        .ToList(Rd4.Where(path, "x", DataType.Text)).Data.Count);

                _out.WriteLine($"{path,-20} strict -> {strict}");
                _out.WriteLine($"{path,-20} conv.  -> {convenience}");

                // Whatever the tier answers, the answer is a name answer, never the compute refusal:
                // the shape is never even asked, because the path resolves to no member.
                Assert.DoesNotContain("cannot compute", strict);
                Assert.DoesNotContain("cannot compute", convenience);
                Assert.Contains("ConditionMustHasValidFieldName", convenience);
            }

            // And the property beside them, for contrast: that one the shape does answer for.
            _out.WriteLine($"Name.IsEmpty (a property) Expresses -> {Rd4.Show(Rd4.Ask(_db.Roles, "Name.IsEmpty"))}");

            Assert.False(Rd4.Ask(_db.Roles, "Name.IsEmpty"));
        }
    }

    // =============================================================================================
    // 2. How far a projection is read: every assignment kind, and where the reading stops.
    // =============================================================================================

    public sealed class Rd4ProjectionProbe : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly Rd4Context _db;

        private static readonly ZyLocalizedText Captured = new() { Ar = "a", En = "e" };

        public Rd4ProjectionProbe(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new Rd4Context(_connection);
            _db.Database.EnsureCreated();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static ZyLocalizedText Make(string code) => new() { Ar = code, En = code };

        /// <summary>
        /// The two lists the text gives: the assignments a projection is read through, and the ones
        /// that leave the member alone. One row per shape, printed, so the report can quote it.
        /// </summary>
        [Fact]
        public void Every_assignment_kind_the_text_lists()
        {
            List<(string Shape, string Expected, bool? Answer)> rows = new();

            void Row(string shape, string expected, IQueryable<Rd4Row> source) =>
                rows.Add((shape, expected, Rd4.Ask(source, "Name.IsEmpty")));

            Row("a nested initializer", "read",
                _db.Roles.Select(r => new Rd4Row
                {
                    Id = r.Id, Name = new ZyLocalizedText { Ar = r.Name.Ar, En = r.Name.En }
                }));

            Row("a member copied from the entity", "read",
                _db.Roles.Select(r => new Rd4Row { Id = r.Id, Name = r.Name }));

            Row("a value built and left empty", "read",
                _db.Roles.Select(r => new Rd4Row { Id = r.Id, Name = new ZyLocalizedText() }));

            // ROUND-4 FINDING. Every document lists "a null" beside the four forms above, as one of
            // the assignments a projection is read through. On its own it is not: ReadValue answers
            // "I could read that" and records nothing, so the member has no entry and the shape says
            // "cannot say". A null only carries its weight as one branch of a conditional, where the
            // other branch is what records the members. The row below is expected against the code,
            // not against the text, and the text is what the report names.
            Row("a null, on its own", "left alone",
                _db.Roles.Select(r => new Rd4Row { Id = r.Id, Name = null }));

            Row("a conditional: built | built", "read",
                _db.Roles.Select(r => new Rd4Row
                {
                    Id = r.Id,
                    Name = r.Code == "x"
                        ? new ZyLocalizedText()
                        : new ZyLocalizedText { Ar = r.Name.Ar, En = r.Name.En }
                }));

            Row("a conditional: null | built", "read",
                _db.Roles.Select(r => new Rd4Row
                {
                    Id = r.Id,
                    Name = r.Code == "x" ? null : new ZyLocalizedText { Ar = r.Name.Ar, En = r.Name.En }
                }));

            Row("a conditional: copied | copied, one member", "read",
                _db.Roles.Select(r => new Rd4Row
                {
                    Id = r.Id, Name = r.Code == "x" ? r.Name : r.Name
                }));

            Row("a method call", "left alone",
                _db.Roles.Select(r => new Rd4Row { Id = r.Id, Name = Make(r.Code) }));

            Row("a captured value", "left alone",
                _db.Roles.Select(r => new Rd4Row { Id = r.Id, Name = Captured }));

            Row("a subquery", "left alone",
                _db.Roles.Select(r => new Rd4Row
                {
                    Id = r.Id, Name = _db.Roles.Select(other => other.Name).FirstOrDefault()
                }));

            Row("two branches building it two ways", "left alone",
                _db.Roles.Select(r => new Rd4Row
                {
                    Id = r.Id, Name = r.Code == "x" ? r.Name : r.Alt
                }));

            Row("a conditional: built | copied", "left alone",
                _db.Roles.Select(r => new Rd4Row
                {
                    Id = r.Id, Name = r.Code == "x" ? new ZyLocalizedText() : r.Name
                }));

            foreach ((string shape, string expected, bool? answer) in rows)
            {
                _out.WriteLine($"{shape,-45} code says {Rd4.Show(answer)}   (expected {expected})");
            }

            // Said plainly, so the finding is an outcome rather than a reading of the code: the four
            // forms the text lists beside it are read, and a bare null is not.
            _out.WriteLine(string.Empty);
            _out.WriteLine("the text's list: nested initializer, copied member, built-and-left-empty, A NULL, "
                           + "and a conditional over those");
            _out.WriteLine($"  Name = new ZyLocalizedText {{ … }}  -> {Rd4.Show(rows[0].Answer)}");
            _out.WriteLine($"  Name = r.Name                      -> {Rd4.Show(rows[1].Answer)}");
            _out.WriteLine($"  Name = new ZyLocalizedText()       -> {Rd4.Show(rows[2].Answer)}");
            _out.WriteLine($"  Name = null                        -> {Rd4.Show(rows[3].Answer)}  <-- the text says this one is read");
            _out.WriteLine($"  Name = c ? null : new … {{ … }}      -> {Rd4.Show(rows[5].Answer)}");

            Assert.Null(rows[3].Answer);

            // The text's first list: these are the ones a projection is read through.
            foreach ((string shape, string expected, bool? answer) in rows.Where(r => r.Expected == "read"))
            {
                Assert.True(answer == false, $"{shape}: expected refused, got {Rd4.Show(answer)}");
            }

            // The text's second list: these leave the member alone.
            foreach ((string shape, string expected, bool? answer) in rows.Where(r => r.Expected == "left alone"))
            {
                Assert.True(answer is null, $"{shape}: expected left alone, got {Rd4.Show(answer)}");
            }
        }

        /// <summary>
        /// "Past <c>MaxComplexDepth</c> — eight levels — it stops reading and stops speaking."
        /// Nests one initializer inside another and reports the last depth the shape answers for.
        /// </summary>
        [Fact]
        public void The_depth_at_which_a_nested_initializer_stops_being_read()
        {
            int cap = (int)typeof(RowShape)
                .GetField("MaxComplexDepth", BindingFlags.Static | BindingFlags.NonPublic)!
                .GetValue(null)!;

            _out.WriteLine($"RowShape.MaxComplexDepth = {cap}");

            int? lastSpoken = null;
            int? firstSilent = null;

            for (int nesting = 0; nesting <= cap + 3; nesting++)
            {
                IQueryable<Rd4Node> projected = _db.Roles.Select(Chain(nesting));

                string prefix = string.Concat(Enumerable.Repeat("Next.", nesting));

                bool? computable = Rd4.Ask(projected, prefix + "IsEmpty");
                bool? assigned = Rd4.Ask(projected, prefix + "Value");

                _out.WriteLine($"{nesting,2} nested initializer(s): "
                               + $"\"{prefix}Value\" = {Rd4.Show(assigned)}   "
                               + $"\"{prefix}IsEmpty\" = {Rd4.Show(computable)}");

                if (computable == false)
                {
                    lastSpoken = nesting;
                }
                else if (firstSilent is null)
                {
                    firstSilent = nesting;
                }
            }

            _out.WriteLine($"last level the shape refuses at = {lastSpoken}; first level it stops speaking at = {firstSilent}");

            Assert.NotNull(lastSpoken);
            Assert.NotNull(firstSilent);
            Assert.Equal(lastSpoken + 1, firstSilent);
            Assert.Equal(cap, lastSpoken);
        }

        /// <summary>Builds <c>r =&gt; new Rd4Node { Value = r.Code, Next = new Rd4Node { … } }</c>.</summary>
        private static Expression<Func<Rd4Role, Rd4Node>> Chain(int nesting)
        {
            ParameterExpression role = Expression.Parameter(typeof(Rd4Role), "r");
            MemberExpression code = Expression.Property(role, nameof(Rd4Role.Code));

            MemberInitExpression body = Level(code, null);

            for (int i = 0; i < nesting; i++)
            {
                body = Level(code, body);
            }

            return Expression.Lambda<Func<Rd4Role, Rd4Node>>(body, role);
        }

        private static MemberInitExpression Level(Expression value, Expression? next)
        {
            List<MemberBinding> bindings = new()
            {
                Expression.Bind(typeof(Rd4Node).GetProperty(nameof(Rd4Node.Value))!, value)
            };

            if (next is not null)
            {
                bindings.Add(Expression.Bind(typeof(Rd4Node).GetProperty(nameof(Rd4Node.Next))!, next));
            }

            return Expression.MemberInit(Expression.New(typeof(Rd4Node)), bindings);
        }
    }

    // =============================================================================================
    // 3. "Left alone" is not a promise the path runs.
    // =============================================================================================

    public sealed class Rd4LeftAloneProbe : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly Rd4Context _db;

        public Rd4LeftAloneProbe(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new Rd4Context(_connection);
            _db.Database.EnsureCreated();
            _db.Roles.Add(new Rd4Role
            {
                Code = "admin",
                Name = new ZyLocalizedText { Ar = "مدير", En = "Admin" },
                Alt = new ZyLocalizedText()
            });
            _db.Orders.Add(new Rd4Order { Code = "A-1", Total = new Rd4Money { Amount = 12m } });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        /// <summary>"which for rows in memory … means it runs and returns rows".</summary>
        [Fact]
        public void Rows_in_memory_run_and_return_rows()
        {
            List<Rd4Role> rows = new()
            {
                new Rd4Role { Id = 1, Code = "a", Name = new ZyLocalizedText { Ar = "م", En = "A" } },
                new Rd4Role { Id = 2, Code = "b", Name = new ZyLocalizedText() }
            };

            IQueryable<Rd4Role> memory = rows.AsQueryable();

            _out.WriteLine($"Expresses(\"Name.IsEmpty\") = {Rd4.Show(Rd4.Ask(memory, "Name.IsEmpty"))}");

            FilterResult<Rd4Role>? result = null;

            string outcome = Rd4.Outcome(() =>
            {
                result = Rd4.Guard(memory).ToList(Rd4.Where("Name.IsEmpty", "true", DataType.Boolean));

                return result.Data.Count;
            });

            _out.WriteLine($"  guarded Name.IsEmpty -> {outcome}");

            Assert.Null(Rd4.Ask(memory, "Name.IsEmpty"));
            Assert.StartsWith("ran,", outcome);
            Assert.Single(result!.Data);
        }

        /// <summary>"…and a framework member means it runs and returns rows".</summary>
        [Fact]
        public void A_framework_member_runs_and_returns_rows()
        {
            _out.WriteLine($"Expresses(\"Code.Length\") = {Rd4.Show(Rd4.Ask(_db.Roles, "Code.Length"))}");

            string guarded = Rd4.Outcome(
                () => Rd4.Guard(_db.Roles).ToList(Rd4.Where("Code.Length", "5", DataType.Number)).Data.Count);

            string unguarded = Rd4.Outcome(() => _db.Roles.Where(role => role.Code.Length == 5).ToList().Count);

            _out.WriteLine($"  guarded   -> {guarded}");
            _out.WriteLine($"  unguarded -> {unguarded}");

            Assert.Null(Rd4.Ask(_db.Roles, "Code.Length"));
            Assert.StartsWith("ran,", guarded);
            Assert.Equal(unguarded, guarded);
        }

        /// <summary>
        /// "…and beneath a converted column means the provider decides." The path is left alone, and
        /// what happens next is not the policy's answer to give.
        /// </summary>
        [Fact]
        public void Beneath_a_converted_column_the_provider_decides()
        {
            _out.WriteLine("Total is one column, converted to "
                           + $"{_db.Model.FindEntityType(typeof(Rd4Order))!.FindProperty(nameof(Rd4Order.Total))!.GetValueConverter()!.ProviderClrType.Name}");
            _out.WriteLine($"Expresses(\"Total.Amount\") = {Rd4.Show(Rd4.Ask(_db.Orders, "Total.Amount"))}");
            _out.WriteLine($"Expresses(\"Total.IsZero\") = {Rd4.Show(Rd4.Ask(_db.Orders, "Total.IsZero"))}");

            string guardedAmount = Rd4.Outcome(
                () => Rd4.Guard(_db.Orders).ToList(Rd4.Where("Total.Amount", "12", DataType.Number)).Data.Count);

            string unguardedAmount = Rd4.Outcome(
                () => _db.Orders.Where(order => order.Total.Amount == 12m).ToList().Count);

            _out.WriteLine($"  guarded   Total.Amount -> {guardedAmount}");
            _out.WriteLine($"  unguarded Total.Amount -> {unguardedAmount}");

            // Left alone by the policy...
            Assert.Null(Rd4.Ask(_db.Orders, "Total.Amount"));
            Assert.Null(Rd4.Ask(_db.Orders, "Total.IsZero"));

            // ...and the provider still decides, which here is a throw rather than rows.
            Assert.DoesNotContain("REFUSED", guardedAmount);
            Assert.Equal(unguardedAmount, guardedAmount);
            Assert.StartsWith("threw", guardedAmount);
        }
    }

    // =============================================================================================
    // 4. The posture comparison: which values are compared by what applies, and which as written.
    // =============================================================================================

    public sealed class Rd4CapProbe
    {
        private readonly ITestOutputHelper _out;

        public Rd4CapProbe(ITestOutputHelper output) => _out = output;

        private static readonly MethodInfo SamePosture = typeof(DwPolicy)
            .GetMethod("SamePosture", BindingFlags.Static | BindingFlags.NonPublic)!;

        private static bool Compare(DwPolicyOptions inForce, DwPolicyOptions asked) =>
            (bool)SamePosture.Invoke(null, new object?[] { inForce, asked, Array.Empty<IDwPolicyProvider>() })!;

        /// <summary>
        /// Walks every cap: for each, whether two postures differing only in that cap are told apart,
        /// and whether the value the enforcement path applies is the value written down.
        /// </summary>
        [Fact]
        public void Only_the_group_floor_is_compared_by_the_value_that_applies()
        {
            // The floor: unset and written-as-the-default are the same posture.
            DwPolicyOptions floorUnset = new() { Tier = DwTier.Strict };
            DwPolicyOptions floorWritten = new()
            {
                Tier = DwTier.Strict, Caps = { MinGroupSize = DwCaps.DefaultMinGroupSize }
            };

            bool floorSame = Compare(floorUnset, floorWritten);

            _out.WriteLine($"MinGroupSize     unset({floorUnset.Caps.MinGroupSize}) vs written({floorWritten.Caps.MinGroupSize})"
                           + $"  -> same posture = {floorSame}");

            // DefaultPageSize: two postures that page a caller identically, written differently.
            DwPolicyOptions pageLow = new()
            {
                Tier = DwTier.Strict, Caps = { MaxPageSize = 100, DefaultPageSize = 100 }
            };

            DwPolicyOptions pageHigh = new()
            {
                Tier = DwTier.Strict, Caps = { MaxPageSize = 100, DefaultPageSize = 5000 }
            };

            bool pageSame = Compare(pageLow, pageHigh);

            int appliedLow = Math.Min(pageLow.Caps.DefaultPageSize, pageLow.Caps.MaxPageSize);
            int appliedHigh = Math.Min(pageHigh.Caps.DefaultPageSize, pageHigh.Caps.MaxPageSize);

            _out.WriteLine($"DefaultPageSize  written {pageLow.Caps.DefaultPageSize} vs {pageHigh.Caps.DefaultPageSize}, "
                           + $"both applied as {appliedLow}/{appliedHigh}  -> same posture = {pageSame}");

            Assert.True(floorSame, "the group floor is compared by the value that applies");
            Assert.Equal(appliedLow, appliedHigh);
            Assert.False(pageSame, "DefaultPageSize is compared as written, not by the value that applies");
        }

        /// <summary>Proves the same sentence end to end: the sanitizer pages both postures alike.</summary>
        [Fact]
        public void The_two_page_size_postures_page_a_caller_identically()
        {
            List<Rd4Role> rows = Enumerable.Range(1, 250)
                .Select(i => new Rd4Role { Id = i, Code = $"c{i}" })
                .ToList();

            foreach (int written in new[] { 100, 5000 })
            {
                DwPolicyOptions options = new()
                {
                    Tier = DwTier.Convenience, Caps = { MaxPageSize = 100, DefaultPageSize = written }
                };

                FilterResult<Rd4Role> result = rows.AsQueryable()
                    .ApplyPolicy(
                        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                        options,
                        new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }))
                    .ToList(new Filter());

                _out.WriteLine($"DefaultPageSize written as {written,5} -> the caller got {result.Data.Count} row(s)");

                Assert.Equal(100, result.Data.Count);
            }
        }
    }
}
