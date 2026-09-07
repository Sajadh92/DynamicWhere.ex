using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.DTOs;

/// <summary>
/// Which stage of the transform chain a fragment speaks to.
/// </summary>
/// <remarks>
/// Stages are elected independently, one winner each, which is what lets a chain compose across
/// sources: a runtime rule can add a truncation on top of a sealed mask, and cannot replace the
/// mask. Electing the whole chain as a unit would let a rule discard a compile-time mask by
/// supplying anything at all.
/// </remarks>
public enum TransformKind
{
    /// <summary>A transformer the application supplied. Runs first, on the real value.</summary>
    Mutate = 0,

    /// <summary>Precision reduced, staying the kind of thing it was.</summary>
    Generalize = 1,

    /// <summary>Rendered to text through a format string.</summary>
    Format = 2,

    /// <summary>Characters obscured.</summary>
    Mask = 3,

    /// <summary>Shortened to a maximum length. Runs last.</summary>
    Truncate = 4,

    /// <summary>Replaced outright. Short-circuits every other stage.</summary>
    Default = 5
}

/// <summary>
/// One stage of a transform chain, resolved from an attribute or a runtime rule.
/// </summary>
/// <remarks>
/// A closed hierarchy: the pipeline switches over the six derived types, and adding a seventh is a
/// compile error at every switch rather than a silently skipped stage.
/// </remarks>
public abstract class TransformStage
{
    /// <summary>Initializes the stage.</summary>
    /// <param name="kind">Which stage this is.</param>
    /// <param name="allowAggregate">
    /// True to permit aggregating the field this stage transforms.
    /// </param>
    /// <param name="minGroupSize">
    /// The smallest group this stage's field may be aggregated over, or zero to set no floor of its
    /// own.
    /// </param>
    private protected TransformStage(
        TransformKind kind, bool allowAggregate = false, int minGroupSize = 0)
    {
        Kind = kind;
        AllowAggregate = allowAggregate;
        MinGroupSize = minGroupSize;
    }

    /// <summary>Which stage this is.</summary>
    public TransformKind Kind { get; }

    /// <summary>
    /// True when the field this stage transforms may still be aggregated.
    /// </summary>
    /// <remarks>
    /// False by default, which is the whole mitigation. Aggregation runs in SQL against the stored
    /// values, long before any stage applies — so <c>MAX</c> over a masked salary returns the real
    /// maximum and the transform obscured a column nobody asked to see. Design section 7.2.
    /// </remarks>
    public bool AllowAggregate { get; }

    /// <summary>
    /// The smallest group this stage's field may be aggregated over, or zero when it sets no floor.
    /// </summary>
    /// <remarks>
    /// The per-field half of the k-anonymity floor. Without a floor, <see cref="AllowAggregate"/> is
    /// an opening rather than a permission: a group of one returns that row's exact value under any
    /// aggregate function.
    /// </remarks>
    public int MinGroupSize { get; }
}

/// <summary>Hands the value to a transformer the application supplied.</summary>
public sealed class MutateStage : TransformStage
{
    /// <summary>Initializes the stage.</summary>
    /// <param name="transformer">A type implementing <c>IValueTransformer</c>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transformer"/> is null.</exception>
    /// <param name="allowAggregate">True to permit aggregating the field this stage transforms.</param>
    /// <param name="minGroupSize">The smallest group it may be aggregated over, or zero for none.</param>
    public MutateStage(Type transformer, bool allowAggregate = false, int minGroupSize = 0)
        : base(TransformKind.Mutate, allowAggregate, minGroupSize) =>
        Transformer = transformer ?? throw new ArgumentNullException(nameof(transformer));

    /// <summary>The transformer to run.</summary>
    public Type Transformer { get; }
}

