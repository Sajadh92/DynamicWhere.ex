using System.Collections;
using System.Reflection;
using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;

namespace DynamicWhere.ex.Policies.Masking;

/// <summary>
/// Applies a type's transform chains to a materialized result.
/// </summary>
/// <remarks>
/// Driven by the paths the policy names rather than by a blind walk of the object graph. The set of
/// paths that need transforming is known before a single row is read, so the walker navigates
/// exactly those and nothing else — which is both far cheaper on a wide entity and, more
/// importantly, incapable of missing a path because it failed to recognise a navigation. That was
/// the shape of five separate defects on the input side.
/// <para>
/// The objects being written to are detached. That is what makes this safe at all: a transform
/// applied to a tracked entity is recorded by EF as a pending modification and written back as the
/// real value on the next save anywhere in the same unit of work.
/// </para>
/// </remarks>
internal static class GraphWalker
{
    /// <summary>
    /// Transforms every value the policy speaks to, across a materialized result.
    /// </summary>
    /// <param name="rows">The materialized objects. Nulls are skipped.</param>
    /// <param name="policy">The type's transform chains, keyed by canonical path.</param>
    /// <param name="projected">
    /// The paths the result actually carries, or null when it carries the whole entity. A path
    /// outside this set was never selected, so there is no value present to transform.
    /// </param>
    /// <param name="context">Who is asking.</param>
    /// <param name="options">Carries the hash salt and the service provider.</param>
    /// <param name="trace">Collects what was transformed.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a path the result should carry cannot be reached on it. Skipping instead would
    /// emit the value untransformed, which is the failure this whole layer exists to prevent.
    /// </exception>
    internal static void Apply(
        IEnumerable rows,
        TypePolicy policy,
        IReadOnlyCollection<string>? projected,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyTrace trace)
    {
        if (policy.Transforms.Count == 0)
        {
            return;
        }

        List<object> roots = new();

        foreach (object? row in rows)
        {
            if (row is not null)
            {
                roots.Add(row);
            }
        }

        if (roots.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<string, ValueTransform> entry in policy.Transforms)
        {
            if (!IsCarried(entry.Key, projected))
            {
                continue;
            }

            ApplyPath(roots, entry.Key, entry.Value, context, options, trace);
        }
    }

    /// <summary>
    /// True when a result carrying this projection holds the path at all.
    /// </summary>
    /// <remarks>
    /// A null projection means the whole entity, so every path is carried. Otherwise a path is
    /// carried when it was selected, or when it sits beneath something that was: projecting
    /// <c>Contact</c> brings <c>Contact.Email</c> with it.
    /// </remarks>
    private static bool IsCarried(string path, IReadOnlyCollection<string>? projected)
    {
        if (projected is null)
        {
            return true;
        }

        foreach (string selected in projected)
        {
            if (string.Equals(selected, path, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(selected + ".", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Navigates one path from every root and transforms the value it lands on.</summary>
    private static void ApplyPath(
        List<object> roots,
        string path,
        ValueTransform chain,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyTrace trace)
    {
        string[] segments = path.Split('.');

        // Reference identity, not equality. Two rows can share one referenced object, and
        // transforming it twice would hash a hash or truncate a truncation.
        HashSet<object> owners = new(ReferenceComparer.Instance);

        foreach (object root in roots)
        {
            owners.Add(root);
        }

        for (int i = 0; i < segments.Length - 1; i++)
        {
            owners = Descend(owners, segments[i], path);

            if (owners.Count == 0)
            {
                return;
            }
        }

        bool transformed = false;

        foreach (object owner in owners)
        {
            PropertyInfo property = Find(owner, segments[^1], path);

            object? value = MutatorCache.Read(property, owner);
            object? replacement = TransformPipeline.Apply(
                chain, value, property.PropertyType, new DwTransformContext(owner, path, context), options);

            if (!MutatorCache.Write(property, owner, replacement))
            {
                throw new InvalidOperationException(
                    $"'{path}' is transformed by policy but has no setter, so the transformed value " +
                    "cannot replace the real one. Give the member a setter, or project into a type " +
                    "that has one.");
            }

            transformed = true;
        }

        if (transformed)
        {
            trace.Add(new PolicyDecision(
                path, Enums.PolicyFeature.Select, chain.Action,
                string.Join(" then ", chain.Stages.Select(s => s.Kind))));
        }
    }

    /// <summary>
    /// Steps one segment down, following collections into their elements.
    /// </summary>
    /// <remarks>
    /// A null navigation contributes nothing rather than failing: there is no value beneath it to
    /// leave untransformed. A missing <em>property</em> is a different matter and does fail, because
    /// it means the result is not the shape the policy was resolved against.
    /// </remarks>
    private static HashSet<object> Descend(HashSet<object> owners, string segment, string path)
    {
        HashSet<object> next = new(ReferenceComparer.Instance);

        foreach (object owner in owners)
        {
            object? value = MutatorCache.Read(Find(owner, segment, path), owner);

            if (value is null)
            {
                continue;
            }

            if (value is IEnumerable collection and not string)
            {
                foreach (object? element in collection)
                {
                    if (element is not null)
                    {
                        next.Add(element);
                    }
                }

                continue;
            }

            next.Add(value);
        }

        return next;
    }

    /// <summary>
    /// Finds a property on an instance's runtime type.
    /// </summary>
    /// <remarks>
    /// The runtime type, never the declared one: a dynamic projection's type is generated per query
    /// shape, and the declared type of a row in one is <see cref="object"/>.
    /// </remarks>
    private static PropertyInfo Find(object instance, string segment, string path) =>
        CacheReflection.FindProperty(instance.GetType(), segment)
        ?? throw new InvalidOperationException(
            $"'{path}' is transformed by policy, but '{segment}' does not exist on " +
            $"{instance.GetType().Name}. The value would otherwise be emitted exactly as stored.");

    /// <summary>Compares by reference, so two equal-but-distinct objects are both visited.</summary>
    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static ReferenceComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
