using DynamicWhere.ex.Enums;

namespace DynamicWhere.ex.Policies.Discovery;

/// <summary>
/// What one caller may do with one entity: the fields they can name, what each accepts, and what
/// each is called in their vocabulary.
/// </summary>
/// <remarks>
/// Resolved per caller, never cached per type. A rule may rename a field for one role and deny it
/// to another, so a schema computed once and shared would advertise names some callers cannot use
/// and hide fields others can.
/// <para>
/// Flat, and deliberately. <see cref="Fields"/> and <see cref="Nodes"/> each carry a parent, which
/// is a tree in adjacency form: one grouping pass on the client builds the tree, and filtering,
/// sorting and deduplication all stay trivial. Nesting the fields inside their nodes would have
/// made searching recursive for every consumer, and would have needed a rule for which parent owns
/// a field two requested roots can both reach.
/// </para>
/// </remarks>
public sealed class PolicySchema
{
    /// <summary>Initializes a schema.</summary>
    /// <param name="entity">The public name of the entity.</param>
    /// <param name="entityType">The entity's full type name, as rules match on it.</param>
    /// <param name="roots">The paths the walk started from, empty when it started at the entity.</param>
    /// <param name="depth">How many levels the walk actually covered, after clamping.</param>
    /// <param name="maxDepth">The deepest a query may reach, which is what depth is clamped to.</param>
    /// <param name="truncated">True when the field cap cut the list short.</param>
    /// <param name="fields">The fields this caller may use.</param>
    /// <param name="nodes">The navigations the walk touched, expanded or not.</param>
    /// <exception cref="ArgumentException">Thrown when either name is blank.</exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="fields"/>, <paramref name="nodes"/> or <paramref name="roots"/>
    /// is null.
    /// </exception>
    public PolicySchema(
        string entity,
        string entityType,
        IReadOnlyList<string> roots,
        int depth,
        int maxDepth,
        bool truncated,
        IReadOnlyList<PolicySchemaField> fields,
        IReadOnlyList<PolicySchemaNode> nodes)
    {
        if (string.IsNullOrWhiteSpace(entity))
        {
            throw new ArgumentException("A schema requires an entity name.", nameof(entity));
        }

        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("A schema requires an entity type.", nameof(entityType));
        }

        Entity = entity;
        EntityType = entityType;
        Roots = roots ?? throw new ArgumentNullException(nameof(roots));
        Depth = depth;
        MaxDepth = maxDepth;
        Truncated = truncated;
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
        Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
    }

    /// <summary>The public name of the entity.</summary>
    public string Entity { get; }

    /// <summary>The entity's full type name, which is what a rule is written against.</summary>
    public string EntityType { get; }

    /// <summary>
    /// The paths the walk started from, empty when it started at the entity itself.
    /// </summary>
    /// <remarks>
    /// Echoed back rather than left implicit, because a request naming several roots gets one flat
    /// list of fields and nothing else in the response says where it was rooted.
    /// </remarks>
    public IReadOnlyList<string> Roots { get; }

    /// <summary>
    /// How many levels of type the walk covered, counted from each root.
    /// </summary>
    /// <remarks>
    /// What was used, never what was asked for. A request naming a depth beyond what the query cap
    /// allows from where it started is clamped, and this is how a caller finds out.
    /// </remarks>
    public int Depth { get; }

    /// <summary>
    /// The deepest a query may reach, which is the ceiling <see cref="Depth"/> is clamped to.
    /// </summary>
    /// <remarks>
    /// Reported so a caller learns the ceiling from its first response instead of guessing at it.
    /// It is why the request needs no sentinel for "as deep as possible": send a large number, read
    /// back what you got.
    /// </remarks>
    public int MaxDepth { get; }

    /// <summary>
    /// True when <c>DwCaps.MaxSchemaFields</c> cut the field list short.
    /// </summary>
    /// <remarks>
    /// False for any ordinary request. It is set rather than thrown on, because a field picker
    /// missing its tail can say so, where a failed request can only say nothing.
    /// </remarks>
    public bool Truncated { get; }

    /// <summary>The fields this caller may use, grouped and ordered as declared.</summary>
    public IReadOnlyList<PolicySchemaField> Fields { get; }

    /// <summary>
    /// The navigations the walk touched, whether or not it descended into them.
    /// </summary>
    /// <remarks>
    /// Both, not only the ones left unexpanded. A tree that listed only what was skipped would
    /// leave a consumer inferring the expanded branches from field paths, which is precisely the
    /// string surgery carrying a parent on every entry exists to remove.
    /// </remarks>
    public IReadOnlyList<PolicySchemaNode> Nodes { get; }
}

