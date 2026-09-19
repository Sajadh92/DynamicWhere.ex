using System.Collections;
using System.Reflection;
using DynamicWhere.ex.Source;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DynamicWhere.ex.Policies.Source;

/// <summary>
/// What a guarded query's source puts in the members of its rows, as far as a projection the library
/// synthesizes is concerned.
/// </summary>
/// <remarks>
/// A caller who sends no projection is owed what an unguarded call would return, less what the policy
/// withholds. Which members that is depends on the source. An entity query loads a navigation only
/// when something includes it; a projection computes every member it assigns; rows already in memory
/// hold everything. A synthesized projection that left out every navigation was right for the first
/// and emptied every list and nested object of the other two.
/// <para>
/// Read from the query itself, never from the rows, so the sanitizer stays pure: this is decided once,
/// before it runs.
/// </para>
/// </remarks>
internal sealed class RowShape
{
    /// <summary>An entity query whose model could not be read: no navigation is known to load.</summary>
    internal static readonly RowShape Entity = new(RowKind.Entity, Empty(), Empty());

    /// <summary>A projection that builds its rows, whose every member holds what the projection gave it.</summary>
    internal static readonly RowShape Projected = new(RowKind.Projected, Empty(), Empty());

    /// <summary>Rows held in memory, whose every member holds whatever the object holds.</summary>
    internal static readonly RowShape InMemory = new(RowKind.InMemory, Empty(), Empty());

    private readonly HashSet<string> _alwaysLoaded;
    private readonly HashSet<string> _complex;

    private RowShape(RowKind kind, HashSet<string> alwaysLoaded, HashSet<string> complex)
    {
        Kind = kind;
        _alwaysLoaded = alwaysLoaded;
        _complex = complex;
    }

    /// <summary>Where the rows come from.</summary>
    internal RowKind Kind { get; }

    /// <summary>
    /// True when rows from this source carry a value in the member whatever the caller includes: every
    /// member of a projected or in-memory row, and an entity's owned and complex members, which EF Core
    /// loads with the entity on every query.
    /// </summary>
    internal bool Carries(string member) => Kind != RowKind.Entity || _alwaysLoaded.Contains(member);

    /// <summary>
    /// True when the core's projection can narrow the member to some of the fields beneath it. Not in
    /// memory, where its narrowing of a reference reads it through EF Core, and not for an EF Core
    /// complex property, which it compares to null and EF Core refuses to.
    /// </summary>
    internal bool Narrows(string member) => Kind != RowKind.InMemory && !_complex.Contains(member);

    /// <summary>Reads the shape of a guarded query's source.</summary>
    /// <remarks>
    /// Rows in memory first, because a projection over them is still in memory. Then a projection that
    /// builds its rows, which is how a caller makes its own row type out of entities. What is left is an
    /// entity query, or a projection handing back entities, <c>Select(o =&gt; o.Customer)</c>. Their owned
    /// and complex members are read from the EF Core model; a navigation the entity does not own loads
    /// only when something includes it, and a guarded query that has to narrow the row leaves such a
    /// navigation out, as it always has. Projecting one would load it, and return more than the
    /// unguarded call does.
    /// </remarks>
    internal static RowShape Of<T>(IQueryable<T> source) where T : class
    {
        if (source.Provider is EnumerableQuery)
        {
            return InMemory;
        }

        if (DefaultOrder.BuildsRows(source.Expression))
        {
            return Projected;
        }

        IEntityType? entityType = QueryRoot.EntityType(source.Expression, typeof(T));

        if (entityType is null)
        {
            return Entity;
        }

        HashSet<string> alwaysLoaded = Empty();
        HashSet<string> complex = Empty();

        foreach (INavigation navigation in entityType.GetNavigations())
        {
            if (navigation.ForeignKey.IsOwnership && !navigation.IsOnDependent)
            {
                alwaysLoaded.Add(navigation.Name);
            }
        }

        foreach (string name in ComplexProperties(entityType))
        {
            alwaysLoaded.Add(name);
            complex.Add(name);
        }

        return alwaysLoaded.Count == 0 ? Entity : new RowShape(RowKind.Entity, alwaysLoaded, complex);
    }

    private static HashSet<string> Empty() => new(StringComparer.Ordinal);

    /// <summary>
    /// The names of an entity type's complex properties, which EF Core 8 introduced and loads with the
    /// entity. None on an earlier version, where the method does not exist.
    /// </summary>
    /// <remarks>
    /// Read by reflection because the library compiles against EF Core 6. The method is declared on an
    /// interface the entity type implements, so the interfaces are searched rather than the class.
    /// </remarks>
    private static IEnumerable<string> ComplexProperties(IEntityType entityType)
    {
        foreach (Type contract in entityType.GetType().GetInterfaces())
        {
            MethodInfo? method = contract.GetMethod("GetComplexProperties", Type.EmptyTypes);

            if (method is null)
            {
                continue;
            }

            if (method.Invoke(entityType, null) is IEnumerable properties)
            {
                foreach (object? property in properties)
                {
                    if (property?.GetType().GetProperty("Name")?.GetValue(property) is string name)
                    {
                        yield return name;
                    }
                }
            }

            yield break;
        }
    }
}

/// <summary>Where a guarded query's rows come from.</summary>
internal enum RowKind
{
    /// <summary>An entity query: a navigation holds a value only when something loads it.</summary>
    Entity,

    /// <summary>A projection that builds its rows: every member holds what the projection gave it.</summary>
    Projected,

    /// <summary>A sequence in memory: every member holds whatever the object holds.</summary>
    InMemory
}
