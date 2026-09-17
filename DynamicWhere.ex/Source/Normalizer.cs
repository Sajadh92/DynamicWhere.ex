using System.Globalization;
using System.Text.Json;
using DynamicWhere.ex.Enums;

namespace DynamicWhere.ex.Source;

/// <summary>
/// Normalizes raw <see cref="object"/> values supplied via <c>Condition.Values</c> into
/// canonical string tokens consumed by <see cref="Validator"/> and <see cref="Builder"/>.
/// <para>
/// Supports heterogeneous input shapes commonly produced by JSON deserializers:
/// <list type="bullet">
///   <item><description><see cref="string"/> — returned as-is.</description></item>
///   <item><description><see cref="bool"/> — emitted as lowercase <c>"true"</c>/<c>"false"</c>.</description></item>
///   <item><description><see cref="JsonElement"/> (System.Text.Json) — unwrapped by <see cref="JsonValueKind"/>.</description></item>
///   <item><description>Numeric / <see cref="IFormattable"/> — formatted with <see cref="CultureInfo.InvariantCulture"/>.</description></item>
///   <item><description>Anything else — falls back to <see cref="object.ToString"/> (covers <c>JValue</c> from Newtonsoft.Json).</description></item>
/// </list>
/// </para>
/// </summary>
internal static class Normalizer
{ 
    /// <summary>
    /// Converts a single raw value into its canonical string representation.
    /// </summary>
    public static string Normalize(object? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return value switch
        {
            string s => s,
            bool b => b ? "true" : "false",
            JsonElement je => je.ValueKind switch
            {
                JsonValueKind.String => je.GetString() ?? string.Empty,
                JsonValueKind.Number => je.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => string.Empty,
                _ => je.GetRawText()
            },
            // Dates before the general IFormattable case. Its invariant form is month-first —
            // "09/01/2026 12:00:00" — which is exactly the shape a date value is refused for, so a C#
            // caller placing a DateTime in Values would be refused for sending an unambiguous value.
            // Year-first text reads the same everywhere. A DateTime keeps no zone marker, as it had
            // none before; a DateTimeOffset keeps its offset.
            DateTime dateTime => dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
            DateTimeOffset offset => offset.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture),
            DateOnly day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    /// Normalizes every element of a value list into its string form.
    /// </summary>
    public static List<string> Normalize(IEnumerable<object?> values)
    {
        return values.Select(Normalize).ToList();
    }

    /// <summary>
    /// Normalizes every element of a value list for a condition of the given data type on a member of
    /// the given type.
    /// </summary>
    /// <param name="values">The raw values.</param>
    /// <param name="dataType">The condition's data type.</param>
    /// <param name="memberType">The member's CLR type, or null where it is not known.</param>
    /// <remarks>
    /// A local <see cref="DateTime"/> compared as an instant with a <see cref="DateTimeOffset"/> member
    /// is written with its offset. Without one the text is read as UTC, so on a host at UTC+3
    /// <c>DateTime.Now</c> named a moment three hours after the one it holds, and nothing reported it.
    /// Everywhere else the text keeps no zone, as it always has: a day comparison compares the day the
    /// value was written for, a <c>DateTime</c> member holds wall-clock time, and a <c>DateOnly</c>
    /// member holds a day.
    /// </remarks>
    public static List<string> Normalize(IEnumerable<object?> values, DataType dataType, Type? memberType)
    {
        bool instant = dataType == DataType.DateTime && DateValue.KindOf(memberType) == DateValue.Kind.Offset;

        return values
            .Select(value => instant && value is DateTime { Kind: DateTimeKind.Local } local
                ? local.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz", CultureInfo.InvariantCulture)
                : Normalize(value))
            .ToList();
    }
}
