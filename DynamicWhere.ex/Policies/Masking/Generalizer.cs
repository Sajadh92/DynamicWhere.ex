using System.Globalization;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Masking;

/// <summary>
/// Reduces the precision of a numeric or temporal value, keeping it the kind of thing it was.
/// </summary>
/// <remarks>
/// The half of the type space masking cannot reach. Arithmetic runs in <see cref="decimal"/> and the
/// result is converted back to the value's own type, so rounding an <see cref="int"/> yields an
/// <see cref="int"/> and the member can still hold it.
/// <para>
/// <see cref="GeneralizeMode.Bucket"/> is the one mode that emits text, because a band is a label
/// rather than a number. It is therefore valid only on a text member, which the assignability check
/// at the end of the chain enforces.
/// </para>
/// </remarks>
internal static class Generalizer
{
    /// <summary>Applies one generalization to one value.</summary>
    /// <param name="stage">What to do.</param>
    /// <param name="value">The value, as materialized.</param>
    /// <returns>The reduced value.</returns>
    internal static object? Apply(GeneralizeStage stage, object? value)
    {
        if (value is null)
        {
            return null;
        }

        return stage.Mode switch
        {
            GeneralizeMode.Round => Round(stage, value),
            GeneralizeMode.Bucket => Bucket(stage, value),
            GeneralizeMode.Truncate => Truncate(stage, value),
            _ => DatePart(stage, value)
        };
    }

    /// <summary>Rounds to the nearest step, staying in the value's own type.</summary>
    private static object Round(GeneralizeStage stage, object value)
    {
        decimal number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        decimal rounded = Math.Round(number / stage.Step, MidpointRounding.AwayFromZero) * stage.Step;

        return Convert.ChangeType(rounded, value.GetType(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Replaces the value with the label of the band it falls in.
    /// </summary>
    /// <remarks>
    /// Bands are half-open and anchored at zero, so a step of ten yields <c>0-9</c>, <c>10-19</c>
    /// and so on, and a negative value lands in the band below zero rather than folding onto a
    /// positive one.
    /// </remarks>
    private static string Bucket(GeneralizeStage stage, object value)
    {
        decimal number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        decimal floor = Math.Floor(number / stage.Step) * stage.Step;

        return string.Create(
            CultureInfo.InvariantCulture, $"{floor}-{floor + stage.Step - 1}");
    }

    /// <summary>Discards digits after the configured decimal place, without rounding.</summary>
    private static object Truncate(GeneralizeStage stage, object value)
    {
        decimal number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        decimal scale = 1m;

        for (int i = 0; i < stage.Decimals; i++)
        {
            scale *= 10m;
        }

        decimal truncated = Math.Truncate(number * scale) / scale;

        return Convert.ChangeType(truncated, value.GetType(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Reduces a date to one of its components, keeping the value a date.
    /// </summary>
    /// <remarks>
    /// Every part yields the first instant of the period rather than a number, so the member keeps
    /// its type and a caller can still sort by it. Reducing to a year and emitting the integer 1987
    /// would need a text or numeric member and would stop being a date.
    /// </remarks>
    private static object DatePart(GeneralizeStage stage, object value)
    {
        if (value is DateOnly dateOnly)
        {
            DateTime reduced = Reduce(dateOnly.ToDateTime(TimeOnly.MinValue), stage.Part);

            return DateOnly.FromDateTime(reduced);
        }

        if (value is DateTimeOffset offset)
        {
            return new DateTimeOffset(Reduce(offset.DateTime, stage.Part), offset.Offset);
        }

        return Reduce(Convert.ToDateTime(value, CultureInfo.InvariantCulture), stage.Part);
    }

    /// <summary>The first instant of the period the date falls in.</summary>
    private static DateTime Reduce(DateTime value, DatePart part) =>
        part switch
        {
            Enums.DatePart.Year => new DateTime(value.Year, 1, 1),
            Enums.DatePart.Quarter => new DateTime(value.Year, ((value.Month - 1) / 3 * 3) + 1, 1),
            Enums.DatePart.Month => new DateTime(value.Year, value.Month, 1),
            _ => value.Date
        };
}
