using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Reflection;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Source;

namespace DynamicWhere.ex.Policies.Resolution;

/// <summary>
/// Produces policy fragments by reflecting over the attributes on a type.
/// </summary>
/// <remarks>
/// The result is identical for every caller, so it is computed once per type and cached, until an
/// assembly that could declare a subtype loads (see the denials below). An
/// attribute with <c>Overridable = false</c> lands at <see cref="PolicyLevel.SealedAttribute"/> and
/// nothing at runtime can replace it; one with <c>Overridable = true</c> lands at
/// <see cref="PolicyLevel.OverridableAttribute"/>, the least authoritative level, and acts only as
/// a default.
/// </remarks>
public sealed class AttributePolicyProvider : IDwPolicyProvider
{
    private static readonly ConcurrentDictionary<Type, (int Epoch, IReadOnlyList<PolicyFragment> Fragments)> Cache = new();

    /// <summary>
    /// How many navigation segments a generated field path may contain. Matches the default
    /// navigation-depth cap, so the provider never produces a path the sanitizer would reject.
    /// </summary>
    public const int MaxDepth = 4;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entityType"/> is null.</exception>
    public IReadOnlyList<PolicyFragment> GetFragments(Type entityType, DwPolicyContext context)
    {
        if (entityType == null)
        {
            throw new ArgumentNullException(nameof(entityType));
        }

        int epoch = KnownSubtypes.Epoch;

        if (Cache.TryGetValue(entityType, out (int Epoch, IReadOnlyList<PolicyFragment> Fragments) known)
            && known.Epoch == epoch)
        {
            return known.Fragments;
        }

        IReadOnlyList<PolicyFragment> fragments = Build(entityType);

        Cache[entityType] = (epoch, fragments);

        return fragments;
    }

    /// <summary>
    /// Reflects over one type, turning every policy attribute into a fragment. Reference
    /// navigations and collection element types are walked so that dotted paths such as
    /// <c>Contact.Email</c> carry their own policy.
    /// </summary>
    /// <remarks>
    /// The result is handed to every caller and lives in a process-wide cache, so it is wrapped
    /// before it leaves: a caller who cast it back to <see cref="List{T}"/> and removed a fragment
    /// would be granting access to a denied field for the lifetime of the process.
    /// </remarks>
    private static IReadOnlyList<PolicyFragment> Build(Type entityType)
    {
        List<PolicyFragment> fragments = new();

        Walk(entityType, prefix: string.Empty, depth: 0, new HashSet<Type>(), fragments);

        return new ReadOnlyCollection<PolicyFragment>(fragments);
    }

    /// <summary>
    /// Adds fragments for one type, then descends into its navigations.
    /// </summary>
    /// <param name="type">The type being walked.</param>
    /// <param name="prefix">The dotted path leading to this type, empty at the root.</param>
    /// <param name="depth">How many navigations deep the walk currently is.</param>
    /// <param name="ancestors">The types already on this path, for the cycle check below.</param>
    /// <param name="fragments">The accumulator.</param>
    /// <remarks>
    /// Depth alone terminates the walk. A visited-type guard would be cheaper on a graph with many
    /// cycles, but it would also suppress legitimate paths: a self-referencing type would never
    /// yield <c>Next.Secret</c>, even though a caller can filter on exactly that path. Bidirectional
    /// navigations are cyclic by nature, so the depth cap is doing the real work either way, and the
    /// whole walk is computed once per type and cached.
    /// </remarks>
    private static void Walk(
        Type type, string prefix, int depth, HashSet<Type> ancestors, List<PolicyFragment> fragments)
    {
        // True once this type is reachable from itself: Employee.Manager, Category.Parent,
        // Order.PreviousOrder. Three attributes below are declarations about the entity being
        // queried rather than facts about every path that reaches one, and replicating them around
        // a cycle is what made them unusable. Everything else still propagates, because a caller
        // really can name Manager.Salary and it must be masked there.
        //
        // A cycle check rather than a depth-0 check on purpose: forcing Customer.TenantId while
        // querying Order is a real and useful thing to declare, and only the reflections of a type
        // back onto itself are meaningless.
        bool reflected = ancestors.Contains(type);

        // Before the type is marked. A frame that returns here reads nothing, and one that had marked
        // the type first left it marked for the rest of the walk: a type first met at the depth limit
        // then read as a cycle wherever it was met again, and its forced scope, its required filter
        // and its alias were dropped from a path that reaches it directly. Which of two members was
        // declared first decided whether a tenant scope applied.
        if (depth >= MaxDepth)
        {
            return;
        }

        // Added once for this frame rather than per navigation, and removed only when this frame is
        // the one that added it — otherwise a diamond (two properties of the same type) would clear
        // a marker an outer frame is still standing on.
        bool marked = ancestors.Add(type);

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            string path = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";

            Emit(type, property, path, reflected, fragments);

            Type? navigation = NavigationTypeOf(property.PropertyType);

            if (navigation is not null)
            {
                Walk(navigation, path, depth + 1, ancestors, fragments);
            }
        }

