using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace DynamicWhere.ex.Policies.Config;

/// <summary>
/// The page caps of every declared purpose, by the purpose's name.
/// </summary>
/// <remarks>
/// Any name, and as many as a deployment has: <c>excel</c>, <c>csv</c>, <c>pdf</c>,
/// <c>audit-logs</c>. A name is trimmed and matched without regard to letter case, which is how a
/// purpose-bound rule matches the same context, so one purpose means one thing to a rule and to a
/// cap. A context that declares no purpose, or one named nowhere here, runs under the deployment's
/// caps.
/// <para>
/// A dictionary, so a configuration section binds straight onto it,
/// <c>Caps:Purposes:excel:MaxPageSize</c>, and an environment variable can set one,
/// <c>DynamicWhere__Policies__Caps__Purposes__excel__MaxPageSize</c>. Several names may share one
/// <see cref="DwPageCaps"/>. Frozen with the rest of the caps.
/// </para>
/// </remarks>
public sealed class DwPurposeCaps : IDictionary<string, DwPageCaps>
{
    private readonly Dictionary<string, DwPageCaps> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _frozen;

    /// <summary>The page caps of one purpose.</summary>
    /// <param name="purpose">The purpose's name.</param>
    /// <exception cref="ArgumentException">Thrown when the name is null or blank.</exception>
    /// <exception cref="ArgumentNullException">Thrown when the value set is null.</exception>
    /// <exception cref="KeyNotFoundException">Thrown when reading a purpose that has none.</exception>
    /// <exception cref="InvalidOperationException">Thrown when setting after startup.</exception>
    public DwPageCaps this[string purpose]
    {
        get => _entries[Name(purpose)];
        set
        {
            Guard();
            _entries[Name(purpose)] = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    /// <inheritdoc />
    public ICollection<string> Keys => _entries.Keys;

    /// <inheritdoc />
    public ICollection<DwPageCaps> Values => _entries.Values;

    /// <inheritdoc />
    public int Count => _entries.Count;

    /// <summary>True once the caps are frozen.</summary>
    public bool IsReadOnly => _frozen;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// Thrown when the name is null or blank, or already has page caps.
    /// </exception>
    public void Add(string key, DwPageCaps value)
    {
        Guard();
        _entries.Add(Name(key), value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <inheritdoc />
    public void Add(KeyValuePair<string, DwPageCaps> item) => Add(item.Key, item.Value);

    /// <inheritdoc />
    public void Clear()
    {
        Guard();
        _entries.Clear();
    }

    /// <inheritdoc />
    public bool Contains(KeyValuePair<string, DwPageCaps> item) =>
        TryGetValue(item.Key, out DwPageCaps? caps) && ReferenceEquals(caps, item.Value);

    /// <inheritdoc />
    public bool ContainsKey(string key) => TryGetValue(key, out _);

    /// <inheritdoc />
    public void CopyTo(KeyValuePair<string, DwPageCaps>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<string, DwPageCaps>>)_entries).CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, DwPageCaps>> GetEnumerator() => _entries.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public bool Remove(string key)
    {
        Guard();

        return _entries.Remove(Name(key));
    }

    /// <inheritdoc />
    public bool Remove(KeyValuePair<string, DwPageCaps> item)
    {
        Guard();

        return Contains(item) && _entries.Remove(Name(item.Key));
    }

    /// <summary>The page caps of a purpose, when it has any.</summary>
    /// <remarks>A null or blank name has none, rather than being refused: a context need not declare a purpose.</remarks>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out DwPageCaps value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            value = null;

            return false;
        }

        return _entries.TryGetValue(key.Trim(), out value);
    }

    /// <summary>Prevents any further change, to the names and to each purpose's caps.</summary>
    internal void Freeze()
    {
        _frozen = true;

        foreach (DwPageCaps caps in _entries.Values)
        {
            caps.Freeze();
        }
    }

    private void Guard()
    {
        if (_frozen)
        {
            throw new InvalidOperationException("Policy caps cannot be changed after startup.");
        }
    }

    private static string Name(string purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            throw new ArgumentException(
                "A purpose's page caps need the purpose's name.", nameof(purpose));
        }

        return purpose.Trim();
    }
}
