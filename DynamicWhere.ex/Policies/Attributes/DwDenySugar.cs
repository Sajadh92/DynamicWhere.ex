using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>Refuses every query feature. The common case for a field that is simply off-limits.</summary>
public sealed class DwDeniedAttribute : DwDenyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    public DwDeniedAttribute() : base(PolicyFeature.All) { }
}

/// <summary>Refuses filtering on the decorated member.</summary>
public sealed class DwNoWhereAttribute : DwDenyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    public DwNoWhereAttribute() : base(PolicyFeature.Where) { }
}

/// <summary>Refuses projecting the decorated member.</summary>
public sealed class DwNoSelectAttribute : DwDenyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    public DwNoSelectAttribute() : base(PolicyFeature.Select) { }
}

/// <summary>Refuses sorting by the decorated member.</summary>
public sealed class DwNoOrderAttribute : DwDenyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    public DwNoOrderAttribute() : base(PolicyFeature.Order) { }
}

/// <summary>Refuses grouping by the decorated member.</summary>
public sealed class DwNoGroupAttribute : DwDenyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    public DwNoGroupAttribute() : base(PolicyFeature.Group) { }
}

/// <summary>Refuses aggregating the decorated member.</summary>
public sealed class DwNoAggregateAttribute : DwDenyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    public DwNoAggregateAttribute() : base(PolicyFeature.Aggregate) { }
}
