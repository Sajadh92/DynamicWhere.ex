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
/// Builds the schema one caller sees for one entity, or for part of one.
/// </summary>
/// <remarks>
/// In the core package rather than in the ASP.NET Core one, because deciding what counts as a field
/// means walking a type through the same reflection cache the pipeline validates paths with. A
/// second walk in another assembly would be a second definition of "field", and the two would drift
/// — leaving a schema that advertises a path the query then refuses, or omits one it accepts. It
/// also means a host with no ASP.NET Core gets discovery.
/// <para>
/// The walk was previously bounded only by <c>MaxNavigationDepth</c>, which enumerates every
/// <c>Manager.Subordinates.Manager</c> combination a self-referencing entity allows: 335 fields for
/// a thirty-three property type. Correct, and unusable as a field picker. It is now bounded by
/// three things a deployment sets and a request can narrow — how many levels to walk, how many
/// times one type may repeat on a path, and how many fields one response may carry.
/// </para>
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
    /// Describes what a caller may do with an entity, or with named parts of one.
    /// </summary>
    /// <param name="entityType">The type to describe. Must be exposed by the catalogue.</param>
    /// <param name="catalogue">The types an administrative surface may be asked about.</param>
    /// <param name="context">The caller.</param>
    /// <param name="options">The posture, for the caps and the standing field cost.</param>
    /// <param name="resolver">Resolves the policy for one field.</param>
    /// <param name="request">Where to start and how far to walk, or null for the defaults.</param>
    /// <returns>The schema, never null.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any required argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="entityType"/> was never exposed, when a requested path names
    /// nothing or names a field rather than a navigation, or when a requested depth is below one.
    /// </exception>
    public static PolicySchema Describe(
        Type entityType,
        DwEntityCatalog catalogue,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyResolver resolver,
        PolicySchemaRequest? request = null)
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

        int requested = request?.Depth ?? options.Caps.SchemaDepth;

        if (requested < 1)
        {
            throw new ArgumentException(
                $"A schema depth of {requested} describes nothing. One is the entity's own fields; " +
                "two adds a level of navigation.",
                nameof(request));
        }

        Walker walker = new(entityType, catalogue, context, options, resolver);

        List<Root> roots = Roots(entityType, request?.Paths, walker);
        List<string> rootPaths = new(roots.Count);

        int covered = 0;

        foreach (Root root in roots)
        {
            if (root.Path.Length != 0)
            {
                rootPaths.Add(root.Path);
            }

            // Clamped per root, because a root already three navigations deep has one level left
            // whatever the request asked for. The reported depth is the most any root covered; a
            // single-rooted request, which is nearly all of them, reports exactly what it got.
            int start = root.Level;
            int stop = Math.Min(start + requested - 1, options.Caps.MaxNavigationDepth);

            covered = Math.Max(covered, stop - start + 1);

            walker.Walk(root.Type, root.Path, root.Level, stop, root.Occurrences, root.Parent);
        }

        walker.Fields.Sort(Compare);
        walker.Nodes.Sort(static (left, right) =>
            string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));

        return new PolicySchema(
            entity,
            entityType.FullName ?? entityType.Name,
            rootPaths,
            covered,
            options.Caps.MaxNavigationDepth,
            walker.Truncated,
            walker.Fields,
            walker.Nodes);
    }

    /// <summary>
    /// Turns a path a caller wrote into the canonical one, or null when nothing on the entity
    /// answers to it.
    /// </summary>
    /// <param name="entityType">The entity the path is rooted in.</param>
    /// <param name="path">The path, in any spelling a filter accepts.</param>
    /// <param name="context">The caller, because an alias can be theirs alone.</param>
    /// <param name="options">The posture, for the navigation cap.</param>
    /// <param name="resolver">Resolves the alias of a candidate segment.</param>
    /// <returns>The canonical path, or null when no navigation answers to it.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the path names a simple field rather than a navigation. Distinguished from "no
    /// such path" on purpose, so a transport can tell a caller which mistake they made — the two
    /// are answered very differently, and "employee has no Salary" would be a lie.
    /// </exception>
    /// <remarks>
    /// Segment by segment, matching a property name first and an alias second. Resolving the alias
    /// costs one field resolution per candidate, which is a few microseconds on an administrative
    /// endpoint and buys the property that a caller may write the same spelling here that they write
    /// in a filter.
    /// </remarks>
    public static string? ResolveNavigation(
        Type entityType,
        string path,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyResolver resolver)
    {
        if (entityType is null)
        {
            throw new ArgumentNullException(nameof(entityType));
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "A schema path cannot be blank. Leave the list empty to describe the entity itself.",
                nameof(path));
        }

        string[] segments = path.Trim().Split('.');

        if (segments.Length >= options.Caps.MaxNavigationDepth)
        {
            throw new ArgumentException(
                $"'{path}' is {segments.Length} navigations deep and MaxNavigationDepth is " +
                $"{options.Caps.MaxNavigationDepth}, so no level remains to describe beneath it.",
                nameof(path));
        }

        Type current = entityType;
        string canonical = string.Empty;

        foreach (string segment in segments)
        {
            PropertyInfo? match = null;

            foreach (KeyValuePair<string, PropertyInfo> entry in CacheReflection.GetTypeProperties(current))
            {
                PropertyInfo property = entry.Value;

                if (property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                string candidate = canonical.Length == 0
                    ? property.Name
                    : $"{canonical}.{property.Name}";

                if (string.Equals(property.Name, segment, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        resolver.Resolve(entityType, candidate, context).Alias,
                        segment,
                        StringComparison.OrdinalIgnoreCase))
                {
                    match = property;

                    break;
                }
            }

            if (match is null)
            {
                return null;
            }

            canonical = canonical.Length == 0 ? match.Name : $"{canonical}.{match.Name}";

            Type? navigation = AttributePolicyProvider.NavigationTypeOf(match.PropertyType);

            if (navigation is null)
            {
                throw new ArgumentException(
                    $"'{path}' names '{match.Name}', which holds a value rather than a related " +
                    "entity. Only a navigation can be the root of a schema request; ask for the " +
                    "entity and read the field out of the list.",
                    nameof(path));
            }

            current = navigation;
        }

        return canonical;
    }

    /// <summary>Resolves the requested paths into roots, or the entity itself when none was named.</summary>
    private static List<Root> Roots(Type entityType, IReadOnlyList<string>? paths, Walker walker)
    {
        List<Root> roots = new();

        if (paths is null || paths.Count == 0)
        {
            roots.Add(new Root(entityType, string.Empty, 1, null, Occurrences(entityType)));

            return roots;
        }

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (string path in paths)
        {
            string canonical = ResolveNavigation(
                entityType, path, walker.Context, walker.Options, walker.Resolver)
                ?? throw new ArgumentException(
                    $"'{path}' is not a navigation of this entity.", nameof(paths));

            // A repeated root would walk the same subtree twice. The field and node sets would
            // deduplicate it anyway, so this is about the work rather than the answer.
            if (!seen.Add(canonical))
            {
                continue;
            }

            string[] segments = canonical.Split('.');
            Type type = entityType;

            foreach (string segment in segments)
            {
                type = AttributePolicyProvider.NavigationTypeOf(
                    CacheReflection.FindProperty(type, segment)!.PropertyType)!;
            }

            string parent = segments.Length == 1
                ? string.Empty
                : canonical[..canonical.LastIndexOf('.')];

            // Counted within the view, not from the entity: the walk to get here is forgotten and
            // this type starts on one. That is what keeps drilling productive. Counting the path
            // instead would mean a request for Manager.Manager on a self-referencing entity arrived
            // with that type already three times over and described nothing at all, so a front end
            // would offer a node that opens onto an empty response.
            //
            // The consequence, stated because it is surprising: the same path can be described more
            // deeply when it is asked for than it was listed at from the entity. That is the feature
            // — expanding a branch is how you see past the guard — and it is why a node carries the
            // depth remaining beneath it rather than leaving a caller to infer one.
            roots.Add(new Root(
                type,
                canonical,
                segments.Length + 1,
                parent.Length == 0 ? null : parent,
                Occurrences(type)));
        }

        return roots;
    }

    /// <summary>The occurrence map a walk starts from, with the root's own type already on it.</summary>
    /// <remarks>
    /// One entry, whatever path led here. See the note in <c>Roots</c>: the count is taken within
    /// the view a request asked for, so a subtree reached by drilling is described as though it were
    /// the entity.
    /// </remarks>
    private static Dictionary<Type, int> Occurrences(Type rootType) =>
        new() { [rootType] = 1 };

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

    /// <summary>One place a walk starts from.</summary>
    private sealed record Root(
        Type Type, string Path, int Level, string? Parent, Dictionary<Type, int> Occurrences);

    /// <summary>
    /// The state one <c>Describe</c> accumulates, so the recursion carries a reference rather than
    /// eleven arguments.
    /// </summary>
    private sealed class Walker
    {
        private readonly Type _root;
        private readonly DwEntityCatalog _catalogue;
        private readonly HashSet<string> _seenFields = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _seenNodes = new(StringComparer.OrdinalIgnoreCase);

        internal Walker(
            Type root,
            DwEntityCatalog catalogue,
            DwPolicyContext context,
            DwPolicyOptions options,
            PolicyResolver resolver)
        {
            _root = root;
            _catalogue = catalogue;
            Context = context;
            Options = options;
            Resolver = resolver;
        }

        internal DwPolicyContext Context { get; }

        internal DwPolicyOptions Options { get; }

        internal PolicyResolver Resolver { get; }

        internal List<PolicySchemaField> Fields { get; } = new();

        internal List<PolicySchemaNode> Nodes { get; } = new();

        internal bool Truncated { get; private set; }

        /// <summary>
        /// Adds every usable field of a type, then descends into the navigations it may.
        /// </summary>
        /// <remarks>
        /// Three things stop the descent, and they answer different questions. The level stops it
        /// where the request asked; the occurrence count stops a type repeating on one path, which
        /// is what makes a self-referencing entity describable at all; and the field cap stops the
        /// whole walk, which is the only one of the three that loses information rather than
        /// shaping it — so it is the only one that sets <see cref="Truncated"/>.
        /// </remarks>
        internal void Walk(
            Type type,
            string prefix,
            int level,
            int stop,
            Dictionary<Type, int> occurrences,
            string? parent)
        {
            foreach (KeyValuePair<string, PropertyInfo> entry in CacheReflection.GetTypeProperties(type))
            {
                if (Truncated)
                {
                    return;
                }

                PropertyInfo property = entry.Value;

                if (property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                string path = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";

                if (CacheReflection.IsSimpleType(property.PropertyType))
                {
                    AddField(path, prefix.Length == 0 ? null : prefix, property);

                    continue;
                }

                Type? navigation = AttributePolicyProvider.NavigationTypeOf(property.PropertyType);

                if (navigation is null)
                {
                    continue;
                }

                bool expandable =
                    level < stop
                    && occurrences.GetValueOrDefault(navigation) < Options.Caps.SchemaCycleLimit;

                AddNode(path, prefix.Length == 0 ? parent : prefix, navigation, level + 1, expandable, occurrences);

                if (!expandable)
                {
                    continue;
                }

                occurrences[navigation] = occurrences.GetValueOrDefault(navigation) + 1;

                Walk(navigation, path, level + 1, stop, occurrences, path);

                occurrences[navigation] -= 1;
            }
        }

        /// <summary>Adds one field, unless it is already listed or the cap has been reached.</summary>
        private void AddField(string path, string? parent, PropertyInfo property)
        {
            if (!_seenFields.Add(path))
            {
                return;
            }

            PolicySchemaField? field = Describe(path, parent, property);

            if (field is null)
            {
                return;
            }

            if (Fields.Count >= Options.Caps.MaxSchemaFields)
            {
                Truncated = true;

                return;
            }

            Fields.Add(field);
        }

        /// <summary>Adds one node, unless it is already listed.</summary>
        private void AddNode(
            string path,
            string? parent,
            Type navigation,
            int depth,
            bool expanded,
            Dictionary<Type, int> occurrences)
        {
            if (!_seenNodes.Add(path))
            {
                return;
            }

            FieldPolicy policy = Resolver.Resolve(_root, path, Context);

            // Measured as a request for this path would measure it, which means a fresh occurrence
            // count holding this node's own type once. That is the same reset Roots performs, and it
            // has to be: the number answers "what do I get if I ask for this", and asking for it is
            // what resets the count. Inheriting the walk's map instead would report nothing beneath
            // a node that returns a field the moment somebody opens it.
            int remaining = Remaining(navigation, depth, new Dictionary<Type, int> { [navigation] = 1 });

            Nodes.Add(new PolicySchemaNode(
                path,
                policy.Alias ?? path,
                parent,
                _catalogue.NameOf(navigation),
                depth,
                expanded,
                remaining));
        }

        /// <summary>
        /// How many further levels of type lie beneath a node.
        /// </summary>
        /// <remarks>
        /// A walk of the type graph and nothing else: no policy is resolved, which is what makes
        /// this affordable at all. Resolution is the expensive half of describing a field, and the
        /// depth limit exists to avoid paying it — spending it again to report a depth would give
        /// the number away for the price of the thing it was meant to save.
        /// <para>
        /// Bounded by the query cap and by the cycle limit, and measured from a fresh occurrence
        /// count, so it reports what a request for this path would actually return rather than what
        /// the graph contains or what the current walk had room for. It says nothing about whether
        /// the caller may use any of it: a subtree three levels deep whose every field is denied
        /// still reports three.
        /// </para>
        /// </remarks>
        private int Remaining(Type type, int level, Dictionary<Type, int> occurrences)
        {
            if (level >= Options.Caps.MaxNavigationDepth)
            {
                return 0;
            }

            int deepest = 0;

            foreach (KeyValuePair<string, PropertyInfo> entry in CacheReflection.GetTypeProperties(type))
            {
                PropertyInfo property = entry.Value;

                if (property.GetIndexParameters().Length != 0
                    || CacheReflection.IsSimpleType(property.PropertyType))
                {
                    continue;
                }

                Type? navigation = AttributePolicyProvider.NavigationTypeOf(property.PropertyType);

                if (navigation is null
                    || occurrences.GetValueOrDefault(navigation) >= Options.Caps.SchemaCycleLimit)
                {
                    continue;
                }

                occurrences[navigation] = occurrences.GetValueOrDefault(navigation) + 1;

                deepest = Math.Max(deepest, 1 + Remaining(navigation, level + 1, occurrences));

                occurrences[navigation] -= 1;
            }

            return deepest;
        }

        /// <summary>
        /// Describes one field, or returns null when the caller may do nothing at all with it.
        /// </summary>
        /// <remarks>
        /// Design section 5.7 says sealed fields never appear. Taken at its word that would also
        /// hide a masked field, which is one a sealed attribute speaks to and which the caller can
        /// still filter, sort and read — a front end that cannot offer it is simply wrong about the
        /// query it would have run. So the rule applied is the one behind the sentence: a field
        /// appears when the caller can do at least one thing with it. A field denied for everything
        /// — which is what <c>[DwDenied]</c> produces, and the example the section gives — appears
        /// nowhere, and nothing a caller may not use is advertised as though an operator could grant
        /// it.
        /// <para>
        /// A member whose type has no <see cref="DataType"/> counterpart is dropped too. A filter on
        /// it could not declare a type, so advertising it would offer a field no query could name.
        /// </para>
        /// </remarks>
        private PolicySchemaField? Describe(string path, string? parent, PropertyInfo property)
        {
            DataType? dataType = AttributePolicyProvider.TryDataTypeOf(property);

            if (dataType is null)
            {
                return null;
            }

            // Against the root, never the declaring type. The path is rooted in the entity the walk
            // started from — 'Contact.Email' is a path on the employee, not on the contact — and the
            // fragments for it belong to that root. Resolving against the type that happens to
            // declare the member finds no fragment at all, and a field with no fragment is
            // permitted: the schema would advertise a denied field as usable, to the front end that
            // builds its UI from it.
            FieldPolicy policy = Resolver.Resolve(_root, path, Context);

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
                parent,
                dataType.Value,
                effects,
                policy.IsMasked(PolicyFeature.Select),
                policy.AllowedOperators,
                policy.AllowedValues,
                policy.IsRequiredInWhere,
                policy.CostWeight ?? Options.Caps.DefaultFieldCost,
                policy.Label,
                policy.Description,
                policy.Group,
                policy.Order);
        }
    }
}
