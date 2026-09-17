using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace DynamicWhere.ex.Source;

/// <summary>
/// The date formats a deployment accepts in condition values, beyond the ones every deployment does.
/// </summary>
/// <remarks>
/// With nothing configured a date value must be ISO 8601 (<c>2026-09-01</c>, optionally with a time,
/// a fraction, and <c>Z</c> or an offset) or year-first (<c>2026/09/01</c>, <c>2026.09.01</c>). A
/// numeric day/month date such as <c>01/09/2026</c> is refused with <c>AmbiguousDateFormat</c>:
/// nothing in the text says which number is the day, and guessing silently filters on the wrong one.
/// <para>
/// A deployment whose clients send a known local form declares it here, as a .NET exact format read
/// with the invariant culture — <c>dd/MM/yyyy</c> for a day-first API. The formats above stay
/// accepted alongside whatever is declared.
/// </para>
/// </remarks>
public sealed class DwDateOptions
{
    private readonly List<string> _formats = new();
    private IList<string>? _frozen;

    /// <summary>
    /// Exact formats accepted in addition to ISO 8601 and year-first dates, read with the invariant
    /// culture.
    /// </summary>
    /// <remarks>
    /// Read-only once the options are configured. Two formats that read the same text as different
    /// dates — <c>dd/MM/yyyy</c> beside <c>MM/dd/yyyy</c> — are refused when they are configured
    /// rather than left to disagree on a request.
    /// <para>
    /// A format whose own text ISO 8601 or a year-first date already reads is refused as well, because
    /// declaring it could only change what such a value means. <c>yyyy-MM-dd'T'HH:mm:ss'Z'</c> writes the
    /// <c>Z</c> as a letter, so it reads 12:00 as a wall time where ISO 8601 reads an instant; on a
    /// <c>DateTime</c> member the ISO reading converts to the host's local time, so off UTC the two
    /// disagreed and every such value was refused as <c>AmbiguousDateFormat</c> on that host alone.
    /// </para>
    /// </remarks>
    public IList<string> Formats => _frozen ?? _formats;

    /// <summary>True once these options have been handed to <see cref="DwDates.Configure(DwDateOptions)"/>.</summary>
    public bool IsFrozen => _frozen is not null;

    /// <summary>
    /// Checks every declared format and prevents any further change.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a format is blank or malformed; cannot read back the text it writes; carries no
    /// year, or a day but no month; reads text another accepted format also reads as a different
    /// date; puts the day and the month in the opposite order to another declared format; or writes
    /// text ISO 8601 or a year-first date already reads.
    /// </exception>
    internal void Freeze()
    {
        if (_frozen is not null)
        {
            return;
        }

        // One copy, checked and then kept, so a format added while this runs can neither be frozen
        // in unchecked nor change what was checked.
        string[] declared = _formats.ToArray();

        foreach (string format in declared)
        {
            if (string.IsNullOrWhiteSpace(format))
            {
                throw new ArgumentException("A date format cannot be blank.", nameof(Formats));
            }

            foreach (DateTimeOffset probe in Probes)
            {
                ReadsBack(format, probe);
            }
        }

        // Two formats that read one text as two dates would make a request's meaning depend on which
        // was tried first. Probed with a date whose day and month are both twelve or less and
        // different from each other, which is exactly the text a day/month swap can misread.
        string[] everything = DateValue.BuiltInFormats.Concat(declared).ToArray();

        foreach (string format in declared)
        {
            string written = Probe.ToString(format, CultureInfo.InvariantCulture);

            DateTimeOffset.TryParseExact(
                written, format, CultureInfo.InvariantCulture, DateValue.Universal, out DateTimeOffset own);

            foreach (string other in everything)
            {
                if (ReferenceEquals(other, format) || other == format)
                {
                    continue;
                }

                if (DateTimeOffset.TryParseExact(
                        written, other, CultureInfo.InvariantCulture, DateValue.Universal,
                        out DateTimeOffset theirs)
                    && theirs != own)
                {
                    throw new ArgumentException(
                        $"The date formats '{format}' and '{other}' read '{written}' as two different " +
                        "dates. Declare one day/month order, not both.",
                        nameof(Formats));
                }
            }
        }

        // Last, because the checks above name a sharper reason for the formats they refuse. A format
        // that writes what ISO 8601 already reads is redundant at best, and at worst says something
        // else: "yyyy-MM-dd'T'HH:mm:ss'Z'" writes the Z as a letter, so it reads 12:00 as a wall time
        // where ISO 8601 reads it as an instant. On a DateTime member the ISO reading converts to the
        // host's local time, so off UTC the two readings differ and every such value is refused as
        // ambiguous — on that host only. Refused here instead, where the answer is the same everywhere.
        foreach (string format in declared)
        {
            foreach (DateTimeOffset probe in Probes)
            {
                string text = probe.ToString(format, CultureInfo.InvariantCulture);

                if (DateValue.ReadsAsBuiltIn(text))
                {
                    throw new ArgumentException(
                        $"The date format '{format}' writes '{text}', which ISO 8601 or a year-first date "
                        + "already reads. Declaring it can only change what such a value means, and what it "
                        + "means would then depend on the host's time zone. Remove it.",
                        nameof(Formats));
                }
            }
        }

        // Two formats need not read the same text to disagree. With "dd/MM/yyyy HH:mm" beside
        // "MM/dd/yyyy", "01/09/2026 00:00" is 1 September and "01/09/2026" is 9 January: one client
        // gets a different day by leaving out the time. Every declared format that leads with a day or
        // a month has to lead with the same one.
        string? dayOrder = null;

        foreach (string format in declared)
        {
            bool? dayFirst = DayFirst(format);

            if (dayFirst is null)
            {
                continue;
            }

            if (dayOrder is not null && DayFirst(dayOrder) != dayFirst)
            {
                throw new ArgumentException(
                    $"The date formats '{dayOrder}' and '{format}' put the day and the month in opposite " +
                    "orders, so the same numbers would name two dates depending on which shape a client " +
                    "sends. Declare one day/month order.",
                    nameof(Formats));
            }

            dayOrder ??= format;
        }

        _frozen = new ReadOnlyCollection<string>(declared);
    }