/// <summary>Reduces the precision of a numeric or temporal value.</summary>
public sealed class GeneralizeStage : TransformStage
{
    /// <summary>Initializes the stage.</summary>
    /// <param name="mode">How precision is reduced.</param>
    /// <param name="step">The width of a rounding or bucketing step.</param>
    /// <param name="part">The component a date is reduced to.</param>
    /// <param name="decimals">Digits kept after the point.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="mode"/> needs a positive step and does not have one. A step of
    /// zero would divide by zero at the moment of masking, on a caller's query.
    /// </exception>
    /// <param name="allowAggregate">True to permit aggregating the field this stage transforms.</param>
    /// <param name="minGroupSize">The smallest group it may be aggregated over, or zero for none.</param>
    public GeneralizeStage(
        GeneralizeMode mode,
        int step = 0,
        DatePart part = DatePart.Year,
        int decimals = 0,
        bool allowAggregate = false,
        int minGroupSize = 0)
        : base(TransformKind.Generalize, allowAggregate, minGroupSize)
    {
        if (mode is GeneralizeMode.Round or GeneralizeMode.Bucket && step <= 0)
        {
            throw new ArgumentException(
                $"{mode} needs a positive Step; it was {step}.", nameof(step));
        }

        if (mode == GeneralizeMode.Truncate && decimals < 0)
        {
            throw new ArgumentException(
                $"Truncate needs a non-negative Decimals; it was {decimals}.", nameof(decimals));
        }

        Mode = mode;
        Step = step;
        Part = part;
        Decimals = decimals;
    }

    /// <summary>How precision is reduced.</summary>
    public GeneralizeMode Mode { get; }

    /// <summary>The width of a rounding or bucketing step.</summary>
    public int Step { get; }

    /// <summary>The component a date is reduced to.</summary>
    public DatePart Part { get; }

    /// <summary>Digits kept after the point.</summary>
    public int Decimals { get; }
}

/// <summary>Renders a value to text through a format string.</summary>
public sealed class FormatStage : TransformStage
{
    /// <summary>Initializes the stage.</summary>
    /// <param name="format">A standard or custom .NET format string.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="format"/> is blank.</exception>
    /// <param name="allowAggregate">True to permit aggregating the field this stage transforms.</param>
    /// <param name="minGroupSize">The smallest group it may be aggregated over, or zero for none.</param>
    public FormatStage(string format, bool allowAggregate = false, int minGroupSize = 0)
        : base(TransformKind.Format, allowAggregate, minGroupSize)
    {
        if (string.IsNullOrWhiteSpace(format))
        {
            throw new ArgumentException("A format stage requires a format string.", nameof(format));
        }

        Format = format;
    }

    /// <summary>The format string applied to the value.</summary>
    public string Format { get; }
}

/// <summary>Obscures the characters of a value.</summary>
public sealed class MaskStage : TransformStage
{
    /// <summary>Initializes the stage.</summary>
    /// <param name="strategy">How the value is obscured.</param>
    /// <param name="keepStart">Characters kept at the start under <see cref="MaskStrategy.Partial"/>.</param>
    /// <param name="keepEnd">Characters kept at the end under <see cref="MaskStrategy.Partial"/>.</param>
    /// <param name="maskChar">The character standing in for a hidden one.</param>
    /// <param name="preserveLength">Whether the mask is as long as the value it hides.</param>
    /// <param name="pattern">The pattern matched under <see cref="MaskStrategy.Regex"/>.</param>
    /// <param name="replacement">What each match becomes under <see cref="MaskStrategy.Regex"/>.</param>
    /// <param name="text">The constant under <see cref="MaskStrategy.Fixed"/>.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when a strategy is missing the parameter it cannot work without, or when a keep count
    /// is negative. Refused here rather than at the moment of masking, so a misconfiguration is a
    /// startup failure rather than a query failure.
    /// </exception>
    /// <param name="allowAggregate">True to permit aggregating the field this stage transforms.</param>
    /// <param name="minGroupSize">The smallest group it may be aggregated over, or zero for none.</param>
    public MaskStage(
        MaskStrategy strategy,
        int keepStart = 0,
        int keepEnd = 0,
        char maskChar = '*',
        bool preserveLength = true,
        string? pattern = null,
        string? replacement = null,
        string? text = null,
        bool allowAggregate = false,
        int minGroupSize = 0)
        : base(TransformKind.Mask, allowAggregate, minGroupSize)
    {
        if (keepStart < 0 || keepEnd < 0)
        {
            throw new ArgumentException(
                $"A partial mask cannot keep a negative number of characters; it was " +
                $"KeepStart {keepStart}, KeepEnd {keepEnd}.", nameof(keepStart));
        }

        if (strategy == MaskStrategy.Regex && string.IsNullOrEmpty(pattern))
        {
            throw new ArgumentException(
                "A regex mask requires a Pattern; without one it matches nothing and the value " +
                "would pass through unmasked.", nameof(pattern));
        }

        if (strategy == MaskStrategy.Fixed && text is null)
        {
            throw new ArgumentException(
                "A fixed mask requires Text; without it there is nothing to put in the value's " +
                "place.", nameof(text));
        }

        Strategy = strategy;
        KeepStart = keepStart;
        KeepEnd = keepEnd;
        MaskChar = maskChar;
        PreserveLength = preserveLength;
        Pattern = pattern;
        Replacement = replacement ?? string.Empty;
        Text = text;
    }

