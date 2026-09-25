namespace DynamicWhere.ex.Policies.Config;

/// <summary>
/// The page caps a query runs under when its context declares one purpose, in place of the
/// deployment's own <see cref="DwCaps.MaxPageSize"/> and <see cref="DwCaps.DefaultPageSize"/>.
/// </summary>
/// <remarks>
/// A search and an export read the same rows for different reasons. The search returns a page to a
/// screen, and its caps bound what one response can carry. The export writes every match to a file,
/// and under the search's caps it either stopped at the first page or walked the rest one page at a
/// time: many statements, each with its own count, none of them one snapshot of the data. A purpose
/// gives the export its own numbers without raising the search's.
/// <para>
/// A value left null takes the deployment's cap, so a purpose that only needs a larger maximum names
/// only that. A value that is set is at least one: a purpose bounds a read differently, and cannot
/// make a read unbounded.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// options.Caps.Purposes["excel"] = new DwPageCaps { MaxPageSize = 10_000, DefaultPageSize = 10_000 };
///
/// context.Purpose = "excel";
/// FilterResult&lt;TenantRow&gt; file = await rows.ApplyPolicy(context).ToListAsync(filter, ct);
/// </code>
/// </example>
public sealed class DwPageCaps
{
    private bool _frozen;
    private int? _maxPageSize;
    private int? _defaultPageSize;

    /// <summary>
    /// The largest page a query under this purpose may request, or null for
    /// <see cref="DwCaps.MaxPageSize"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is below one.</exception>
    /// <exception cref="InvalidOperationException">Thrown after startup.</exception>
    public int? MaxPageSize
    {
        get => _maxPageSize;
        set => _maxPageSize = Set(value);
    }

    /// <summary>
    /// The page a query under this purpose is given when it asks for none, or null for
    /// <see cref="DwCaps.DefaultPageSize"/>.
    /// </summary>
    /// <remarks>
    /// Bounded by the purpose's maximum as the deployment's default is by its own, so a default
    /// above it is applied as the maximum.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is below one.</exception>
    /// <exception cref="InvalidOperationException">Thrown after startup.</exception>
    public int? DefaultPageSize
    {
        get => _defaultPageSize;
        set => _defaultPageSize = Set(value);
    }

    /// <summary>Prevents any further change.</summary>
    internal void Freeze() => _frozen = true;

    private int? Set(int? value)
    {
        if (_frozen)
        {
            throw new InvalidOperationException("Policy caps cannot be changed after startup.");
        }

        if (value is < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value,
                "A purpose's page cap must be at least 1. Null takes the deployment's own cap.");
        }

        return value;
    }
}
