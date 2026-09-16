using System.Globalization;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Source;
using Microsoft.Extensions.Configuration;

namespace DynamicWhere.Tests;

/// <summary>
/// Which date texts a condition accepts, how a deployment declares more, and the member types the
/// predicate builder reads them for.
/// </summary>
/// <remarks>
/// The reader is exercised directly with explicit options, rather than through
/// <see cref="DwDates.Configure(DwDateOptions)"/>, because that configuration is process-wide and
/// set once: a test that declared <c>dd/MM/yyyy</c> for itself would declare it for every test in the
/// run.
/// </remarks>
public class DateFormatTests
{
    private static readonly DwDateOptions Nothing = Freeze(new DwDateOptions());

    private static DwDateOptions Freeze(DwDateOptions options)
    {
        options.Freeze();

        return options;
    }

    private static DwDateOptions Declaring(params string[] formats)
    {
        DwDateOptions options = new();

        foreach (string format in formats)
        {
            options.Formats.Add(format);
        }

        return Freeze(options);
    }

    private static string Read(string value, Type type, DwDateOptions? options = null) =>
        DateValue.Read(value, type, "Field", options ?? Nothing);

    private static string Refusal(string value, Type type, DwDateOptions? options = null) =>
        Assert.Throws<LogicException>(() => Read(value, type, options)).Message;

    #region With nothing declared

    [Theory]
    [InlineData("2026-09-01", "2026-09-01T00:00:00.0000000")]
    [InlineData("2026-9-1", "2026-09-01T00:00:00.0000000")]
    [InlineData("2026-09-01T12:30", "2026-09-01T12:30:00.0000000")]
    [InlineData("2026-09-01T12:30:15", "2026-09-01T12:30:15.0000000")]
    [InlineData("2026-09-01T12:30:15.123", "2026-09-01T12:30:15.1230000")]
    [InlineData("2026-09-01 12:30:15", "2026-09-01T12:30:15.0000000")]
    [InlineData("2026/09/01", "2026-09-01T00:00:00.0000000")]
    [InlineData("2026.09.01", "2026-09-01T00:00:00.0000000")]
    [InlineData("2026-09-01t12:30:15", "2026-09-01T12:30:15.0000000")]
    [InlineData("2026-09-01T12:30:15,5", "2026-09-01T12:30:15.5000000")]
    [InlineData("2026-09-01T12:30:15.123456789", "2026-09-01T12:30:15.1234567")]
    public void ISO_8601_and_year_first_dates_are_accepted(string value, string read) =>
        Assert.Equal(read, Read(value, typeof(DateTime)));

    [Theory]
    [InlineData("2026-09-01T12:00:00Z", "2026-09-01T12:00:00.0000000+00:00")]
    [InlineData("2026-09-01T15:00:00+03:00", "2026-09-01T12:00:00.0000000+00:00")]
    [InlineData("2026-09-01T12:00:00.1234567Z", "2026-09-01T12:00:00.1234567+00:00")]
    [InlineData("2026-09-01", "2026-09-01T00:00:00.0000000+00:00")]
    [InlineData("2026-09-01T15:00:00+0300", "2026-09-01T12:00:00.0000000+00:00")]
    [InlineData("2026-09-01T15:00:00+03", "2026-09-01T12:00:00.0000000+00:00")]
    [InlineData("2026-09-01 15:00+03", "2026-09-01T12:00:00.0000000+00:00")]
    [InlineData("2026-09-01T12:00:00z", "2026-09-01T12:00:00.0000000+00:00")]
    [InlineData("2026-09-01T15:00:00.123456789+03:00", "2026-09-01T12:00:00.1234567+00:00")]
    [InlineData("2026-09-01T12:00:00,25Z", "2026-09-01T12:00:00.2500000+00:00")]
    public void A_zone_is_honoured_and_a_DateTimeOffset_reads_as_UTC(string value, string read) =>
        Assert.Equal(read, Read(value, typeof(DateTimeOffset)));

    [Theory]
    [InlineData("01/09/2026")]
    [InlineData("15/09/2026")]
    [InlineData("09/15/2026")]
    [InlineData("1/9/26")]
    [InlineData("01.09.2026")]
    [InlineData("01-09-2026")]
    [InlineData("01/09/2026 12:00:00")]
    public void A_date_that_leads_with_a_day_or_a_month_is_ambiguous(string value)
    {
        // Refused by its shape, not its numbers: "15/09/2026" has only one valid reading, but a
        // client that sends it would send "05/09/2026" next, and that one has two. Refusing both
        // means the client finds out on its first request instead of on the fifth of the month.
        Assert.Equal(ErrorCode.AmbiguousDateFormat, Refusal(value, typeof(DateTime)));
        Assert.Equal(ErrorCode.AmbiguousDateFormat, Refusal(value, typeof(DateTimeOffset)));
    }