    /// <summary>How the value is obscured.</summary>
    public MaskStrategy Strategy { get; }

    /// <summary>Characters kept at the start.</summary>
    public int KeepStart { get; }

    /// <summary>Characters kept at the end.</summary>
    public int KeepEnd { get; }

    /// <summary>The character standing in for a hidden one.</summary>
    public char MaskChar { get; }

    /// <summary>Whether the mask is as long as the value it hides.</summary>
    public bool PreserveLength { get; }

    /// <summary>The pattern matched under <see cref="MaskStrategy.Regex"/>.</summary>
    public string? Pattern { get; }

    /// <summary>What each match becomes under <see cref="MaskStrategy.Regex"/>.</summary>
    public string Replacement { get; }

    /// <summary>The constant under <see cref="MaskStrategy.Fixed"/>.</summary>
    public string? Text { get; }
}

/// <summary>Shortens text to a maximum length.</summary>
public sealed class TruncateStage : TransformStage
{
    /// <summary>Initializes the stage.</summary>
    /// <param name="length">The greatest number of characters kept, before any ellipsis.</param>
    /// <param name="ellipsis">Appended when the value was shortened, or null to append nothing.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="length"/> is negative.</exception>
    /// <param name="allowAggregate">True to permit aggregating the field this stage transforms.</param>
    /// <param name="minGroupSize">The smallest group it may be aggregated over, or zero for none.</param>
    public TruncateStage(
        int length, string? ellipsis = null, bool allowAggregate = false, int minGroupSize = 0)
        : base(TransformKind.Truncate, allowAggregate, minGroupSize)
    {
        if (length < 0)
        {
            throw new ArgumentException(
                $"A truncation cannot keep a negative number of characters; it was {length}.",
                nameof(length));
        }

        Length = length;
        Ellipsis = ellipsis;
    }

    /// <summary>The greatest number of characters kept, before any ellipsis.</summary>
    public int Length { get; }

    /// <summary>Appended when the value was shortened.</summary>
    public string? Ellipsis { get; }
}

/// <summary>Replaces a value outright, short-circuiting every other stage.</summary>
public sealed class DefaultStage : TransformStage
{
    /// <summary>Initializes the stage.</summary>
    /// <param name="value">The constant, in string form, or null to use the member type's default.</param>
    /// <param name="hasValue">
    /// True when a constant was supplied. Distinguishes <c>[DwDefault]</c> from
    /// <c>[DwDefault(null)]</c>, which <paramref name="value"/> alone cannot.
    /// </param>
    /// <param name="allowAggregate">True to permit aggregating the field this stage transforms.</param>
    /// <param name="minGroupSize">The smallest group it may be aggregated over, or zero for none.</param>
    public DefaultStage(
        string? value, bool hasValue, bool allowAggregate = false, int minGroupSize = 0)
        : base(TransformKind.Default, allowAggregate, minGroupSize)
    {
        Value = value;
        HasValue = hasValue;
    }

    /// <summary>The constant, or null when the member type's default is used.</summary>
    public string? Value { get; }

    /// <summary>True when a constant was supplied.</summary>
    public bool HasValue { get; }
}