    /// <summary>
    /// Refuses a format that cannot write a date and read the same date back.
    /// </summary>
    /// <remarks>
    /// A part the format writes has to come back as written. <c>hh</c> without <c>tt</c> writes 4 PM
    /// as <c>04</c> and reads it as 4 AM. A part the format does not write is completed by the parser,
    /// which is harmless for a missing day or month — they read as the first — and not for a missing
    /// year, which is taken from the clock: <c>dd/MM</c> names a different date every January, and
    /// <c>HH:mm</c> every midnight. A day with no month is a typo, most often <c>mm</c> (minutes) for
    /// <c>MM</c>.
    /// </remarks>
    private static void ReadsBack(string format, DateTimeOffset probe)
    {
        string written;

        try
        {
            written = probe.ToString(format, CultureInfo.InvariantCulture);
        }
        catch (FormatException malformed)
        {
            throw new ArgumentException(
                $"'{format}' is not a valid .NET date format: {malformed.Message}", nameof(Formats), malformed);
        }

        if (!DateTimeOffset.TryParseExact(
                written, format, CultureInfo.InvariantCulture, DateValue.Universal, out DateTimeOffset read))
        {
            throw new ArgumentException(
                $"The date format '{format}' cannot read the text it writes ('{written}'), so no value " +
                "could ever match it.",
                nameof(Formats));
        }

        bool Writes(Func<DateTimeOffset, DateTimeOffset> change) =>
            change(probe).ToString(format, CultureInfo.InvariantCulture) != written;

        if (!Writes(date => date.AddYears(1)))
        {
            throw new ArgumentException(
                $"The date format '{format}' carries no year, so '{written}' would be read in whichever " +
                "year the query happens to run. Declare a format with a year.",
                nameof(Formats));
        }

        if (Writes(date => date.AddDays(1)) && !Writes(date => date.AddMonths(1)))
        {
            throw new ArgumentException(
                $"The date format '{format}' carries a day but no month. 'mm' is minutes; the month is 'MM'.",
                nameof(Formats));
        }

        (string Part, Func<DateTimeOffset, DateTimeOffset> Change, Func<DateTimeOffset, int> Value)[] parts =
        {
            ("year", date => date.AddYears(1), date => date.Year),
            ("month", date => date.AddMonths(1), date => date.Month),
            ("day", date => date.AddDays(1), date => date.Day),
            ("hour", date => date.AddHours(1), date => date.Hour),
            ("minute", date => date.AddMinutes(1), date => date.Minute),
            ("second", date => date.AddSeconds(1), date => date.Second)
        };

        foreach (var (part, change, value) in parts)
        {
            if (Writes(change) && value(read) != value(probe))
            {
                throw new ArgumentException(
                    $"The date format '{format}' writes the {part} of {probe:yyyy-MM-dd HH:mm:ss} as " +
                    $"'{written}' and reads it back as {value(read)}, not {value(probe)}" +
                    (part == "hour" ? " — 'hh' is a 12-hour clock and needs 'tt'." : "."),
                    nameof(Formats));
            }
        }
    }

    /// <summary>
    /// Whether a format that leads with a day or a month leads with the day; null for one that does
    /// neither, or writes no numeric day and month.
    /// </summary>
    /// <remarks>
    /// Read from what the format writes for 28 November 2019, where the day, the month and every
    /// other part are distinct numbers: <c>28</c> and <c>11</c> are found only where the day and the
    /// month are. A format that writes the year first is left out, because a leading year is what
    /// makes a date unambiguous in the first place.
    /// </remarks>
    private static bool? DayFirst(string format)
    {
        string written = OrderProbe.ToString(format, CultureInfo.InvariantCulture);

        int day = written.IndexOf("28", StringComparison.Ordinal);
        int month = written.IndexOf("11", StringComparison.Ordinal);

        if (day < 0 || month < 0)
        {
            return null;
        }

        int year = written.IndexOf("2019", StringComparison.Ordinal);

        if (year < 0)
        {
            year = written.IndexOf("19", StringComparison.Ordinal);
        }

        if (year >= 0 && year < day && year < month)
        {
            return null;
        }

        return day < month;
    }

