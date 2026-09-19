using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Source;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;

namespace DynamicWhere.ex.Policies.Source;

/// <summary>
/// What a guarded query's source puts in the members of its rows, as far as a projection the library
/// synthesizes is concerned.
/// </summary>
/// <remarks>
/// A caller who sends no projection is owed what an unguarded call would return, less what the policy
/// withholds. Which members hold what depends on the source, and so does which denied value could
/// reach the result at all:
/// <list type="bullet">
///   <item><description>
///     An entity query loads the entity's columns, its owned and complex members, and a navigation
///     only when something loads it: an <c>Include</c>, an automatic include or a lazy loader. A value
///     beneath a navigation nothing loads never reaches the result, so a denial there needs no
///     projection; and projecting the navigation would load it.
///   </description></item>
///   <item><description>
///     A projection holds exactly the members its initializer assigns. The others hold defaults, and
///     narrowing one reads it through <c>EF.Property</c>, which EF Core cannot translate for a member
///     the initializer never bound.
///   </description></item>
///   <item><description>
///     Rows in memory hold everything, and a member kept from them is the caller's own object, which
///     a transform would then change in place.
///   </description></item>
/// </list>
/// Read from the query itself, never from the rows, so the sanitizer stays pure: this is decided
/// once, before it runs.
/// </remarks>
internal sealed class RowShape
{
    /// <summary>The key EF Core's relational layer gives an owned type stored in a JSON column.</summary>
    private const string JsonContainer = "Relational:ContainerColumnName";

    /// <summary>
    /// A source this library cannot read: no model, not a projection it can see into, not rows in
    /// memory. Every value beneath a member is taken to reach the result, and only members holding a
    /// value are kept, as in 3.1.0.
    /// </summary>
    internal static readonly RowShape Unknown = new(RowKind.Entity);

    /// <summary>Rows in memory.</summary>
    internal static readonly RowShape InMemory = new(RowKind.InMemory);

    private readonly HashSet<string>? _assigned;
    private readonly HashSet<string> _narrowable = new(StringComparer.Ordinal);
    private readonly HashSet<string> _kept = new(StringComparer.Ordinal);
    private readonly HashSet<string> _includes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _loadedWhole = new(StringComparer.Ordinal);
    private readonly IEntityType? _entity;
    private readonly bool _includeUnknown;

    private RowShape(RowKind kind) => Kind = kind;

    private RowShape(RowKind kind, HashSet<string>? assigned, IEnumerable<string> narrowable)
        : this(kind)
    {
        _assigned = assigned;
        _narrowable.UnionWith(narrowable);
    }

    private RowShape(IEntityType entity, HashSet<string> includes, bool includeUnknown)
        : this(RowKind.Entity)
    {
        _entity = entity;
        _includes = includes;
        _includeUnknown = includeUnknown;

        foreach (IProperty property in entity.GetProperties())
        {
            _kept.Add(property.Name);
            _loadedWhole[property.Name] = "it is a column, which EF Core reads whole";
        }

        foreach (INavigation navigation in entity.GetNavigations())
        {
            if (IsJson(navigation.TargetEntityType))
            {
                _loadedWhole[navigation.Name] = "it is stored as JSON, which EF Core cannot narrow";
            }
            else
            {
                // An owned member is kept and narrowed; any other navigation is only ever narrowed for a
                // caller who names it, since a synthesized projection leaves it out.
                _narrowable.Add(navigation.Name);
            }

            if (IsOwned(navigation))
            {
                _kept.Add(navigation.Name);
            }
        }

        foreach (ISkipNavigation navigation in entity.GetSkipNavigations())
        {
            _narrowable.Add(navigation.Name);
        }

        foreach (string complex in ComplexProperties(entity))
        {
            _kept.Add(complex);
            _loadedWhole[complex] = "it is a complex property, which EF Core cannot narrow";
        }
    }

    /// <summary>Where the rows come from.</summary>
    internal RowKind Kind { get; }

