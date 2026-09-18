using System.Linq.Dynamic.Core;

namespace DynamicWhere.ex.Source;

/// <summary>
/// The configuration every expression string the library builds is parsed with.
/// </summary>
/// <remarks>
/// System.Linq.Dynamic.Core reads <c>it</c>, <c>root</c> and <c>parent</c> as keywords, in any letter
/// case, wherever an identifier can stand. The library writes member paths as they are named, so a
/// navigation called <c>Root</c> or <c>It</c> was read as the row itself — <c>Root.Name</c> filtered,
/// sorted, grouped and projected the row's own <c>Name</c> — and one called <c>Parent</c> could not be
/// parsed at all. Under a policy that split the decision from the query: the gate decided on the path
/// the caller named while the database read another column, so a denied value could be projected
/// and a forced scope landed on the wrong member.
/// <para>
/// With the keywords off, every identifier names a member. An expression that needs the current
/// element uses the <c>$</c> symbol, which the setting leaves available.
/// </para>
/// <para>
/// Everything else is the parser's default. The library used to parse through the shared
/// <see cref="ParsingConfig.Default"/> and no longer reads it: a host's changes to that instance do
/// not reach the library's queries, and this setting does not reach the host's.
/// </para>
/// </remarks>
internal static class DynamicLinq
{
    /// <summary>The configuration passed to every parse the library performs.</summary>
    internal static readonly ParsingConfig Config = new() { AreContextKeywordsEnabled = false };
}
