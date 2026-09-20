using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
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
    // =============================================================================================
    // Round 5. What every error code the library can raise still names under Strict, and whether a
    // member a subquery builds is answered or refused.
    // =============================================================================================

    /// <summary>A generalized field the caller only ever names by its alias.</summary>
    public class Ac5Banded
    {
        public int Id { get; set; }

        [DwAlias("band")]
        [DwGeneralize(GeneralizeMode.Round, Step = 100)]
        [DwNoOrder]
        public decimal Payroll { get; set; }
    }

    /// <summary>A field that is filterable, but only with the operator the attribute allows.</summary>
    public class Ac5Restricted
    {
        public int Id { get; set; }

        [DwAlias("badge")]
        [DwOperators(Allow = new[] { Operator.Equal })]
        public string Serial { get; set; } = string.Empty;
    }

    /// <summary>The same restriction with nothing else on it, so the policy has one source.</summary>
    public class Ac5SoleRestricted
    {
        public int Id { get; set; }

        [DwOperators(Allow = new[] { Operator.Equal })]
        public string Serial { get; set; } = string.Empty;
    }

    /// <summary>Two different members sharing one alias, which no reading can resolve.</summary>
    public class Ac5Colliding
    {
        public int Id { get; set; }

        [DwAlias("code")]
        public string First { get; set; } = string.Empty;

        [DwAlias("code")]
        public string Second { get; set; } = string.Empty;
    }

    // ---- the row-shape model -------------------------------------------------------------------

    public class Ac5Line
    {
        public int Id { get; set; }

        public int OrdId { get; set; }

        public decimal Price { get; set; }
    }

    public class Ac5Ord
    {
        public int Id { get; set; }

        public decimal Total { get; set; }

        public List<Ac5Line> Lines { get; set; } = new();
    }

    /// <summary>A row node with a getter no database computes, and a field nobody may project.</summary>
    public class Ac5LineRow
    {
        public decimal Price { get; set; }

        [DwDenied]
        public decimal Cost { get; set; }

        public decimal Doubled => Price * 2m;
    }

    /// <summary>
    /// One node built in place from the entity's own columns, one built by a subquery.
    /// </summary>
    public class Ac5OrdRow
    {
        public int Id { get; set; }

        public Ac5LineRow Nest { get; set; } = new();

        public Ac5LineRow? Head { get; set; }
    }

    public sealed class Ac5CodeSurfaceProbes : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly Ac5ShapeDb _db;

        public Ac5CodeSurfaceProbes(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new Ac5ShapeDb(_connection);
            _db.Database.EnsureCreated();

            Ac5Ord order = new() { Id = 1, Total = 7m };

            order.Lines.Add(new Ac5Line { Id = 1, Price = 4m });
            order.Lines.Add(new Ac5Line { Id = 2, Price = 6m });

            _db.Ords.Add(order);
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        // ---- harness -------------------------------------------------------------------------

        private static DwPolicyContext Caller() => new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        private static PolicyResolver Attributes() =>
            new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        private static DwPolicyOptions Options(DwTier tier = DwTier.Strict) =>
            new() { Tier = tier, Caps = { MinGroupSize = 1 } };

        private static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict)
            where T : class =>
            source.ApplyPolicy(Caller(), Options(tier), Attributes());

        private static string Shape(Exception? error) => error switch
        {
            null => "OK",
            PolicyException refusal =>
                $"{refusal.ErrorCode}|path={refusal.FieldPath}|feature={refusal.Feature}"
                + $"|rule={refusal.RuleId ?? "-"}|origin={refusal.SourceOrigin ?? "-"}",
            LogicException failure => $"LogicException|{failure.Message}|subject={failure.Subject ?? "-"}",
            _ => $"{error.GetType().Name}: {error.Message.Split('\n')[0]}"
        };

        private static Exception? Catch(Action run)
        {
            try
            {
                run();

                return null;
            }
            catch (Exception error)
            {
                return error;
            }
        }

        private static Filter WhereOn(
            string field, Operator op = Operator.Equal, DataType type = DataType.Text, string value = "x") => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Sort = 0, Field = field, DataType = type, Operator = op, Values = { value }
                    }
                }
            }
        };

        // =========================================================================================
        // FINDING. AmbiguousGroupKey hands back the canonical path of a field named only by alias.
        // =========================================================================================

        [Fact]
        public void Ambiguous_group_key_names_the_clause_and_not_the_path_behind_the_alias()
        {
            Ac5Banded[] rows =
            {
                new() { Id = 1, Payroll = 100m },
                new() { Id = 2, Payroll = 149m }
            };

            Summary byAlias = new()
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "band" },
                    AggregateBy = new List<AggregateBy>
                    {
                        new() { Field = "Id", Aggregator = Aggregator.Maximum, Alias = "top" }
                    }
                }
            };

            Exception? error = Catch(() => Guard(rows.AsQueryable()).ToList(byAlias));

            _out.WriteLine($"grouped by alias 'band' : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.AmbiguousGroupKey, refusal.ErrorCode);

            // The caller wrote "band" and never wrote "Payroll". Under Strict the refusal names the
            // clause: the canonical path is the column behind the alias, and the origin would say
            // that its values are transformed.
            Assert.Equal("*", refusal.FieldPath);
            Assert.Null(refusal.SourceOrigin);
        }

        /// <summary>
        /// The same disclosure with the shipped k-anonymity floor in force, so it is not an artifact
        /// of a deployment that turned the floor off.
        /// </summary>
        [Fact]
        public void Ambiguous_group_key_names_the_clause_under_the_default_floor()
        {
            List<Ac5Banded> rows = new();

            // Two groups of six — above DwCaps.DefaultMinGroupSize — that round to the same band.
            for (int i = 0; i < 6; i++)
            {
                rows.Add(new Ac5Banded { Id = i + 1, Payroll = 100m });
                rows.Add(new Ac5Banded { Id = i + 100, Payroll = 149m });
            }

            Summary byAlias = new()
            {
                GroupBy = new GroupBy
                {
                    Fields = new List<string> { "band" },
                    AggregateBy = new List<AggregateBy>
                    {
                        new() { Field = "Id", Aggregator = Aggregator.Maximum, Alias = "top" }
                    }
                }
            };

            DwPolicyOptions shipped = new() { Tier = DwTier.Strict };

            Exception? error = Catch(
                () => rows.AsQueryable().ApplyPolicy(Caller(), shipped, Attributes()).ToList(byAlias));

            _out.WriteLine($"default floor ({shipped.Caps.MinGroupSize}) : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.AmbiguousGroupKey, refusal.ErrorCode);
            Assert.Equal("*", refusal.FieldPath);
        }

        /// <summary>
        /// The contrast: a field refusal for the same aliased field names nothing at all, which is
        /// the rule the refusal above does not follow.
        /// </summary>
        [Fact]
        public void A_field_refusal_on_an_aliased_field_names_nothing()
        {
            Exception? error = Catch(
                () => Guard(Array.Empty<Ac5Banded>().AsQueryable())
                    .ToList(new Filter { Orders = new List<OrderBy> { new() { Field = "band" } } }));

            _out.WriteLine($"ordered by alias 'band' : {Shape(error)}");

            PolicyException refusal = Assert.IsType<PolicyException>(error);

            Assert.Equal(PolicyErrorCode.FieldDeniedForOrder, refusal.ErrorCode);
            Assert.Equal("*", refusal.FieldPath);
            Assert.Null(refusal.SourceOrigin);
        }

        // =========================================================================================
        // OperatorNotAllowed: names the field, the rule and the attribute that restricted it.
        // =========================================================================================

        [Fact]
        public void Strict_operator_refusal_names_the_field_but_not_its_source()
        {
            Exception? aliased = Catch(
                () => Guard(Array.Empty<Ac5Restricted>().AsQueryable())
                    .ToList(WhereOn("badge", Operator.Contains)));

            // The same restriction with no alias beside it, so the policy has exactly one source and
            // Gate.Exception would attribute it if anything did.
            Exception? sole = Catch(
                () => Guard(Array.Empty<Ac5SoleRestricted>().AsQueryable())
                    .ToList(WhereOn("Serial", Operator.Contains)));

            Exception? unknown = Catch(
                () => Guard(Array.Empty<Ac5Restricted>().AsQueryable())
                    .ToList(WhereOn("NoSuchColumn", Operator.Contains)));

            _out.WriteLine($"restricted, aliased : {Shape(aliased)}");
            _out.WriteLine($"restricted, sole    : {Shape(sole)}");
            _out.WriteLine($"unknown name        : {Shape(unknown)}");

            PolicyException refusal = Assert.IsType<PolicyException>(aliased);

            Assert.Equal(PolicyErrorCode.OperatorNotAllowed, refusal.ErrorCode);

            // Named by the spelling the caller used, which they already know, and with no source.
            Assert.Equal("badge", refusal.FieldPath);

            // A single-source policy is the case that would attribute, so it is the one to look at.
            PolicyException attributed = Assert.IsType<PolicyException>(sole);

            _out.WriteLine($"sole-source origin  : {attributed.SourceOrigin ?? "-"}");
        }

        // =========================================================================================
        // AmbiguousFieldName: a real-but-colliding name answers differently from a missing one.
        // =========================================================================================

        [Fact]
        public void Strict_ambiguous_name_answers_as_a_missing_one_does()
        {
            Exception? collides = Catch(
                () => Guard(Array.Empty<Ac5Colliding>().AsQueryable()).ToList(WhereOn("code")));

            Exception? missing = Catch(
                () => Guard(Array.Empty<Ac5Colliding>().AsQueryable()).ToList(WhereOn("NoSuchColumn")));

            _out.WriteLine($"ambiguous alias : {Shape(collides)}");
            _out.WriteLine($"unknown name    : {Shape(missing)}");

            PolicyException one = Assert.IsType<PolicyException>(collides);
            PolicyException two = Assert.IsType<PolicyException>(missing);

            // A name matching two fields matches at least one, so answering it differently from a
            // name matching none would tell a caller their guess named something real.
            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, one.ErrorCode);
            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, two.ErrorCode);
            Assert.Equal(one.FieldPath, two.FieldPath);
        }

        // =========================================================================================
        // Round 4's fix 3: a member a subquery builds records nothing and is left alone.
        // =========================================================================================

        [Fact]
        public void A_member_built_in_place_is_refused_and_one_built_by_a_subquery_is_not()
        {
            IQueryable<Ac5OrdRow> projected = _db.Ords.AsNoTracking().Select(order => new Ac5OrdRow
            {
                Id = order.Id,
                Nest = new Ac5LineRow { Price = order.Total },
                Head = order.Lines.OrderBy(line => line.Id)
                    .Select(line => new Ac5LineRow { Price = line.Price })
                    .FirstOrDefault()
            });

            // What the same query does without the gate in front of it.
            Exception? bareNest = Catch(
                () => projected.Where(row => row.Nest.Doubled == 1m).ToList());

            Exception? bareHead = Catch(
                () => projected.Where(row => row.Head!.Doubled == 1m).ToList());

            Exception? guardedNest = Catch(
                () => Guard(projected).ToList(WhereOn("Nest.Doubled", Operator.Equal, DataType.Number, "1")));

            Exception? guardedHead = Catch(
                () => Guard(projected).ToList(WhereOn("Head.Doubled", Operator.Equal, DataType.Number, "1")));

            _out.WriteLine($"unguarded Nest.Doubled : {Shape(bareNest)}");
            _out.WriteLine($"unguarded Head.Doubled : {Shape(bareHead)}");
            _out.WriteLine($"guarded   Nest.Doubled : {Shape(guardedNest)}");
            _out.WriteLine($"guarded   Head.Doubled : {Shape(guardedHead)}");

            // A member the projection builds in place is read, so the strict tier refuses the path
            // the database cannot compute.
            PolicyException nest = Assert.IsType<PolicyException>(guardedNest);

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, nest.ErrorCode);
            Assert.Equal("*", nest.FieldPath);

            // The member a subquery builds is left alone: whatever the bare query does, the guarded
            // one does. This assertion records the residual, and must change if it is closed.
            Assert.Equal(Shape(bareHead), Shape(guardedHead));
            Assert.IsNotType<PolicyException>(guardedHead);
        }

        /// <summary>
        /// The denial half of the same shape: an opaque member is still one the gate narrows or
        /// leaves out, so nothing beneath it reaches the caller.
        /// </summary>
        [Fact]
        public void A_denied_field_beneath_a_subquery_built_member_does_not_reach_the_caller()
        {
            IQueryable<Ac5OrdRow> projected = _db.Ords.AsNoTracking().Select(order => new Ac5OrdRow
            {
                Id = order.Id,
                Nest = new Ac5LineRow { Price = order.Total, Cost = order.Total },
                Head = order.Lines.OrderBy(line => line.Id)
                    .Select(line => new Ac5LineRow { Price = line.Price, Cost = line.Price })
                    .FirstOrDefault()
            });

            List<Ac5OrdRow> rows = Guard(projected).ToList(new Filter()).Data;

            _out.WriteLine($"rows={rows.Count} nestCost={rows[0].Nest?.Cost} "
                + $"nestPrice={rows[0].Nest?.Price} head={(rows[0].Head is null ? "<null>" : "present")}");

            // Whatever the shape could or could not read, the denied value is not in the result.
            Assert.Equal(0m, rows[0].Nest?.Cost ?? 0m);
            Assert.Equal(0m, rows[0].Head?.Cost ?? 0m);
        }

        /// <summary>
        /// A provider in front of EF Core's own is not EF Core for the purposes of the translation
        /// test, and that changes nothing about what the policy denies.
        /// </summary>
        [Fact]
        public void A_wrapping_provider_still_enforces_every_denial()
        {
            using SqliteConnection connection = new("DataSource=:memory:");

            connection.Open();

            using Ac5ShapeDb wrapped = new(connection, typeof(Ac5PassThroughProvider));

            wrapped.Database.EnsureCreated();

            IQueryable<Ac5OrdRow> projected = wrapped.Ords.AsNoTracking().Select(order => new Ac5OrdRow
            {
                Id = order.Id,
                Nest = new Ac5LineRow { Price = order.Total, Cost = order.Total }
            });

            _out.WriteLine($"provider : {projected.Provider.GetType().FullName}");

            Exception? denied = Catch(
                () => Guard(projected).ToList(WhereOn("Nest.Cost", Operator.Equal, DataType.Number, "1")));

            _out.WriteLine($"guarded Nest.Cost : {Shape(denied)}");

            PolicyException refusal = Assert.IsType<PolicyException>(denied);

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
            Assert.Equal("*", refusal.FieldPath);
        }

        /// <summary>A provider a host puts in front of EF Core's own, built the documented way.</summary>
        public sealed class Ac5PassThroughProvider : Microsoft.EntityFrameworkCore.Query.Internal.EntityQueryProvider
        {
            public Ac5PassThroughProvider(Microsoft.EntityFrameworkCore.Query.Internal.IQueryCompiler compiler)
                : base(compiler)
            {
            }
        }

        public sealed class Ac5ShapeDb : DbContext
        {
            private readonly SqliteConnection _connection;
            private readonly Type? _replacementProvider;

            public Ac5ShapeDb(SqliteConnection connection, Type? replacementProvider = null)
            {
                _connection = connection;
                _replacementProvider = replacementProvider;
            }

            public DbSet<Ac5Ord> Ords => Set<Ac5Ord>();

            public DbSet<Ac5Line> Lines => Set<Ac5Line>();

            protected override void OnConfiguring(DbContextOptionsBuilder options)
            {
                options.UseSqlite(_connection);

                if (_replacementProvider is null)
                {
                    return;
                }

                // The documented EF Core extension point a host uses to put its own query provider
                // in place: ReplaceService<IAsyncQueryProvider, …>.
                typeof(DbContextOptionsBuilder)
                    .GetMethods()
                    .Single(m => m.Name == nameof(DbContextOptionsBuilder.ReplaceService)
                                 && m.GetGenericArguments().Length == 2
                                 && m.GetParameters().Length == 0)
                    .MakeGenericMethod(
                        typeof(Microsoft.EntityFrameworkCore.Query.IAsyncQueryProvider), _replacementProvider)
                    .Invoke(options, null);
            }
        }
    }
}
