using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests;

/// <summary>
/// The four shapes a date member can have — <c>DateTimeOffset</c>, <c>DateTimeOffset?</c>,
/// <c>DateTime</c>, <c>DateTime?</c> — against both date data types, in memory and on a provider.
/// </summary>
/// <remarks>
/// Its own model and its own database rather than the sales fixture, because none of those entities
/// carries a <c>DateTimeOffset</c> and the point of these tests is the member's type.
/// <para>
/// Three faults sat in one emitted string before this suite existed. The predicate carried
/// <c>{field} != null</c> whether or not the member could be null, and comparing a
/// <c>DateTimeOffset</c> against the null constant throws out of the expression parser rather than
/// being optimised away. It compared every date member against a <c>DateTime</c> literal, and
/// <c>DateTimeOffset >= DateTime</c> has no signature. And it appended <c>.Date</c> to the member as
/// written, which a nullable member does not have. Every case below failed on 3.0.0 except the two
/// non-nullable <c>DateTime</c> controls.
/// </para>
/// </remarks>
public sealed class DateFilterTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<DateContext> _options;

    public DateFilterTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<DateContext>().UseSqlite(_connection).Options;

        using DateContext context = new(_options);

        context.Database.EnsureCreated();
        context.Rows.AddRange(Rows());
        context.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>
    /// Three rows two days apart, with the nullable members unset on the first so a negated
    /// operator has something to leave out.
    /// </summary>
    private static DateRow[] Rows() => new[]
    {
        new DateRow
        {
            Id = 1,
            At = Noon.AddDays(-2),
            MaybeAt = null,
            When = Noon.UtcDateTime.AddDays(-2),
            MaybeWhen = null
        },
        new DateRow
        {
            Id = 2,
            At = Noon,
            MaybeAt = Noon,
            When = Noon.UtcDateTime,
            MaybeWhen = Noon.UtcDateTime
        },
        new DateRow
        {
            Id = 3,
            At = Noon.AddDays(2),
            MaybeAt = Noon.AddDays(2),
            When = Noon.UtcDateTime.AddDays(2),
            MaybeWhen = Noon.UtcDateTime.AddDays(2)
        }
    };

    private static Condition Cond(string field, DataType type, Operator op, params object[] values) =>
        new() { Sort = 1, Field = field, DataType = type, Operator = op, Values = values.ToList() };

    /// <summary>Ids the condition returns from SQLite, ordered so the assertion reads as a set.</summary>
    private int[] OnSqlite(Condition condition)
    {
        using DateContext context = new(_options);

        return context.Rows.Where(condition).Select(r => r.Id).ToList().OrderBy(id => id).ToArray();
    }

    /// <summary>Ids the condition returns under LINQ to Objects, with no provider in the way.</summary>
    private static int[] InMemory(Condition condition) =>
        Rows().AsQueryable().Where(condition).Select(r => r.Id).OrderBy(id => id).ToArray();

    /// <summary>
    /// Asserts what the predicate selects, under LINQ to Objects.
    /// </summary>
    /// <remarks>
    /// Where the member is a <c>DateTimeOffset</c> this is the only leg available: EF Core's SQLite
    /// provider has no translation for comparing one, and refuses the whole query with "could not be
    /// translated" whatever the predicate says. That is a provider limit rather than anything this
    /// builder emits — the same predicate translates on Npgsql, which is where DCMP first saw the
    /// defect these tests cover.
    /// </remarks>
    private static void Assert(Condition condition, params int[] ids) =>
        Xunit.Assert.Equal(ids, InMemory(condition));

    /// <summary>
    /// Asserts both paths agree, which is what tells a provider quirk from a builder one.
    /// </summary>
    private void AssertOnBoth(Condition condition, params int[] ids)
    {
        Xunit.Assert.Equal(ids, InMemory(condition));
        Xunit.Assert.Equal(ids, OnSqlite(condition));
    }

    #region DateTimeOffset, not nullable

    [Fact]
    public void A_window_on_a_DateTimeOffset_returns_the_rows_inside_it() =>
        Assert(Cond("At", DataType.DateTime, Operator.Between, "2026-08-31T00:00:00Z", "2026-09-02T00:00:00Z"), 2);

    [Fact]
    public void Each_ordered_operator_on_a_DateTimeOffset_returns_its_own_side()
    {
        Assert(Cond("At", DataType.DateTime, Operator.GreaterThanOrEqual, "2026-09-01T12:00:00Z"), 2, 3);
        Assert(Cond("At", DataType.DateTime, Operator.GreaterThan, "2026-09-01T12:00:00Z"), 3);
        Assert(Cond("At", DataType.DateTime, Operator.LessThanOrEqual, "2026-09-01T12:00:00Z"), 1, 2);
        Assert(Cond("At", DataType.DateTime, Operator.LessThan, "2026-09-01T12:00:00Z"), 1);
    }

    [Fact]
    public void Equality_on_a_DateTimeOffset_matches_the_instant() =>
        Assert(Cond("At", DataType.DateTime, Operator.Equal, "2026-09-01T12:00:00Z"), 2);

    [Fact]
    public void A_value_written_in_another_zone_is_the_same_instant() =>
        Assert(Cond("At", DataType.DateTime, Operator.Equal, "2026-09-01T15:00:00+03:00"), 2);

    [Fact]
    public void A_DateTimeOffset_that_cannot_be_null_is_never_null_and_always_not_null()
    {
        Assert(Cond("At", DataType.DateTime, Operator.IsNull));
        Assert(Cond("At", DataType.DateTime, Operator.IsNotNull), 1, 2, 3);
    }

    #endregion

    #region DateTimeOffset, nullable

    [Fact]
    public void A_window_on_a_nullable_DateTimeOffset_never_returns_the_unset_row() =>
        Assert(Cond("MaybeAt", DataType.DateTime, Operator.Between, "2026-08-01T00:00:00Z", "2026-10-01T00:00:00Z"), 2, 3);

    [Fact]
    public void An_unset_DateTimeOffset_fails_NotEqual_and_NotBetween()
    {
        Assert(Cond("MaybeAt", DataType.DateTime, Operator.NotEqual, "2026-09-01T12:00:00Z"), 3);
        Assert(Cond("MaybeAt", DataType.DateTime, Operator.NotBetween, "2026-08-31T00:00:00Z", "2026-09-02T00:00:00Z"), 3);
    }

    [Fact]
    public void A_nullable_DateTimeOffset_answers_IsNull_and_IsNotNull()
    {
        Assert(Cond("MaybeAt", DataType.DateTime, Operator.IsNull), 1);
        Assert(Cond("MaybeAt", DataType.DateTime, Operator.IsNotNull), 2, 3);
    }

    #endregion

    #region DataType.Date — calendar days on both sides

    [Fact]
    public void A_day_comparison_on_a_DateTimeOffset_reads_the_day_the_caller_wrote() =>
        Assert(Cond("At", DataType.Date, Operator.Equal, "2026-09-01"), 2);

    [Fact]
    public void A_day_window_on_a_DateTimeOffset_includes_both_ends() =>
        Assert(Cond("At", DataType.Date, Operator.Between, "2026-08-30", "2026-09-01"), 1, 2);

    [Fact]
    public void A_day_comparison_on_a_nullable_DateTimeOffset_never_reads_the_unset_row()
    {
        Assert(Cond("MaybeAt", DataType.Date, Operator.Equal, "2026-09-01"), 2);
        Assert(Cond("MaybeAt", DataType.Date, Operator.NotEqual, "2026-09-01"), 3);
    }

    [Fact]
    public void A_day_comparison_on_a_nullable_DateTime_never_reads_the_unset_row()
    {
        AssertOnBoth(Cond("MaybeWhen", DataType.Date, Operator.Equal, "2026-09-01"), 2);
        AssertOnBoth(Cond("MaybeWhen", DataType.Date, Operator.NotEqual, "2026-09-01"), 3);
    }

    #endregion

    #region DateTime — the shapes that already worked, held in place

    [Fact]
    public void A_window_on_a_DateTime_returns_the_rows_inside_it() =>
        AssertOnBoth(Cond("When", DataType.DateTime, Operator.Between, "2026-08-31T00:00:00", "2026-09-02T00:00:00"), 2);

    [Fact]
    public void A_day_comparison_on_a_DateTime_reads_that_day() =>
        AssertOnBoth(Cond("When", DataType.Date, Operator.Equal, "2026-09-01"), 2);

    [Fact]
    public void A_DateTime_that_cannot_be_null_is_never_null_and_always_not_null()
    {
        AssertOnBoth(Cond("When", DataType.DateTime, Operator.IsNull));
        AssertOnBoth(Cond("When", DataType.DateTime, Operator.IsNotNull), 1, 2, 3);
    }

    #endregion

    #region Refusals

    [Fact]
    public void A_value_that_is_not_a_date_is_refused_with_InvalidFormat()
    {
        LogicException thrown = Xunit.Assert.Throws<LogicException>(
            () => InMemory(Cond("At", DataType.DateTime, Operator.Equal, "not-a-date")));

        Xunit.Assert.Equal(ErrorCode.InvalidFormat, thrown.Message);
    }

    [Fact]
    public void A_date_value_in_a_host_specific_format_is_refused_with_InvalidFormat()
    {
        // Parsed with the invariant culture, so a filter means the same day on every server. The
        // shipped builder carried the caller's text into a DateTime.Parse the runtime read in the
        // host's culture, which made "01.09.2026" a date on one machine and an error on another.
        LogicException thrown = Xunit.Assert.Throws<LogicException>(
            () => InMemory(Cond("At", DataType.DateTime, Operator.Equal, "15/09/2026 12:00:00")));

        Xunit.Assert.Equal(ErrorCode.InvalidFormat, thrown.Message);
    }

    [Fact]
    public void The_wrong_number_of_values_is_refused_with_the_arity_code()
    {
        LogicException thrown = Xunit.Assert.Throws<LogicException>(
            () => InMemory(Cond("At", DataType.DateTime, Operator.Between, "2026-09-01T00:00:00Z")));

        Xunit.Assert.Equal(ErrorCode.RequiredTwoValue, thrown.Message);
    }

    [Fact]
    public void An_operator_the_package_does_not_offer_on_a_date_is_refused()
    {
        LogicException thrown = Xunit.Assert.Throws<LogicException>(
            () => InMemory(Cond("At", DataType.DateTime, Operator.Contains, "2026-09-01T12:00:00Z")));

        Xunit.Assert.Contains("Unsupported combination", thrown.Message);
    }

    #endregion
}

/// <summary>One row carrying each of the four date shapes.</summary>
public class DateRow
{
    public int Id { get; set; }

    public DateTimeOffset At { get; set; }

    public DateTimeOffset? MaybeAt { get; set; }

    public DateTime When { get; set; }

    public DateTime? MaybeWhen { get; set; }
}

/// <summary>A one-table context, kept out of the sales model it has nothing to do with.</summary>
public class DateContext : DbContext
{
    public DateContext(DbContextOptions<DateContext> options) : base(options) { }

    public DbSet<DateRow> Rows => Set<DateRow>();
}
