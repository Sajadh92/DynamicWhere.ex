using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
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
///     projection; and projecting the navigation would load it. A member the model does not map is
///     computed by the class, from whatever EF Core loaded into it, so it counts as holding its type.
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
/// once, before it runs. Where the query does not say, every value counts as reaching the result.
/// </remarks>
internal sealed class RowShape
{
    /// <summary>The key EF Core's relational layer gives an owned type stored in a JSON column.</summary>
    private const string JsonContainer = "Relational:ContainerColumnName";

    /// <summary>
    /// A source this library cannot read: no model, not a projection it can see into, not rows in
    /// memory. Every value beneath a member is taken to reach the result.
    /// </summary>
    internal static readonly RowShape Unknown = new(RowKind.Entity);

    /// <summary>Rows in memory.</summary>
    internal static readonly RowShape InMemory = new(RowKind.InMemory);

    /// <summary>Whether a class can be handed a lazy loader, read once per class.</summary>
    private static readonly ConcurrentDictionary<Type, bool> TakesLoader = new();

    /// <summary>The complex properties of each entity type, read once per entity type.</summary>
    private static readonly ConditionalWeakTable<IEntityType, HashSet<string>> ComplexByType = new();

    private readonly HashSet<string>? _assigned;
    private readonly Dictionary<string, Type> _built = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _narrowable = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _kept = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _includes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _loadedWhole = new(StringComparer.OrdinalIgnoreCase);
    private readonly IEntityType? _entity;
    private readonly bool _includeUnknown;

    private RowShape(RowKind kind) => Kind = kind;

    private RowShape(
        RowKind kind, HashSet<string>? assigned, IEnumerable<string> narrowable, Type? built, IDictionary<string, Type> members)
        : this(kind)
    {
        _assigned = assigned;
        _narrowable.UnionWith(narrowable);
        Built = built;

        foreach (KeyValuePair<string, Type> member in members)
        {
            _built[member.Key] = member.Value;
        }
    }

    private RowShape(IEntityType entity, IEnumerable<string> includes, bool includeUnknown)
        : this(RowKind.Entity)
    {
        _entity = entity;
        _includes.UnionWith(includes);
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
    /// The type a projection constructs its rows as, which may be a subtype of the query's element type;
    /// null for any other source.
    /// </summary>
    internal Type? Built { get; }

    /// <summary>
    /// The type a projection constructs a member's value as, or each element of a list it builds, when
    /// the initializer says: <c>Contact = new ContactRow { … }</c> holds a <c>ContactRow</c> whatever the
    /// member is declared as. Null when the value is read from somewhere else.
    /// </summary>
    internal Type? BuiltType(string member) =>
        Kind == RowKind.Projected && _built.TryGetValue(member, out Type? type) ? type : null;

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
    /// True when the value at a path beneath the root can reach the result: every one in memory or in
    /// a projection's assigned member, and, on an entity, one every navigation on the way to is loaded.
    /// </summary>
    /// <remarks>
    /// A navigation is loaded when it is owned, included, automatically included, or reachable by a
    /// lazy loader. A member the model does not map is computed by the class, and a getter over a mapped
    /// field or a private navigation hands out what EF Core loaded, so it counts as loaded too. Anything
    /// the model cannot answer for counts as loaded, which only ever asks for a projection that turns out
    /// not to be needed. A rule may spell a path in any letter case, so each segment is read through the
    /// member it names.
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
            if (current is null || CacheReflection.FindProperty(current.ClrType, segments[i]) is not { } member)
            {
                return true;
            }

            string name = member.Name;

            prefix = i == 0 ? name : $"{prefix}.{name}";

            // A column holding an object, or a complex property, is read whole with its row.
            if (current.FindProperty(name) is not null || ComplexProperties(current).Contains(name))
            {
                return true;
            }

            INavigationBase? navigation =
                (INavigationBase?)current.FindNavigation(name) ?? current.FindSkipNavigation(name);

            if (navigation is null)
            {
                return true;
            }

            if (!Loads(current, navigation, prefix))
            {
                return false;
            }

            current = navigation.TargetEntityType;
        }

