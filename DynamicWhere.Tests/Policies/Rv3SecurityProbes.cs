using System.Linq.Expressions;
using System.Reflection;
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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Query.Internal;
using Xunit.Abstractions;

#pragma warning disable EF1001 // the probe is about EF Core's own provider type, which is internal by design

namespace DynamicWhere.Tests.Policies
{
    // ==== the model =================================================================================

    /// <summary>Two columns and a getter over them: a member no relational database can compute.</summary>
    public class Rv3Text
    {
        public string Ar { get; set; } = string.Empty;

        public string En { get; set; } = string.Empty;

        public bool IsEmpty => string.IsNullOrWhiteSpace(Ar) && string.IsNullOrWhiteSpace(En);
    }

    public class Rv3Role
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public Rv3Text Name { get; set; } = new();

        public Rv3Text Other { get; set; } = new();

        /// <summary>Refused for every feature.</summary>
        [DwDenied]
        public string Secret { get; set; } = string.Empty;

        /// <summary>Refused for projection only, so a synthesized projection has to leave it out.</summary>
        [DwNoSelect]
        public string Cost { get; set; } = string.Empty;

        /// <summary>An unmapped getter over two mapped columns.</summary>
        public string Display => $"{Code}:{Id}";
    }

    /// <summary>The row a caller projects before the guard sees it.</summary>
    public class Rv3Row
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public Rv3Text Name { get; set; } = new();

        public Rv3Text Other { get; set; } = new();
    }

    /// <summary>Two members of one type, to see whether one level's member set leaks into another's.</summary>
    public class Rv3Pair
    {
        public int Id { get; set; }

        public Rv3Text A { get; set; } = new();

        public Rv3Text B { get; set; } = new();
    }

    /// <summary>Nests inside itself, for the initializer depth cap.</summary>
    public class Rv3Deep
    {
        public string V { get; set; } = string.Empty;

        public string W { get; set; } = string.Empty;

        public Rv3Deep? Next { get; set; }
    }

    public class Rv3DeepRow
    {
        public int Id { get; set; }

        public Rv3Deep Deep { get; set; } = new();
    }

    public sealed class Rv3Context : DbContext
    {
        private readonly SqliteConnection? _connection;

        public Rv3Context(SqliteConnection connection) => _connection = connection;

        public Rv3Context(DbContextOptions<Rv3Context> options)
            : base(options)
        {
        }

        public DbSet<Rv3Role> Roles => Set<Rv3Role>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            if (_connection is not null)
            {
                options.UseSqlite(_connection);
            }
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            // Owned rather than complex, so the EF Core 6.0.22 floor builds this model too.
            model.Entity<Rv3Role>().OwnsOne(role => role.Name);
            model.Entity<Rv3Role>().OwnsOne(role => role.Other);
            model.Entity<Rv3Role>().Ignore(role => role.Display);
        }
    }

    /// <summary>The same model with one options constructor, which is what pooling requires.</summary>
    public sealed class Rv3PooledContext : DbContext
    {
        public Rv3PooledContext(DbContextOptions<Rv3PooledContext> options)
            : base(options)
        {
        }

        public DbSet<Rv3Role> Roles => Set<Rv3Role>();

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Rv3Role>().OwnsOne(role => role.Name);
            model.Entity<Rv3Role>().OwnsOne(role => role.Other);
            model.Entity<Rv3Role>().Ignore(role => role.Display);
        }
    }

    // ==== providers =================================================================================

    /// <summary>
    /// A provider in front of EF Core's: the shape LinqKit's <c>AsExpandable</c> and
    /// DelegateDecompiler's <c>Decompile</c> have.
    /// </summary>
    public sealed class Rv3Wrapping<T> : IQueryable<T>, IQueryProvider
    {
        private readonly IQueryable<T> _inner;

        public Rv3Wrapping(IQueryable<T> inner)
        {
            _inner = inner;
            Expression = inner.Expression;
        }

        public Type ElementType => typeof(T);

        public Expression Expression { get; }

        public IQueryProvider Provider => this;

        public IEnumerator<T> GetEnumerator() => _inner.Provider.CreateQuery<T>(Expression).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        public IQueryable CreateQuery(Expression expression) => _inner.Provider.CreateQuery(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new Rv3Wrapping<TElement>(_inner.Provider.CreateQuery<TElement>(expression));

        public object? Execute(Expression expression) => _inner.Provider.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => _inner.Provider.Execute<TResult>(expression);
    }

    /// <summary>
    /// A provider that <i>derives</i> from EF Core's rather than wrapping it: the shape a
    /// second-level cache or an expression-rewriting provider takes when it subclasses.
    /// </summary>
    public sealed class Rv3Derived : EntityQueryProvider
    {
        public Rv3Derived(Microsoft.EntityFrameworkCore.Query.Internal.IQueryCompiler compiler)
            : base(compiler)
        {
        }
    }

    /// <summary>Static helpers a projection can call, which no provider translates.</summary>
    public static class Rv3Util
    {
        public static string Tag(string value) => value + "!";
    }

    // ==== the probes ================================================================================

    /// <summary>
    /// Third review round. What the second round's fixes left reachable: the provider test that
    /// decides whether a path is held to EF Core's model, the assignment reader behind it, the
    /// posture comparison, and the refusal surface.
    /// </summary>
    public sealed class Rv3SecurityProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly Rv3Context _db;

        public Rv3SecurityProbes(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new Rv3Context(_connection);
            _db.Database.EnsureCreated();
            _db.Roles.Add(new Rv3Role
            {
                Code = "admin",
                Secret = "S-TOP",
                Cost = "C-9",
                Name = new Rv3Text { Ar = "AR", En = "Admin" },
                Other = new Rv3Text { Ar = "ar2", En = "Other" }
            });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier, bool traceInResult = false)
            where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                traceInResult
                    ? new DwPolicyOptions { Tier = tier, IncludeTraceInResult = true }
                    : new DwPolicyOptions { Tier = tier },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static Filter Where(string field, string value, DataType type = DataType.Text) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition { Field = field, DataType = type, Operator = Operator.Equal, Values = { value } }
                }
            }
        };

        private static Filter WhereEmpty(string field) => Where(field, "false", DataType.Boolean);

        /// <summary>Calls the private provider test directly, so its answer can be read rather than inferred.</summary>
        private static bool EfCoreOwns(IQueryProvider provider) =>
            (bool)typeof(RowShape)
                .GetMethod("EfCoreOwns", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { provider })!;

        /// <summary>Reads a shape's answer for a path directly, rather than inferring it from a refusal.</summary>
        private static bool? Expresses<T>(IQueryable<T> source, string path) where T : class
        {
            object shape = typeof(RowShape)
                .GetMethod("Of", BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(typeof(T))
                .Invoke(null, new object[] { source })!;

            return (bool?)typeof(RowShape)
                .GetMethod("Expresses", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(shape, new object[] { path });
        }

        // ==== A. the provider test ==================================================================

        /// <summary>
        /// EF Core's own provider, and not a provider built by deriving from it: one that derives
        /// rewrites what EF Core cannot translate exactly as one that wraps it does, so the two are
        /// answered for the same way.
        /// </summary>
        [Fact]
        public void A1_The_provider_test_is_EF_Cores_own_type_and_not_a_type_derived_from_it()
        {
            IQueryProvider own = _db.Roles.AsQueryable().Provider;

            object compiler = typeof(EntityQueryProvider)
                .GetField("_queryCompiler", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(own)!;

            Rv3Derived derived = new((Microsoft.EntityFrameworkCore.Query.Internal.IQueryCompiler)compiler);

            _out.WriteLine($"A1 own={own.GetType().Name} owns={EfCoreOwns(own)}");
            _out.WriteLine($"A1 derived={derived.GetType().Name} owns={EfCoreOwns(derived)}");

            // A provider built by deriving from EF Core's rewrites what EF Core cannot translate,
            // exactly as one built by wrapping it does, so the two are answered for the same way.
            // EF Core's own provider derives from object in every version, so nothing real is lost.
            Assert.True(EfCoreOwns(own));
            Assert.False(EfCoreOwns(derived));
        }

        /// <summary>A provider in front of EF Core is not EF Core's, by the same test.</summary>
        [Fact]
        public void A2_The_provider_test_rejects_a_wrapping_provider()
        {
            Assert.False(EfCoreOwns(new Rv3Wrapping<Rv3Role>(_db.Roles).Provider));
            Assert.False(EfCoreOwns(new[] { new Rv3Role() }.AsQueryable().Provider));
        }

        /// <summary>
        /// A raw-SQL root is still EF Core's to translate what is composed on it, so the refusal
        /// stands and the mapped members still answer.
        /// </summary>
        [Fact]
        public void A3_A_raw_sql_root_is_still_held_to_the_model()
        {
            IQueryable<Rv3Role> raw = _db.Roles.FromSqlRaw("SELECT * FROM Roles");

            Assert.True(EfCoreOwns(raw.Provider));

            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(raw, DwTier.Strict).ToList(WhereEmpty("Name.IsEmpty")));

            _out.WriteLine($"A3 {refusal.ErrorCode}");

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
            Assert.Single(Guard(_db.Roles.FromSqlRaw("SELECT * FROM Roles"), DwTier.Strict)
                .ToList(Where("Code", "admin")).Data);
        }

        /// <summary>
        /// The tracking, split-query and query-filter knobs leave the provider alone, so none of
        /// them can turn the check off.
        /// </summary>
        [Fact]
        public void A4_The_query_knobs_do_not_change_the_provider()
        {
            IQueryable<Rv3Role>[] shapes =
            {
                _db.Roles.AsNoTracking(),
                _db.Roles.AsNoTrackingWithIdentityResolution(),
                _db.Roles.AsSplitQuery(),
                _db.Roles.IgnoreQueryFilters(),
                _db.Roles.AsTracking(),
                _db.Roles.TagWith("probe")
            };

            foreach (IQueryable<Rv3Role> shape in shapes)
            {
                Assert.True(EfCoreOwns(shape.Provider), shape.Expression.ToString());

                PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                    () => Guard(shape, DwTier.Strict).ToList(WhereEmpty("Name.IsEmpty")));

                Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
            }

            _out.WriteLine($"A4 {shapes.Length} shapes, all EF Core's own provider");
        }

        /// <summary>A pooled context hands out the same provider a plain one does.</summary>
        [Fact]
        public void A5_A_pooled_context_is_still_EF_Cores_own_provider()
        {
            DbContextOptions<Rv3PooledContext> options = new DbContextOptionsBuilder<Rv3PooledContext>()
                .UseSqlite(_connection)
                .Options;

            PooledDbContextFactory<Rv3PooledContext> factory = new(options);

            using Rv3PooledContext pooled = factory.CreateDbContext();

            Assert.True(EfCoreOwns(pooled.Roles.AsQueryable().Provider));

            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(pooled.Roles, DwTier.Strict).ToList(WhereEmpty("Name.IsEmpty")));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
            Assert.Single(Guard(pooled.Roles, DwTier.Strict).ToList(Where("Code", "admin")).Data);
        }

        // ==== B. the wrapping exemption cannot weaken any other decision =============================

        /// <summary>A field denied for everything is refused behind a wrapping provider too.</summary>
        [Fact]
        public void B1_A_denied_field_is_still_refused_behind_a_wrapping_provider()
        {
            foreach (DwTier tier in new[] { DwTier.Strict, DwTier.Convenience })
            {
                IQueryable<Rv3Role> wrapped = new Rv3Wrapping<Rv3Role>(_db.Roles);

                PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                    () => Guard(wrapped, tier).ToList(Where("Secret", "S-TOP")));

                _out.WriteLine($"B1 {tier}: {refusal.ErrorCode}");
            }
        }

        /// <summary>So is one named in a projection.</summary>
        [Fact]
        public void B2_A_denied_select_is_still_refused_behind_a_wrapping_provider()
        {
            IQueryable<Rv3Role> wrapped = new Rv3Wrapping<Rv3Role>(_db.Roles);

            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(wrapped, DwTier.Strict).ToList(new Filter
                {
                    Selects = new List<string> { "Id", "Secret" }
                }));

            _out.WriteLine($"B2 {refusal.ErrorCode}");

            Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, refusal.ErrorCode);
        }

        /// <summary>
        /// The projection the library synthesizes for a caller who sent none still withholds the
        /// denied members when the rows arrive through a wrapping provider.
        /// </summary>
        [Fact]
        public void B3_The_synthesized_projection_still_withholds_behind_a_wrapping_provider()
        {
            IQueryable<Rv3Role> wrapped = new Rv3Wrapping<Rv3Role>(_db.Roles);

            FilterResult<Rv3Role> guarded = Guard(wrapped, DwTier.Strict).ToList(new Filter());

            Rv3Role row = Assert.Single(guarded.Data);

            _out.WriteLine($"B3 secret='{row.Secret}' cost='{row.Cost}' code='{row.Code}'");

            Assert.True(string.IsNullOrEmpty(row.Secret), $"Secret leaked: '{row.Secret}'");
            Assert.True(string.IsNullOrEmpty(row.Cost), $"Cost leaked: '{row.Cost}'");
            Assert.Equal("admin", row.Code);
        }

        /// <summary>
        /// Bare and wrapped answer the same query with the same decisions, so the provider test
        /// changes nothing but whether a path is held to EF Core's model.
        /// </summary>
        [Fact]
        public void B4_The_provider_test_changes_only_the_expressible_check()
        {
            FilterResult<Rv3Role> bare = Guard(_db.Roles, DwTier.Strict, traceInResult: true)
                .ToList(Where("Code", "admin"));

            FilterResult<Rv3Role> wrapped = Guard(new Rv3Wrapping<Rv3Role>(_db.Roles), DwTier.Strict, traceInResult: true)
                .ToList(Where("Code", "admin"));

            string Render(FilterResult<Rv3Role> result) => string.Join(
                " | ",
                result.Policy!.Decisions.Select(d => $"{d.FieldPath}/{d.Feature}/{d.Action}/{d.Reason}"));

            _out.WriteLine($"B4 bare:    {Render(bare)}");
            _out.WriteLine($"B4 wrapped: {Render(wrapped)}");

            Assert.Equal(Render(bare), Render(wrapped));
            Assert.True(string.IsNullOrEmpty(Assert.Single(wrapped.Data).Secret));
        }

        // ==== C. the assignment reader ===============================================================

        private IQueryable<Rv3Row> Project(Expression<Func<Rv3Role, Rv3Row>> selector) => _db.Roles.Select(selector);

        private Exception? Filtering(IQueryable<Rv3Row> rows, string field) =>
            Record.Exception(() => Guard(rows, DwTier.Strict).ToList(WhereEmpty(field)));

        /// <summary>A conditional one level down, inside a nested initializer, keeps the refusal.</summary>
        [Fact]
        public void C1_A_conditional_inside_a_nested_initializer_keeps_the_refusal()
        {
            IQueryable<Rv3Row> rows = Project(role => new Rv3Row
            {
                Id = role.Id,
                Name = new Rv3Text
                {
                    Ar = role.Code == null ? role.Name.Ar : role.Name.En,
                    En = role.Name.En
                }
            });

            Exception? raised = Filtering(rows, "Name.IsEmpty");

            _out.WriteLine($"C1 {raised?.GetType().Name}: {raised?.Message}");

            Assert.IsAssignableFrom<PolicyException>(raised);
        }

        /// <summary>A conditional inside a conditional, both arms building in place, keeps it too.</summary>
        [Fact]
        public void C2_A_conditional_nested_in_a_conditional_keeps_the_refusal()
        {
            IQueryable<Rv3Row> rows = Project(role => new Rv3Row
            {
                Id = role.Id,
                Name = role.Code == null
                    ? new Rv3Text()
                    : role.Code == "admin"
                        ? new Rv3Text { Ar = role.Name.Ar }
                        : new Rv3Text { En = role.Name.En }
            });

            Exception? raised = Filtering(rows, "Name.IsEmpty");

            _out.WriteLine($"C2 {raised?.GetType().Name}: {raised?.Message}");

            Assert.IsAssignableFrom<PolicyException>(raised);
        }

        /// <summary>Two arms copying the same member of the entity keep the refusal the entity gives.</summary>
        [Fact]
        public void C3_Two_arms_copying_one_member_keep_the_refusal()
        {
            IQueryable<Rv3Row> rows = Project(role => new Rv3Row
            {
                Id = role.Id,
                Name = role.Code == null ? role.Name : role.Name
            });

            Exception? raised = Filtering(rows, "Name.IsEmpty");

            _out.WriteLine($"C3 {raised?.GetType().Name}: {raised?.Message}");

            Assert.IsAssignableFrom<PolicyException>(raised);
        }

        /// <summary>
        /// Two arms copying two different members: the shape refuses to speak, so the refusal is
        /// lost and EF Core answers with its own failure.
        /// </summary>
        [Fact]
        public void C4_Two_arms_copying_two_members_lose_the_refusal()
        {
            IQueryable<Rv3Row> rows = Project(role => new Rv3Row
            {
                Id = role.Id,
                Name = role.Code == null ? role.Name : role.Other
            });

            Exception? raised = Filtering(rows, "Name.IsEmpty");

            _out.WriteLine($"C4 {raised?.GetType().Name}: {raised?.Message}");

            Assert.False(raised is PolicyException, "refused, so the shape did speak for it");
            Assert.NotNull(raised);
        }

        /// <summary>A coalesce beneath the member leaves the level's own member set intact.</summary>
        [Fact]
        public void C5_A_coalesce_beneath_the_member_keeps_the_refusal()
        {
            IQueryable<Rv3Row> rows = Project(role => new Rv3Row
            {
                Id = role.Id,
                Name = new Rv3Text { Ar = role.Code ?? role.Name.Ar, En = role.Name.En }
            });

            Exception? raised = Filtering(rows, "Name.IsEmpty");

            _out.WriteLine($"C5 {raised?.GetType().Name}: {raised?.Message}");

            Assert.IsAssignableFrom<PolicyException>(raised);
        }

        /// <summary>A conversion around a copied member is stripped, so the copy is still read.</summary>
        [Fact]
        public void C6_A_conversion_around_a_copy_keeps_the_refusal()
        {
            IQueryable<Rv3Row> rows = _db.Roles.Select(role => new Rv3Row
            {
                Id = role.Id,
                Name = (Rv3Text)(object)role.Name
            });

            Exception? raised = Filtering(rows, "Name.IsEmpty");

            _out.WriteLine($"C6 {raised?.GetType().Name}: {raised?.Message}");

            Assert.IsAssignableFrom<PolicyException>(raised);
        }

        /// <summary>A captured value is an assignment the shape cannot read, so it speaks for nothing.</summary>
        [Fact]
        public void C7_A_captured_value_is_left_alone()
        {
            Rv3Text captured = new() { Ar = "x", En = "y" };

            IQueryable<Rv3Row> rows = _db.Roles.Select(role => new Rv3Row { Id = role.Id, Name = captured });

            Exception? raised = Filtering(rows, "Name.IsEmpty");

            _out.WriteLine($"C7 {raised?.GetType().Name ?? "ran"}: {raised?.Message}");

            Assert.False(raised is PolicyException, "refused a member the shape cannot speak for");
        }

        /// <summary>
        /// A member the shape records as assigned, whose value is a call no provider translates. The
        /// level says it produces the member, the database cannot compute it, and the strict tier
        /// answers with neither an answer nor a refusal.
        /// </summary>
        [Fact]
        public void C8_A_method_call_beneath_a_member_is_claimed_and_then_fails()
        {
            IQueryable<Rv3Row> rows = _db.Roles.Select(role => new Rv3Row
            {
                Id = role.Id,
                Name = new Rv3Text { Ar = Rv3Util.Tag(role.Code), En = role.Name.En }
            });

            // Unguarded, the projection itself is answered: EF Core evaluates the last one on the client.
            Assert.Single(rows.ToList());

            Exception? computed = Record.Exception(
                () => Guard(rows, DwTier.Strict).ToList(Where("Name.Ar", "admin!")));

            Exception? unassigned = Filtering(rows, "Name.IsEmpty");

            _out.WriteLine($"C8 filter on the claimed member: {computed?.GetType().Name ?? "ran"}: {computed?.Message}");
            _out.WriteLine($"C8 filter on the unassigned one: {unassigned?.GetType().Name}");

            // The unassigned member is still refused, so the level was read.
            Assert.IsAssignableFrom<PolicyException>(unassigned);

            // The claimed one is not, and it is not answered either.
            Assert.NotNull(computed);
            Assert.False(computed is PolicyException, "the claimed member was refused after all");

            // And the shape says so rather than claiming the member: the initializer assigns it from
            // something the shape cannot read, so the answer for it is "cannot say". Only false is
            // acted on, so this changes no query today; it stops the shape making a claim it cannot
            // support.
            _out.WriteLine($"C8 Expresses(\"Name.Ar\") = {Expresses(rows, "Name.Ar")?.ToString() ?? "null"}");

            Assert.Null(Expresses(rows, "Name.Ar"));
            Assert.False(Expresses(rows, "Name.IsEmpty"));
        }

        /// <summary>A constructor with arguments one level down records nothing, so nothing is refused.</summary>
        [Fact]
        public void C9_A_constructor_with_arguments_one_level_down_is_left_alone()
        {
            IQueryable<Rv3Row> rows = _db.Roles.Select(role => new Rv3Row
            {
                Id = role.Id,
                Name = new Rv3Text(),
                Other = new Rv3Text { Ar = role.Other.Ar, En = role.Other.En }
            });

            // The level that is read still refuses.
            Assert.IsAssignableFrom<PolicyException>(Filtering(rows, "Other.IsEmpty"));

            // An empty new() records "assigns nothing", so every member beneath it is refused.
            Exception? empty = Filtering(rows, "Name.Ar");

            _out.WriteLine($"C9 empty new(): {empty?.GetType().Name}: {empty?.Message}");

            Assert.IsAssignableFrom<PolicyException>(empty);
        }

        /// <summary>A subquery beneath a member is an assignment the shape cannot read.</summary>
        [Fact]
        public void C10_A_subquery_beneath_a_member_is_left_alone()
        {
            IQueryable<Rv3Row> rows = _db.Roles.Select(role => new Rv3Row
            {
                Id = role.Id,
                Name = _db.Roles.Where(other => other.Id == role.Id).Select(other => other.Name).First()
            });

            Exception? raised = Filtering(rows, "Name.IsEmpty");

            _out.WriteLine($"C10 {raised?.GetType().Name ?? "ran"}: {raised?.Message}");

            Assert.False(raised is PolicyException, "refused a member the shape cannot speak for");
        }

        /// <summary>
        /// Past the initializer depth cap the shape stops recording, so it refuses nothing there —
        /// and nothing above the cap is affected.
        /// </summary>
        [Fact]
        public void C11_Past_the_depth_cap_the_shape_stops_speaking()
        {
            IQueryable<Rv3DeepRow> rows = _db.Roles.Select(role => new Rv3DeepRow
            {
                Id = role.Id,
                Deep = new Rv3Deep
                {
                    V = role.Code,
                    Next = new Rv3Deep
                    {
                        V = role.Code,
                        Next = new Rv3Deep
                        {
                            V = role.Code,
                            Next = new Rv3Deep
                            {
                                V = role.Code,
                                Next = new Rv3Deep
                                {
                                    V = role.Code,
                                    Next = new Rv3Deep
                                    {
                                        V = role.Code,
                                        Next = new Rv3Deep
                                        {
                                            V = role.Code,
                                            Next = new Rv3Deep
                                            {
                                                V = role.Code,
                                                Next = new Rv3Deep
                                                {
                                                    V = role.Code,
                                                    Next = new Rv3Deep { V = role.Code }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            });

            // The navigation cap would refuse a long path before the shape ever looked at it.
            PolicyQueryable<Rv3DeepRow> Deep(IQueryable<Rv3DeepRow> source)
            {
                DwPolicyOptions options = new() { Tier = DwTier.Strict };

                options.Caps.MaxNavigationDepth = 24;

                return source.ApplyPolicy(
                    new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                    options,
                    new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));
            }

            Exception? shallow = Record.Exception(() => Deep(rows).ToList(Where("Deep.W", "x")));

            string deep = "Deep." + string.Join(".", Enumerable.Repeat("Next", 9)) + ".W";

            Exception? beyond = Record.Exception(() => Deep(rows).ToList(Where(deep, "x")));

            _out.WriteLine($"C11 shallow unassigned: {shallow?.GetType().Name ?? "ran"}");
            _out.WriteLine($"C11 beyond the cap:     {beyond?.GetType().Name ?? "ran"}: {beyond?.Message}");

            Assert.IsAssignableFrom<PolicyException>(shallow);
            Assert.False(beyond is PolicyException, "the shape spoke past its own cap");
        }

        /// <summary>
        /// Two members built at one level. What one level's initializer assigns must not widen what
        /// another's is read as assigning: <c>Record</c> unions into an existing set, so a set
        /// handed out by reference would make every member of A a member of B.
        /// </summary>
        [Fact]
        public void C12_One_levels_member_set_does_not_widen_another()
        {
            IQueryable<Rv3Pair> rows = _db.Roles.Select(role => new Rv3Pair
            {
                Id = role.Id,
                A = role.Code == null
                    ? new Rv3Text { Ar = role.Name.Ar }
                    : new Rv3Text { En = role.Name.En },
                B = new Rv3Text { Ar = role.Other.Ar }
            });

            // A's two arms union to { Ar, En }, so neither is refused.
            Exception? aAr = Record.Exception(() => Guard(rows, DwTier.Strict).ToList(Where("A.Ar", "AR")));
            Exception? aEn = Record.Exception(() => Guard(rows, DwTier.Strict).ToList(Where("A.En", "Admin")));

            // B assigns Ar alone. If the sets were shared, En would be there too and this would run.
            Exception? bEn = Record.Exception(() => Guard(rows, DwTier.Strict).ToList(Where("B.En", "Other")));

            _out.WriteLine($"C12 A.Ar={aAr?.GetType().Name ?? "ran"} A.En={aEn?.GetType().Name ?? "ran"} "
                           + $"B.En={bEn?.GetType().Name ?? "ran"}");

            Assert.False(aAr is PolicyException, "an arm's own member was refused");
            Assert.False(aEn is PolicyException, "the other arm's member was refused");
            Assert.IsAssignableFrom<PolicyException>(bEn);
        }

        // ==== D. the posture comparison =============================================================

        /// <summary>
        /// Writing the trace flag out as the value the tier already answers is accepted, and the
        /// posture the query path reads is still the first call's instance, unchanged.
        /// </summary>
        [Fact]
        public void D1_Writing_the_trace_flag_the_tier_answers_leaves_the_posture_untouched()
        {
            PolicyBootstrap.Ensure();

            DwPolicyOptions before = DwPolicy.Options;
            bool effectiveBefore = TraceInResult(before);

            DwPolicyOptions copy = CopyOfInForce();

            copy.IncludeTraceInResult = effectiveBefore;

            DwPolicy.Configure(copy);

            _out.WriteLine($"D1 written={copy.IncludeTraceInResult} in-force-written={DwPolicy.Options.IncludeTraceInResult} "
                           + $"effective={TraceInResult(DwPolicy.Options)}");

            Assert.Same(before, DwPolicy.Options);
            Assert.Equal(effectiveBefore, TraceInResult(DwPolicy.Options));

            // The instance the second host built is frozen, so it cannot go on deciding anything.
            Assert.True(copy.IsFrozen);
            Assert.Throws<InvalidOperationException>(() => copy.IncludeTraceInResult = !effectiveBefore);
        }

        /// <summary>
        /// Two postures that differ only in whether the flag is written carry the same answer
        /// everywhere the library reads it, and a guarded query under each carries the trace alike.
        /// </summary>
        [Fact]
        public void D2_Written_and_default_agree_everywhere_the_flag_is_read()
        {
            foreach (DwTier tier in new[] { DwTier.Strict, DwTier.Convenience })
            {
                DwPolicyOptions unwritten = new() { Tier = tier };
                DwPolicyOptions written = new() { Tier = tier, IncludeTraceInResult = TraceInResult(unwritten) };

                Assert.Equal(TraceInResult(unwritten), TraceInResult(written));

                FilterResult<Rv3Role> left = Run(unwritten);
                FilterResult<Rv3Role> right = Run(written);

                _out.WriteLine($"D2 {tier}: effective={TraceInResult(unwritten)} "
                               + $"left={left.Policy is not null} right={right.Policy is not null}");

                Assert.Equal(left.Policy is not null, right.Policy is not null);
                Assert.Equal(TraceInResult(unwritten), left.Policy is not null);
            }

            FilterResult<Rv3Role> Run(DwPolicyOptions options) =>
                _db.Roles.ApplyPolicy(
                        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                        options,
                        new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }))
                    .ToList(Where("Code", "admin"));
        }

        /// <summary>
        /// An accepted second call replaces nothing: the resolver, the store providers and the
        /// posture the query path reads are all the first call's. A second host supplying sources of
        /// the same types therefore hands them over and has them dropped.
        /// </summary>
        [Fact]
        public void D3_An_accepted_second_call_replaces_neither_the_resolver_nor_the_sources()
        {
            PolicyBootstrap.Ensure();

            DwPolicyOptions optionsBefore = DwPolicy.Options;
            PolicyResolver resolverBefore = DwPolicy.Resolver;
            IReadOnlyList<StorePolicyProvider> storesBefore = DwPolicy.StoreProviders;

            DwPolicy.Configure(CopyOfInForce());

            _out.WriteLine($"D3 options same={ReferenceEquals(optionsBefore, DwPolicy.Options)} "
                           + $"resolver same={ReferenceEquals(resolverBefore, DwPolicy.Resolver)} "
                           + $"stores={DwPolicy.StoreProviders.Count}");

            Assert.Same(optionsBefore, DwPolicy.Options);
            Assert.Same(resolverBefore, DwPolicy.Resolver);
            Assert.Same(storesBefore, DwPolicy.StoreProviders);
        }

        private static bool TraceInResult(DwPolicyOptions options) =>
            (bool)typeof(DwPolicyOptions)
                .GetProperty("TraceInResult", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(options)!;

        private static DwPolicyOptions CopyOfInForce()
        {
            DwPolicyOptions inForce = DwPolicy.Options;

            DwPolicyOptions copy = new()
            {
                Tier = inForce.Tier,
                DryRun = inForce.DryRun,
                AuditRefusals = inForce.AuditRefusals,
                StoreFailure = inForce.StoreFailure,
                MaxSnapshotAge = inForce.MaxSnapshotAge,
                RefreshInterval = inForce.RefreshInterval
            };

            if (!string.IsNullOrEmpty(inForce.HashSalt))
            {
                copy.HashSalt = inForce.HashSalt;
            }

            foreach (PropertyInfo cap in typeof(DwCaps)
                         .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(property => property.CanWrite && property.PropertyType == typeof(int)))
            {
                if (cap.Name == nameof(DwCaps.MinGroupSize) && !inForce.Caps.IsMinGroupSizeSet)
                {
                    continue;
                }

                cap.SetValue(copy.Caps, cap.GetValue(inForce.Caps));
            }

            foreach (KeyValuePair<Type, string> exposed in inForce.Entities.Entities)
            {
                copy.Entities.Expose(exposed.Key, exposed.Value);
            }

            return copy;
        }

        // ==== E. the refusal surface ================================================================

        /// <summary>
        /// A name that matches nothing, a field denied for everything, and a member the query cannot
        /// compute must be one refusal, field by field.
        /// </summary>
        [Fact]
        public void E1_The_three_refusals_are_one_refusal()
        {
            string Surface(string field)
            {
                PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                    () => Guard(_db.Roles, DwTier.Strict).ToList(Where(field, "x")));

                string? audit = (string?)typeof(PolicyException)
                    .GetProperty("AuditPath", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(refusal);

                return $"code={refusal.ErrorCode} path='{refusal.FieldPath}' feature={refusal.Feature} "
                       + $"tier={refusal.Tier} rule='{refusal.RuleId}' origin='{refusal.SourceOrigin}' "
                       + $"msg='{refusal.Message}' audit='{audit}'";
            }

            // The three have to be spelled the same length apart so the audit path, which is the
            // caller's own string, is the only thing that may differ.
            string missing = Surface("Nonexistent");
            string denied = Surface("Secret");
            string uncomputable = Surface("Display");

            _out.WriteLine($"E1 missing:      {missing}");
            _out.WriteLine($"E1 denied:       {denied}");
            _out.WriteLine($"E1 uncomputable: {uncomputable}");

            Assert.Equal(
                missing.Replace("Nonexistent", "@"),
                denied.Replace("Secret", "@"));

            Assert.Equal(
                missing.Replace("Nonexistent", "@"),
                uncomputable.Replace("Display", "@"));
        }

        /// <summary>
        /// The canonical path of a member the query cannot compute reaches the in-process trace and
        /// nothing a caller holds: a refused query returns no result to carry it.
        /// </summary>
        [Fact]
        public void E2_The_canonical_path_reaches_the_in_process_trace_only()
        {
            PolicyQueryable<Rv3Role> guarded = Guard(_db.Roles, DwTier.Strict, traceInResult: true);

            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => guarded.ToList(WhereEmpty("Name.IsEmpty")));

            Assert.DoesNotContain("IsEmpty", refusal.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("IsEmpty", refusal.FieldPath, StringComparison.OrdinalIgnoreCase);

            PolicyTrace trace = Assert.IsType<PolicyTrace>(guarded.LastTrace);

            string recorded = string.Join(
                " | ", trace.Decisions.Select(d => $"{d.FieldPath}/{d.Action}/{d.Reason}"));

            _out.WriteLine($"E2 in-process trace: {recorded}");

            Assert.Contains(
                trace.Decisions,
                d => d.Reason is not null && d.Reason.Contains("cannot compute"));
        }

        /// <summary>
        /// An alias hides the canonical path from the caller. The refusal for a member the query
        /// cannot compute must not hand it back.
        /// </summary>
        [Fact]
        public void E3_An_alias_does_not_leak_its_target_through_the_new_refusal()
        {
            PolicyException refusal = Assert.ThrowsAny<PolicyException>(
                () => Guard(_db.Roles, DwTier.Strict).ToList(WhereEmpty("Name.IsEmpty")));

            _out.WriteLine($"E3 path='{refusal.FieldPath}' rule='{refusal.RuleId}' origin='{refusal.SourceOrigin}'");

            Assert.DoesNotContain("Name", refusal.FieldPath, StringComparison.OrdinalIgnoreCase);
            Assert.Null(refusal.RuleId);
            Assert.Null(refusal.SourceOrigin);
        }

        /// <summary>
        /// A caller guessing at the schema one name at a time. Every refusal has to be the same
        /// refusal, whatever the reason: a name that matches nothing, a field denied for everything,
        /// a member the query cannot compute, and a path beneath one.
        /// </summary>
        [Fact]
        public void E4_Guessing_at_the_schema_gets_one_answer_for_every_kind_of_no()
        {
            string Probe(string field)
            {
                try
                {
                    return $"rows={Guard(_db.Roles, DwTier.Strict).ToList(Where(field, "x")).Data.Count}";
                }
                catch (PolicyException refusal)
                {
                    return $"{refusal.ErrorCode}/{refusal.FieldPath}/{refusal.Feature}/"
                           + $"{refusal.RuleId}/{refusal.SourceOrigin}/{refusal.Message}";
                }
            }

            string[] refused = { "Nonexistent", "Secret", "Display", "Name.IsEmpty", "Other.IsEmpty" };
            string[] answered = { "Code", "Cost", "Name.En" };

            List<string> outcomes = refused.Select(Probe).ToList();

            foreach (string line in refused.Zip(outcomes, (name, outcome) => $"{name} -> {outcome}"))
            {
                _out.WriteLine($"E4 {line}");
            }

            Assert.Single(outcomes.Distinct());

            foreach (string field in answered)
            {
                string outcome = Probe(field);

                _out.WriteLine($"E4 {field} -> {outcome}");

                Assert.StartsWith("rows=", outcome);
            }
        }

        // ==== F. the composed handle, where a projection is no longer last ===========================

        /// <summary>
        /// The projection exemption rests on "EF Core evaluates the last projection on the client".
        /// The composable handle lets a caller put an order, a page and a filter after it.
        /// </summary>
        [Fact]
        public void F1_A_projection_the_caller_composes_past_is_still_answered_or_refused()
        {
            foreach (string field in new[] { "Display" })
            {
                PolicyQueryable<Rv3Role> projected =
                    Guard(_db.Roles, DwTier.Strict).Select(new List<string> { "Id", field });

                Exception? ordered = Record.Exception(
                    () => projected.Order(new OrderBy { Field = "Id", Direction = Direction.Ascending })
                        .ToList(new Filter()));

                Exception? paged = Record.Exception(
                    () => Guard(_db.Roles, DwTier.Strict).Select(new List<string> { "Id", field })
                        .Page(new PageBy { PageNumber = 1, PageSize = 10 })
                        .ToList(new Filter()));

                _out.WriteLine($"F1 {field} order-after-select: {ordered?.GetType().Name ?? "ran"}: {ordered?.Message}");
                _out.WriteLine($"F1 {field} page-after-select:  {paged?.GetType().Name ?? "ran"}: {paged?.Message}");

                Assert.True(ordered is null or PolicyException, $"{ordered?.GetType().Name}: {ordered?.Message}");
                Assert.True(paged is null or PolicyException, $"{paged?.GetType().Name}: {paged?.Message}");
            }
        }
    }
}

#pragma warning restore EF1001
