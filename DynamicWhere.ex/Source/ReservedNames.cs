using DynamicWhere.ex.Exceptions;

namespace DynamicWhere.ex.Source;

/// <summary>
/// The names System.Linq.Dynamic.Core keeps for itself when one begins a field path.
/// </summary>
/// <remarks>
/// Every expression the library builds is text the parser reads back, and it reads its own functions and
/// literals before it looks for a member. A path beginning with one of these never reaches the member:
/// <c>New</c>, <c>Iif</c>, <c>Np</c>, <c>IsNull</c>, <c>Is</c>, <c>As</c> and <c>Cast</c> raised the
/// parser's own <c>ParseException</c>, <c>True</c> and <c>False</c> an <c>InvalidOperationException</c>,
/// and <c>Null</c> was read as the null literal, so the predicate compared null with the caller's value
/// and the query returned no rows and no error.
/// <para>
/// Refused here by name, where a path is validated, so every clause answers the same way and the answer
/// is the library's own. A member that cannot be reached cannot be filtered, sorted, grouped, aggregated
/// or projected; rename the property and map the column with <c>[Column]</c>.
/// </para>
/// <para>
/// Only the first segment is affected: <c>Owner.New</c> reads the member, because the parser looks for a
/// member after a dot. The parser's context keywords — <c>it</c>, <c>root</c>, <c>parent</c> and
/// <c>outerIt</c> — are off (see <see cref="DynamicLinq"/>), so they name members like any other
/// identifier, and its predefined type names (<c>String</c>, <c>Math</c>, <c>Guid</c>, <c>Uri</c> and the
/// rest) are read as members too. Neither is listed here.
/// </para>
/// </remarks>
internal static class ReservedNames
{
    /// <summary>The names, matched whatever their letter case, as the parser matches them.</summary>
    internal static readonly IReadOnlyCollection<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "new", "iif", "np", "isnull", "is", "as", "cast", "true", "false", "null",
    };

    /// <summary>True when a path begins with a name the parser keeps for itself.</summary>
    /// <param name="propertyPath">The path as the caller wrote it.</param>
    internal static bool Starts(string? propertyPath)
    {
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            return false;
        }

        string first = propertyPath!.Split('.')[0].Trim();

        return ((HashSet<string>)All).Contains(first);
    }

    /// <summary>Refuses a path the parser would read as its own.</summary>
    /// <param name="propertyPath">The path as the caller wrote it.</param>
    /// <exception cref="LogicException">
    /// Thrown with <c>FieldPath[{path}]StartsWithReservedName</c> when the first segment is one of these
    /// names. <c>LogicException.Subject</c> carries that segment.
    /// </exception>
    internal static void Refuse(string? propertyPath)
    {
        if (!Starts(propertyPath))
        {
            return;
        }

        throw new LogicException(
            $"FieldPath[{propertyPath!.Trim()}]StartsWithReservedName", propertyPath.Split('.')[0].Trim());
    }
}
