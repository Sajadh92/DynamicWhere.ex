using System.Globalization;
using System.Text.Json;

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
}