    [Theory]
    [InlineData("12:00")]
    [InlineData("1/9")]
    [InlineData("Sep 2026")]
    [InlineData("1 September 2026")]
    [InlineData("20260901")]
    [InlineData("not-a-date")]
    [InlineData("")]
    public void Anything_else_is_not_a_date(string value)
    {
        // The lenient parser read "12:00" as today at noon and "1/9" as 9 January of this year.
        Assert.Equal(ErrorCode.InvalidFormat, Refusal(value, typeof(DateTime)));
    }

    [Fact]
    public void The_refusal_names_the_field()
    {
        LogicException thrown = Assert.Throws<LogicException>(
            () => DateValue.Read("01/09/2026", typeof(DateTime), "CreatedAt", Nothing));

        Assert.Equal("CreatedAt", thrown.Subject);
    }

    [Fact]
    public void The_hosts_culture_and_calendar_change_nothing()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            foreach (string culture in new[] { "en-US", "en-GB", "ar-IQ", "ar-SA", "th-TH", "fa-IR" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);

                Assert.Equal("2026-09-01T00:00:00.0000000", Read("2026-09-01", typeof(DateTime)));
                Assert.Equal("2026-09-01", Read("2026-09-01", typeof(DateOnly)));
                Assert.Equal(ErrorCode.AmbiguousDateFormat, Refusal("01/09/2026", typeof(DateTime)));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    #endregion

    #region A deployment that declares its own format

    [Fact]
    public void A_declared_day_first_format_reads_day_first()
    {
        DwDateOptions dayFirst = Declaring("dd/MM/yyyy");

        Assert.Equal("2026-09-01T00:00:00.0000000", Read("01/09/2026", typeof(DateTime), dayFirst));
        Assert.Equal("2026-09-15T00:00:00.0000000", Read("15/09/2026", typeof(DateTime), dayFirst));
        Assert.Equal("2026-09-01", Read("01/09/2026", typeof(DateOnly), dayFirst));
    }

    [Fact]
    public void Declaring_a_format_keeps_ISO_8601()
    {
        Assert.Equal(
            "2026-09-01T00:00:00.0000000",
            Read("2026-09-01", typeof(DateTime), Declaring("dd/MM/yyyy")));
    }

    [Fact]
    public void A_value_the_declared_format_cannot_read_is_still_refused()
    {
        // Month-first text sent to a day-first API: the fifteenth month is not a date in any declared
        // form, and its shape is still the one that cannot say which number is the day.
        Assert.Equal(
            ErrorCode.AmbiguousDateFormat,
            Refusal("09/15/2026", typeof(DateTime), Declaring("dd/MM/yyyy")));
    }

    [Fact]
    public void Two_day_month_orders_are_refused_when_declared()
    {
        DwDateOptions both = new();

        both.Formats.Add("dd/MM/yyyy");
        both.Formats.Add("MM/dd/yyyy");

        ArgumentException thrown = Assert.Throws<ArgumentException>(() => both.Freeze());

        Assert.Contains("dd/MM/yyyy", thrown.Message);
        Assert.Contains("MM/dd/yyyy", thrown.Message);
    }

    [Fact]
    public void A_format_that_contradicts_ISO_8601_is_refused_when_declared()
    {
        DwDateOptions swapped = new();

        swapped.Formats.Add("yyyy-dd-MM");

        Assert.Throws<ArgumentException>(() => swapped.Freeze());
    }

    [Theory]
    [InlineData("dd/MM")]
    [InlineData("MM-dd HH:mm")]
    [InlineData("HH:mm")]
    [InlineData("t")]
    public void A_format_with_no_year_is_refused_when_declared(string format)
    {
        // The parser completes a missing year from the clock, and a missing date from today, so the
        // same value would name a different date depending on when the query runs.
        DwDateOptions yearless = new();

        yearless.Formats.Add(format);

        ArgumentException thrown = Assert.Throws<ArgumentException>(() => yearless.Freeze());

        Assert.Contains("no year", thrown.Message);
    }

    [Theory]
    [InlineData("'dd/MM/yyyy")]
    [InlineData("%")]
    [InlineData("q")]
    public void A_malformed_format_is_refused_as_an_argument(string format)
    {
        // The parser throws FormatException for these; configuration promises ArgumentException, so
        // a host catching that one at startup is not surprised by another.
        DwDateOptions malformed = new();

        malformed.Formats.Add(format);

        Assert.Throws<ArgumentException>(() => malformed.Freeze());
    }

    [Fact]
    public void A_twelve_hour_clock_with_no_designator_is_refused_when_declared()
    {
        // "hh" writes 4 PM as 04 and reads it back as 4 AM: every afternoon value would filter twelve
        // hours early.
        DwDateOptions twelveHour = new();

        twelveHour.Formats.Add("dd/MM/yyyy hh:mm");

        ArgumentException thrown = Assert.Throws<ArgumentException>(() => twelveHour.Freeze());

        Assert.Contains("hour", thrown.Message);
    }

    [Fact]
    public void A_twelve_hour_clock_with_its_designator_is_accepted() =>
        Assert.Equal(
            "2026-09-01T16:30:00.0000000",
            Read("01/09/2026 04:30 PM", typeof(DateTime), Declaring("dd/MM/yyyy hh:mm tt")));

    [Fact]
    public void A_day_with_no_month_is_refused_when_declared()
    {
        // "mm" is minutes. "dd/mm/yyyy" reads "01/09/2026" as 1 January at 00:09.
        DwDateOptions typo = new();

        typo.Formats.Add("dd/mm/yyyy");

        ArgumentException thrown = Assert.Throws<ArgumentException>(() => typo.Freeze());

        Assert.Contains("no month", thrown.Message);
    }

    [Theory]
    [InlineData("dd/MM/yyyy HH:mm", "MM/dd/yyyy")]
    [InlineData("d/M/yy", "M/d/yyyy")]
    [InlineData("dd.MM.yyyy", "MM/dd/yyyy")]
    public void Formats_of_different_shapes_must_share_one_day_month_order(string first, string second)
    {
        // No text is read by both, so the same-text check passes them — and yet "01/09/2026 00:00"
        // would be 1 September while "01/09/2026" is 9 January.
        DwDateOptions mixed = new();

        mixed.Formats.Add(first);
        mixed.Formats.Add(second);

        ArgumentException thrown = Assert.Throws<ArgumentException>(() => mixed.Freeze());

        Assert.Contains("opposite", thrown.Message);
    }

    [Fact]
    public void Formats_of_different_shapes_in_one_order_are_accepted()
    {
        DwDateOptions options = Declaring("dd/MM/yyyy", "dd/MM/yyyy HH:mm", "d/M/yy", "dd.MM.yyyy", "dd MMM yyyy");

        Assert.Equal("2026-09-01T00:00:00.0000000", Read("1/9/26", typeof(DateTime), options));
        Assert.Equal("2026-09-01T00:00:00.0000000", Read("01 Sep 2026", typeof(DateTime), options));
    }

    [Theory]
    [InlineData("yyyy-MM", "2026-09", "2026-09-01T00:00:00.0000000")]
    [InlineData("dd/MM/yy", "01/09/26", "2026-09-01T00:00:00.0000000")]
    public void A_format_with_a_year_reads_the_same_date_whenever_it_runs(string format, string value, string read) =>
        Assert.Equal(read, Read(value, typeof(DateTime), Declaring(format)));

    [Fact]
    public void A_blank_format_is_refused_when_declared()
    {
        DwDateOptions blank = new();

        blank.Formats.Add(" ");

        Assert.Throws<ArgumentException>(() => blank.Freeze());
    }

    [Fact]
    public void Declared_formats_cannot_change_once_configured()
    {
        DwDateOptions options = Declaring("dd/MM/yyyy");

        Assert.True(options.IsFrozen);
        Assert.Throws<NotSupportedException>(() => options.Formats.Add("dd.MM.yyyy"));
    }

    [Fact]
    public void Formats_bind_from_configuration()
    {
        IConfiguration section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicWhere:Dates:Formats:0"] = "dd/MM/yyyy",
                ["DynamicWhere:Dates:Formats:1"] = "dd/MM/yyyy HH:mm"
            })
            .Build()
            .GetSection("DynamicWhere:Dates");

