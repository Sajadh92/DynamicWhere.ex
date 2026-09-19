using System.Collections;
using System.Linq.Expressions;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // ============================================================================ reshaped chains and tracking: models

    public class ZxShopper
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<ZxPayCard> Cards { get; set; } = new();
    }

    public class ZxPayCard
    {
        public int Id { get; set; }

        public int ZxShopperId { get; set; }

        public string Label { get; set; } = string.Empty;

        [DwDenied]
        public string? Pan { get; set; }
    }

    public class ZxPurchase
    {
        public int Id { get; set; }

        public string Code { get; set; } = string.Empty;

        public int ZxShopperId { get; set; }

        public ZxShopper? Shopper { get; set; }
    }

    /// <summary>An application helper whose result is only a flag, and which hands the row its cards on the way.</summary>
    public static class ZxHydrator
    {
        public static bool Attach(ZxShopper shopper, List<ZxPayCard> cards)
        {
            shopper.Cards = cards;

            return true;
        }
    }

    /// <summary>The same, done by a constructor whose object is only read for a flag.</summary>
    public sealed class ZxAttachment
    {
        public ZxAttachment(ZxShopper shopper, List<ZxPayCard> cards) => shopper.Cards = cards;

        public bool Done => true;
    }

    public class ZxMaskedNote
    {
        public int Id { get; set; }

        [DwMask(MaskStrategy.Full)]
        public string Body { get; set; } = string.Empty;
    }

    public sealed class ZxShopContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZxShopContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZxShopper> Shoppers => Set<ZxShopper>();

        public DbSet<ZxPayCard> Cards => Set<ZxPayCard>();

        public DbSet<ZxPurchase> Purchases => Set<ZxPurchase>();

        public DbSet<ZxMaskedNote> Notes => Set<ZxMaskedNote>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    /// <summary>
    /// A queryable that forwards to another provider, as LinqKit's AsExpandable, DelegateDecompiler's Decompile and
    /// similar wrappers do over an EF Core query. Its provider is not EF Core's, so EF Core's AsNoTracking leaves it
    /// unchanged.
    /// </summary>
    public sealed class ZxForwardingQuery<T> : IOrderedQueryable<T>
    {
        private readonly IQueryable<T> _inner;

        public ZxForwardingQuery(IQueryable<T> inner)
        {
            _inner = inner;
            Provider = new ZxForwardingProvider(inner.Provider);
        }

        public Type ElementType => typeof(T);

        public Expression Expression => _inner.Expression;

        public IQueryProvider Provider { get; }

        public IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class ZxForwardingProvider : IQueryProvider
    {
        private readonly IQueryProvider _inner;

        public ZxForwardingProvider(IQueryProvider inner) => _inner = inner;

        public IQueryable CreateQuery(Expression expression)
        {
            IQueryable created = _inner.CreateQuery(expression);

            return (IQueryable)Activator.CreateInstance(typeof(ZxForwardingQuery<>).MakeGenericType(created.ElementType), created)!;
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new ZxForwardingQuery<TElement>(_inner.CreateQuery<TElement>(expression));

        public object? Execute(Expression expression) => _inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => _inner.Execute<TResult>(expression);
    }

    // ============================================================================ reshaped chains and tracking: tests

    public sealed class ReviewTrackingTests : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZxShopContext _db;

        public ReviewTrackingTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZxShopContext(_connection);
            _db.Database.EnsureCreated();

            ZxShopper shopper = new() { Name = "S1", Cards = { new ZxPayCard { Label = "visa", Pan = "zx-pan-secret" } } };

            _db.Shoppers.Add(shopper);
            _db.Purchases.Add(new ZxPurchase { Code = "P1", Shopper = shopper });
            _db.Notes.Add(new ZxMaskedNote { Body = "original-note-body" });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private (string Unguarded, object? Guarded) Run<T>(string label, IQueryable<T> source, DwTier tier, bool dynamic = false) where T : class
        {
            string unguarded;

            try
            {
                unguarded = ZxKit.Holds(source.AsNoTracking().ToList(), "zx-pan-secret") ? "HOLDS PAN" : "no pan";
            }
            catch (Exception e)
            {
                unguarded = $"THREW {e.GetType().Name}: {e.Message.Split('\n')[0]}";
            }

            PolicyQueryable<T> guarded = ZxKit.Guard(source, tier);
            object? data;

            try
            {
                data = dynamic ? guarded.ToListDynamic(new Filter()).Data : guarded.ToList(new Filter()).Data;
            }
            catch (Exception e)
            {
                data = null;
                _out.WriteLine($"{label}: guarded threw {e.GetType().Name}: {e.Message.Split('\n')[0]}");
            }

            RowShape shape = RowShape.Of(source);

            _out.WriteLine($"{label} {tier}: shape={shape.Kind} materializes(Cards.Pan)={shape.Materializes("Cards.Pan")} "
                           + $"reshapes={QueryRoot.Reshapes(source.Expression)} builds={QueryRoot.Builds(source.Expression)}");
            _out.WriteLine($"{label} {tier}: unguarded={unguarded}");
            _out.WriteLine($"{label} {tier}: guarded={ZxKit.Json(data)} trace=[{ZxKit.Trace(guarded)}]");

            return (unguarded, data);
        }

        // ------------------------------------------------------------------------ B1: a flag-typed subtree that hands the row its cards

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_B1_an_application_method_read_only_for_a_flag_in_a_conditional(DwTier tier)
        {
            IQueryable<ZxShopper> source = _db.Purchases
                .Select(p => ZxHydrator.Attach(p.Shopper!, p.Shopper!.Cards.ToList()) ? p.Shopper! : p.Shopper!);

            (string unguarded, object? guarded) = Run("B1", source, tier);

            if (!unguarded.StartsWith("HOLDS", StringComparison.Ordinal))
            {
                return;
            }

            Assert.False(ZxKit.Holds(guarded, "zx-pan-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_B2_a_constructor_read_only_for_a_flag_in_a_conditional(DwTier tier)
        {
            IQueryable<ZxShopper> source = _db.Purchases
                .Select(p => new ZxAttachment(p.Shopper!, p.Shopper!.Cards.ToList()).Done ? p.Shopper! : p.Shopper!);

            (string unguarded, object? guarded) = Run("B2", source, tier);

            if (!unguarded.StartsWith("HOLDS", StringComparison.Ordinal))
            {
                return;
            }

            Assert.False(ZxKit.Holds(guarded, "zx-pan-secret"));
        }

        // ------------------------------------------------------------------------ T: a provider that wraps EF Core runs a tracking query

        [Theory]
        [InlineData(DwTier.Strict, false)]
        [InlineData(DwTier.Convenience, false)]
        [InlineData(DwTier.Strict, true)]
        public void Zx_T1_a_wrapped_query_over_a_context_that_already_tracks_the_cards(DwTier tier, bool dynamic)
        {
            // Earlier in the same unit of work, the application read the cards with tracking.
            _ = _db.Cards.ToList();

            IQueryable<ZxShopper> source = new ZxForwardingQuery<ZxShopper>(_db.Shoppers);

            (_, object? guarded) = Run("T1", source, tier, dynamic);

            Assert.False(ZxKit.Holds(guarded, "zx-pan-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_T2_a_wrapped_query_over_a_context_that_already_tracks_the_shopper_with_its_cards(DwTier tier)
        {
            _ = _db.Shoppers.Include(s => s.Cards).ToList();

            IQueryable<ZxShopper> source = new ZxForwardingQuery<ZxShopper>(_db.Shoppers);

            (_, object? guarded) = Run("T2", source, tier);

            Assert.False(ZxKit.Holds(guarded, "zx-pan-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_T3_control_the_same_query_unwrapped(DwTier tier)
        {
            _ = _db.Cards.ToList();

            (_, object? guarded) = Run("T3", _db.Shoppers, tier);

            Assert.False(ZxKit.Holds(guarded, "zx-pan-secret"));
        }

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public void Zx_T4_a_wrapped_reshaped_query_over_tracked_cards(DwTier tier)
        {
            _ = _db.Cards.ToList();

            IQueryable<ZxShopper> source = new ZxForwardingQuery<ZxPurchase>(_db.Purchases).Select(p => p.Shopper!);

            (_, object? guarded) = Run("T4", source, tier);

            Assert.False(ZxKit.Holds(guarded, "zx-pan-secret"));
        }

        /// <summary>
        /// Guarded() detaches the query so that a transform applied to a materialized entity is never written back.
        /// Through a wrapping provider the query tracks, and the masked value is the entity's pending state.
        /// </summary>
        [Fact]
        public void Zx_T5_a_wrapped_query_leaves_a_masked_value_as_a_pending_change()
        {
            IQueryable<ZxMaskedNote> source = new ZxForwardingQuery<ZxMaskedNote>(_db.Notes);

            object? data = ZxKit.Guard(source).ToList(new Filter()).Data;

            _out.WriteLine($"sent={ZxKit.Json(data)} tracked={_db.ChangeTracker.Entries().Count()} hasChanges={_db.ChangeTracker.HasChanges()}");

            _db.SaveChanges();
            _db.ChangeTracker.Clear();

            string stored = _db.Notes.AsNoTracking().Single().Body;

            _out.WriteLine($"stored after SaveChanges: {stored}");

            Assert.Equal("original-note-body", stored);
        }
    }
}
