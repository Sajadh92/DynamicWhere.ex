using System.Reflection;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.ex.Policies.Discovery;

/// <summary>
/// Builds the schema one caller sees for one entity.
/// </summary>
/// <remarks>
/// In the core package rather than in the ASP.NET Core one, because deciding what counts as a field
/// means walking a type through the same reflection cache the pipeline validates paths with. A
/// second walk in another assembly would be a second definition of "field", and the two would drift
/// — leaving a schema that advertises a path the query then refuses, or omits one it accepts. It
/// also means a host with no ASP.NET Core gets discovery.
/// </remarks>
public static class PolicySchemaBuilder
{
    private static readonly PolicyFeature[] Features =
    {
        PolicyFeature.Where,
        PolicyFeature.Select,
        PolicyFeature.Order,
        PolicyFeature.Group,
        PolicyFeature.Aggregate,
        PolicyFeature.Segment
    };

    /// <summary>
    /// Describes what a caller may do with an entity.
    /// </summary>
    /// <param name="entityType">The type to describe. Must be exposed by the catalogue.</param>
    /// <param name="catalogue">The types an administrative surface may be asked about.</param>
    /// <param name="context">The caller.</param>
    /// <param name="options">The posture, for the navigation cap and the standing field cost.</param>
    /// <param name="resolver">Resolves the policy for one field.</param>
    /// <returns>The schema, never null.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="entityType"/> was never exposed.
    /// </exception>
    public static PolicySchema Describe(
        Type entityType,
        DwEntityCatalog catalogue,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyResolver resolver)
    {
        if (entityType is null)
        {
            throw new ArgumentNullException(nameof(entityType));
        }

        if (catalogue is null)
        {
            throw new ArgumentNullException(nameof(catalogue));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (resolver is null)
        {
            throw new ArgumentNullException(nameof(resolver));
        }

        // Checked here as well as at the endpoint. A caller reaching this with a type they named
        // themselves is the enumeration the catalogue exists to stop, and a guard that lives only in
        // the transport is one the next transport will not have.
        string? entity = catalogue.NameOf(entityType);

        if (entity is null)
        {
            throw new ArgumentException(
                $"'{entityType.FullName}' is not an exposed entity. Add it to the catalogue at " +
                "startup; describing a type nobody exposed would let a caller enumerate the " +
                "application's types by asking about them.",
                nameof(entityType));
        }

        List<PolicySchemaField> fields = new();

        Walk(entityType, entityType, prefix: string.Empty, depth: 1, options, context, resolver, fields);

        // Grouped and ordered as declared, so a front end can render the schema without knowing the
        // model. An undeclared order sorts last rather than first: zero is a position an author
        // chose, and defaulting to it would put every undecorated field ahead of the ones somebody
        // took the trouble to place.
        fields.Sort(Compare);

        return new PolicySchema(entity, entityType.FullName ?? entityType.Name, fields);
    }

    /// <summary>
    /// Adds every usable field of a type, then descends into its navigations.
    /// </summary>
    /// <remarks>
    /// Bounded by <c>MaxNavigationDepth</c> — the same cap the sanitizer refuses on — so the schema
    /// cannot advertise a path a query would then reject for being too deep. The bound is also what
    /// makes the walk terminate at all: a self-referencing type has no bottom to reach.
    /// </remarks>
    private static void Walk(
        Type root,
        Type type,
        string prefix,
        int depth,
        DwPolicyOptions options,
        DwPolicyContext context,
        PolicyResolver resolver,
        List<PolicySchemaField> fields)
    {
        foreach (KeyValuePair<string, PropertyInfo> entry in CacheReflection.GetTypeProperties(type))
        {
            PropertyInfo property = entry.Value;

            if (property.GetIndexParameters().Length != 0)
            {
                continue;
            }

            string path = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";

            if (CacheReflection.IsSimpleType(property.PropertyType))
            {
                PolicySchemaField? field = Describe(root, path, property, options, context, resolver);

                if (field is not null)
                {
                    fields.Add(field);
                }

                continue;
            }

            Type? navigation = AttributePolicyProvider.NavigationTypeOf(property.PropertyType);

            if (navigation is not null && depth < options.Caps.MaxNavigationDepth)
            {
                Walk(root, navigation, path, depth + 1, options, context, resolver, fields);
            }
        }
    }

    /// <summary>
    /// Describes one field, or returns null when the caller may do nothing at all with it.
    /// </summary>
    /// <remarks>
    /// Design section 5.7 says sealed fields never appear. Taken at its word that would also hide a
    /// masked field, which is one a sealed attribute speaks to and which the caller can still
    /// filter, sort and read — a front end that cannot offer it is simply wrong about the query it
    /// would have run. So the rule applied is the one behind the sentence: a field appears when the
    /// caller can do at least one thing with it. A field denied for everything — which is what
    /// <c>[DwDenied]</c> produces, and the example the section gives — appears nowhere, and nothing
    /// a caller may not use is advertised as though an operator could grant it.
    /// <para>
    /// A member whose type has no <see cref="DataType"/> counterpart is dropped too. A filter on it
    /// could not declare a type, so advertising it would offer a field no query could name.
    /// </para>
    /// </remarks>
    private static PolicySchemaField? Describe(
        Type root,
        string path,
        PropertyInfo property,
        DwPolicyOptions options,
        DwPolicyContext context,
        PolicyResolver resolver)
    {
        DataType? dataType = AttributePolicyProvider.TryDataTypeOf(property);

        if (dataType is null)
        {
            return null;
        }

        // Against the root, never the declaring type. The path is rooted in the entity the walk
        // started from — 'Contact.Email' is a path on the employee, not on the contact — and the
        // fragments for it belong to that root. Resolving against the type that happens to declare
        // the member finds no fragment at all, and a field with no fragment is permitted: the schema
        // would advertise a denied field as usable, to the front end that builds its UI from it.
        FieldPolicy policy = resolver.Resolve(root, path, context);

        Dictionary<PolicyFeature, bool> effects = new();
        bool anything = false;

        foreach (PolicyFeature feature in Features)
        {
            bool allowed = policy.Allows(feature);

            effects[feature] = allowed;
            anything |= allowed;
        }

        if (!anything)
        {
            return null;
        }

        return new PolicySchemaField(
            path,
            policy.Alias ?? path,
            dataType.Value,
            effects,
            policy.IsMasked(PolicyFeature.Select),
            policy.AllowedOperators,
            policy.AllowedValues,
            policy.IsRequiredInWhere,
            policy.CostWeight ?? options.Caps.DefaultFieldCost,
            policy.Label,
            policy.Description,
            policy.Group,
            policy.Order);
    }

    /// <summary>Orders by group, then by declared order, then by name.</summary>
    private static int Compare(PolicySchemaField left, PolicySchemaField right)
    {
        int group = string.Compare(left.Group, right.Group, StringComparison.OrdinalIgnoreCase);

        if (group != 0)
        {
            // A field with no group sorts after every field that has one, rather than before: an
            // ungrouped field is one nobody placed, and the placed ones are the ones an author
            // arranged for a reader.
            return left.Group is null ? 1 : right.Group is null ? -1 : group;
        }

        int order = (left.Order ?? int.MaxValue).CompareTo(right.Order ?? int.MaxValue);

        return order != 0
            ? order
            : string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
    }
}
