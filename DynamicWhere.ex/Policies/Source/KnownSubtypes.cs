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
/// for any value a query returns. Only an assembly that is the type's own, or references it, can declare
/// a subtype, which keeps the framework's and every unrelated library's assemblies out of the search.
/// Loading another assembly may add subtypes, so each answer is kept only until the next one loads;
/// an assembly emitted at run time is not searched, since it cannot declare a policy attribute a
/// compiled type does not already carry.
/// </para>
/// </remarks>
internal static class KnownSubtypes
{
    private static readonly ConcurrentDictionary<Type, (int Epoch, Type[] Types)> Found = new();

    private static int _epoch;

    private static Snapshot? _snapshot;

    static KnownSubtypes() =>
        AppDomain.CurrentDomain.AssemblyLoad += (_, loaded) =>
        {
            if (!loaded.LoadedAssembly.IsDynamic)
            {
                Interlocked.Increment(ref _epoch);
            }
        };

    /// <summary>Changes whenever an assembly loads, and with it any answer read from the subtypes.</summary>
    internal static int Epoch => Volatile.Read(ref _epoch);

    /// <summary>Every loaded type, other than the type itself, whose values the type's members can hold.</summary>
    internal static IReadOnlyList<Type> Of(Type type)
    {
        if (type.IsSealed || type.IsValueType || type.IsGenericParameter || type == typeof(object))
        {
            return Array.Empty<Type>();
        }

        int epoch = Epoch;

        if (Found.TryGetValue(type, out (int Epoch, Type[] Types) known) && known.Epoch == epoch)
        {
            return known.Types;
        }

        Type[] types = Search(type, Assemblies(epoch));

        Found[type] = (epoch, types);

        return types;
    }

    private static Type[] Search(Type type, IEnumerable<Candidate> assemblies)
    {
        string home = type.Assembly.GetName().Name ?? string.Empty;
        List<Type> found = new();

        foreach (Candidate candidate in assemblies)
        {
            if (candidate.Assembly != type.Assembly && !candidate.References.Contains(home))
            {
                continue;
            }

            foreach (Type declared in candidate.Types.Value)
            {
                if (declared != type && !declared.ContainsGenericParameters && type.IsAssignableFrom(declared))
                {
                    found.Add(declared);
                }
            }
        }

        return found.ToArray();
    }

    /// <summary>
    /// The loaded assemblies that could declare a subtype, each with the names it references, read once
    /// per epoch; an assembly's types are read only when a search reaches it.
    /// </summary>
    private static Candidate[] Assemblies(int epoch)
    {
        if (Volatile.Read(ref _snapshot) is { } snapshot && snapshot.Epoch == epoch)
        {
            return snapshot.Candidates;
        }

        Candidate[] candidates = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic)
            .Select(assembly => new Candidate(
                assembly,
                new HashSet<string>(
                    assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty),
                    StringComparer.Ordinal),
                new Lazy<Type[]>(() => TypesOf(assembly))))
            .ToArray();

        Volatile.Write(ref _snapshot, new Snapshot(epoch, candidates));

        return candidates;
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

    private sealed record Candidate(Assembly Assembly, HashSet<string> References, Lazy<Type[]> Types);

    private sealed record Snapshot(int Epoch, Candidate[] Candidates);
}
