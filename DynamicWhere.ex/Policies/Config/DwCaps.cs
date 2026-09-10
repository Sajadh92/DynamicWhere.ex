namespace DynamicWhere.ex.Policies.Config;

/// <summary>
/// Numeric limits applied to every guarded query.
/// </summary>
/// <remarks>
/// These exist independently of access control. Before this release the library accepted a filter
/// of any size and a navigation path of any depth, so a single request could generate an unbounded
/// join. The defaults below are generous enough not to disturb existing callers while removing the
/// unbounded case.
/// </remarks>
public sealed class DwCaps
{
    private bool _frozen;
    private int _maxPageSize = 1000;
    private int _maxConditions = 50;
    private int _maxOrderFields = 10;
    private int _maxNavigationDepth = 4;
    private int _maxQueryCost = 1000;
    private int _defaultFieldCost = 1;
    private int _maxAuditEvents = 10_000;
    private int _minGroupSize;

    /// <summary>
    /// The floor a deployment gets when it never mentions <see cref="MinGroupSize"/> at all.
    /// </summary>
    /// <remarks>
    /// Five, the usual k-anonymity choice, rather than the one that changes nothing. The
    /// compatibility argument for shipping this off does not survive being looked at: the floor
    /// applies only to a guarded summary, guarded queries are new in this release, and so there is
    /// no caller anywhere whose results it can change.
    /// </remarks>
    public const int DefaultMinGroupSize = 5;

    /// <summary>The largest page a caller may request.</summary>
    public int MaxPageSize
    {
        get => _maxPageSize;
        set => _maxPageSize = Set(value);
    }

    /// <summary>The most conditions one filter may contain, counted across every nested group.</summary>
    public int MaxConditions
    {
        get => _maxConditions;
        set => _maxConditions = Set(value);
    }

    /// <summary>The most fields one query may sort by.</summary>
    public int MaxOrderFields
    {
        get => _maxOrderFields;
        set => _maxOrderFields = Set(value);
    }

    /// <summary>The deepest navigation path a field may traverse.</summary>
    public int MaxNavigationDepth
    {
        get => _maxNavigationDepth;
        set => _maxNavigationDepth = Set(value);
    }

    /// <summary>
    /// The most a single query may spend, summed over every field reference the caller wrote.
    /// </summary>
    /// <remarks>
    /// A reference costs the field's <c>[DwCost]</c> weight, or <see cref="DefaultFieldCost"/> when
    /// nothing weighs it. Every reference is charged, not every distinct field: charging per field
    /// would let a caller generate the same work by naming one field a thousand times, which is the
    /// case this cap exists for.
    /// <para>
    /// The default is deliberately generous, as every other cap here is. A control that refuses
    /// working queries the day a library is upgraded is a control that gets switched off.
    /// </para>
    /// </remarks>
    public int MaxQueryCost
    {
        get => _maxQueryCost;
        set => _maxQueryCost = Set(value);
    }

    /// <summary>
    /// What one reference to an unweighted field spends against <see cref="MaxQueryCost"/>.
    /// </summary>
    /// <remarks>
    /// Separate from the cap so a host can decide what "ordinary" costs. Zero is accepted and means
    /// only fields carrying an explicit <c>[DwCost]</c> are charged at all, which is the posture for
    /// a model that weighs its few expensive fields and wants the rest free.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is negative.</exception>
    public int DefaultFieldCost
    {
        get => _defaultFieldCost;
        set
        {
            if (_frozen)
            {
                throw new InvalidOperationException("Policy caps cannot be changed after startup.");
            }

            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value,
                    "A field cost cannot be negative: a query would buy budget back by naming a field.");
            }

            _defaultFieldCost = value;
        }
    }

    /// <summary>
    /// The most audit events one context may hold before being drained.
    /// </summary>
    /// <remarks>
    /// Reaching it refuses the query rather than dropping the record. That is the fail-closed
    /// reading and the only defensible one: an audited field whose log has quietly stopped being
    /// written is exactly the outcome <c>[DwAudit]</c> exists to make impossible.
    /// <para>
    /// The default is far above what one request can produce — a context is built per request and
    /// drained with it — so reaching it means a context is being reused across many requests
    /// without ever being drained, which is a leak worth failing on.
    /// </para>
    /// </remarks>
    public int MaxAuditEvents
    {
        get => _maxAuditEvents;
        set => _maxAuditEvents = Set(value);
    }

    /// <summary>
    /// The smallest group a summary may report, below which the group is suppressed.
    /// </summary>
    /// <remarks>
    /// The k-anonymity floor of design section 7.2, and the half that keeps
    /// <c>AllowAggregate</c> from being an opening rather than a permission: aggregation over a
    /// group of one returns that row's exact value under any function, so permitting a masked field
    /// to be aggregated without a floor hands back precisely what the mask was there to hide.
    /// <para>
    /// A deployment that never mentions this gets <see cref="DefaultMinGroupSize"/>. A deployment
    /// that writes <c>MinGroupSize = 1</c> gets no floor at all, in production, with nothing
    /// refused and nothing warned about. Those are two different instructions and the difference is
    /// readable through <see cref="IsMinGroupSizeSet"/> — which is the whole reason this is not
    /// simply defaulted to one. Shipping the safe value as the default and the unsafe value as an
    /// explicit sentence is the only arrangement that is both safe by default and honest about
    /// whose choice it is.
    /// </para>
    /// <para>
    /// Above one it applies to every grouped summary rather than only to those touching a
    /// transformed field — a group of one is a re-identification risk whatever is in it, and making
    /// the floor conditional on a transform would leave an unmasked-but-sensitive field with none.
    /// </para>
    /// <para>
    /// A field may raise it for itself through <c>MinGroupSize</c> on the attribute that transforms
    /// it. The effective floor is the largest in play.
    /// </para>
    /// </remarks>
    public int MinGroupSize
    {
        get => _minGroupSize == 0 ? DefaultMinGroupSize : _minGroupSize;
        set => _minGroupSize = Set(value);
    }

    /// <summary>
    /// True when a deployment set <see cref="MinGroupSize"/> itself, rather than inheriting
    /// <see cref="DefaultMinGroupSize"/>.
    /// </summary>
    /// <remarks>
    /// Reported because "off" and "never configured" have to be told apart by something. Without
    /// this the only way to switch the floor off would be to write the value the default already
    /// holds, and a startup check could not tell a deliberate opt-out from a deployment that had
    /// never heard of the setting — which is exactly the trap that makes refusing to start the
    /// wrong control here.
    /// </remarks>
    public bool IsMinGroupSizeSet => _minGroupSize != 0;

    /// <summary>Prevents any further change.</summary>
    internal void Freeze() => _frozen = true;

    /// <summary>
    /// Guards a setter against post-startup mutation and against a nonsensical limit.
    /// </summary>
    private int Set(int value)
    {
        if (_frozen)
        {
            throw new InvalidOperationException("Policy caps cannot be changed after startup.");
        }

        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A cap must be at least 1.");
        }

        return value;
    }
}
