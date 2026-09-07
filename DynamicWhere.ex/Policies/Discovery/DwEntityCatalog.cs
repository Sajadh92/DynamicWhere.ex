namespace DynamicWhere.ex.Policies.Discovery;

/// <summary>
/// The types an administrative surface may be asked about, and the names it may ask by.
/// </summary>
/// <remarks>
/// A security boundary rather than a convenience. Rules match on <c>Type.FullName</c>, so a schema
/// endpoint that resolved a name straight to a type would let whoever reaches it enumerate every
/// type in every loaded assembly — the entity model, the framework, and whatever else happens to be
/// referenced. Only what a host lists here can be described.
/// <para>
/// It doubles as the argument list <c>DwPolicy.ValidateModel</c> already takes, since the types an
/// application exposes are the types whose policy model is worth checking at startup.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// options.Entities.Expose&lt;Employee&gt;("staff").Expose&lt;Invoice&gt;();
/// </code>
/// </example>
public sealed class DwEntityCatalog
{
    private readonly Dictionary<string, Type> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Type, string> _names = new();

    private bool _frozen;

    /// <summary>Every type exposed, with the name it answers to.</summary>
    public IReadOnlyDictionary<Type, string> Entities => _names;

    /// <summary>True when nothing has been exposed.</summary>
    public bool IsEmpty => _names.Count == 0;

    /// <summary>
    /// Exposes a type under a public name.
    /// </summary>
    /// <typeparam name="T">The type to expose.</typeparam>
    /// <param name="name">The name to ask by, or null to use the type's own short name.</param>
    /// <returns>This catalogue, for chaining.</returns>
    public DwEntityCatalog Expose<T>(string? name = null) => Expose(typeof(T), name);

    /// <summary>
    /// Exposes a type under a public name.
    /// </summary>
    /// <param name="type">The type to expose.</param>
    /// <param name="name">The name to ask by, or null to use the type's own short name.</param>
    /// <returns>This catalogue, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="type"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the name is blank, or is already taken by another type.
    /// </exception>
    /// <exception cref="InvalidOperationException">Thrown after startup.</exception>
    public DwEntityCatalog Expose(Type type, string? name = null)
    {
        if (_frozen)
        {
            throw new InvalidOperationException(
                "The entity catalogue cannot change after startup. What an administrative surface " +
                "may be asked about is read without synchronization by every request that asks.");
        }

        if (type is null)
        {
            throw new ArgumentNullException(nameof(type));
        }

        string public_ = string.IsNullOrWhiteSpace(name) ? type.Name : name!.Trim();

        // Refused rather than resolved in favour of either. One name meaning two entities sends an
        // operator writing a rule to a type they did not mean, and a rule aimed at the wrong entity
        // matches nothing — which reads, to whoever wrote it, as a control that is in force.
        if (_byName.TryGetValue(public_, out Type? taken) && taken != type)
        {
            throw new ArgumentException(
                $"The name '{public_}' is already exposed by '{taken.FullName}', so it cannot also " +
                $"stand for '{type.FullName}'. Give one of them a name of its own.",
                nameof(name));
        }

        _byName[public_] = type;
        _names[type] = public_;

        return this;
    }

    /// <summary>
    /// The type answering to a public name, or null when nothing does.
    /// </summary>
    /// <param name="name">The name to look up. Compared case-insensitively.</param>
    /// <remarks>
    /// Null rather than an exception, so an endpoint answers a bad name with a not-found rather than
    /// a stack trace — and answers identically whether the type does not exist or merely was not
    /// exposed. The difference between those two is exactly what an enumeration attempt is looking
    /// for.
    /// </remarks>
    public Type? Resolve(string? name) =>
        !string.IsNullOrWhiteSpace(name) && _byName.TryGetValue(name!.Trim(), out Type? type)
            ? type
            : null;

    /// <summary>The public name of an exposed type, or null when it was never exposed.</summary>
    /// <param name="type">The type to name.</param>
    public string? NameOf(Type type) =>
        type is not null && _names.TryGetValue(type, out string? name) ? name : null;

    /// <summary>The types exposed, for a startup model check.</summary>
    public Type[] ToArray()
    {
        Type[] types = new Type[_names.Count];

        _names.Keys.CopyTo(types, 0);

        return types;
    }

    /// <summary>Prevents any further change.</summary>
    public void Freeze() => _frozen = true;
}