        DwDateOptions options = new DwDateOptions().Bind(section);

        Assert.Equal(new[] { "dd/MM/yyyy", "dd/MM/yyyy HH:mm" }, options.Formats);
    }

    [Fact]
    public void A_misspelt_key_refuses_to_bind()
    {
        // The binder's own default is to ignore a key nothing matches, which would leave a deployment
        // on the defaults while its file says otherwise.
        IConfiguration section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicWhere:Dates:Fromats:0"] = "dd/MM/yyyy"
            })
            .Build()
            .GetSection("DynamicWhere:Dates");

        Assert.Throws<InvalidOperationException>(() => new DwDateOptions().Bind(section));
    }

    [Theory]
    [InlineData("DynamicWhere:Dates:Formats")]
    [InlineData("DynamicWhere:Dates")]
    public void A_single_value_where_the_list_belongs_refuses_to_bind(string key)
    {
        // The binder raises nothing for it and binds nothing from it, which is the same silent fall
        // back to the defaults that refusing an unknown key exists to prevent.
        IConfiguration section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [key] = "dd/MM/yyyy" })
            .Build()
            .GetSection("DynamicWhere:Dates");

        InvalidOperationException thrown =
            Assert.Throws<InvalidOperationException>(() => new DwDateOptions().Bind(section));

        Assert.Contains("DynamicWhere:Dates:Formats", thrown.Message);
    }

    [Fact]
    public void An_empty_section_binds_nothing_and_refuses_nothing()
    {
        IConfiguration section = new ConfigurationBuilder().Build().GetSection("DynamicWhere:Dates");

        Assert.Empty(new DwDateOptions().Bind(section).Formats);
    }

    #endregion

    #region C# dates placed in Values

    [Fact]
    public void A_CSharp_date_is_written_as_text_every_deployment_accepts()
    {
        // The invariant form of a DateTime is "09/01/2026 12:00:00" — month-first, the very shape a
        // date value is refused for. Written year-first, it reads the same everywhere.
        Assert.Equal(
            new[] { "2026-09-01T12:30:00", "2026-09-01T12:30:00+03:00", "2026-09-01" },
            Normalizer.Normalize(new object?[]
            {
                new DateTime(2026, 9, 1, 12, 30, 0),
                new DateTimeOffset(2026, 9, 1, 12, 30, 0, TimeSpan.FromHours(3)),
                new DateOnly(2026, 9, 1)
            }));
    }

    [Fact]
    public void A_CSharp_date_filters_on_the_day_it_names_whatever_the_host()
    {
        // Before 3.1.0 a day-first host read the month-first text of a C# DateTime back as the
        // ninth of January. It is year-first text now, and there is nothing left to misread.
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-GB");

            Condition condition = new()
            {
                Sort = 1,
                Field = "Day",
                DataType = DataType.Date,
                Operator = Operator.Equal,
                Values = { new DateTime(2026, 9, 1) }
            };

            Assert.Equal(new[] { 1 }, Days().Where(condition).Select(d => d.Id).ToArray());
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    #endregion

    #region DateOnly members

    private static IQueryable<Dated> Days() => new[]
    {
        new Dated { Id = 1, Day = new DateOnly(2026, 9, 1), MaybeDay = null },
        new Dated { Id = 2, Day = new DateOnly(2026, 9, 2), MaybeDay = new DateOnly(2026, 9, 2) },
        new Dated { Id = 3, Day = new DateOnly(2026, 9, 3), MaybeDay = new DateOnly(2026, 9, 3) }
    }.AsQueryable();

    private static int[] Ids(string field, DataType type, Operator op, params object[] values) =>
        Days()
            .Where(new Condition { Sort = 1, Field = field, DataType = type, Operator = op, Values = values.ToList() })
            .Select(d => d.Id)
            .OrderBy(id => id)
            .ToArray();

    [Fact]
    public void A_DateOnly_member_can_be_filtered_by_day()
    {
        // It could not: DataType.Date asked a DateOnly for a .Date it does not have, and DataType.DateTime
        // compared it against a DateTime literal — while the policy schema told every filter UI to use
        // DataType.Date for exactly these members.
        Assert.Equal(new[] { 1 }, Ids("Day", DataType.Date, Operator.Equal, "2026-09-01"));
        Assert.Equal(new[] { 2, 3 }, Ids("Day", DataType.Date, Operator.GreaterThan, "2026-09-01"));
        Assert.Equal(new[] { 1, 2 }, Ids("Day", DataType.DateTime, Operator.Between, "2026-09-01", "2026-09-02"));
    }

    [Fact]
    public void A_nullable_DateOnly_is_guarded()
    {
        Assert.Equal(new[] { 3 }, Ids("MaybeDay", DataType.Date, Operator.NotEqual, "2026-09-02"));
        Assert.Equal(new[] { 1 }, Ids("MaybeDay", DataType.Date, Operator.IsNull));
        Assert.Empty(Ids("Day", DataType.Date, Operator.IsNull));
    }

    [Fact]
    public void A_DateOnly_literal_survives_a_non_Gregorian_host_calendar()
    {
        // The literal is a constructor, not DateOnly.Parse: the runtime evaluates it in the host's
        // culture, and DateOnly.Parse reads "2026-09-01" as the year 1483 on a Thai server.
        CultureInfo previous = CultureInfo.CurrentCulture;

        try
        {
            foreach (string culture in new[] { "th-TH", "fa-IR", "ar-SA" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);

                Assert.Equal(new[] { 1 }, Ids("Day", DataType.Date, Operator.Equal, "2026-09-01"));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    #endregion

    #region Process-wide configuration

    [Fact]
    public void Nothing_is_declared_until_a_deployment_declares_it()
    {
        Assert.True(DwDates.Options.IsFrozen);
    }

    #endregion
}

/// <summary>A row with a date-only member, nullable and not.</summary>
public class Dated
{
    public int Id { get; set; }

    public DateOnly Day { get; set; }

    public DateOnly? MaybeDay { get; set; }
}
