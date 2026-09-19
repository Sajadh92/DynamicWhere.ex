using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using System.Collections;
using System.Data.Common;
using System.Linq.Expressions;

namespace DynamicWhere.Tests;

public class CancelledRow
{
    public int Id { get; set; }

    public string Team { get; set; } = string.Empty;

    public int Points { get; set; }
}

public sealed class CancellationContext : DbContext
{
    private readonly SqliteConnection _connection;
    private readonly IInterceptor[] _interceptors;

    public CancellationContext(SqliteConnection connection, params IInterceptor[] interceptors)
    {
        _connection = connection;
        _interceptors = interceptors;
    }

    public DbSet<CancelledRow> Rows => Set<CancelledRow>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSqlite(_connection).AddInterceptors(_interceptors);
}

/// <summary>
/// Cancels a token just before the numbered command runs, and counts the commands that finished, so a
/// test can tell which read the token reached.
/// </summary>
public sealed class CancelBeforeCommand : DbCommandInterceptor
{
    private readonly CancellationTokenSource _source;
    private readonly int _at;
    private int _started;

    public CancelBeforeCommand(CancellationTokenSource source, int at)
    {
        _source = source;
        _at = at;
    }

    public int Finished { get; private set; }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (++_started == _at)
        {
            _source.Cancel();
        }

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        Finished++;

        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }
}

/// <summary>An async provider that fails as it builds the query, before any task exists.</summary>
public sealed class UntranslatableQuery<T> : IQueryable<T>, IAsyncQueryProvider
{
    public UntranslatableQuery() => Expression = Expression.Constant(this);

    public Type ElementType => typeof(T);

    public Expression Expression { get; }

    public IQueryProvider Provider => this;

    public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException("untranslatable");

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IQueryable CreateQuery(Expression expression) => this;

    public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => throw new InvalidOperationException("untranslatable");

    public object Execute(Expression expression) => throw new InvalidOperationException("untranslatable");

    public TResult Execute<TResult>(Expression expression) => throw new InvalidOperationException("untranslatable");

    public TResult ExecuteAsync<TResult>(Expression expression, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("untranslatable");
}

/// <summary>
/// Every asynchronous terminal takes a <see cref="CancellationToken"/>, guarded or not, and hands it to
/// the provider.
/// </summary>
/// <remarks>
/// Before 3.2.0 no method took one, so a request its client had abandoned could not cancel its read.
/// The overloads sit beside the ones without a token rather than replacing them: a parameter added to
/// an existing method changes its signature, which a caller compiled against the old one cannot find.
/// <para>
/// Runs on the EF Core 6 leg too, because a dynamic read reaches EF Core's own asynchronous operators by
/// reflection, and a method found by name is exactly what differs between versions.
/// </para>
/// </remarks>
public sealed class CancellationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CancellationContext _db;

    public CancellationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _db = new CancellationContext(_connection);
        _db.Database.EnsureCreated();

        for (int i = 1; i <= 6; i++)
        {
            _db.Rows.Add(new CancelledRow { Id = i, Team = i % 2 == 0 ? "even" : "odd", Points = i * 10 });
        }

        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static CancellationToken Canceled()
    {
        using CancellationTokenSource source = new();

        source.Cancel();

        return source.Token;
    }

    private static Filter Paged() => new()
    {
        Orders = new List<OrderBy> { new() { Sort = 1, Field = "Id", Direction = Direction.Ascending } },
        Page = new PageBy { PageNumber = 1, PageSize = 4 }
    };

    private static Filter PagedDynamic()
    {
        Filter filter = Paged();

        filter.Selects = new List<string> { "Id", "Team" };

        return filter;
    }

    private static Summary ByTeam() => new()
    {
        GroupBy = new GroupBy
        {
            Fields = new List<string> { "Team" },
            AggregateBy = new List<AggregateBy>
            {
                new() { Field = "Points", Alias = "Total", Aggregator = Aggregator.Sumation }
            }
        },
        Orders = new List<OrderBy> { new() { Sort = 1, Field = "Team", Direction = Direction.Ascending } }
    };

    private static Segment OddOrFirst() => new()
    {
        ConditionSets =
        {
            new ConditionSet
            {
                Sort = 1,
                ConditionGroup = new ConditionGroup
                {
                    Conditions = { new Condition { Field = "Team", DataType = DataType.Text, Operator = Operator.Equal, Values = { "odd" } } }
                }
            },
            new ConditionSet
            {
                Sort = 2,
                Intersection = Intersection.Union,
                ConditionGroup = new ConditionGroup
                {
                    Conditions = { new Condition { Field = "Id", DataType = DataType.Number, Operator = Operator.Equal, Values = { 2 } } }
                }
            }
        }
    };

