using DynamicWhere.ex.Enums;

namespace DynamicWhere.ex.Policies.DTOs;

/// <summary>
/// Everything about one type's policy that cannot be answered one field at a time: the names a
/// caller may use, the predicates the library will add, and the fields the caller must filter on.
/// </summary>
/// <remarks>
/// <see cref="FieldPolicy"/> answers "what may this caller do with this field", which presupposes a
/// field path. The sanitizer needs the other direction first — it holds a name the caller wrote and
/// has to discover which field that is, and it has to know what to inject before it has looked at
/// any field at all. One sweep of the providers answers both, per query.
/// <para>
/// Resolved against a context, and deliberately not cached per type. A runtime rule may set an
/// alias, so the public vocabulary varies by caller: two subjects can spell the same field
/// differently, and one of them can have a name the other does not.
/// </para>
/// </remarks>
public sealed class TypePolicy
{
    /// <summary>An empty policy, for a type nothing speaks to.</summary>
    internal static TypePolicy Empty { get; } = new(
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
        Array.Empty<ForcedPredicate>(),
        new Dictionary<string, IReadOnlyList<Operator>>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Initializes a type policy. The instance takes ownership of every collection handed to it;
    /// they are stored by reference rather than copied.
    /// </summary>
    /// <param name="aliases">Each public name, mapped to the field paths it could mean.</param>
    /// <param name="forced">Every predicate to add to a query on this type.</param>
    /// <param name="required">Each field the caller must filter on, mapped to the satisfying operators.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public TypePolicy(
        IReadOnlyDictionary<string, IReadOnlyList<string>> aliases,
        IReadOnlyList<ForcedPredicate> forced,
        IReadOnlyDictionary<string, IReadOnlyList<Operator>> required)
    {
        Aliases = aliases ?? throw new ArgumentNullException(nameof(aliases));
        Forced = forced ?? throw new ArgumentNullException(nameof(forced));
        Required = required ?? throw new ArgumentNullException(nameof(required));
    }

    /// <summary>
    /// Each public name, mapped to the field paths it could mean.
    /// </summary>
    /// <remarks>
    /// A name maps to a <em>list</em> because it can genuinely mean more than one field: an alias
    /// can collide with another alias, and an aliased type reached by two navigations gives one name
    /// two paths. Resolving such a name in favour of either is a coin flip that silently sends a
    /// filter somewhere the caller did not mean, so the sanitizer refuses it. Recording only the
    /// first would hide the collision instead.
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Aliases { get; }

    /// <summary>
    /// Every predicate to add to a query on this type, joined by <c>And</c>.
    /// </summary>
    /// <remarks>
    /// Accumulated across sources rather than elected. A conjunction can only narrow, so an extra
    /// predicate is always safe; electing one would let a low-authority rule discard a sealed
    /// tenant scope.
    /// </remarks>
    public IReadOnlyList<ForcedPredicate> Forced { get; }

    /// <summary>
    /// Each field the caller must filter on, mapped to the operators that satisfy the requirement.
    /// </summary>
    /// <remarks>
    /// An empty operator list is a requirement nothing satisfies, which is not the same as the field
    /// being absent from this map.
    /// </remarks>
    public IReadOnlyDictionary<string, IReadOnlyList<Operator>> Required { get; }

    /// <summary>
    /// True when this type's policy adds nothing to a query.
    /// </summary>
    /// <remarks>
    /// The sanitizer skips three whole passes on this, which is what keeps a type nobody has an
    /// opinion about generating the same work — and the same SQL — as the unguarded path.
    /// </remarks>
    public bool IsEmpty => Aliases.Count == 0 && Forced.Count == 0 && Required.Count == 0;
}
