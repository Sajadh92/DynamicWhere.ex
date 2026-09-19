using System.Collections.Concurrent;
using System.Reflection;

namespace DynamicWhere.ex.Policies.Source;

/// <summary>
/// The types loaded into the process that derive from a type or implement it: what a member declared as
/// that type can hold at run time.
/// </summary>
/// <remarks>
/// A policy is read from declared types, and a member declared as a base class or an interface holds
/// whichever subtype its value is. A <c>[DwDenied]</c> field a subtype declares is therefore carried by a
/// member the policy reads as clean, unless the subtypes are read too.
/// <para>
/// Every object's type is loaded before the object exists, so the loaded assemblies are the whole answer
/// for any value a query returns. The framework's own assemblies are left out: none of them declares a
/// subtype of an application's type, and a framework type carries no policy. Every other assembly is
/// indexed once, each type under every base class and interface it has, so a search is a lookup. A
/// generic type is indexed under its definition as well as its own instantiation, and a subtype that is
/// itself generic is returned open, since the instantiation a row holds is not known.
/// </para>
/// <para>
/// Loading another such assembly may add subtypes, so the index is rebuilt after one loads. An assembly
/// emitted at run time, or one holding only resources, cannot declare one.
/// </para>
/// </remarks>
internal static class KnownSubtypes
{
    private static readonly object Building = new();

    /// <summary>Every assembly seen loading, in case one raises its event before the domain lists it.</summary>
    private static readonly ConcurrentDictionary<Assembly, byte> SeenLoading = new();

    private static int _epoch;

    private static Index? _index;

    static KnownSubtypes() =>
        AppDomain.CurrentDomain.AssemblyLoad += (_, loaded) =>
        {
            if (Searched(loaded.LoadedAssembly))
            {
                SeenLoading.TryAdd(loaded.LoadedAssembly, 0);
                Interlocked.Increment(ref _epoch);
            }
        };

    /// <summary>Changes whenever an assembly that could declare a subtype loads, and with it every answer read from one.</summary>
    internal static int Epoch => Volatile.Read(ref _epoch);

    /// <summary>Every loaded type, other than the type itself, whose values the type's members can hold.</summary>
    internal static IReadOnlyList<Type> Of(Type type)
    {
        if (type.IsSealed || type.IsValueType || type.IsGenericParameter || type == typeof(object))
        {
            return Array.Empty<Type>();
        }

        Index index = Current();
        List<Type>? found = null;

        if (index.Descendants.TryGetValue(type, out Type[]? direct))
        {
            found = new List<Type>(direct);
        }

        // A subtype declared over the definition, class Tagged<T> : Base<T>, may be any instantiation of it;
        // and a type still open itself may be any instantiation, so every subtype of the definition counts.
        if (type.IsGenericType
            && index.Descendants.TryGetValue(type.GetGenericTypeDefinition(), out Type[]? byDefinition))
        {
            found ??= new List<Type>();

            foreach (Type candidate in byDefinition)
            {
                if ((type.ContainsGenericParameters || CanBe(candidate, type)) && !found.Contains(candidate))
                {
                    found.Add(candidate);
                }
            }
        }

        return found is null ? Array.Empty<Type>() : found;
    }

    /// <summary>
    /// True when a type indexed under a generic type's definition can be a value of one instantiation of it.
    /// A closed type can when the runtime says so, which reads variance: a member typed
    /// IFeed&lt;Card&gt;, with <c>out T</c>, holds an IFeed&lt;VisaCard&gt;. An open generic one can when its own
    /// base or interface of that definition is still open, or is one of those. One over another
    /// instantiation, class Fixed&lt;T&gt; : Base&lt;string&gt;, never holds a Base&lt;int&gt;.
    /// </summary>
    private static bool CanBe(Type candidate, Type type)
    {
        if (!candidate.ContainsGenericParameters)
        {
            return type.IsAssignableFrom(candidate);
        }

        Type definition = type.GetGenericTypeDefinition();
        IEnumerable<Type> ancestors = type.IsInterface ? Interfaces(candidate) : BaseTypes(candidate);

        return ancestors.Any(ancestor => ancestor.IsGenericType
                                         && ancestor.GetGenericTypeDefinition() == definition
                                         && (ancestor.ContainsGenericParameters || type.IsAssignableFrom(ancestor)));
    }

