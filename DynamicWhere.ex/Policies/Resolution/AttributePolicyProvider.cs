using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Reflection;
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

            Type? navigation = NavigationTypeOf(property.PropertyType);

            if (navigation is not null)
            {
                Walk(navigation, path, depth + 1, fragments);
            }
        }
    }

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
    /// <c>PagedList&lt;T&gt;</c> alike. Interfaces and user-defined structs are followed as well as
    /// classes, because these attributes are supported on DTOs, where an <c>IContact</c> reference
    /// or a record struct is ordinary.
    /// </remarks>
    private static Type? NavigationTypeOf(Type propertyType)
    {
        Type underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        if (underlying.IsArray)
        {
            return AsNavigation(underlying.GetElementType());
        }

        Type? element = ElementTypeOf(underlying);

        if (element is not null)
        {
            return AsNavigation(element);
        }

        return underlying.Namespace?.StartsWith("System", StringComparison.Ordinal) == true
            ? null
            : AsNavigation(underlying);
    }

    /// <summary>
    /// Returns the <c>T</c> of the first <see cref="IEnumerable{T}"/> a type implements, or null
    /// when it implements none.
    /// </summary>
    /// <remarks>
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
}
