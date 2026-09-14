namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// How a numeric or temporal value has its precision reduced.
/// </summary>
/// <remarks>
/// The numeric and temporal counterpart to masking. A <see cref="decimal"/> cannot be star-masked
/// without ceasing to be a number, so without generalization half the type space has no transform
/// at all.
/// </remarks>
public enum GeneralizeMode
{
    /// <summary>Rounded to the nearest step, staying numeric.</summary>
    Round = 0,

    /// <summary>Replaced by the label of the band it falls in, such as <c>25-34</c>.</summary>
    Bucket = 1,

    /// <summary>Reduced to one component of a date.</summary>
    DatePart = 2,

    /// <summary>Kept numeric, with digits after the given decimal place discarded.</summary>
    Truncate = 3
}