    private static IEnumerable<Type> BaseTypes(Type type)
    {
        for (Type? ancestor = type.BaseType; ancestor is not null; ancestor = ancestor.BaseType)
        {
            yield return ancestor;
        }
    }

    /// <summary>The index for the assemblies loaded now, built once for each epoch.</summary>
    private static Index Current()
    {
        if (Volatile.Read(ref _index) is { } index && index.Epoch == Epoch)
        {
            return index;
        }

        lock (Building)
        {
            if (_index is { } built && built.Epoch == Epoch)
            {
                return built;
            }

            // The epoch is read before the assemblies, so one loading meanwhile rebuilds the index again.
            int epoch = Epoch;
            Index fresh = Build(epoch);

            Volatile.Write(ref _index, fresh);

            return fresh;
        }
    }

    private static Index Build(int epoch)
    {
        HashSet<Assembly> assemblies = new(AppDomain.CurrentDomain.GetAssemblies().Where(Searched));

        // One the domain lists now needs no holding on to: this index reads it, and every later one will.
        foreach (Assembly seen in SeenLoading.Keys)
        {
            if (!assemblies.Add(seen))
            {
                SeenLoading.TryRemove(seen, out _);
            }
        }

        Dictionary<Type, List<Type>> descendants = new();

        void Add(Type ancestor, Type type)
        {
            Type key = ancestor.IsGenericType && ancestor.ContainsGenericParameters
                ? ancestor.GetGenericTypeDefinition()
                : ancestor;

            if (!descendants.TryGetValue(key, out List<Type>? list))
            {
                descendants[key] = list = new List<Type>();
            }

            list.Add(type);

            if (key != ancestor || !ancestor.IsGenericType)
            {
                return;
            }

            // A closed ancestor, Base<int>, is also one instantiation of its definition.
            Type definition = ancestor.GetGenericTypeDefinition();

            if (!descendants.TryGetValue(definition, out List<Type>? byDefinition))
            {
                descendants[definition] = byDefinition = new List<Type>();
            }

            byDefinition.Add(type);
        }

        foreach (Assembly assembly in assemblies)
        {
            foreach (Type type in TypesOf(assembly))
            {
                for (Type? ancestor = type.BaseType; ancestor is not null && ancestor != typeof(object); ancestor = ancestor.BaseType)
                {
                    Add(ancestor, type);
                }

                foreach (Type contract in Interfaces(type))
                {
                    Add(contract, type);
                }
            }
        }

        return new Index(epoch, descendants.ToDictionary(entry => entry.Key, entry => entry.Value.Distinct().ToArray()));
    }

    /// <summary>An assembly that can declare a subtype of an application's type.</summary>
    private static bool Searched(Assembly assembly)
    {
        if (assembly.IsDynamic)
        {
            return false;
        }

        AssemblyName name = assembly.GetName();

        if (!string.IsNullOrEmpty(name.CultureName))
        {
            return false;
        }

        string simple = name.Name ?? string.Empty;

        return !(simple is "mscorlib" or "netstandard" or "System" or "WindowsBase"
                 || simple.StartsWith("System.", StringComparison.Ordinal)
                 || simple.StartsWith("Microsoft.", StringComparison.Ordinal));
    }

    private static Type[] TypesOf(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            return partial.Types.OfType<Type>().ToArray();
        }
    }

    private static Type[] Interfaces(Type type)
    {
        try
        {
            return type.GetInterfaces();
        }
        catch (TypeLoadException)
        {
            return Array.Empty<Type>();
        }
    }

    private sealed record Index(int Epoch, Dictionary<Type, Type[]> Descendants);
}
