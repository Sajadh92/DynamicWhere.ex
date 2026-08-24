using System.Collections.Concurrent;
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
    /// Reflects over one type's properties, turning every policy attribute into a fragment.
    /// </summary>
    private static IReadOnlyList<PolicyFragment> Build(Type entityType)
    {
        List<PolicyFragment> fragments = new();

        foreach (PropertyInfo property in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (DwDenyAttribute attribute in property.GetCustomAttributes<DwDenyAttribute>(inherit: true))
            {
                fragments.Add(ToFragment(property.Name, attribute));
            }
        }

        return fragments;
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