    /// <summary>
    /// True when a synthesized projection may keep a member holding an object: one a projection
    /// assigned, or an entity's column, owned or complex member. Never an entity's navigation, which
    /// projecting would load, and never an object held by a row in memory.
    /// </summary>
    internal bool Carries(string member) => Kind switch
    {
        RowKind.Projected => _assigned is null || _assigned.Contains(member),
        RowKind.Entity => _entity is not null && _kept.Contains(member),
        _ => false
    };

    /// <summary>
    /// True when a synthesized projection may keep a member that holds a value. An entity keeps only
    /// its mapped ones: a value EF Core does not map makes it read the whole entity to compute it, the
    /// columns the policy denies included, and holds only its initial value anyway.
    /// </summary>
    internal bool CarriesValue(string member) => Kind switch
    {
        RowKind.Projected => _assigned is null || _assigned.Contains(member),
        RowKind.Entity => _entity is null || _kept.Contains(member),
        _ => true
    };

    /// <summary>
    /// True when the core's projection can narrow the member to some of the fields beneath it. Only on
    /// EF Core: its narrowing of a reference reads it through <c>EF.Property</c>. Not a column or a
    /// complex property, which EF Core loads whole, not an owned type stored as JSON, and not a member
    /// a projection built in a way the narrowing cannot reach. A source this cannot read narrows as it
    /// always did, and leaves the core to refuse what it cannot build.
    /// </summary>
    internal bool Narrows(string member) =>
        (Kind == RowKind.Entity && _entity is null) || _narrowable.Contains(member);

    /// <summary>Why the core cannot narrow a member this source carries, for the trace.</summary>
    internal string WhyNotNarrowed(string member) => Kind switch
    {
        RowKind.InMemory => "the rows are in memory, and narrowing it needs EF Core",
        RowKind.Projected => "the projection builds it in a way the core cannot narrow",
        _ => _loadedWhole.TryGetValue(member, out string? reason) ? reason : "narrowing it needs EF Core"
    };

    /// <summary>
    /// True when a member named whole can carry anything its type can hold. An entity's navigation
    /// cannot: projecting it loads that entity and what the entity owns, never the navigations beneath
    /// it. Its columns, owned and complex members, and every member of any other source, can.
    /// </summary>
    internal bool LoadsWhole(string path)
    {
        string member = path.Split('.')[0];

        return Kind != RowKind.Entity || _entity is null || _kept.Contains(member);
    }

    /// <summary>
    /// True when the value at a path beneath the root can reach the result: every one in memory or in
    /// a projection's assigned member, and, on an entity, one every navigation on the way to is loaded.
    /// </summary>
    /// <remarks>
    /// A navigation is loaded when it is owned, included, automatically included, or reachable by a
    /// lazy loader. Anything the model cannot answer for counts as loaded, which only ever asks for a
    /// projection that turns out not to be needed.
    /// </remarks>
    internal bool Materializes(string path)
    {
        string[] segments = path.Split('.');

        if (Kind == RowKind.InMemory)
        {
            return true;
        }

        if (Kind == RowKind.Projected)
        {
            return _assigned is null || _assigned.Contains(segments[0]);
        }

        IEntityType? current = _entity;
        string prefix = string.Empty;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            prefix = i == 0 ? segments[0] : $"{prefix}.{segments[i]}";

            if (current is null)
            {
                return true;
            }

            // A column holding an object, or a complex property, is read whole with its row.
            if (current.FindProperty(segments[i]) is not null || ComplexProperties(current).Contains(segments[i]))
            {
                return true;
            }

            INavigationBase? navigation =
                (INavigationBase?)current.FindNavigation(segments[i]) ?? current.FindSkipNavigation(segments[i]);

            if (navigation is null)
            {
                return true;
            }

            bool loaded = (navigation is INavigation owned && IsOwned(owned))
                          || navigation.IsEagerLoaded
                          || _includeUnknown
                          || _includes.Contains(prefix)
                          || LoadsLazily(current);

            if (!loaded)
            {
                return false;
            }

            current = navigation.TargetEntityType;
        }