    private PolicyQueryable<CancelledRow> Guarded() =>
        _db.Rows.ApplyPolicy(
            new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
            new DwPolicyOptions { Tier = DwTier.Strict, Caps = { MinGroupSize = 1 } },
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

    [Fact]
    public async Task A_canceled_token_stops_every_unguarded_async_terminal()
    {
        CancellationToken canceled = Canceled();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _db.Rows.ToListAsync(Paged(), canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _db.Rows.ToListAsync(Paged(), true, canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _db.Rows.ToListAsyncDynamic(PagedDynamic(), canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _db.Rows.ToListAsyncDynamic(PagedDynamic(), true, canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _db.Rows.ToListAsync(ByTeam(), canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _db.Rows.ToListAsync(ByTeam(), true, canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _db.Rows.ToListAsync(OddOrFirst(), canceled));
    }

    [Fact]
    public async Task A_canceled_token_stops_every_guarded_async_terminal()
    {
        CancellationToken canceled = Canceled();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Guarded().ToListAsync(Paged(), canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Guarded().ToListAsyncDynamic(PagedDynamic(), canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Guarded().ToListAsync(ByTeam(), canceled));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Guarded().ToListAsync(OddOrFirst(), canceled));
    }

    /// <summary>
    /// Each terminal counts, then reads. Canceling just before the first command shows the count took the
    /// token; canceling just before the second shows the read did, after a count that finished.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task The_token_reaches_both_the_count_and_the_read(int at)
    {
        async Task Check(Func<CancellationContext, CancellationToken, Task> run)
        {
            using CancellationTokenSource source = new();
            CancelBeforeCommand interceptor = new(source, at);
            using CancellationContext db = new(_connection, interceptor);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run(db, source.Token));
            Assert.Equal(at - 1, interceptor.Finished);
        }

        await Check((db, token) => db.Rows.ToListAsync(Paged(), token));
        await Check((db, token) => db.Rows.ToListAsyncDynamic(PagedDynamic(), token));
        await Check((db, token) => db.Rows.ToListAsync(ByTeam(), token));
        await Check((db, token) => db.Rows.ToListAsync(OddOrFirst(), token));
    }

    /// <summary>
    /// A grouped count reaches EF Core's operator by reflection. A query it cannot build fails with its own
    /// exception, as the synchronous count it replaced did, not one wrapped by the reflection call.
    /// </summary>
    [Fact]
    public async Task A_provider_failure_leaves_the_grouped_count_as_itself()
    {
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => AsyncReads.CountAsync(new UntranslatableQuery<int>(), CancellationToken.None));

        Assert.Equal("untranslatable", failure.Message);
    }

    /// <summary>A token that is never canceled changes nothing: each overload answers as the one without a token does.</summary>
    [Fact]
    public async Task A_live_token_returns_what_the_overload_without_one_returns()
    {
        using CancellationTokenSource live = new();

        FilterResult<CancelledRow> plain = await _db.Rows.ToListAsync(Paged());
        FilterResult<CancelledRow> tokened = await _db.Rows.ToListAsync(Paged(), live.Token);
        Assert.Equal(plain.Data.Select(row => row.Id), tokened.Data.Select(row => row.Id));
        Assert.Equal((plain.TotalCount, plain.PageCount), (tokened.TotalCount, tokened.PageCount));

        FilterResult<dynamic> dynamicRows = await _db.Rows.ToListAsyncDynamic(PagedDynamic(), live.Token);
        Assert.Equal(new[] { 1, 2, 3, 4 }, dynamicRows.Data.Select(row => (int)row.Id));
        Assert.Equal(6, dynamicRows.TotalCount);

        SummaryResult summary = await _db.Rows.ToListAsync(ByTeam(), live.Token);
        Assert.Equal(2, summary.TotalCount);
        Assert.Equal(new[] { 120, 90 }, summary.Data.Select(row => (int)row.Total));

        SegmentResult<CancelledRow> segment = await _db.Rows.ToListAsync(OddOrFirst(), live.Token);
        Assert.Equal(new[] { 1, 2, 3, 5 }, segment.Data!.Select(row => row.Id).OrderBy(id => id));

        Assert.Equal(new[] { 1, 2, 3, 4 }, (await Guarded().ToListAsync(Paged(), live.Token)).Data.Select(row => row.Id));
        Assert.Equal(new[] { 1, 2, 3, 4 }, (await Guarded().ToListAsyncDynamic(PagedDynamic(), live.Token)).Data.Select(row => (int)row.Id));
        Assert.Equal(new[] { 120, 90 }, (await Guarded().ToListAsync(ByTeam(), live.Token)).Data.Select(row => (int)row.Total));
        Assert.Equal(4, (await Guarded().ToListAsync(OddOrFirst(), live.Token)).TotalCount);
    }
}
