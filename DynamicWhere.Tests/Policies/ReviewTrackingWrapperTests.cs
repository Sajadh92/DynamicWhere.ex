using System.Collections;
using System.Linq.Expressions;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Xunit.Abstractions;

// A guarded query through a provider wrapping EF Core's runs untracked: AsNoTracking goes into the query itself.

namespace DynamicWhere.Tests.Policies
{
    public class ZwShopper
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public List<ZwPayCard> Cards { get; set; } = new();
    }

    public class ZwPayCard
    {
        public int Id { get; set; }

        public int ZwShopperId { get; set; }

        public string Label { get; set; } = string.Empty;

        [DwDenied]
        public string? Pan { get; set; }
    }

    public class ZwNote
    {
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;

        [DwMask(MaskStrategy.Full)]
        public string Body { get; set; } = string.Empty;
    }

    public sealed class ZwShopContext : DbContext
    {
        private readonly SqliteConnection _connection;

        public ZwShopContext(SqliteConnection connection) => _connection = connection;

        public DbSet<ZwShopper> Shoppers => Set<ZwShopper>();

        public DbSet<ZwPayCard> Cards => Set<ZwPayCard>();

        public DbSet<ZwNote> Notes => Set<ZwNote>();

        protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
    }

    /// <summary>A wrapper that forwards to EF Core's provider, async included, as LinqKit's EF Core build does.</summary>
    public sealed class ZwAsyncQuery<T> : IOrderedQueryable<T>, IAsyncEnumerable<T>
    {
        private readonly IQueryable<T> _inner;

        public ZwAsyncQuery(IQueryable<T> inner)
        {
            _inner = inner;
            Provider = new ZwAsyncProvider(inner.Provider);
        }

        public Type ElementType => typeof(T);

        public Expression Expression => _inner.Expression;

        public IQueryProvider Provider { get; }

        public IEnumerator<T> GetEnumerator() => _inner.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            ((IAsyncEnumerable<T>)_inner).GetAsyncEnumerator(cancellationToken);
    }

    public sealed class ZwAsyncProvider : IAsyncQueryProvider
    {
        private readonly IQueryProvider _inner;

        public ZwAsyncProvider(IQueryProvider inner) => _inner = inner;

        public IQueryable CreateQuery(Expression expression)
        {
            IQueryable created = _inner.CreateQuery(expression);

            return (IQueryable)Activator.CreateInstance(typeof(ZwAsyncQuery<>).MakeGenericType(created.ElementType), created)!;
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            new ZwAsyncQuery<TElement>(_inner.CreateQuery<TElement>(expression));

        public object? Execute(Expression expression) => _inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => _inner.Execute<TResult>(expression);

        public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default) =>
            ((IAsyncQueryProvider)_inner).ExecuteAsync<TResult>(expression, cancellationToken);
    }

    /// <summary>
    /// A wrapper whose expression is a constant of itself, which its provider swaps for the inner query's before
    /// handing it on: the EF Core root is not in the tree the guard reads.
    /// </summary>
    public sealed class ZwConstantRootQuery<T> : IOrderedQueryable<T>
    {
        public ZwConstantRootQuery(IQueryable<T> inner, Expression? expression = null)
        {
            Inner = inner;
            Expression = expression ?? Expression.Constant(this);
            Provider = new ZwConstantRootProvider<T>(this);
        }

        internal IQueryable<T> Inner { get; }

        public Type ElementType => typeof(T);

        public Expression Expression { get; }

        public IQueryProvider Provider { get; }

        public IEnumerator<T> GetEnumerator() =>
            Inner.Provider.CreateQuery<T>(ZwConstantRootProvider<T>.Unwrap(Expression)).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class ZwConstantRootProvider<TRoot> : IQueryProvider
    {
        private readonly ZwConstantRootQuery<TRoot> _root;

        public ZwConstantRootProvider(ZwConstantRootQuery<TRoot> root) => _root = root;

        internal static Expression Unwrap(Expression expression) => new Swap().Visit(expression);

        private sealed class Swap : ExpressionVisitor
        {
            protected override Expression VisitConstant(ConstantExpression node) =>
                node.Value is ZwConstantRootQuery<TRoot> query ? query.Inner.Expression : node;
        }

        public IQueryable CreateQuery(Expression expression) =>
            (IQueryable)Activator.CreateInstance(
                typeof(ZwConstantRootQuery<>).MakeGenericType(expression.Type.GetGenericArguments()[0]),
                _root.Inner.Provider.CreateQuery(Unwrap(expression)), expression)!;

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) =>
            (IQueryable<TElement>)CreateQuery(expression);

        public object? Execute(Expression expression) => _root.Inner.Provider.Execute(Unwrap(expression));

        public TResult Execute<TResult>(Expression expression) => _root.Inner.Provider.Execute<TResult>(Unwrap(expression));
    }

    public sealed class ReviewTrackingWrapperTests : IDisposable
    {
        private const string Secret = "zw-shop-pan-secret";

        private readonly ITestOutputHelper _out;
        private readonly SqliteConnection _connection;
        private readonly ZwShopContext _db;

        public ReviewTrackingWrapperTests(ITestOutputHelper output)
        {
            _out = output;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new ZwShopContext(_connection);
            _db.Database.EnsureCreated();

            _db.Shoppers.Add(new ZwShopper { Name = "S1", Cards = { new ZwPayCard { Label = "visa", Pan = Secret } } });
            _db.Shoppers.Add(new ZwShopper { Name = "S2" });
            _db.Notes.Add(new ZwNote { Title = "t1", Body = "original-note-body" });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
        }

        public void Dispose()
        {
            _db.Dispose();
            _connection.Dispose();
        }

        private static Segment Union() => new()
        {
            ConditionSets =
            {
                new ConditionSet
                {
                    Sort = 1,
                    ConditionGroup = new ConditionGroup
                    {
                        Conditions = { new Condition { Sort = 1, Field = "Name", DataType = DataType.Text, Operator = Operator.Equal, Values = { "S1" } } }
                    }
                },
                new ConditionSet
                {
                    Sort = 2,
                    Intersection = Intersection.Union,
                    ConditionGroup = new ConditionGroup
                    {
                        Conditions = { new Condition { Sort = 1, Field = "Name", DataType = DataType.Text, Operator = Operator.Equal, Values = { "S2" } } }
                    }
                }
            }
        };

        private static Summary CountByName() => new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Name" },
                AggregateBy = new List<AggregateBy> { new() { Alias = "Total", Aggregator = Aggregator.Count } }
            }
        };

        // ------------------------------------------------------------------ W: a wrapper with async support

        [Theory]
        [InlineData(DwTier.Strict)]
        [InlineData(DwTier.Convenience)]
        public async Task Zw_W1_async_reads_through_a_wrapper_over_a_context_tracking_the_cards(DwTier tier)
        {
            _ = _db.Cards.ToList();
            int before = _db.ChangeTracker.Entries().Count();

            IQueryable<ZwShopper> source = new ZwAsyncQuery<ZwShopper>(_db.Shoppers);

            object? data = null;
            string code = await ZwKit.CodeAsync(async () => data = (await ZwKit.Guard(source, tier).ToListAsync(new Filter())).Data);
            string async = await ZwKit.CodeAsync(() => ZwKit.Guard(source, tier).ToListAsyncDynamic(new Filter()));
            string segment = await ZwKit.CodeAsync(() => ZwKit.Guard(source, tier).ToListAsync(Union()));
            string summary = await ZwKit.CodeAsync(() => ZwKit.Guard(source, tier).ToListAsync(CountByName()));
            int after = _db.ChangeTracker.Entries().Count();

            _out.WriteLine($"W1 {tier}: list={code} dynamic={async} segment={segment} summary={summary} tracked {before}->{after} sent={ZwKit.Json(data)}");

            Assert.Equal("ran", code);
            Assert.Equal("ran", async);
            Assert.Equal("ran", segment);
            Assert.Equal("ran", summary);
            Assert.False(ZwKit.Holds(data, Secret));
        }

        [Fact]
        public async Task Zw_W2_a_masked_value_read_async_through_a_wrapper_is_never_a_pending_change()
        {
            IQueryable<ZwNote> source = new ZwAsyncQuery<ZwNote>(_db.Notes);

            object? data = (await ZwKit.Guard(source).ToListAsync(new Filter())).Data;

            _out.WriteLine($"W2 sent={ZwKit.Json(data)} tracked={_db.ChangeTracker.Entries().Count()} changes={_db.ChangeTracker.HasChanges()}");

            _db.SaveChanges();
            _db.ChangeTracker.Clear();

            Assert.Equal("original-note-body", _db.Notes.AsNoTracking().Single().Body);
        }

        [Fact]
        public async Task Zw_W3_a_masked_value_read_through_a_wrapped_segment_is_never_a_pending_change()
        {
            IQueryable<ZwNote> source = new ZwAsyncQuery<ZwNote>(_db.Notes);

            Segment segment = new()
            {
                ConditionSets =
                {
                    new ConditionSet
                    {
                        Sort = 1,
                        ConditionGroup = new ConditionGroup
                        {
                            Conditions = { new Condition { Sort = 1, Field = "Title", DataType = DataType.Text, Operator = Operator.Equal, Values = { "t1" } } }
                        }
                    }
                }
            };

            object? data = (await ZwKit.Guard(source).ToListAsync(segment)).Data;

            _out.WriteLine($"W3 sent={ZwKit.Json(data)} tracked={_db.ChangeTracker.Entries().Count()} changes={_db.ChangeTracker.HasChanges()}");

            _db.SaveChanges();
            _db.ChangeTracker.Clear();

            Assert.Equal("original-note-body", _db.Notes.AsNoTracking().Single().Body);
        }

        [Fact]
        public void Zw_W4_the_callers_AsTracking_inside_a_wrapper_does_not_outlive_the_guard()
        {
            _ = _db.Cards.ToList();

            IQueryable<ZwShopper> source = new ZwAsyncQuery<ZwShopper>(_db.Shoppers.AsTracking());
            object? data = ZwKit.SafeRead(() => ZwKit.Guard(source).ToList(new Filter()).Data);

            _out.WriteLine($"W4 sent={ZwKit.Json(data)} tracked={_db.ChangeTracker.Entries().Count()}");

            Assert.False(ZwKit.Holds(data, Secret));
        }

        // ------------------------------------------------------------------ C: a wrapper whose root the guard cannot see

        [Fact]
        public void Zw_C2_shoppers_through_a_wrapper_whose_expression_is_a_constant_of_itself()
        {
            _ = _db.Cards.ToList();

            IQueryable<ZwShopper> source = new ZwConstantRootQuery<ZwShopper>(_db.Shoppers);

            object? data = null;
            string code = ZwKit.Code(() => data = ZwKit.Guard(source).ToList(new Filter()).Data);

            _out.WriteLine($"C2 code={code} sent={ZwKit.Json(data)} tracked={_db.ChangeTracker.Entries().Count()}");

            Assert.False(ZwKit.Holds(data, Secret));
        }
    }
}