        return true;
    }

    /// <summary>Reads the shape of a guarded query's source.</summary>
    /// <remarks>
    /// Rows in memory first, because a projection over them is still in memory. Then a projection that
    /// builds its rows, which is how a caller makes its own row type out of entities. What is left is an
    /// entity query, or a projection handing back entities, <c>Select(o =&gt; o.Customer)</c>, both read
    /// from the EF Core model; without one, the source is <see cref="Unknown"/>.
    /// </remarks>
    internal static RowShape Of<T>(IQueryable<T> source) where T : class
    {
        if (source.Provider is EnumerableQuery)
        {
            return InMemory;
        }

        if (DefaultOrder.BuildsRows(source.Expression))
        {
            return Projected(source);
        }

        if (QueryRoot.EntityType(source.Expression, typeof(T)) is not { } entity)
        {
            return Unknown;
        }

        HashSet<string> includes = new(StringComparer.Ordinal);
        bool unknown = !ReadIncludes(source.Expression, includes);

        return new RowShape(entity, includes, unknown);
    }

    /// <summary>
    /// A projection that builds its rows: which members its outermost initializer assigns, and which of
    /// those the core can narrow.
    /// </summary>
    private static RowShape Projected<T>(IQueryable<T> source) where T : class
    {
        MethodCallExpression select = DefaultOrder.OutermostSelect(source.Expression)!;
        LambdaExpression selector = (LambdaExpression)DefaultOrder.StripQuotes(select.Arguments[1]);
        Expression body = DefaultOrder.StripConversions(selector.Body);

        // A constructor with arguments says nothing about which member each argument sets.
        if (body is not MemberInitExpression initializer)
        {
            return new RowShape(RowKind.Projected, null, Array.Empty<string>());
        }

        HashSet<string> assigned = new(StringComparer.Ordinal);
        List<string> narrowable = new();

        ParameterExpression row = selector.Parameters[0];
        bool efCore = source.Provider is IAsyncQueryProvider;
        IEntityType? rowSource = efCore ? QueryRoot.EntityType(source.Expression, row.Type) : null;

        foreach (MemberBinding binding in initializer.Bindings)
        {
            assigned.Add(binding.Member.Name);

            // The narrowing reads the member through EF.Property, so only EF Core can run it.
            if (efCore && binding is MemberAssignment assignment && CanNarrow(assignment, row, rowSource))
            {
                narrowable.Add(binding.Member.Name);
            }
        }

        return new RowShape(RowKind.Projected, assigned, narrowable);
    }

    /// <summary>
    /// True when the core's narrowing of an assigned member translates: an object the initializer
    /// builds, a list a subquery reads into one the core can bind, or a navigation of the entity the
    /// projection reads that is neither complex nor stored as JSON.
    /// </summary>
    private static bool CanNarrow(MemberAssignment assignment, ParameterExpression row, IEntityType? rowSource)
    {
        Type memberType = assignment.Member is PropertyInfo property ? property.PropertyType : typeof(object);

        // The core builds a List<T> for a narrowed collection and skips a member that cannot hold one.
        if (CacheReflection.GetCollectionElementType(memberType) is { } element
            && !memberType.IsAssignableFrom(typeof(List<>).MakeGenericType(element)))
        {
            return false;
        }

        Expression value = DefaultOrder.StripConversions(assignment.Expression);

        if (value is MemberInitExpression)
        {
            return true;
        }

        if (value is MethodCallExpression { Method.Name: "ToList" or "ToArray" or "ToHashSet" } read
            && (read.Method.DeclaringType == typeof(Enumerable) || read.Method.DeclaringType == typeof(Queryable)))
        {
            return true;
        }

        if (value is MemberExpression { Member: PropertyInfo member, Expression: { } owner }
            && DefaultOrder.StripConversions(owner) == row
            && rowSource?.FindNavigation(member.Name) is { } navigation)
        {
            return !IsJson(navigation.TargetEntityType);
        }

        return false;
    }

    /// <summary>
    /// Collects every navigation path an <c>Include</c> or <c>ThenInclude</c> on the chain loads, each
    /// with its prefixes. False when one names its path in a form this cannot read, so every navigation
    /// counts as loaded.
    /// </summary>
    private static bool ReadIncludes(Expression expression, HashSet<string> includes)
    {
        List<MethodCallExpression> calls = new();

        for (Expression? node = expression; node is MethodCallExpression call;
             node = call.Arguments.Count > 0 ? call.Arguments[0] : null)
        {
            if (call.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                && call.Method.Name is nameof(EntityFrameworkQueryableExtensions.Include)
                    or nameof(EntityFrameworkQueryableExtensions.ThenInclude))
            {
                calls.Add(call);
            }
        }

        bool readable = true;
        string? current = null;

        for (int i = calls.Count - 1; i >= 0; i--)
        {
            MethodCallExpression call = calls[i];
            bool then = call.Method.Name == nameof(EntityFrameworkQueryableExtensions.ThenInclude);
            string? path = null;
            bool named = false;

            if (!then && call.Arguments[1] is ConstantExpression { Value: string text })
            {
                path = string.Join('.', text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                named = true;
            }
            else if (DefaultOrder.StripQuotes(call.Arguments[1]) is LambdaExpression lambda
                     && MemberChain(lambda.Body, lambda.Parameters[0]) is { } chain)
            {
                path = then ? (current is null ? null : $"{current}.{chain}") : chain;
            }

            if (string.IsNullOrEmpty(path))
            {
                readable = false;
                current = null;

                continue;
            }

            string[] segments = path.Split('.');

            for (int j = 1; j <= segments.Length; j++)
            {
                includes.Add(string.Join('.', segments, 0, j));
            }

            // A ThenInclude continues a lambda Include; one named by a string starts nothing to continue.
            current = named ? null : path;
        }

        return readable;
    }

    /// <summary>
    /// The member path a navigation lambda reads, <c>o =&gt; o.Lines</c> or <c>o =&gt; o.Customer.Address</c>,
    /// through casts and a filtered include's operators; null for any other shape.
    /// </summary>
    private static string? MemberChain(Expression body, ParameterExpression parameter)
    {
        Expression node = DefaultOrder.StripConversions(body);

        // A filtered include reads the navigation through Where, OrderBy, Skip, Take and their kin.
        while (node is MethodCallExpression call
               && (call.Method.DeclaringType == typeof(Enumerable) || call.Method.DeclaringType == typeof(Queryable))
               && call.Arguments.Count > 0)
        {
            node = DefaultOrder.StripConversions(call.Arguments[0]);
        }

        List<string> names = new();

        while (node is MemberExpression { Member: PropertyInfo or FieldInfo, Expression: { } owner } member)
        {
            names.Add(member.Member.Name);
            node = DefaultOrder.StripConversions(owner);
        }

        if (node != parameter || names.Count == 0)
        {
            return null;
        }

        names.Reverse();

        return string.Join('.', names);
    }

    /// <summary>True for a navigation to a type the entity owns.</summary>
    private static bool IsOwned(INavigation navigation) =>
        navigation.ForeignKey.IsOwnership && !navigation.IsOnDependent;

    /// <summary>True for an owned type EF Core stores in a JSON column, which it cannot project into.</summary>
    private static bool IsJson(IEntityType entity) => entity.FindAnnotation(JsonContainer)?.Value is not null;

    /// <summary>
    /// True when a lazy loader can fill this entity's navigations after the query: EF Core's proxies and
    /// an injected <c>ILazyLoader</c> both give it a service property.
    /// </summary>
    private static bool LoadsLazily(IEntityType entity) =>
        entity.GetServiceProperties().Any(service =>
            service.ClrType == typeof(ILazyLoader) || service.ClrType == typeof(Action<object, string>));

    /// <summary>
    /// The names of an entity type's complex properties, which EF Core 8 introduced and loads with the
    /// entity. None on an earlier version, where the method does not exist.
    /// </summary>
    /// <remarks>
    /// Read by reflection because the library compiles against EF Core 6. The method is declared on an
    /// interface the entity type implements, so the interfaces are searched rather than the class.
    /// </remarks>
    private static HashSet<string> ComplexProperties(IEntityType entityType)
    {
        HashSet<string> names = new(StringComparer.Ordinal);

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
                        names.Add(name);
                    }
                }
            }

            break;
        }

        return names;
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
