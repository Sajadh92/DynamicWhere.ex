using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Reflection;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Resolution;

/// <summary>
/// Produces policy fragments by reflecting over the attributes on a type.
/// </summary>
/// <remarks>
/// The result is identical for every caller, so it is computed once per type and cached. An
/// attribute with <c>Overridable = false</c> lands at <see cref="PolicyLevel.SealedAttribute"/> and
/// nothing at runtime can replace it; one with <c>Overridable = true</c> lands at
/// <see cref="PolicyLevel.OverridableAttribute"/>, the least authoritative level, and acts only as
/// a default.
/// </remarks>
public sealed class AttributePolicyProvider : IDwPolicyProvider
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<PolicyFragment>> Cache = new();

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

        return Cache.GetOrAdd(entityType, Build);
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

        Walk(entityType, prefix: string.Empty, depth: 0, fragments);

        return new ReadOnlyCollection<PolicyFragment>(fragments);
    }

    /// <summary>
    /// Adds fragments for one type, then descends into its navigations.
    /// </summary>
    /// <param name="type">The type being walked.</param>
    /// <param name="prefix">The dotted path leading to this type, empty at the root.</param>
    /// <param name="depth">How many navigations deep the walk currently is.</param>
    /// <param name="fragments">The accumulator.</param>
    /// <remarks>
    /// Depth alone terminates the walk. A visited-type guard would be cheaper on a graph with many
    /// cycles, but it would also suppress legitimate paths: a self-referencing type would never
    /// yield <c>Next.Secret</c>, even though a caller can filter on exactly that path. Bidirectional
    /// navigations are cyclic by nature, so the depth cap is doing the real work either way, and the
    /// whole walk is computed once per type and cached.
    /// </remarks>
    private static void Walk(Type type, string prefix, int depth, List<PolicyFragment> fragments)
    {
        if (depth >= MaxDepth)
        {
            return;
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            string path = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";

            foreach (DwDenyAttribute attribute in property.GetCustomAttributes<DwDenyAttribute>(inherit: true))
            {
                fragments.Add(ToFragment(path, attribute));
            }

            foreach (DwOperatorsAttribute attribute in property.GetCustomAttributes<DwOperatorsAttribute>(inherit: true))
            {
                fragments.Add(ToFragment(path, attribute));
            }

            Type? navigation = NavigationTypeOf(property.PropertyType);

            if (navigation is not null)
            {
                Walk(navigation, path, depth + 1, fragments);
            }
        }
    }

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
    private static Type? NavigationTypeOf(Type propertyType)
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

        return AsNavigation(current);
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

        return type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true ? null : type;
    }

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
    /// The effect is <see cref="PolicyEffect.Allow"/> and the features are
    /// <see cref="PolicyFeature.Where"/>, because a restriction refuses nothing on its own — it
    /// only narrows what a permitted filter may do. The restriction itself rides on the fragment's
    /// typed operator list, which the resolver intersects rather than elects, so this fragment
    /// losing the election for <c>Where</c> does not discard it.
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
            PolicyFeature.Where,
            PolicyEffect.Allow,
            level,
            source,
            allowedOperators: attribute.Resolve());
    }
}
