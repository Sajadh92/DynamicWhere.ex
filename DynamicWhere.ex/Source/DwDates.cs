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
    /// </remarks>
    public IList<string> Formats => _frozen ?? _formats;

    /// <summary>True once these options have been handed to <see cref="DwDates.Configure(DwDateOptions)"/>.</summary>
    public bool IsFrozen => _frozen is not null;

    /// <summary>
    /// Checks every declared format and prevents any further change.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when a format is blank, cannot read the text it writes, carries no year, or reads text
    /// another accepted format also reads as a different date.
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
                string written = probe.ToString(format, CultureInfo.InvariantCulture);

                if (!DateTimeOffset.TryParseExact(
                        written, format, CultureInfo.InvariantCulture, DateValue.Universal,
                        out DateTimeOffset read))
                {
                    throw new ArgumentException(
                        $"The date format '{format}' cannot read the text it writes ('{written}'), so " +
                        "no value could ever match it.",
                        nameof(Formats));
                }

                // A format with no year is completed from the clock: "dd/MM" reads the current year
                // and "HH:mm" today, so the same value names a different date depending on when the
                // query runs. That is the guess this whole mechanism exists to refuse.
                if (read.Year != probe.Year)
                {
                    throw new ArgumentException(
                        $"The date format '{format}' carries no year, so '{written}' would be read in " +
                        "whichever year the query happens to run. Declare a format with a year.",
                        nameof(Formats));
                }
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

        _frozen = new ReadOnlyCollection<string>(declared);
    }

    /// <summary>3 February 2026, 04:05:06.7 — every field distinct, day and month both twelve or less.</summary>
    private static readonly DateTimeOffset Probe = new(2026, 2, 3, 4, 5, 6, 700, TimeSpan.Zero);

    /// <summary>
    /// <see cref="Probe"/> and a date in another year, both inside the two-digit-year window.
    /// </summary>
    /// <remarks>
    /// Two years because a format with no year reads the current one, and the process may be running
    /// in the year of either probe — never in both.
    /// </remarks>
    private static readonly DateTimeOffset[] Probes =
    {
        Probe,
        new(2019, 11, 28, 16, 45, 30, 250, TimeSpan.Zero)
    };
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
    /// <exception cref="ArgumentException">Thrown when the formats contradict each other.</exception>
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
