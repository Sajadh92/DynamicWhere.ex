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