        if (marked)
        {
            ancestors.Remove(type);
        }
    }

    /// <summary>
    /// Adds the fragments one member's attributes declare, at the path that reaches it.
    /// </summary>
    /// <param name="type">The type the member was read from.</param>
    /// <param name="property">The member.</param>
    /// <param name="path">The dotted path that reaches it from the entity being queried.</param>
    /// <param name="reflected">
    /// True when the declarations about the queried entity itself, an alias, a required filter and a
    /// forced scope, are left out: around a cycle, and on a path the walk does not reach.
    /// </param>
    /// <param name="fragments">The accumulator.</param>
    private static void Emit(
        Type type, PropertyInfo property, string path, bool reflected, List<PolicyFragment> fragments)
    {

        foreach (DwDenyAttribute attribute in property.GetCustomAttributes<DwDenyAttribute>(inherit: true))
        {
            fragments.Add(ToFragment(path, attribute));
        }

        foreach (DwDenyAttribute attribute in DenialsElsewhere(type, property))
        {
            fragments.Add(ToFragment(path, attribute));
        }

        foreach (DwOperatorsAttribute attribute in property.GetCustomAttributes<DwOperatorsAttribute>(inherit: true))
        {
            fragments.Add(ToFragment(path, attribute));
        }

        // An alias is a public *name*. Emitting "code" for StaffCode, Manager.StaffCode and
        // Reports.StaffCode alike makes one declaration match many paths, and the sanitizer
        // refuses the name as AmbiguousFieldName — so decorating a member of a self-referencing
        // type made the alias unusable rather than convenient.
        DwAliasAttribute? alias = reflected
            ? null
            : property.GetCustomAttribute<DwAliasAttribute>(inherit: true);

        if (alias is not null)
        {
            fragments.Add(ToFragment(path, alias));
        }

        // A demand on the caller. "You must filter on Division" is a sentence about the
        // query's subject; "you must also filter on Manager.Division" is not one anyone meant,
        // and once the depth cap has produced Manager.Division, Reports.Division and
        // Manager.Reports.Division, no caller can satisfy every copy.
        DwRequireWhereAttribute? required = reflected
            ? null
            : property.GetCustomAttribute<DwRequireWhereAttribute>(inherit: true);

        if (required is not null)
        {
            fragments.Add(ToFragment(path, required));
        }

        // A row-level scope on the entity being queried. This is the one that did not fail
        // loudly: "only active employees" replicated into "and whose manager is active, and
        // whose manager's manager is active" — a conjunction almost no row satisfies — so a
        // perfectly good query came back EMPTY rather than refused. Fewer rows is technically
        // fail-closed, which is why nothing caught it; silently returning nothing is still the
        // worst way to be wrong.
        if (!reflected)
        {
            foreach (DwForceWhereAttribute attribute in property.GetCustomAttributes<DwForceWhereAttribute>(inherit: true))
            {
                fragments.Add(ToFragment(path, property, attribute));
            }
        }

        foreach (TransformStage stage in TransformStagesOn(property))
        {
            fragments.Add(ToFragment(path, stage, StageAttributeOn(property, stage.Kind)));
        }

        // One fragment per attribute rather than one merged fragment for the member, because
        // each attribute carries its own Overridable flag: [DwCost(10)] can be sealed while the
        // [DwDescribe] beside it is replaceable, and merging them would force one ceiling on
        // both.
        foreach (PolicyFragment fragment in FactFragmentsOn(path, property))
        {
            fragments.Add(fragment);
        }
    }

    /// <summary>
    /// A path as the policy names it: without the <c>Value</c> a query writes after a nullable struct.
    /// </summary>
    /// <param name="entityType">The entity being queried.</param>
    /// <param name="path">A dotted path, as the path validator accepts it.</param>
    /// <remarks>
    /// A query reaches a member of an <c>Iban?</c> through the nullable, <c>Iban.Value.Number</c>, because
    /// that is the member it compiles. The attribute walk looks through the nullable and names the same
    /// member <c>Iban.Number</c>, and so do the type's transforms, audits and fragments. Looked up as the
    /// query spells it, the path matched none of them: a <c>[DwDenied]</c> on the <c>Iban</c>, or on a
    /// member of the struct, and a mask on one, did not reach it. So a <c>Value</c> after a nullable
    /// struct of the application's is dropped, before a member of it or at the end, where it names the
    /// struct whole. <c>HasValue</c> stays: it is the nullable's own, and <see cref="Governing"/> reads it
    /// as the nullable member. A framework nullable, <c>Salary.Value</c>, is left as it is.
    /// </remarks>
    internal static string PolicyPath(Type entityType, string path)
    {
        if (path.IndexOf('.') < 0 || path.IndexOf(nameof(Nullable<int>.Value), StringComparison.OrdinalIgnoreCase) < 0)
        {
            return path;
        }

        string[] segments = path.Split('.');
        List<string>? kept = null;
        Type type = entityType;

        for (int i = 0; i < segments.Length; i++)
        {
            Type? underlying = Nullable.GetUnderlyingType(type);

            if (i > 0
                && underlying is not null
                && NavigationTypeOf(underlying) == underlying
                && string.Equals(segments[i], nameof(Nullable<int>.Value), StringComparison.OrdinalIgnoreCase))
            {
                kept ??= segments.Take(i).ToList();
                type = underlying;

                continue;
            }

            kept?.Add(segments[i]);

            PropertyInfo? property = Find(underlying ?? type, segments[i]) ?? Find(type, segments[i]);

            if (property is null)
            {
                return kept is null ? path : string.Join('.', kept.Concat(segments.Skip(i + 1)));
            }

            Type next = property.PropertyType;
            type = CacheReflection.GetCollectionElementType(next) ?? next;
        }

        return kept is null ? path : string.Join('.', kept);
    }

    /// <summary>
    /// The members whose policy decides a path that continues beneath them, outermost first, and the one
    /// among them the path reads, when the path goes beneath a member the framework declares.
    /// </summary>
    /// <param name="entityType">The entity being queried.</param>
    /// <param name="path">A normalized dotted path.</param>
    /// <remarks>
    /// Two kinds of member decide the paths beneath them.
    /// <list type="bullet">
    ///   <item><description>
    ///     A member the framework declares the type of, where no attribute can be placed beneath it:
    ///     <c>Salary.Value</c> and <c>Salary.HasValue</c> on a <c>decimal?</c>, <c>Secret.Length</c> on a
    ///     <see cref="string"/>, <c>Born.Year</c> on a <see cref="DateTime"/>, <c>Bag.Count</c> on a
    ///     dictionary, or <c>Lines.Count</c> on an application's own collection class, where the member
    ///     named is the collection's and not the element's. The path reads that member, which is
    ///     <c>Reads</c>, and nothing beneath it is walked.
    ///   </description></item>
    ///   <item><description>
    ///     A member holding an application's own struct, or a collection of them (3.4.0). A struct is a
    ///     value, not a navigation: <c>Iban.Number</c> is part of the <c>Iban</c>, and a
    ///     <c>[DwDenied]</c> on the <c>Iban</c> that left <c>Iban.Number</c> to be filtered on and
    ///     selected was a sealed field one segment away from having no policy at all. Its members are
    ///     still walked, since they can carry attributes of their own, and both decide.
    ///   </description></item>
    /// </list>
    /// A class is still a navigation, whose members are separate fields: a denial of <c>Customer</c> does
    /// not deny <c>Customer.Name</c>. A member only a subtype declares is decided by the fragments naming
    /// it, so that a grant of <c>Zone</c> does not grant what a subtype of <c>Zone</c> declares.
    /// </remarks>
    internal static (IReadOnlyList<string> Above, string? Reads) Governing(Type entityType, string path)
    {
        // Asked of every path a query resolves, and nearly every one of them is a single member.
        if (path.IndexOf('.') < 0)
        {
            return (Array.Empty<string>(), null);
        }

        string[] segments = path.Split('.');
        Type type = entityType;
        List<string>? above = null;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            PropertyInfo? property = Find(type, segments[i]);

            if (property is null)
            {
                break;
            }

            Type? navigation = NavigationTypeOf(property.PropertyType);

            if (navigation is null)
            {
                string reads = string.Join('.', segments, 0, i + 1);

                (above ??= new List<string>()).Add(reads);

                return (above, reads);
            }

            if (Find(navigation, segments[i + 1]) is null)
            {
                // Not the element's member. The collection's own, Lines.Count on an application's
                // collection class, reads the member above it, and so does a nullable's own, HasValue on
                // an Iban?. Anything else is a subtype's member or names nothing, and is decided as it
                // always was: by the fragments naming it.
                Type container = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                bool nullables = container != property.PropertyType && Find(property.PropertyType, segments[i + 1]) is not null;

                if (nullables || (container != navigation && Find(container, segments[i + 1]) is not null))
                {
                    string reads = string.Join('.', segments, 0, i + 1);

                    (above ??= new List<string>()).Add(reads);

                    return (above, reads);
                }

                break;
            }

            if (navigation.IsValueType)
            {
                (above ??= new List<string>()).Add(string.Join('.', segments, 0, i + 1));
            }

            type = navigation;
        }

        return (above ?? (IReadOnlyList<string>)Array.Empty<string>(), null);
    }

    /// <summary>
    /// The fragments a member's attributes declare on a path longer than the walk goes, or none when
    /// the path is one the walk names or names nothing.
    /// </summary>
    /// <param name="entityType">The entity being queried.</param>
    /// <param name="path">A normalized dotted path.</param>
    /// <remarks>
    /// The walk stops at <see cref="MaxDepth"/> segments because a connected model fans out at every
    /// level, and the navigation-depth cap defaults to the same number. A host that raises the cap lets
    /// a request name a longer path, which no fragment of the walk reaches, and a denial declared on the
    /// member at its end did not apply. One path is cheap where every path is not, so the member is read
    /// directly. What is declared about the queried entity itself is left out, as it is around a cycle.
    /// </remarks>
    internal static IReadOnlyList<PolicyFragment> Unwalked(Type entityType, string path)
    {
        // Counted before anything is split: only a path past the walk has anything to read here.
        int separators = 0;

        for (int i = 0; i < path.Length; i++)
        {
            if (path[i] == '.')
            {
                separators++;
            }
        }

        if (separators < MaxDepth)
        {
            return Array.Empty<PolicyFragment>();
        }

        string[] segments = path.Split('.');

        Type type = entityType;

        for (int i = 0; i < segments.Length; i++)
        {
            PropertyInfo? property = Find(type, segments[i]);

            if (property is null)
            {
                return Array.Empty<PolicyFragment>();
            }

            if (i == segments.Length - 1)
            {
                List<PolicyFragment> fragments = new();

                Emit(type, property, path, reflected: true, fragments);

                return fragments;
            }

            if (NavigationTypeOf(property.PropertyType) is not { } navigation)
            {
                return Array.Empty<PolicyFragment>();
            }

            type = navigation;
        }

        return Array.Empty<PolicyFragment>();
    }

    /// <summary>
    /// The chain a member's own attributes declare, or null when it declares none.
    /// </summary>
    /// <remarks>
    /// For a member no path of the walk reaches: one a subtype declares, one past the walk's depth, one
    /// on an object a framework collection holds. No fragment names it, so no election is held over
    /// it and no rule can speak to it; its attributes are all there is, and a member carries at most
    /// one of each kind. Read once per member.
    /// </remarks>
    internal static ValueTransform? TransformOn(PropertyInfo property) =>
        TransformsByMember.GetOrAdd(property, static member =>
        {
            MutateStage? mutate = null;
            GeneralizeStage? generalize = null;
            FormatStage? format = null;
            MaskStage? mask = null;
            TruncateStage? truncate = null;
            DefaultStage? replacement = null;

            foreach (TransformStage stage in TransformStagesOn(member))
            {
                switch (stage)
                {
                    case MutateStage found: mutate = found; break;
                    case GeneralizeStage found: generalize = found; break;
                    case FormatStage found: format = found; break;
                    case MaskStage found: mask = found; break;
                    case TruncateStage found: truncate = found; break;
                    case DefaultStage found: replacement = found; break;
                }
            }

            ValueTransform chain = new(mutate, generalize, format, mask, truncate, replacement);

            return chain.IsEmpty ? null : chain;
        });

    private static readonly ConcurrentDictionary<PropertyInfo, ValueTransform?> TransformsByMember = new();

    /// <summary>A public instance property by name, whatever its letter case, as a path names one.</summary>
    private static PropertyInfo? Find(Type type, string name)
    {
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length == 0
                && string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property;
            }
        }

        return null;
    }

    /// <summary>
    /// The denials a member carries from another declaration of it: an interface member it implements,
    /// and, in a type loaded below the one walked, the override, the member hidden with <c>new</c>, or the
    /// implementation that a row of that type runs.
    /// </summary>
    /// <remarks>
    /// Attributes are read from the declaration walked, and inheritance only reaches up the chain. A row
    /// read through a base type or an interface is still the subtype it is, though, and its member returns
    /// what the subtype's declaration returns: a <c>[DwDenied]</c> on <c>override Code</c>, or on the
    /// class's implementation of <c>IAccount.Iban</c>, was never seen through the base path, which filtered,
    /// sorted, grouped and returned it. Each such denial applies to the path for every row, since a
    /// projection cannot withhold a field from some rows only.
    /// <para>
    /// Every declaration a row can run counts: an override of either accessor, an implementation declared
    /// explicitly, inherited from a base class or by an open generic class, and an interface a subtype adds
    /// over a member it inherits. So does a member a subtype hides with <c>new</c>: whether it reads the
    /// member it hides cannot be told from outside, and a row serialized as its own type writes it under the
    /// same name.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<DwDenyAttribute> DenialsElsewhere(Type type, PropertyInfo property)
    {
        int epoch = KnownSubtypes.Epoch;

        if (Elsewhere.TryGetValue((type, property), out (int Epoch, DwDenyAttribute[] Denials) known) && known.Epoch == epoch)
        {
            return known.Denials;
        }

        DwDenyAttribute[] denials = ReadDenialsElsewhere(type, property).ToArray();

        Elsewhere[(type, property)] = (epoch, denials);

        return denials;
    }

    /// <summary>The denials of each member's other declarations, read once for each type and member until another assembly loads.</summary>
    private static readonly ConcurrentDictionary<(Type Type, PropertyInfo Property), (int Epoch, DwDenyAttribute[] Denials)> Elsewhere = new();

    private static IEnumerable<DwDenyAttribute> ReadDenialsElsewhere(Type type, PropertyInfo property)
    {
        MethodInfo[] accessors = property.GetAccessors(nonPublic: true);

        if (accessors.Length == 0)
        {
            yield break;
        }

        // Many rows reach one declaration, an interface every subtype implements, and it is read once.
        HashSet<PropertyInfo> read = new() { property };

        foreach (Type row in KnownSubtypes.Of(type).Prepend(type))
        {
            if (row.IsInterface)
            {
                continue;
            }

            // What a row of this type runs for the member: the member, or what the row declares in its
            // place; read through an interface, the row's implementation of it.
            List<MethodInfo> runs = new();

            if (type.IsInterface)
            {
                foreach (PropertyInfo implementation in Implementations(row, type, accessors))
                {
                    runs.AddRange(implementation.GetAccessors(nonPublic: true));

                    if (read.Add(implementation))
                    {
                        foreach (DwDenyAttribute attribute in implementation.GetCustomAttributes<DwDenyAttribute>(inherit: true))
                        {
                            yield return attribute;
                        }
                    }
                }
            }
            else
            {
                runs.AddRange(accessors);

                if (row != type)
                {
                    foreach (PropertyInfo below in Redeclarations(row, property))
                    {
                        runs.AddRange(below.GetAccessors(nonPublic: true));

                        if (read.Add(below))
                        {
                            foreach (DwDenyAttribute attribute in below.GetCustomAttributes<DwDenyAttribute>(inherit: false))
                            {
                                yield return attribute;
                            }
                        }
                    }
                }
            }

            foreach (PropertyInfo declared in Declarations(row, runs, accessors))
            {
                if (read.Add(declared))
                {
                    foreach (DwDenyAttribute attribute in declared.GetCustomAttributes<DwDenyAttribute>(inherit: false))
                    {
                        yield return attribute;
                    }
                }
            }
        }
    }

    /// <summary>
    /// What a subtype declares in a member's place: an override of either accessor, or a public member of the
    /// same name that hides it. Both carry the member's name. One the subtype keeps to itself hides nothing a
    /// caller reads, and no serializer writes it.
    /// </summary>
    private static IEnumerable<PropertyInfo> Redeclarations(Type subtype, PropertyInfo property) =>
        subtype
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(candidate => candidate.Name == property.Name);

    /// <summary>
    /// The properties a class implements an interface's member with: through the interface itself, through an
    /// instantiation variance lets stand for it, IFeed&lt;VisaCard&gt; for IFeed&lt;Card&gt; with <c>out T</c>, and, for
    /// a class that is itself open generic, through one over its own type parameters, which may be any.
    /// </summary>
    private static IEnumerable<PropertyInfo> Implementations(Type row, Type contract, MethodInfo[] accessors)
    {
        List<PropertyInfo> found = new();

        foreach (Type implemented in row.GetInterfaces())
        {
            bool same = implemented == contract
                        || (implemented.IsGenericType && contract.IsGenericType
                            && implemented.GetGenericTypeDefinition() == contract.GetGenericTypeDefinition()
                            && (implemented.ContainsGenericParameters || contract.ContainsGenericParameters
                                || contract.IsAssignableFrom(implemented)));

            if (!same || Map(row, implemented) is not { } map)
            {
                continue;
            }

            for (int i = 0; i < map.InterfaceMethods.Length; i++)
            {
                if (accessors.Any(accessor => Same(map.InterfaceMethods[i], accessor))
                    && Declaring(map.TargetMethods[i]) is { } implementation
                    && !found.Contains(implementation))
                {
                    found.Add(implementation);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// The interface properties a class implements with any of the accessors it runs, other than the member
    /// walked itself.
    /// </summary>
    private static IEnumerable<PropertyInfo> Declarations(Type row, List<MethodInfo> runs, MethodInfo[] own)
    {
        MethodInfo[] roots = runs.Select(accessor => accessor.GetBaseDefinition()).ToArray();
        List<PropertyInfo> found = new();

        foreach (Type contract in row.GetInterfaces())
        {
            if (Map(row, contract) is not { } map)
            {
                continue;
            }

            for (int i = 0; i < map.TargetMethods.Length; i++)
            {
                MethodInfo declared = map.InterfaceMethods[i];

                if (roots.Any(root => Same(map.TargetMethods[i].GetBaseDefinition(), root))
                    && !own.Any(accessor => Same(accessor, declared))
                    && Declaring(declared) is { } property
                    && !found.Contains(property))
                {
                    found.Add(property);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// A class's interface map, or null when the runtime cannot map it, as for some open generic types.
    /// Read once for each class and interface.
    /// </summary>
    private static InterfaceMapping? Map(Type type, Type contract) =>
        Maps.GetOrAdd((type, contract), static key =>
        {
            try
            {
                return key.Type.GetInterfaceMap(key.Contract);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                return null;
            }
        });

    private static readonly ConcurrentDictionary<(Type Type, Type Contract), InterfaceMapping?> Maps = new();

    /// <summary>The property an accessor belongs to, found on the type that declares the accessor.</summary>
    private static PropertyInfo? Declaring(MethodInfo accessor) =>
        accessor.DeclaringType?
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .FirstOrDefault(candidate => candidate.GetAccessors(nonPublic: true).Any(method => Same(method, accessor)));

    private static bool Same(MethodInfo left, MethodInfo right) =>
        left.MetadataToken == right.MetadataToken && left.Module == right.Module;

    /// <summary>
    /// How many collection layers <see cref="NavigationTypeOf"/> peels off a single property type
    /// before it stops and walks whatever it has reached.
    /// </summary>
    /// <remarks>
    /// Two layers cover every shape seen in practice — an array of a collection, a jagged array, a
    /// collection of collections — so four leaves headroom without the count ever mattering to a
    /// real model. The limit exists for the types that have no bottom to reach: a tree node
    /// declared as <c>class Node : IEnumerable&lt;Node&gt;</c> unwraps to itself forever, and a pair
    /// declared as <c>A : IEnumerable&lt;B&gt;</c> and <c>B : IEnumerable&lt;A&gt;</c> alternates
    /// forever without ever unwrapping to the type it just came from, so a guard comparing one
    /// layer against the next would not stop it. Any fixed count stops both, whatever the length of
    /// the cycle. Reaching the limit is not treated as "no navigation": the type reached is still
    /// walked, because skipping it would suppress every attribute beneath it.
    /// </remarks>
    private const int MaxCollectionLayers = 4;

    /// <summary>
    /// Returns the type to descend into for a property, or null when the property holds a value
    /// rather than a navigation. Collections yield their element type, so <c>Orders.Total</c>
    /// resolves the same way a reference navigation does.
    /// </summary>
    /// <remarks>
    /// A navigation the walker fails to recognize produces no fragments for anything beneath it,
    /// and a field with no fragment is allowed — so this errs toward descending. Element types are
    /// taken from the <see cref="IEnumerable{T}"/> a type implements rather than from where that
    /// type is declared, which covers arrays, the BCL collections, and an application's own
    /// <c>PagedList&lt;T&gt;</c> alike. Collection layers are peeled until a non-collection is
    /// reached, so <c>PagedList&lt;LineDto&gt;[]</c>, <c>LineDto[][]</c> and
    /// <c>List&lt;List&lt;LineDto&gt;&gt;</c> all resolve to <c>LineDto</c> rather than to the
    /// collection in between, whose own members (<c>Count</c>, <c>Length</c>, <c>Item</c>) carry no
    /// policy and would end the walk short of the decorated fields. The value tests are applied
    /// once, to whatever is left at the bottom, so peeling extra layers never turns
    /// <c>byte[][]</c> into a walk over <see cref="byte"/>. Interfaces and user-defined structs are
    /// followed as well as classes, because these attributes are supported on DTOs, where an
    /// <c>IContact</c> reference or a record struct is ordinary.
    /// </remarks>
    internal static Type? NavigationTypeOf(Type propertyType) => AsNavigation(Peeled(propertyType));

    /// <summary>
    /// The type a property holds once every collection layer and <see cref="Nullable{T}"/> is peeled
    /// off: <c>LineDto</c> for <c>IReadOnlyList&lt;LineDto&gt;</c>, <see cref="string"/> for
    /// <c>List&lt;string&gt;</c>, and the property's own type when it is not a collection.
    /// </summary>
    /// <remarks>
    /// The one reading of a property's shape that the walker and the projection gate share. The gate
    /// once read collections through a narrower list of its own, and a member typed
    /// <c>IReadOnlyList&lt;T&gt;</c> hid every denial beneath it from the projection while the walker
    /// had put a fragment on each of them.
    /// </remarks>
    internal static Type Peeled(Type propertyType)
    {
        Type current = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        for (int layer = 0; layer < MaxCollectionLayers; layer++)
        {
            Type? element = ElementTypeOf(current);

            if (element is null)
            {
                break;
            }

            current = Nullable.GetUnderlyingType(element) ?? element;
        }

        return current;
    }

    /// <summary>
    /// The type a property holds, then each collection layer beneath it, down to the element
    /// <see cref="Peeled"/> reaches: <c>List&lt;Tags&gt;</c>, then <c>Tags</c>, then <see cref="string"/>.
    /// </summary>
    internal static IEnumerable<Type> Layers(Type propertyType)
    {
        Type current = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        yield return current;

        for (int layer = 0; layer < MaxCollectionLayers; layer++)
        {
            Type? element = ElementTypeOf(current);

            if (element is null)
            {
                yield break;
            }

            current = Nullable.GetUnderlyingType(element) ?? element;

            yield return current;
        }
    }

    /// <summary>
    /// Returns the element type of one collection layer — the element of an array, or the <c>T</c>
    /// of the first <see cref="IEnumerable{T}"/> a type implements — or null when the type is not a
    /// collection.
    /// </summary>
    /// <remarks>
    /// Arrays are read through <see cref="Type.GetElementType"/> rather than through their
    /// interfaces, because a multidimensional array implements only the non-generic
    /// <see cref="System.Collections.IEnumerable"/> and would otherwise be mistaken for a value.
    /// <see cref="string"/> is excluded explicitly: it implements <c>IEnumerable&lt;char&gt;</c>,
    /// and treating text as a collection of characters would send the walker into
    /// <see cref="char"/> on every string property in the model.
    /// </remarks>
    private static Type? ElementTypeOf(Type type)
    {
        if (type == typeof(string))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return type.GetGenericArguments()[0];
        }

        foreach (Type contract in type.GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return contract.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the type when it is one the walker should descend into, otherwise null.
    /// </summary>
    /// <remarks>
    /// This is the only place a type is rejected as a value, and it runs once, on what is left
    /// after every collection layer has been peeled — which is what keeps <c>byte[][]</c>, and an
    /// array of a <c>List&lt;string&gt;</c> subclass, from being walked as navigations. The
    /// namespace test stands in for "declared by the framework rather than by the application": a
    /// BCL type reached at the bottom of a property, such as <see cref="DateTime"/> or a
    /// <see cref="KeyValuePair{TKey,TValue}"/> out of a dictionary, holds no policy attributes and
    /// is a value as far as a filter is concerned.
    /// </remarks>
    private static Type? AsNavigation(Type? type)
    {
        if (type is null || type == typeof(string) || type.IsPrimitive || type.IsEnum)
        {
            return null;
        }

        return IsFramework(type) ? null : type;
    }

    /// <summary>
    /// True for a type the framework declares: one in the <c>System</c> namespace or beneath it.
    /// </summary>
    /// <remarks>
    /// The namespace is compared as a whole segment. A prefix match took an application's own
    /// <c>SystemsCorp.Payroll</c> or <c>SystemX.Domain</c> for the framework, so nothing beneath one of
    /// its types got a fragment, and a <c>[DwDenied]</c> field there was returned and filtered on as
    /// though it carried no policy.
    /// </remarks>
    internal static bool IsFramework(Type type) =>
        type.Namespace is { } name
        && (name == "System" || name.StartsWith("System.", StringComparison.Ordinal));

    /// <summary>
    /// Converts one attribute into a fragment at the level its <c>Overridable</c> flag implies.
    /// </summary>
    private static PolicyFragment ToFragment(string fieldPath, DwDenyAttribute attribute)
    {
        PolicyLevel level = attribute.Overridable
            ? PolicyLevel.OverridableAttribute
            : PolicyLevel.SealedAttribute;

        PolicySource source = PolicySource.FromAttribute(
            attribute.GetType().Name,
            isSealed: !attribute.Overridable);

        return new PolicyFragment(fieldPath, attribute.Features, PolicyEffect.Deny, level, source);
    }

    /// <summary>
    /// Converts an operator restriction into a fragment.
    /// </summary>
    /// <remarks>
    /// The fragment speaks to <see cref="PolicyFeature.None"/>, because a restriction refuses
    /// nothing on its own — it only narrows what an already-permitted filter may do. The restriction
    /// rides on the fragment's typed operator list, which the resolver intersects rather than
    /// elects, so covering no feature costs it nothing.
    /// <para>
    /// Claiming <c>Where</c> with <see cref="PolicyEffect.Allow"/> would be worse than pointless:
    /// an attribute is sealed unless its author says otherwise, and a sealed allowance outranks
    /// every runtime denial — so decorating a field with an operator restriction would have made
    /// that field impossible for any rule to deny.
    /// </para>
    /// </remarks>
    private static PolicyFragment ToFragment(string fieldPath, DwOperatorsAttribute attribute)
    {
        PolicyLevel level = attribute.Overridable
            ? PolicyLevel.OverridableAttribute
            : PolicyLevel.SealedAttribute;

        PolicySource source = PolicySource.FromAttribute(
            attribute.GetType().Name,
            isSealed: !attribute.Overridable);

        return new PolicyFragment(
            fieldPath,
            PolicyFeature.None,
            PolicyEffect.Allow,
            level,
            source,
            allowedOperators: attribute.Resolve());
    }

    /// <summary>
    /// Converts a public name into a fragment.
    /// </summary>
    /// <remarks>
    /// The fragment speaks to <see cref="PolicyFeature.None"/> because naming a field neither
    /// refuses nor permits anything. The alias rides on the fragment's typed field and is elected
    /// outside the per-feature contest, so covering no feature costs it nothing — and entering that
    /// contest would cost a great deal, since a sealed allowance outranks every runtime denial and
    /// an aliased field would become impossible for a rule to deny.
    /// </remarks>
    private static PolicyFragment ToFragment(string fieldPath, DwAliasAttribute attribute) =>
        new(fieldPath,
            PolicyFeature.None,
            PolicyEffect.Allow,
            LevelOf(attribute),
            SourceOf(attribute),
            alias: attribute.Name);

    /// <summary>
    /// Converts a filtering requirement into a fragment.
    /// </summary>
    /// <remarks>
    /// <see cref="PolicyFeature.None"/> for the reason the alias and the operator restriction use
    /// it: the requirement rides on the fragment's typed operator list and is elected outside the
    /// per-feature contest. Demanding that a caller filter on a field says nothing about whether
    /// they may, and a sealed allowance saying they may would outrank every runtime denial.
    /// </remarks>
    private static PolicyFragment ToFragment(string fieldPath, DwRequireWhereAttribute attribute) =>
        new(fieldPath,
            PolicyFeature.None,
            PolicyEffect.Allow,
            LevelOf(attribute),
            SourceOf(attribute),
            requiredOperators: attribute.Resolve());

    /// <summary>
    /// Converts a forced predicate into a fragment, resolving the value's data type from the member
    /// it decorates.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the attribute names neither a constant nor a context key, names both, or
    /// decorates a member whose CLR type has no <see cref="DataType"/> counterpart.
    /// </exception>
    internal static PolicyFragment ToFragment(
        string fieldPath, PropertyInfo property, DwForceWhereAttribute attribute)
    {
        bool hasValue = attribute.Value is not null;
        bool hasContextValue = attribute.ContextValue is not null;
        bool isNullCheck = attribute.Operator is Operator.IsNull or Operator.IsNotNull;

        // Refused rather than resolved in favour of one. Neither leaves nothing to inject, and both
        // leaves no way to choose -- and the wrong choice here is a tenant scope filtering on a
        // constant somebody left behind. A null check is the exception: it compares against
        // nothing, which is how soft deletion is usually spelled.
        if (isNullCheck && (hasValue || hasContextValue))
        {
            throw new ArgumentException(
                $"[DwForceWhere({attribute.Operator})] on '{fieldPath}' compares against nothing, " +
                "so it must set neither Value nor ContextValue.");
        }

        if (!isNullCheck && hasValue == hasContextValue)
        {
            throw new ArgumentException(
                $"[DwForceWhere] on '{fieldPath}' must set exactly one of Value or ContextValue; " +
                (hasValue ? "it sets both." : "it sets neither."));
        }

        if (attribute.AllowNull && isNullCheck)
        {
            throw new ArgumentException(
                $"[DwForceWhere({attribute.Operator}, AllowNull = true)] on '{fieldPath}' already " +
                "decides about null, so there is no comparison for AllowNull to widen.");
        }

        // A member that can never be null would make the widening dead code at best, and at worst
        // the sign that the attribute was put on the wrong member.
        if (attribute.AllowNull
            && property.PropertyType.IsValueType
            && Nullable.GetUnderlyingType(property.PropertyType) is null)
        {
            throw new ArgumentException(
                $"[DwForceWhere(AllowNull = true)] on '{fieldPath}' decorates a " +
                $"{property.PropertyType.Name}, which can never be null.");
        }

        DataType dataType = DataTypeOf(property, fieldPath);

        ForcedPredicate forced = isNullCheck
            ? ForcedPredicate.FromNullCheck(fieldPath, attribute.Operator, dataType)
            : hasContextValue
                ? ForcedPredicate.FromContext(
                    fieldPath, attribute.Operator, dataType, attribute.ContextValue!, attribute.AllowNull)
                : ForcedPredicate.FromConstant(
                    fieldPath, attribute.Operator, dataType, attribute.Value!, attribute.AllowNull);

        // PolicyFeature.None: a forced predicate is the library filtering on the caller's behalf,
        // which says nothing about whether the caller may filter on the field themselves — the two
        // are routinely opposite, and that pairing is the usual shape of a tenant boundary. The
        // predicate is accumulated outside the per-feature contest, so covering no feature costs it
        // nothing, where claiming Where with Allow at the sealed level would make every scoped
        // field impossible for a rule to deny.
        return new PolicyFragment(
            fieldPath,
            PolicyFeature.None,
            PolicyEffect.Allow,
            LevelOf(attribute),
            SourceOf(attribute),
            forced: forced);
    }

    /// <summary>
    /// Reads every transform attribute on a member and yields the stage each one describes.
    /// </summary>
    /// <remarks>
    /// A member carries at most one of each, so the six are read independently and a member with a
    /// mask and a truncation yields two stages that the resolver elects separately.
    /// </remarks>
    private static IEnumerable<TransformStage> TransformStagesOn(PropertyInfo property)
    {
        if (property.GetCustomAttribute<DwMutateAttribute>(inherit: true) is { } mutate)
        {
            yield return new MutateStage(
                mutate.Transformer, mutate.AllowAggregate, mutate.MinGroupSize);
        }

        if (property.GetCustomAttribute<DwGeneralizeAttribute>(inherit: true) is { } generalize)
        {
            yield return new GeneralizeStage(
                generalize.Mode, generalize.Step, generalize.Part, generalize.Decimals,
                generalize.AllowAggregate, generalize.MinGroupSize);
        }

        if (property.GetCustomAttribute<DwFormatAttribute>(inherit: true) is { } format)
        {
            yield return new FormatStage(
                format.Format, format.AllowAggregate, format.MinGroupSize);
        }

        if (property.GetCustomAttribute<DwMaskAttribute>(inherit: true) is { } mask)
        {
            yield return new MaskStage(
                mask.Strategy, mask.KeepStart, mask.KeepEnd, mask.MaskChar, mask.PreserveLength,
                mask.Pattern, mask.Replacement, mask.Text,
                mask.AllowAggregate, mask.MinGroupSize, mask.TokenScope);
        }

        if (property.GetCustomAttribute<DwTruncateAttribute>(inherit: true) is { } truncate)
        {
            yield return new TruncateStage(
                truncate.Length, truncate.Ellipsis, truncate.AllowAggregate, truncate.MinGroupSize);
        }

        if (property.GetCustomAttribute<DwDefaultAttribute>(inherit: true) is { } replacement)
        {
            yield return new DefaultStage(
                replacement.Value, replacement.HasValue,
                replacement.AllowAggregate, replacement.MinGroupSize);
        }
    }

    /// <summary>Finds the attribute a stage came from, so its level and source are its own.</summary>
    private static DwPolicyAttribute StageAttributeOn(PropertyInfo property, TransformKind kind) =>
        kind switch
        {
            TransformKind.Mutate => property.GetCustomAttribute<DwMutateAttribute>(inherit: true)!,
            TransformKind.Generalize => property.GetCustomAttribute<DwGeneralizeAttribute>(inherit: true)!,
            TransformKind.Format => property.GetCustomAttribute<DwFormatAttribute>(inherit: true)!,
            TransformKind.Mask => property.GetCustomAttribute<DwMaskAttribute>(inherit: true)!,
            TransformKind.Truncate => property.GetCustomAttribute<DwTruncateAttribute>(inherit: true)!,
            _ => property.GetCustomAttribute<DwDefaultAttribute>(inherit: true)!
        };

    /// <summary>
    /// Converts one transform stage into a fragment.
    /// </summary>
    /// <remarks>
    /// The effect is <see cref="PolicyEffect.Mask"/> on <see cref="PolicyFeature.Select"/>, which is
    /// what that effect has always meant: the feature proceeds and the value is transformed on
    /// output. It makes <c>FieldPolicy.IsMasked(Select)</c> real for the first time — nothing could
    /// produce a Mask effect until now — and it keeps a masked field filterable and sortable, since
    /// <c>Allows</c> refuses only a denial.
    /// <para>
    /// Because the effect competes in the per-feature election, a <see cref="DwDenyAttribute"/> on
    /// the same field and level still wins: Deny outranks Mask. A field both denied and masked is
    /// dropped from the projection rather than masked in it, which is the stricter reading.
    /// </para>
    /// </remarks>
    private static PolicyFragment ToFragment(
        string fieldPath, TransformStage stage, DwPolicyAttribute attribute) =>
        new(fieldPath,
            PolicyFeature.Select,
            PolicyEffect.Mask,
            LevelOf(attribute),
            SourceOf(attribute),
            transform: stage);

    /// <summary>
    /// Yields a fragment for each of the four attributes that describe or budget a member rather
    /// than deciding access to it.
    /// </summary>
    /// <remarks>
    /// All four speak to <see cref="PolicyFeature.None"/>. They decide nothing, and a sealed
    /// allowance would outrank every runtime denial — so weighing a field with <c>[DwCost]</c>
    /// would otherwise make that field impossible to deny, which is the opposite of what an
    /// expensive field wants.
    /// </remarks>
    private static IEnumerable<PolicyFragment> FactFragmentsOn(string fieldPath, PropertyInfo property)
    {
        DwDescribeAttribute? describe = property.GetCustomAttribute<DwDescribeAttribute>(inherit: true);

        if (describe is not null)
        {
            yield return ToFactFragment(
                fieldPath,
                describe,
                new FieldFacts(
                    describe.Label, describe.Description, describe.Group, describe.DeclaredOrder));
        }

        DwAllowedValuesAttribute? values =
            property.GetCustomAttribute<DwAllowedValuesAttribute>(inherit: true);

        if (values is not null)
        {
            yield return ToFactFragment(
                fieldPath, values, new FieldFacts(allowedValues: values.Values));
        }

        DwCostAttribute? cost = property.GetCustomAttribute<DwCostAttribute>(inherit: true);

        if (cost is not null)
        {
            yield return ToFactFragment(fieldPath, cost, FieldFacts.ForCost(cost.Weight));
        }

        DwAuditAttribute? audit = property.GetCustomAttribute<DwAuditAttribute>(inherit: true);

        if (audit is not null)
        {
            yield return ToFactFragment(fieldPath, audit, FieldFacts.ForAudit(audit.Features));
        }
    }

    /// <summary>Wraps a set of facts in a fragment that decides nothing.</summary>
    private static PolicyFragment ToFactFragment(
        string fieldPath, DwPolicyAttribute attribute, FieldFacts facts) =>
        new(fieldPath,
            PolicyFeature.None,
            PolicyEffect.Allow,
            LevelOf(attribute),
            SourceOf(attribute),
            facts: facts);

    /// <summary>The level an attribute's <c>Overridable</c> flag places it at.</summary>
    private static PolicyLevel LevelOf(DwPolicyAttribute attribute) =>
        attribute.Overridable ? PolicyLevel.OverridableAttribute : PolicyLevel.SealedAttribute;

    /// <summary>The source describing one attribute.</summary>
    private static PolicySource SourceOf(DwPolicyAttribute attribute) =>
        PolicySource.FromAttribute(attribute.GetType().Name, isSealed: !attribute.Overridable);

    /// <summary>
    /// Maps a member's CLR type onto the <see cref="DataType"/> the pipeline will validate the
    /// injected value against.
    /// </summary>
    /// <remarks>
    /// Read from the member rather than declared on the attribute. C# forbids a nullable enum as an
    /// attribute argument, so an override would need a sentinel or a paired flag — and it could only
    /// ever disagree with the type the value is about to be parsed as.
    /// <para>
    /// A type with no counterpart is refused here, at the misconfiguration, rather than guessed into
    /// a downstream parse failure that names neither the field nor the attribute.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when the CLR type has no counterpart.</exception>
    private static DataType DataTypeOf(PropertyInfo property, string fieldPath) =>
        TryDataTypeOf(property)
        ?? throw new ArgumentException(
            $"[DwForceWhere] on '{fieldPath}' decorates a "
            + $"{(Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType).Name}, "
            + "which has no DataType counterpart, so the injected value has no form the pipeline "
            + "could parse it into.");

    /// <summary>
    /// The <see cref="DataType"/> a filter would use for a member, or null when the member's type
    /// has no counterpart.
    /// </summary>
    /// <remarks>
    /// Internal so the schema builder advertises the same answer this provider injects with. Two
    /// mappings from a CLR type to a <see cref="DataType"/> would eventually disagree, and the
    /// schema would then tell a front end to send a value the pipeline refuses to parse.
    /// </remarks>
    internal static DataType? TryDataTypeOf(PropertyInfo property)
    {
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (type.IsEnum)
        {
            return DataType.Enum;
        }

        if (type == typeof(string) || type == typeof(char))
        {
            return DataType.Text;
        }

        if (type == typeof(Guid))
        {
            return DataType.Guid;
        }

        if (type == typeof(bool))
        {
            return DataType.Boolean;
        }

        if (type == typeof(DateOnly))
        {
            return DataType.Date;
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return DataType.DateTime;
        }

        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short)
            || type == typeof(ushort) || type == typeof(int) || type == typeof(uint)
            || type == typeof(long) || type == typeof(ulong) || type == typeof(float)
            || type == typeof(double) || type == typeof(decimal))
        {
            return DataType.Number;
        }

        return null;
    }
}
