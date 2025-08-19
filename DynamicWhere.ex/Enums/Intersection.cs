namespace DynamicWhere.ex;

/// <summary>
/// Specifies different types of intersections between condition sets.
/// </summary>
public enum Intersection
{
    /// <summary>Represents the union of condition sets.</summary>
    Union,
    /// <summary>Represents the intersection of condition sets.</summary>
    Intersect,
    /// <summary>Represents the set difference of condition sets.</summary>
    Except
}