    /// <summary>3 February 2026, 04:05:06.7 — every field distinct, day and month both twelve or less.</summary>
    private static readonly DateTimeOffset Probe = new(2026, 2, 3, 4, 5, 6, 700, TimeSpan.Zero);

    /// <summary>
    /// 28 November 2019, 16:45:30.25 — an afternoon, another year, and a day and month that no other
    /// part repeats.
    /// </summary>
    private static readonly DateTimeOffset OrderProbe = new(2019, 11, 28, 16, 45, 30, 250, TimeSpan.Zero);

    /// <summary>Both probes: two years, a morning and an afternoon.</summary>
    /// <remarks>
    /// Two years because a format with no year reads the current one and the process may be running
    /// in either; an afternoon because a 12-hour clock with no designator only fails after noon.
    /// </remarks>
    private static readonly DateTimeOffset[] Probes = { Probe, OrderProbe };
}

/// <summary>
/// Configures which date formats condition values may use.
/// </summary>
/// <example>
/// <code>
/// // A day-first API: "01/09/2026" is 1 September.
/// DwDates.Configure(o => o.Formats.Add("dd/MM/yyyy"));
///
/// // Or from configuration:  "DynamicWhere": { "Dates": { "Formats": [ "dd/MM/yyyy" ] } }
/// DwDates.Configure(new DwDateOptions().Bind(configuration.GetSection("DynamicWhere:Dates")));
/// </code>
/// </example>
public static class DwDates
{
    private static volatile DwDateOptions _options = Frozen(new DwDateOptions());
    private static int _configured;

    /// <summary>The formats in force. Frozen; with nothing configured it declares none.</summary>
    public static DwDateOptions Options => _options;

    /// <summary>True once <see cref="Configure(DwDateOptions)"/> has been called.</summary>
    public static bool IsConfigured => _configured == 1;

    /// <summary>
    /// Sets the accepted date formats for the process. Call it once, at startup.
    /// </summary>
    /// <param name="options">The formats. Frozen by this call.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a format is blank or not a valid .NET format, cannot read back the text it writes
    /// (<c>hh</c> without <c>tt</c>), carries no year, or a day but no month; when two formats read one
    /// text as different dates or put the day and the month in opposite orders; or when a format writes
    /// text ISO 8601 or a year-first date already reads.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown on a second call.</exception>
    /// <remarks>
    /// Once, like the policy posture, because every query reads it without a lock: formats that could
    /// change mid-flight would let two requests sent the same text filter on different dates.
    /// </remarks>
    public static void Configure(DwDateOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        options.Freeze();

        if (Interlocked.CompareExchange(ref _configured, 1, 0) != 0)
        {
            throw new InvalidOperationException(
                "Date formats are already configured. DwDates.Configure is called once, at startup.");
        }

        _options = options;
    }

    /// <summary>
    /// Sets the accepted date formats for the process from a callback. Call it once, at startup.
    /// </summary>
    /// <param name="configure">Fills in a fresh <see cref="DwDateOptions"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configure"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the formats are refused, as <see cref="Configure(DwDateOptions)"/> refuses them.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown on a second call.</exception>
    public static void Configure(Action<DwDateOptions> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        DwDateOptions options = new();

        configure(options);

        Configure(options);
    }

    /// <summary>
    /// Reads date formats from a configuration section, refusing any key it does not recognise.
    /// </summary>
    /// <param name="options">The options to fill in. Must not be frozen.</param>
    /// <param name="section">The section, e.g. <c>DynamicWhere:Dates</c>. An absent one adds nothing.</param>
    /// <returns>The same instance, for chaining into <see cref="Configure(DwDateOptions)"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the options are frozen, when the section names a key nothing answers to — so a
    /// misspelt <c>Fromats</c> refuses to start rather than leaving the deployment on the defaults —
    /// or when a single value stands where the list of formats belongs.
    /// </exception>
    public static DwDateOptions Bind(this DwDateOptions options, IConfiguration section)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (section is null)
        {
            throw new ArgumentNullException(nameof(section));
        }

        if (options.IsFrozen)
        {
            throw new InvalidOperationException("Date options cannot be changed once configured.");
        }

        // A value where the list belongs binds nothing and raises nothing: "Formats": "dd/MM/yyyy"
        // — the likeliest way to write one format by hand, and the only way an environment variable
        // can — would leave the deployment on the defaults exactly as a misspelt key would.
        IConfigurationSection formats = section.GetSection(nameof(DwDateOptions.Formats));

        if ((section as IConfigurationSection)?.Value is not null || formats.Value is not null)
        {
            throw new InvalidOperationException(
                $"'{formats.Path}' is a list of date formats, not a single value. Write each format " +
                $"as an element: \"Formats\": [ \"dd/MM/yyyy\" ], or {formats.Path}:0 as a key.");
        }

        section.Bind(options, binder => binder.ErrorOnUnknownConfiguration = true);

        return options;
    }

    private static DwDateOptions Frozen(DwDateOptions options)
    {
        options.Freeze();

        return options;
    }
}