        return true;
    }

    /// <summary>
    /// Every member the value at a path loads beneath it, each with its path from the root, when an
    /// entity query's model can say; null when the value can carry anything its type holds.
    /// </summary>
    /// <remarks>
    /// Naming a member in a projection loads it, so the path's own segments are not asked about. Beneath
    /// it the model decides: an entity's columns, owned and complex members, and the navigations
    /// something loads, and the same for every type the model derives from it, whose rows EF Core
    /// materializes as that type. A member the model does not map is read whole, as a column is: its
    /// getter can hand out anything the entity holds. A lazy loader in reach can fill any navigation,
    /// which bounds nothing, and so does a query whose includes cannot be read.
    /// </remarks>
    internal List<Loaded>? LoadedBeneath(string path)
    {
        if (Kind != RowKind.Entity || _entity is null || _includeUnknown)
        {
            return null;
        }

        IEntityType entity = _entity;
        string prefix = string.Empty;
        string[] segments = path.Split('.');

        for (int i = 0; i < segments.Length; i++)
        {
            if (CacheReflection.FindProperty(entity.ClrType, segments[i]) is not { } member)
            {
                return null;
            }

            prefix = i == 0 ? member.Name : $"{prefix}.{member.Name}";

            // A value read whole: what it holds is its type's to say, and a path inside it names part of it.
            if (entity.FindProperty(member.Name) is not null || ComplexProperties(entity).Contains(member.Name))
            {
                return i == segments.Length - 1 ? new List<Loaded> { new(prefix, member, true) } : null;
            }

            INavigationBase? navigation =
                (INavigationBase?)entity.FindNavigation(member.Name) ?? entity.FindSkipNavigation(member.Name);

            // A member the model does not map holds what its getter computes: read it whole, as its type.
            if (navigation is null)
            {
                return i == segments.Length - 1 ? new List<Loaded> { new(prefix, member, true) } : null;
            }

            entity = navigation.TargetEntityType;
        }

        List<Loaded> loaded = new();

        return Collect(entity, prefix, loaded, new HashSet<(IEntityType, string?)>(), derivedOnly: false)
            ? loaded
            : null;
    }

    /// <summary>
    /// Every member an entity query's rows load that a type the model derives from the root declares,
    /// each with what it loads beneath it; null when a lazy loader or an unreadable include bounds
    /// nothing, and for any other source.
    /// </summary>
    /// <remarks>
    /// EF Core materializes each row as the type the database says it is, so a query over the root of a
    /// hierarchy returns the derived types' columns too, which the root's own members never name.
    /// </remarks>
    internal List<Loaded>? LoadedByDerived()
    {
        if (Kind != RowKind.Entity || _entity is null || _includeUnknown)
        {
            return null;
        }

        List<Loaded> loaded = new();

        return Collect(_entity, string.Empty, loaded, new HashSet<(IEntityType, string?)>(), derivedOnly: true)
            ? loaded
            : null;
    }

    /// <summary>
    /// Collects what an entity loads beneath a path, its derived types' members included. False when a
    /// lazy loader can fill any of its navigations.
    /// </summary>
    private bool Collect(
        IEntityType entity, string prefix, List<Loaded> loaded, HashSet<(IEntityType, string?)> seen, bool derivedOnly)
    {
        // Off an include's path, what loads depends only on the type, so a type is read once there.
        if (!seen.Add((entity, _includes.Contains(prefix) ? prefix : null)))
        {
            return true;
        }

        foreach (IEntityType type in entity.GetDerivedTypesInclusive())
        {
            if (derivedOnly && type == entity)
            {
                continue;
            }

            if (LoadsLazily(type))
            {
                return false;
            }

            foreach (PropertyInfo member in type.ClrType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // A derived type's inherited members are the base type's, read once from there.
                if (!member.CanRead
                    || member.GetIndexParameters().Length != 0
                    || (type != entity && member.DeclaringType!.IsAssignableFrom(entity.ClrType)))
                {
                    continue;
                }

                string path = prefix.Length == 0 ? member.Name : $"{prefix}.{member.Name}";

                if (type.FindProperty(member.Name) is not null || ComplexProperties(type).Contains(member.Name))
                {
                    loaded.Add(new Loaded(path, member, true));

                    continue;
                }

                INavigationBase? navigation =
                    (INavigationBase?)type.FindNavigation(member.Name) ?? type.FindSkipNavigation(member.Name);

                // Not mapped: whatever its getter computes from what EF Core loaded, read as its type.
                if (navigation is null)
                {
                    loaded.Add(new Loaded(path, member, true));

                    continue;
                }

                if (!Loads(type, navigation, path))
                {
                    continue;
                }

                loaded.Add(new Loaded(path, member, false));

                if (!Collect(navigation.TargetEntityType, path, loaded, seen, derivedOnly: false))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>True when something loads a navigation of an entity reached along a path.</summary>
    private bool Loads(IEntityType owner, INavigationBase navigation, string path) =>
        (navigation is INavigation owned && IsOwned(owned))
        || navigation.IsEagerLoaded
        || _includeUnknown
        || _includes.Contains(path)
        || LoadsLazily(owner);

    /// <summary>Reads the shape of a guarded query's source.</summary>
    /// <remarks>
    /// Rows in memory first, because a projection over them is still in memory. Then a projection that
    /// builds its rows, which is how a caller makes its own row type out of entities. What is left is an
    /// entity query, read from the EF Core model; without one, the source is <see cref="Unknown"/>. A
    /// chain that reaches its rows through anything but the root's own, <c>Select(o =&gt; o.Customer)</c>,
    /// a <c>SelectMany</c>, a <c>Join</c> or a <c>GroupBy</c>, holds entities EF Core loaded along
    /// another path. Its includes name paths from another root, which EF Core still applies, and a
    /// projection inside it, such as one behind an identity <c>Select</c>, loads whatever it assigns: with
    /// either, every navigation counts as loaded. With neither, only an automatic include or a lazy
    /// loader fills one, which the model says.
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

        HashSet<string> includes = new(StringComparer.OrdinalIgnoreCase);
        bool unknown = !ReadIncludes(source.Expression, includes, out int included)
                       || (QueryRoot.Reshapes(source.Expression) && (included > 0 || QueryRoot.Builds(source.Expression)));

        return new RowShape(entity, includes, unknown);
    }

    /// <summary>
    /// A projection that builds its rows: which members its outermost initializer assigns, and which of
    /// those the core can narrow.
    /// </summary>
    private static RowShape Projected<T>(IQueryable<T> source) where T : class
    {
        MethodCallExpression select = DefaultOrder.RowSelect(source.Expression)!;
        LambdaExpression selector = (LambdaExpression)DefaultOrder.StripQuotes(select.Arguments[1]);
        Expression body = DefaultOrder.StripConversions(selector.Body);
        Dictionary<string, Type> built = new(StringComparer.OrdinalIgnoreCase);

        if (body is not MemberInitExpression initializer)
        {
            // A constructor with arguments says nothing about which member each argument sets: every
            // member counts as assigned, and none can be narrowed.
            return new RowShape(RowKind.Projected, null, Array.Empty<string>(), body.Type, built);
        }

        List<string> narrowable = new();

        ParameterExpression row = selector.Parameters[0];
        bool efCore = source.Provider is IAsyncQueryProvider;
        IEntityType? rowSource = efCore ? QueryRoot.EntityType(source.Expression, row.Type) : null;
        HashSet<string>? assigned = initializer.NewExpression.Arguments.Count > 0
            ? null
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (MemberBinding binding in initializer.Bindings)
        {
            // After a constructor with arguments every member counts as assigned, since the constructor
            // may set any of them; the initializer's own bindings still say what they hold.
            assigned?.Add(binding.Member.Name);

            if (binding is not MemberAssignment assignment)
            {
                continue;
            }

            if (BuiltBy(assignment.Expression) is { } type)
            {
                built[binding.Member.Name] = type;
            }

            // The narrowing reads the member through EF.Property, so only EF Core can run it.
            if (efCore && CanNarrow(assignment, row, rowSource))
            {
                narrowable.Add(binding.Member.Name);
            }
        }

        return new RowShape(RowKind.Projected, assigned, narrowable, initializer.Type, built);
    }

    /// <summary>
    /// The type an assigned value is constructed as, <c>new ContactRow { … }</c>, or each element of a
    /// list a subquery builds, <c>o.Lines.Select(l =&gt; new LineRow { … }).ToList()</c>; null otherwise.
    /// </summary>
    private static Type? BuiltBy(Expression expression)
    {
        Expression value = DefaultOrder.StripConversions(expression);

        if (value is MemberInitExpression or NewExpression)
        {
            return value.Type;
        }

        if (value is MethodCallExpression { Method.Name: "ToList" or "ToArray" or "ToHashSet" } read
            && (read.Method.DeclaringType == typeof(Enumerable) || read.Method.DeclaringType == typeof(Queryable))
            && DefaultOrder.StripConversions(read.Arguments[0]) is MethodCallExpression { Method.Name: "Select" } select
            && select.Arguments.Count == 2
            && DefaultOrder.StripQuotes(select.Arguments[1]) is LambdaExpression { Body: var element }
            && DefaultOrder.StripConversions(element) is MemberInitExpression or NewExpression)
        {
            return DefaultOrder.StripConversions(element).Type;
        }

        return null;
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
    private static bool ReadIncludes(Expression expression, HashSet<string> includes, out int included)
    {
        List<MethodCallExpression> calls = new();

        for (Expression? node = expression; node is MethodCallExpression call;
             node = call.Arguments.Count > 0 ? call.Arguments[0] : null)
        {
            if (IsInclude(call))
            {
                calls.Add(call);
            }
        }

        // An include somewhere else in the tree, in the other branch of a Concat or a Union, loads a
        // navigation of rows this chain returns, and nothing here can say which.
        IncludeCounter counter = new();

        counter.Visit(expression);

        included = counter.Count;

        bool readable = counter.Count == calls.Count;
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

    private static bool IsInclude(MethodCallExpression call) =>
        call.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
        && call.Method.Name is nameof(EntityFrameworkQueryableExtensions.Include)
            or nameof(EntityFrameworkQueryableExtensions.ThenInclude);

    /// <summary>Counts every <c>Include</c> and <c>ThenInclude</c> anywhere in a query.</summary>
    private sealed class IncludeCounter : ExpressionVisitor
    {
        internal int Count { get; private set; }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (IsInclude(node))
            {
                Count++;
            }

            return base.VisitMethodCall(node);
        }
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
    /// an injected <c>ILazyLoader</c> give it a service property, and a loader the class takes in its
    /// constructor, either delegate form above all, may be kept in a field or a property of any name,
    /// where the model has no record of it. An injected <c>DbContext</c> can load anything.
    /// </summary>
    private static bool LoadsLazily(IEntityType entity) =>
        entity.GetServiceProperties().Any(service => IsLoader(service.ClrType))
        || TakesLoader.GetOrAdd(entity.ClrType, HoldsLoader);

    private static bool HoldsLoader(Type type)
    {
        const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        if (type.GetConstructors(any).Any(constructor => constructor.GetParameters().Any(p => IsLoader(p.ParameterType))))
        {
            return true;
        }

        for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            if (current.GetFields(any | BindingFlags.DeclaredOnly).Any(field => IsLoader(field.FieldType))
                || current.GetProperties(any | BindingFlags.DeclaredOnly).Any(property => IsLoader(property.PropertyType)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLoader(Type type) =>
        type == typeof(ILazyLoader)
        || type == typeof(Action<object, string>)
        || type == typeof(Func<object, CancellationToken, string, Task>)
        || typeof(DbContext).IsAssignableFrom(type);

    /// <summary>
    /// The names of an entity type's complex properties, which EF Core 8 introduced and loads with the
    /// entity. None on an earlier version, where the method does not exist.
    /// </summary>
    /// <remarks>
    /// Read by reflection because the library compiles against EF Core 6. The method is declared on an
    /// interface the entity type implements, so the interfaces are searched rather than the class.
    /// </remarks>
    private static HashSet<string> ComplexProperties(IEntityType entityType) =>
        ComplexByType.GetValue(entityType, ReadComplexProperties);

    private static HashSet<string> ReadComplexProperties(IEntityType entityType)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

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

/// <summary>A member a value loads, with its path from the root.</summary>
/// <param name="Path">The member's path from the root.</param>
/// <param name="Property">The member.</param>
/// <param name="Whole">True when EF Core reads it whole: a column or a complex property.</param>
internal readonly record struct Loaded(string Path, PropertyInfo Property, bool Whole);

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