/// <summary>
/// One navigation the schema walk reached: where it sits, whether it was descended into, and how
/// much lies beneath it.
/// </summary>
/// <remarks>
/// The skeleton a field picker draws its tree from. A node is reported whether or not the caller
/// can use anything beneath it — finding that out would mean resolving a level of policy per
/// navigation, which is most of the work the depth limit exists to avoid, so a node that opens onto
/// nothing is a wart accepted deliberately.
/// </remarks>
public sealed class PolicySchemaNode
{
    /// <summary>Initializes a node.</summary>
    /// <param name="path">The canonical path of the navigation.</param>
    /// <param name="name">The name this caller uses — the alias when there is one.</param>
    /// <param name="parent">The node this one hangs under, or null when it hangs at the root.</param>
    /// <param name="entity">The catalogue's public name for the target type, or null.</param>
    /// <param name="depth">The level this node's own fields occupy.</param>
    /// <param name="expanded">True when the walk described this node's fields.</param>
    /// <param name="remainingDepth">How many further levels lie beneath it.</param>
    public PolicySchemaNode(
        string path,
        string name,
        string? parent,
        string? entity,
        int depth,
        bool expanded,
        int remainingDepth)
    {
        Path = path;
        Name = name;
        Parent = parent;
        Entity = entity;
        Depth = depth;
        Expanded = expanded;
        RemainingDepth = remainingDepth;
    }

    /// <summary>The canonical path of the navigation.</summary>
    public string Path { get; }

    /// <summary>The name this caller uses: the alias when there is one, the path otherwise.</summary>
    public string Name { get; }

    /// <summary>The node this one hangs under, or null when it hangs at the root of the view.</summary>
    public string? Parent { get; }

    /// <summary>
    /// The catalogue's public name for the type behind this navigation, or null when nobody exposed
    /// it.
    /// </summary>
    /// <remarks>
    /// Null rather than the type's own name. The catalogue exists so a caller cannot enumerate the
    /// application's types by asking about them, and a nested navigation does not need its type
    /// exposed for the walk to pass through it — so naming one here would hand back exactly the
    /// fact the catalogue withholds. A consumer labels the node from <see cref="Name"/> instead.
    /// </remarks>
    public string? Entity { get; }

    /// <summary>The level this node's own fields occupy, counted from the entity.</summary>
    public int Depth { get; }

    /// <summary>True when the walk described this node's fields rather than stopping at it.</summary>
    public bool Expanded { get; }

    /// <summary>
    /// How many further levels of type lie beneath this node.
    /// </summary>
    /// <remarks>
    /// What expanding it would actually yield, so it accounts for
    /// <c>DwCaps.SchemaCycleLimit</c> as well as the query cap. <c>Manager.Manager</c> reports one
    /// rather than two on a self-referencing entity, because the guard refuses that type a third
    /// appearance on the path — the smaller number is the true one, and comparing it against
    /// <c>PolicySchema.MaxDepth</c> alone will not explain it.
    /// <para>
    /// Zero means there is nothing beneath it to ask for. It says nothing about whether the caller
    /// may use what is there: a subtree three levels deep whose every field is denied still reports
    /// three.
    /// </para>
    /// </remarks>
    public int RemainingDepth { get; }
}

/// <summary>
/// One field of a schema: what to call it, what it holds, and what may be done with it.
/// </summary>
/// <remarks>
/// Deliberately says nothing about whether the field is audited. Which fields are watched is not a
/// fact any caller needs, and it is exactly the fact someone choosing where to look would want.
/// </remarks>
public sealed class PolicySchemaField
{
    /// <summary>Initializes a field.</summary>
    /// <param name="path">The canonical property path.</param>
    /// <param name="name">The name this caller uses — the alias when there is one.</param>
    /// <param name="parent">The node this field hangs under, or null when it is the entity's own.</param>
    /// <param name="dataType">The data type a filter on this field would declare.</param>
    /// <param name="effects">What may be done with the field.</param>
    /// <param name="isMasked">True when the value is transformed on its way out.</param>
    /// <param name="allowedOperators">The operators permitted, or null when nothing restricts them.</param>
    /// <param name="allowedValues">The values worth offering, or null when the field is not enumerable.</param>
    /// <param name="isRequiredInWhere">True when the caller must filter on this field.</param>
    /// <param name="costWeight">What one reference to this field spends against the budget.</param>
    /// <param name="label">A short human name, or null.</param>
    /// <param name="description">A longer explanation, or null.</param>
    /// <param name="group">The section to list the field under, or null.</param>
    /// <param name="order">Where the field sorts within its group, or null.</param>
    public PolicySchemaField(
        string path,
        string name,
        string? parent,
        DataType dataType,
        IReadOnlyDictionary<Enums.PolicyFeature, bool> effects,
        bool isMasked,
        IReadOnlyList<Operator>? allowedOperators,
        IReadOnlyList<string>? allowedValues,
        bool isRequiredInWhere,
        int costWeight,
        string? label,
        string? description,
        string? group,
        int? order)
    {
        Path = path;
        Name = name;
        Parent = parent;
        DataType = dataType;
        _effects = effects;
        IsMasked = isMasked;
        AllowedOperators = allowedOperators;
        AllowedValues = allowedValues;
        IsRequiredInWhere = isRequiredInWhere;
        CostWeight = costWeight;
        Label = label;
        Description = description;
        Group = group;
        Order = order;
    }

