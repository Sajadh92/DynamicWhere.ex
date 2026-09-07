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
