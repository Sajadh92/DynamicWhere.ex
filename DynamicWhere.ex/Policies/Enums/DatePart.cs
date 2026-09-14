namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// The component a date is reduced to under <see cref="GeneralizeMode.DatePart"/>.
/// </summary>
public enum DatePart
{
    /// <summary>The year alone.</summary>
    Year = 0,

    /// <summary>The first day of the quarter.</summary>
    Quarter = 1,

    /// <summary>The first day of the month.</summary>
    Month = 2,

    /// <summary>The date with its time of day discarded.</summary>
    Day = 3
}
