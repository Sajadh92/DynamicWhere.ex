using System.Globalization;

namespace DynamicWhere.ex.Source;

/// <summary>
/// Reads a date condition value the one way validation and the predicate builder both use.
/// </summary>
/// <remarks>
/// One reader rather than two, because two drifted. Validation used to check a value in the host's
/// culture and the builder then parsed it in the invariant one, so on a day-first server
/// <c>"15/09/2026"</c> passed validation and failed when the predicate was built, and the set of
/// values a filter accepted was whatever both cultures happened to agree on. Both now ask this
/// class, with the member's type, and get the same answer.
/// </remarks>
internal static class DateValue
{
    /// <summary>
    /// True when the member is a <c>DateTimeOffset</c> or a nullable one.
    /// </summary>
    /// <param name="memberType">The member's CLR type, or null where it is not known.</param>
    internal static bool IsOffset(Type? memberType) =>
        memberType is not null
        && (Nullable.GetUnderlyingType(memberType) ?? memberType) == typeof(DateTimeOffset);

    /// <summary>
    /// Reads a value for a member of the given type and writes it back in round-trip form.
    /// </summary>
    /// <param name="value">The value as the caller sent it.</param>
    /// <param name="memberType">
    /// The member's CLR type, or null where it is not known — which reads the value as a
    /// <c>DateTime</c>, the shape that has always shipped for that case.
    /// </param>
    /// <param name="canonical">The literal text to embed, when the value could be read.</param>
    /// <returns>False when the invariant culture cannot read the value.</returns>
    /// <remarks>
    /// The invariant culture, so a filter means the same instant on every server. It reads a slash
    /// date month-first: <c>"01/09/2026"</c> is 9 January everywhere, and <c>"15/09/2026"</c> is not a
    /// date at all. ISO 8601 is the form that means one thing to everyone.
    /// <para>
    /// A <c>DateTimeOffset</c> is read with <c>AssumeUniversal</c> and <c>AdjustToUniversal</c>: a
    /// value with no zone names the day the caller wrote, and a value with one becomes the same
    /// instant at a zero offset, which is what keeps <c>.Date</c> reporting the caller's calendar day.
    /// </para>
    /// <para>
    /// A <c>DateTime</c> keeps the default styles, under which a zoned value is converted to the
    /// host's local time — the reading a <c>timestamp without time zone</c> column is compared
    /// against. It is written back with no zone marker, so parsing the emitted literal does not
    /// convert it a second time.
    /// </para>
    /// </remarks>
    internal static bool TryCanonical(string value, Type? memberType, out string canonical)
    {
        string trimmed = value.Trim();

        if (IsOffset(memberType))
        {
            if (DateTimeOffset.TryParse(
                    trimmed,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset offset))
            {
                canonical = offset.ToString("o", CultureInfo.InvariantCulture);
                return true;
            }
        }
        else if (DateTime.TryParse(
                     trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
        {
            canonical = local.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            return true;
        }

        canonical = string.Empty;
        return false;
    }
}
