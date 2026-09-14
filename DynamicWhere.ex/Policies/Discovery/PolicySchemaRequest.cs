namespace DynamicWhere.ex.Policies.Discovery;

/// <summary>
/// What part of an entity to describe, and how much of it.
/// </summary>
/// <remarks>
/// A typed request rather than a parsed string. The earlier shape took the paths as one
/// comma-separated parameter, which is a bug waiting for the alias that contains a comma — and
/// nothing forbids one: an alias is refused only when it is blank, dotted, or the wildcard, so
/// <c>[DwAlias("last, first")]</c> compiles, passes the startup scan, and would be split into two
/// paths that resolve to nothing. A list removes the class rather than the instance.
/// <para>
/// Every member is optional, and an absent one means the deployment's default rather than a
/// special value. That is what lets the common request be no request at all.
/// </para>
/// </remarks>
public sealed class PolicySchemaRequest
{
    /// <summary>
    /// Where the walk starts, or null to start at the entity itself.
    /// </summary>
    /// <remarks>
    /// Each entry is a navigation path, in any spelling a filter accepts — a canonical path or an
    /// alias. Several are answered in one response, so a field picker opening three branches makes
    /// one request rather than three.
    /// <para>
    /// Two roots can overlap: asking for <c>Manager</c> and <c>Manager.Address</c> together reaches
    /// the city from both. A field is listed once, under one parent, because listing it twice would
    /// leave a picker showing one column two ways.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string>? Paths { get; init; }

    /// <summary>
    /// How many levels of type to walk from each root, or null for <c>DwCaps.SchemaDepth</c>.
    /// </summary>
    /// <remarks>
    /// One is the root's own fields and nothing else; two adds a level of navigation. A value above
    /// what the query cap allows from where the walk starts is clamped down rather than refused,
    /// so a caller wanting everything sends a large number and gets everything the engine permits.
    /// There is deliberately no sentinel for "all of it": a sentinel is a second thing to parse,
    /// and <c>PolicySchema.MaxDepth</c> tells a caller the ceiling on the first response anyway.
    /// </remarks>
    public int? Depth { get; init; }
}
