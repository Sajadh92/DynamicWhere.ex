using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.DTOs;

/// <summary>
/// The whole transform chain for one field, assembled from the stage each source won.
/// </summary>
/// <remarks>
/// The stages run in one fixed order — mutate, generalize, format, mask, truncate — so custom logic
/// sees the real value, precision drops before rendering, characters are hidden after rendering, and
/// a length cap has the last word. Every stage is optional, so the ordinary case of a single
/// attribute is a one-stage chain.
/// <para>
/// <see cref="Default"/> is not part of that order. It short-circuits: a field carrying it emits the
/// replacement and nothing else runs. Masking a value already replaced by <c>"N/A"</c> obscures
/// nothing, and letting the two compose would make the emitted value depend on an ordering rule
/// invisible to anyone reading the entity.
/// </para>
/// </remarks>
public sealed class ValueTransform
{
    /// <summary>Initializes a chain from the stages that won their election.</summary>
    public ValueTransform(
        MutateStage? mutate = null,
        GeneralizeStage? generalize = null,
        FormatStage? format = null,
        MaskStage? mask = null,
        TruncateStage? truncate = null,
        DefaultStage? @default = null)
    {
        Mutate = mutate;
        Generalize = generalize;
        Format = format;
        Mask = mask;
        Truncate = truncate;
        Default = @default;
    }

    /// <summary>Runs first, on the real value.</summary>
    public MutateStage? Mutate { get; }

    /// <summary>Reduces precision while keeping the value's kind.</summary>
    public GeneralizeStage? Generalize { get; }

    /// <summary>Renders to text.</summary>
    public FormatStage? Format { get; }

    /// <summary>Obscures characters.</summary>
    public MaskStage? Mask { get; }

    /// <summary>Caps the length. Runs last.</summary>
    public TruncateStage? Truncate { get; }

    /// <summary>Replaces the value outright, in place of every other stage.</summary>
    public DefaultStage? Default { get; }

    /// <summary>
    /// True when the field may still be aggregated.
    /// </summary>
    /// <remarks>
    /// Every stage must permit it. A chain is only as permissive as its strictest link, so adding a
    /// truncation on top of a mask that opted in takes the permission away again — which is the
    /// direction that is safe to get wrong. An empty chain permits it, because an untransformed
    /// field has nothing to disclose that the caller could not already read.
    /// </remarks>
    public bool AllowsAggregate
    {
        get
        {
            foreach (TransformStage stage in Configured)
            {
                if (!stage.AllowAggregate)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// The smallest group this field may be aggregated over, or zero when no stage sets a floor.
    /// </summary>
    /// <remarks>
    /// The largest of them, not the last: two stages each naming a floor both mean it, and honouring
    /// the smaller would answer for a group one of them was written to protect.
    /// </remarks>
    public int MinGroupSize
    {
        get
        {
            int floor = 0;

            foreach (TransformStage stage in Configured)
            {
                if (stage.MinGroupSize > floor)
                {
                    floor = stage.MinGroupSize;
                }
            }

            return floor;
        }
    }

    /// <summary>
    /// Every stage configured on this chain, whether or not it will run.
    /// </summary>
    /// <remarks>
    /// <see cref="Stages"/> answers what runs; this answers what somebody declared. They differ
    /// only for a replacement, which short-circuits the rest — and a stage that is suppressed for
    /// output still describes a field somebody chose to obscure, so a decision about whether that
    /// field may be aggregated has to see it.
    /// <para>
    /// Reading the running set instead let a replacement cancel the aggregate refusal and the group
    /// floor of a mask it never outranked. A rule may carry a replacement without carrying any
    /// feature at all, so the sealed-field check at the store boundary has no feature to refuse it
    /// on, and the mask stays elected in its own slot — the chain held both and answered for one.
    /// </para>
    /// </remarks>
    private IEnumerable<TransformStage> Configured
    {
        get
        {
            if (Mutate is not null)
            {
                yield return Mutate;
            }

            if (Generalize is not null)
            {
                yield return Generalize;
            }

            if (Format is not null)
            {
                yield return Format;
            }

            if (Mask is not null)
            {
                yield return Mask;
            }

            if (Truncate is not null)
            {
                yield return Truncate;
            }

            if (Default is not null)
            {
                yield return Default;
            }
        }
    }

    /// <summary>True when nothing transforms this field.</summary>
    public bool IsEmpty =>
        Mutate is null && Generalize is null && Format is null
        && Mask is null && Truncate is null && Default is null;

    /// <summary>
    /// True when a replacement is configured alongside a stage it would discard.
    /// </summary>
    /// <remarks>
    /// A configuration error rather than a resolution question: the startup scan refuses it, because
    /// the alternative is a field whose attributes say it is masked and whose output is a constant.
    /// </remarks>
    public bool HasConflictingDefault => Default is not null && !OnlyDefault;

    /// <summary>True when the chain is a replacement and nothing else.</summary>
    private bool OnlyDefault =>
        Mutate is null && Generalize is null && Format is null && Mask is null && Truncate is null;

    /// <summary>
    /// The stages that will run, in the order they run.
    /// </summary>
    /// <remarks>
    /// A replacement yields itself alone, which is what makes the short-circuit a property of the
    /// chain rather than a branch every consumer has to remember.
    /// </remarks>
    public IEnumerable<TransformStage> Stages
    {
        get
        {
            if (Default is not null)
            {
                yield return Default;

                yield break;
            }

            if (Mutate is not null)
            {
                yield return Mutate;
            }

            if (Generalize is not null)
            {
                yield return Generalize;
            }

            if (Format is not null)
            {
                yield return Format;
            }

            if (Mask is not null)
            {
                yield return Mask;
            }

            if (Truncate is not null)
            {
                yield return Truncate;
            }
        }
    }

    /// <summary>
    /// The action recorded for this chain: the most significant stage that runs.
    /// </summary>
    /// <remarks>
    /// A replacement outranks a custom transformer, which outranks a mask, which outranks a
    /// generalization. A chain of only formatting or truncation records as
    /// <see cref="PolicyAction.Masked"/>, because what every one of these means is that the value
    /// handed to the caller is not the value the database holds.
    /// </remarks>
    public PolicyAction Action =>
        Default is not null ? PolicyAction.Defaulted
        : Mutate is not null ? PolicyAction.Mutated
        : Mask is not null ? PolicyAction.Masked
        : Generalize is not null ? PolicyAction.Generalized
        : PolicyAction.Masked;
}
