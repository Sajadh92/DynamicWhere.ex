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
/// </remarks>
public sealed class PolicySchema
{
    /// <summary>Initializes a schema.</summary>
    /// <param name="entity">The public name of the entity.</param>
    /// <param name="entityType">The entity's full type name, as rules match on it.</param>
    /// <param name="fields">The fields this caller may use.</param>
    /// <exception cref="ArgumentException">Thrown when either name is blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fields"/> is null.</exception>
    public PolicySchema(string entity, string entityType, IReadOnlyList<PolicySchemaField> fields)
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
        Fields = fields ?? throw new ArgumentNullException(nameof(fields));
    }

    /// <summary>The public name of the entity.</summary>
    public string Entity { get; }

    /// <summary>The entity's full type name, which is what a rule is written against.</summary>
    public string EntityType { get; }

    /// <summary>The fields this caller may use, grouped and ordered as declared.</summary>
    public IReadOnlyList<PolicySchemaField> Fields { get; }
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
