using System.Globalization;
using System.Text.RegularExpressions;
using DynamicWhere.ex.Exceptions;

namespace DynamicWhere.ex.Source;

/// <summary>
/// Reads a date condition value the one way validation and the predicate builder both use.
/// </summary>
/// <remarks>
/// One reader rather than two, because two drifted: validation used to check a value in the host's
/// culture while the builder parsed it in the invariant one. Both now ask this class, with the
/// member's type, and get the same answer.
/// <para>
/// A value is read against an explicit list of formats, never the lenient parser. The lenient parser
/// accepts <c>12:00</c> as today at noon and <c>1/9</c> as 9 January of this year, and reads
/// <c>01/09/2026</c> month-first without a word — each a filter on a date nobody asked for.
/// </para>
/// </remarks>
internal static class DateValue
{
    /// <summary>
    /// What a date member is, which decides the literal, the null guard and whether <c>.Date</c> exists.
    /// </summary>
    internal enum Kind
    {
        /// <summary>A <c>DateTime</c>, or a member of unknown type.</summary>
        Local,

        /// <summary>A <c>DateTimeOffset</c>.</summary>
        Offset,

        /// <summary>A <c>DateOnly</c>.</summary>
        DateOnly
    }

    /// <summary>
    /// The styles a <c>DateTimeOffset</c> or <c>DateOnly</c> value is read with.
    /// </summary>
    /// <remarks>
    /// <c>AssumeUniversal</c> so a value with no zone names the day the caller wrote, and
    /// <c>AdjustToUniversal</c> so a value with one becomes the same instant at a zero offset. Both
    /// matter to <c>DataType.Date</c>, where the offset decides which calendar day is compared.
    /// </remarks>
    internal const DateTimeStyles Universal = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

    /// <summary>
    /// ISO 8601 and year-first dates, accepted by every deployment whatever else it declares.
    /// </summary>
    /// <remarks>
    /// Year first, so none can be read two ways. <c>M</c>, <c>d</c> and <c>H</c> take one or two
    /// digits; the fraction and the zone (<c>Z</c> or an offset) are optional; the time may follow a
    /// <c>T</c> or a space. What the library writes for a C# date placed in <c>Values</c> is one of these.
    /// </remarks>
    internal static readonly string[] BuiltInFormats = Build();

    /// <summary>
    /// A numeric date that leads with a day or a month: <c>01/09/2026</c>, <c>1.9.26</c>, <c>01-09-2026</c>.
    /// </summary>
    private static readonly Regex DayOrMonthFirst = new(
        @"^\d{1,2}[/.\-]\d{1,2}[/.\-]\d{2,4}(?!\d)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Classifies a member's type.</summary>
    /// <param name="memberType">The member's CLR type, or null where it is not known.</param>
    internal static Kind KindOf(Type? memberType)
    {
        Type? type = memberType is null ? null : Nullable.GetUnderlyingType(memberType) ?? memberType;

        if (type == typeof(DateTimeOffset))
        {
            return Kind.Offset;
        }

        if (type == typeof(DateOnly))
        {
            return Kind.DateOnly;
        }

        return Kind.Local;
    }

    /// <summary>
    /// Reads a value with the formats configured for this process.
    /// </summary>
    /// <inheritdoc cref="Read(string, Type?, string?, DwDateOptions)"/>
    internal static string Read(string value, Type? memberType, string? field) =>
        Read(value, memberType, field, DwDates.Options);

    /// <summary>
    /// Reads a value for a member of the given type and returns the text to embed in the predicate.
    /// </summary>
    /// <param name="value">The value as the caller sent it.</param>
    /// <param name="memberType">
    /// The member's CLR type, or null where it is not known — read as a <c>DateTime</c>, the shape that
    /// has always shipped for that case.
    /// </param>
    /// <param name="field">The field or alias, carried on the refusal as its subject.</param>
    /// <param name="options">The formats this deployment declared.</param>
    /// <returns>
    /// Round-trip text for a <c>DateTime</c> (no zone marker, so it is not converted again) or a
    /// <c>DateTimeOffset</c> (UTC), and <c>yyyy-MM-dd</c> for a <c>DateOnly</c>.
    /// </returns>
    /// <exception cref="LogicException">
    /// <c>AmbiguousDateFormat</c> when the value leads with a day or a month and no accepted format
    /// reads it, or when two accepted formats read it as different dates; <c>InvalidFormat</c> when
    /// it is not a date in any accepted format.
    /// </exception>
    internal static string Read(string value, Type? memberType, string? field, DwDateOptions options)
    {
        string text = (value ?? string.Empty).Trim();
        Kind kind = KindOf(memberType);

        HashSet<string> readings = new(StringComparer.Ordinal);

        Collect(text, BuiltInFormats, kind, readings);

        foreach (string format in options.Formats)
        {
            Collect(text, new[] { format }, kind, readings);
        }

        if (readings.Count == 1)
        {
            return readings.First();
        }

        if (readings.Count > 1 || DayOrMonthFirst.IsMatch(text))
        {
            throw new LogicException(ErrorCode.AmbiguousDateFormat, field);
        }

        throw new LogicException(ErrorCode.InvalidFormat, field);
    }

    /// <summary>
    /// Adds what the formats read the text as, in the form the predicate will embed.
    /// </summary>
    private static void Collect(string text, string[] formats, Kind kind, HashSet<string> readings)
    {
        switch (kind)
        {
            case Kind.Offset:
                if (DateTimeOffset.TryParseExact(
                        text, formats, CultureInfo.InvariantCulture, Universal, out DateTimeOffset offset))
                {
                    readings.Add(offset.ToString("o", CultureInfo.InvariantCulture));
                }
                break;

            case Kind.DateOnly:
                if (DateTimeOffset.TryParseExact(
                        text, formats, CultureInfo.InvariantCulture, Universal, out DateTimeOffset day))
                {
                    readings.Add(day.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }
                break;

            default:
                // Default styles, as a DateTime member always had: a zoned value converts to the host's
                // local time, the reading a timestamp without time zone is compared against.
                if (DateTime.TryParseExact(
                        text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
                {
                    readings.Add(local.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture));
                }
                break;
        }
    }

    private static string[] Build()
    {
        string[] dates = { "yyyy-M-d", "yyyy/M/d", "yyyy.M.d" };
        string[] times = { "H:mmK", "H:mm:ss.FFFFFFFK" };

        List<string> formats = new();

        foreach (string date in dates)
        {
            formats.Add(date);

            foreach (string separator in new[] { "'T'", " " })
            {
                foreach (string time in times)
                {
                    formats.Add(date + separator + time);
                }
            }
        }

        return formats.ToArray();
    }
}
