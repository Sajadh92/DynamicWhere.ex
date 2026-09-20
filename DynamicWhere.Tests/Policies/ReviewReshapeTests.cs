using System.Collections;
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
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ============================================================================ reshaped chains: models

    public class ZvShopper
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<ZvPayCard> Cards { get; set; } = new();
    }

    public class ZvPayCard
    {
        public int Id { get; set; }

        public int ZvShopperId { get; set; }

        public string Label { get; set; } = string.Empty;

        [DwDenied]
        public string? Pan { get; set; }
    }

    public class ZvPurchase
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public int ZvShopperId { get; set; }

        public ZvShopper? Shopper { get; set; }
    }

    /// <summary>What an application's mapper does: builds the row type from what the query hands it.</summary>
    public static class ZvMapper
    {
        public static ZvShopper Shopper(ZvShopper shopper, List<ZvPayCard> cards) =>
            new() { Id = shopper.Id, Name = shopper.Name, Cards = cards };
    }

    public sealed class ZvReshapeContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZvReshapeContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZvShopper> Shoppers => Set<ZvShopper>();

        public DbSet<ZvPurchase> Purchases => Set<ZvPurchase>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    // ============================================================================ reshaped chains: tests

    /// <summary>
    /// A reshaped chain with no include and no object built in its lambdas is read from the model. These are
    /// chains that load a navigation anyway: an application's method builds the row, or a lambda captures a query
    /// with its own projection or include. An EF Core translation failure of the unguarded query is reported,
    /// not asserted.
    /// </summary>
    public sealed class ReviewReshapeTests : IDisposable
    {
        private static readonly JsonSerializerOptions Json = new() { ReferenceHandler = ReferenceHandler.IgnoreCycles };

        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZvReshapeContext _db;

        public ReviewReshapeTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZvReshapeContext(_connection);
            _db.Database.EnsureCreated();

            ZvShopper shopper = new() { Name = "S1", Cards = { new ZvPayCard { Label = "visa", Pan = "reshape-pan-secret" } } };

            _db.Shoppers.Add(shopper);
            _db.Purchases.Add(new ZvPurchase { Code = "P1", Shopper = shopper });
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
                new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        private static string Sent(object? rows) => JsonSerializer.Serialize(rows, Json);

        /// <summary>Runs the unguarded query and the guarded one, and reports what each returned.</summary>
        private (string Unguarded, string Guarded) Run<T>(string label, IQueryable<T> source, DwTier tier = DwTier.Strict) where T : class
        {
            string unguarded;
            string guarded;

            try
            {
                unguarded = Sent(source.AsNoTracking().ToList());
            }
            catch (Exception e)
            {
                unguarded = $"THREW {e.GetType().Name}: {e.Message.Split('\n')[0]}";
            }

            try
            {
                PolicyQueryable<T> handle = Guard(source, tier);

                guarded = Sent(handle.ToList(new Filter()).Data) + " decisions=["
                          + string.Join(" | ", handle.LastTrace?.Decisions.Where(d => d.Action != PolicyAction.Allowed)
                              .Select(d => $"{d.FieldPath} {d.Action}") ?? Array.Empty<string>()) + "]";
            }
            catch (Exception e)
            {
                guarded = $"THREW {e.GetType().Name}: {e.Message.Split('\n')[0]}";
            }

            RowShape shape = RowShape.Of(source);

            _out.WriteLine($"{label}: shape={shape.Kind} materializes(Cards.Pan)={shape.Materializes("Cards.Pan")} "
                           + $"reshapes={QueryRoot.Reshapes(source.Expression)} builds={QueryRoot.Builds(source.Expression)}");
            _out.WriteLine($"{label}: unguarded={unguarded}");
            _out.WriteLine($"{label}: guarded={guarded}");

            return (unguarded, guarded);
        }

        // ------------------------------------------------------------------------ D1: a mapper builds the row

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zv_D1_a_reshaped_chain_whose_row_a_method_builds_from_a_loaded_collection(DwTier tier)
        {
            IQueryable<ZvShopper> source = _db.Purchases.Select(p => ZvMapper.Shopper(p.Shopper!, p.Shopper!.Cards.ToList()));

            (string unguarded, string guarded) = Run("D1", source, tier);

            Assert.Contains("reshape-pan-secret", unguarded);
            Assert.DoesNotContain("reshape-pan-secret", guarded);
        }

        // ------------------------------------------------------------------------ D2: the projection is behind a closure

        [Fact]
        public void Zv_D2_a_projection_captured_in_a_closure_inside_a_SelectMany()
        {
            IQueryable<ZvShopper> built = _db.Shoppers.Select(s => new ZvShopper { Id = s.Id, Name = s.Name, Cards = s.Cards });
            IQueryable<ZvShopper> source = _db.Purchases.SelectMany(p => built.Where(s => s.Id == p.ZvShopperId));

            (string unguarded, string guarded) = Run("D2 SelectMany", source);

            if (unguarded.StartsWith("THREW", StringComparison.Ordinal))
            {
                return;
            }

            Assert.DoesNotContain("reshape-pan-secret", guarded);
        }

        [Fact]
        public void Zv_D2_a_projection_captured_in_a_closure_inside_a_Select()
        {
            IQueryable<ZvShopper> built = _db.Shoppers.Select(s => new ZvShopper { Id = s.Id, Name = s.Name, Cards = s.Cards });
            IQueryable<ZvShopper> source = _db.Purchases.Select(p => built.First(s => s.Id == p.ZvShopperId));

            (string unguarded, string guarded) = Run("D2 Select", source);

            if (unguarded.StartsWith("THREW", StringComparison.Ordinal))
            {
                return;
            }

            Assert.DoesNotContain("reshape-pan-secret", guarded);
        }

        /// <summary>An object captured from memory holds whatever it holds, which no include accounts for.</summary>
        [Fact]
        public void Zv_D2_an_object_captured_from_memory_inside_a_Select()
        {
            ZvShopper keeper = new() { Id = 99, Name = "keeper", Cards = { new ZvPayCard { Label = "mem", Pan = "memory-pan-secret" } } };
            IQueryable<ZvShopper> source = _db.Purchases.Select(p => keeper);

            (string unguarded, string guarded) = Run("D2 memory", source);

            if (unguarded.StartsWith("THREW", StringComparison.Ordinal))
            {
                return;
            }

            Assert.Contains("memory-pan-secret", unguarded);
            Assert.DoesNotContain("memory-pan-secret", guarded);
        }

        /// <summary>The same, with an include in place of the projection: the include is behind the closure too.</summary>
        [Fact]
        public void Zv_D2_an_include_captured_in_a_closure_inside_a_Select()
        {
            IQueryable<ZvShopper> withCards = _db.Shoppers.Include(s => s.Cards);
            IQueryable<ZvShopper> source = _db.Purchases.Select(p => withCards.First(s => s.Id == p.ZvShopperId));

            (string unguarded, string guarded) = Run("D2 include", source);

            if (unguarded.StartsWith("THREW", StringComparison.Ordinal))
            {
                return;
            }

            Assert.DoesNotContain("reshape-pan-secret", guarded);
        }

        // ------------------------------------------------------------------------ ordinary EF Core queries and the epoch

        // ------------------------------------------------------------------------ D3/D4: ruled-out shapes

        /// <summary>An object built only inside a Where subquery loads nothing into the rows.</summary>
        [Fact]
        public void Zv_D3_an_object_built_only_inside_a_Where_subquery()
        {
            IQueryable<ZvShopper> source = _db.Purchases
                .Where(p => _db.Shoppers.Select(s => new { s.Id, s.Name }).Any(x => x.Id == p.ZvShopperId))
                .Select(p => p.Shopper!);

            (string unguarded, string guarded) = Run("D3", source);

            Assert.DoesNotContain("reshape-pan-secret", unguarded);
            Assert.DoesNotContain("reshape-pan-secret", guarded);
        }

        /// <summary>A Join whose inner source projects: the construction is an argument of the chain, so it is seen.</summary>
        [Fact]
        public void Zv_D4_a_Join_whose_inner_source_projects()
        {
            IQueryable<ZvShopper> source = _db.Purchases.Join(
                _db.Shoppers.Select(s => new ZvShopper { Id = s.Id, Name = s.Name, Cards = s.Cards }),
                p => p.ZvShopperId,
                s => s.Id,
                (p, s) => s);

            (string unguarded, string guarded) = Run("D4", source);

            if (unguarded.StartsWith("THREW", StringComparison.Ordinal))
            {
                return;
            }

            Assert.DoesNotContain("reshape-pan-secret", guarded);
        }

        /// <summary>A Join whose inner source is a projection held in a variable: still an argument, still seen.</summary>
        [Fact]
        public void Zv_D4_a_Join_whose_inner_source_is_a_projection_in_a_variable()
        {
            IQueryable<ZvShopper> built = _db.Shoppers.Select(s => new ZvShopper { Id = s.Id, Name = s.Name, Cards = s.Cards });
            IQueryable<ZvShopper> source = _db.Purchases.Join(built, p => p.ZvShopperId, s => s.Id, (p, s) => s);

            (string unguarded, string guarded) = Run("D4 variable", source);

            if (unguarded.StartsWith("THREW", StringComparison.Ordinal))
            {
                return;
            }

            Assert.DoesNotContain("reshape-pan-secret", guarded);
        }
    }
}
