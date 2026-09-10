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
/// <para>
/// This is the only part of the policy layer whose cost grows with the size of the result, so it is
/// the only part written for it. Everything that can be hoisted out of the per-row loop is: the set
/// of root objects is built once for the whole result rather than once per path, and the property
/// and its compiled accessors are resolved once per runtime type rather than once per row. What is
/// left per value is a delegate call, the transform itself, and a second delegate call — which is
/// as close to the floor as changing a value after materialization can get.
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
    /// <param name="options">Carries the hash salt, the token vault and the service provider.</param>
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

        // Reference identity, not equality. Two rows can share one referenced object, and
        // transforming it twice would hash a hash or truncate a truncation.
        //
        // Built once for the whole result rather than once per transformed path. It was the second,
        // and on a result of ten thousand rows that is a ten-thousand-entry set allocated, filled
        // and discarded for every field the policy transforms — several times the cost of the
        // masking it was there to support.
        HashSet<object> roots = new(ReferenceComparer.Instance);

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
    /// <remarks>
    /// The root set is read and never written, so every path walks from the same one. A path with
    /// no navigation in it — which is most of them — does not build a set at all.
    /// </remarks>
    private static void ApplyPath(
        HashSet<object> roots,
        string path,
        ValueTransform chain,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyTrace trace)
    {
        string[] segments = path.Split('.');

        IReadOnlyCollection<object> owners = roots;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            owners = Descend(owners, segments[i], path);

            if (owners.Count == 0)
            {
                return;
            }
        }

        string member = segments[^1];
        bool transformed = false;

        // One entry, not a dictionary. A materialized result is one runtime type in every case that
        // matters — entities of one class, or rows of one generated projection — so a single
        // remembered type turns the per-row property lookup into a reference comparison. A result
        // that really did alternate types would fall back to the lookup it does today.
        Type? resolvedFor = null;
        Accessors accessors = default;

        foreach (object owner in owners)
        {
            Type type = owner.GetType();

            if (!ReferenceEquals(type, resolvedFor))
            {
                accessors = Accessors.For(type, member, path);
                resolvedFor = type;
            }

            object? value = accessors.Get(owner);

            object? replacement = TransformPipeline.Apply(
                chain, value, accessors.MemberType, new DwTransformContext(owner, path, context), options);

            if (accessors.Set is null)
            {
                throw new InvalidOperationException(
                    $"'{path}' is transformed by policy but has no setter, so the transformed value " +
                    "cannot replace the real one. Give the member a setter, or project into a type " +
                    "that has one.");
            }

            accessors.Set(owner, replacement);

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
    private static HashSet<object> Descend(
        IReadOnlyCollection<object> owners, string segment, string path)
    {
        HashSet<object> next = new(ReferenceComparer.Instance);

        Type? resolvedFor = null;
        Accessors accessors = default;

        foreach (object owner in owners)
        {
            Type type = owner.GetType();

            if (!ReferenceEquals(type, resolvedFor))
            {
                accessors = Accessors.For(type, segment, path);
                resolvedFor = type;
            }

            object? value = accessors.Get(owner);

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
    /// One property of one runtime type, with its compiled accessors already resolved.
    /// </summary>
    /// <remarks>
    /// A struct so that remembering one costs nothing. It exists to be resolved once and read many
    /// times, which is the whole of the optimisation: the three lookups behind it — the property by
    /// name, the getter, the setter — are each a hash of something, and each was being paid on
    /// every row of every result.
    /// </remarks>
    private readonly struct Accessors
    {
        private Accessors(
            Type memberType, Func<object, object?> get, Action<object, object?>? set)
        {
            MemberType = memberType;
            Get = get;
            Set = set;
        }

        /// <summary>The type the transformed value has to fit.</summary>
        internal Type MemberType { get; }

        /// <summary>Reads the value from an instance.</summary>
        internal Func<object, object?> Get { get; }

        /// <summary>Writes the value onto an instance, or null when the member is read-only.</summary>
        internal Action<object, object?>? Set { get; }

        /// <summary>
        /// Resolves a property on a runtime type.
        /// </summary>
        /// <remarks>
        /// The runtime type, never the declared one: a dynamic projection's type is generated per
        /// query shape, and the declared type of a row in one is <see cref="object"/>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the type does not carry the member. The result is then not the shape the
        /// policy was resolved against, and going on would emit the value exactly as stored.
        /// </exception>
        internal static Accessors For(Type type, string segment, string path)
        {
            PropertyInfo property = CacheReflection.FindProperty(type, segment)
                ?? throw new InvalidOperationException(
                    $"'{path}' is transformed by policy, but '{segment}' does not exist on " +
                    $"{type.Name}. The value would otherwise be emitted exactly as stored.");

            return new Accessors(
                property.PropertyType, MutatorCache.Getter(property), MutatorCache.Setter(property));
        }
    }

    /// <summary>Compares by reference, so two equal-but-distinct objects are both visited.</summary>
    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static ReferenceComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