    private readonly IReadOnlyDictionary<Enums.PolicyFeature, bool> _effects;

    /// <summary>
    /// The canonical property path.
    /// </summary>
    /// <remarks>
    /// Present because the whole administrative surface sits behind an authorization policy and an
    /// operator writing a rule needs the path the rule is matched on. A consumer building a
    /// caller-facing filter UI should map <see cref="Name"/> and leave this alone — publishing it
    /// is what an alias exists to avoid.
    /// </remarks>
    public string Path { get; }

    /// <summary>The name this caller uses: the alias when there is one, the path otherwise.</summary>
    public string Name { get; }

    /// <summary>
    /// The node this field hangs under, or null when it belongs to the entity itself.
    /// </summary>
    /// <remarks>
    /// Derivable by stripping the last segment of <see cref="Path"/>, and carried anyway. The
    /// server computes it once; the alternative is every consumer doing the same string surgery
    /// forever, which is the thing carrying a parent exists to remove.
    /// </remarks>
    public string? Parent { get; }

    /// <summary>The data type a filter on this field would declare.</summary>
    public DataType DataType { get; }

    /// <summary>True when the caller may filter on this field.</summary>
    public bool CanWhere => Can(Enums.PolicyFeature.Where);

    /// <summary>True when the caller may project this field.</summary>
    public bool CanSelect => Can(Enums.PolicyFeature.Select);

    /// <summary>True when the caller may sort by this field.</summary>
    public bool CanOrder => Can(Enums.PolicyFeature.Order);

    /// <summary>True when the caller may group by this field.</summary>
    public bool CanGroup => Can(Enums.PolicyFeature.Group);

    /// <summary>True when the caller may aggregate this field.</summary>
    public bool CanAggregate => Can(Enums.PolicyFeature.Aggregate);

    /// <summary>True when the caller may use this field in a set operation.</summary>
    public bool CanSegment => Can(Enums.PolicyFeature.Segment);

    /// <summary>
    /// True when the value is transformed on its way out.
    /// </summary>
    /// <remarks>
    /// Advertised rather than hidden. The field is genuinely usable — the query runs and sorts on
    /// the real value — and a front end that knows the value is masked can say so instead of
    /// letting a user believe they are reading the stored one.
    /// </remarks>
    public bool IsMasked { get; }

    /// <summary>The operators permitted, or null when nothing restricts them.</summary>
    public IReadOnlyList<Operator>? AllowedOperators { get; }

    /// <summary>The values worth offering, or null when the field is not enumerable.</summary>
    public IReadOnlyList<string>? AllowedValues { get; }

    /// <summary>True when the caller must filter on this field for a query to proceed.</summary>
    public bool IsRequiredInWhere { get; }

    /// <summary>What one reference to this field spends against the query budget.</summary>
    public int CostWeight { get; }

    /// <summary>A short human name, or null when nothing names it.</summary>
    public string? Label { get; }

    /// <summary>A longer explanation, or null when nothing explains it.</summary>
    public string? Description { get; }

    /// <summary>The section to list the field under, or null.</summary>
    public string? Group { get; }

    /// <summary>Where the field sorts within its group, or null.</summary>
    public int? Order { get; }

    /// <summary>True when the caller may use the field for a feature.</summary>
    /// <param name="feature">The feature to ask about.</param>
    public bool Can(Enums.PolicyFeature feature) =>
        _effects.TryGetValue(feature, out bool allowed) && allowed;
}
