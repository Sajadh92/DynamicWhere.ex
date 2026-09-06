using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Storage;

/// <summary>
/// A policy store held in process memory. The reference implementation, and the one the store
/// conformance suite is written against.
/// </summary>
/// <remarks>
/// Useful in its own right for an application whose rules are configured at startup rather than
/// administered, and for tests. Change notification is immediate, because there is no transport
/// between the write and the read.
/// <para>
/// Thread-safe. Writes take a lock and readers copy under it, so a load never observes a half-
/// applied change.
/// </para>
/// </remarks>
public sealed class InMemoryPolicyStore : IDwPolicyWritableStore, IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, PolicyRule> _rules = new();
    private readonly List<Channel<long>> _watchers = new();
    private readonly Func<string, Type?>? _resolveType;

    private long _version;
    private bool _disposed;

    /// <summary>
    /// Initializes an empty store.
    /// </summary>
    /// <param name="resolveType">
    /// Turns a rule's entity name into a type, so that <see cref="UpsertAsync"/> can refuse a rule
    /// targeting a field a sealed attribute already speaks to. Optional.
    /// </param>
    /// <remarks>
    /// Without a resolver an upsert cannot perform the configuration-time half of the sealed-field
    /// check from design section 5.7, and accepts the rule. That is not a hole: a sealed attribute
    /// outranks every dynamic level, so such a rule loses at resolution and grants nothing. The
    /// configuration-time check exists to tell an operator immediately rather than to be the thing
    /// standing between a rogue row and a protected field.
    /// </remarks>
    public InMemoryPolicyStore(Func<string, Type?>? resolveType = null) => _resolveType = resolveType;

    /// <summary>The version, bumped on every write.</summary>
    public long Version
    {
        get
        {
            lock (_lock)
            {
                return _version;
            }
        }
    }

    /// <summary>
    /// Adds rules without bumping the version once per rule, for configuring a store at startup.
    /// </summary>
    /// <param name="rules">The rules to add.</param>
    /// <returns>This store, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rules"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when any rule is null or targets a sealed field.</exception>
    public InMemoryPolicyStore Seed(params PolicyRule[] rules)
    {
        if (rules is null)
        {
            throw new ArgumentNullException(nameof(rules));
        }

        lock (_lock)
        {
            foreach (PolicyRule rule in rules)
            {
                if (rule is null)
                {
                    throw new ArgumentException("A store cannot hold a null rule.", nameof(rules));
                }

                RefuseSealed(rule);

                _rules[rule.Id] = rule;
            }

            Bump();
        }

        return this;
    }

    /// <inheritdoc />
    public ValueTask<StoreSnapshot> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            List<PolicyRule> broad = new();

            foreach (PolicyRule rule in _rules.Values)
            {
                if (rule.IsBroad)
                {
                    broad.Add(rule);
                }
            }

            return new ValueTask<StoreSnapshot>(
                new StoreSnapshot(_version, DateTimeOffset.UtcNow, broad));
        }
    }

    /// <inheritdoc />
    public ValueTask<NarrowZone> LoadNarrowAsync(
        IReadOnlyList<string> userIdentities, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (userIdentities is null)
        {
            throw new ArgumentNullException(nameof(userIdentities));
        }

        if (userIdentities.Count == 0)
        {
            return new ValueTask<NarrowZone>(NarrowZone.Empty);
        }

        HashSet<string> wanted = new(userIdentities, StringComparer.OrdinalIgnoreCase);

        lock (_lock)
        {
            List<PolicyRule> mine = new();

            foreach (PolicyRule rule in _rules.Values)
            {
                if (rule.SubjectKind == DwSubjectKind.User
                    && rule.SubjectKey is not null
                    && wanted.Contains(rule.SubjectKey))
                {
                    mine.Add(rule);
                }
            }

            return new ValueTask<NarrowZone>(new NarrowZone(_version, mine));
        }
    }

    /// <inheritdoc />
    public ValueTask<long> GetVersionAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return new ValueTask<long>(Version);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<long>? WatchAsync(CancellationToken ct) => Watch(ct);

    /// <inheritdoc />
    public ValueTask<PolicyRule> UpsertAsync(PolicyRule rule, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        RefuseSealed(rule);

        lock (_lock)
        {
            _rules[rule.Id] = rule;

            Bump();
        }

        return new ValueTask<PolicyRule>(rule);
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (_lock)
        {
            // Bumped whether or not anything was removed. A caller that deleted a rule someone else
            // had already deleted still expects the version it reads next to reflect the state it
            // asked for.
            _rules.Remove(id);

            Bump();
        }

        return default;
    }

    /// <summary>Ends every watch.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (Channel<long> watcher in _watchers)
            {
                watcher.Writer.TryComplete();
            }

            _watchers.Clear();
        }
    }

    /// <summary>Yields the version on every write, until the caller stops enumerating.</summary>
    private async IAsyncEnumerable<long> Watch([EnumeratorCancellation] CancellationToken ct)
    {
        Channel<long> channel = Channel.CreateUnbounded<long>(
            new UnboundedChannelOptions { SingleReader = true });

        lock (_lock)
        {
            if (_disposed)
            {
                yield break;
            }

            _watchers.Add(channel);
        }

        try
        {
            await foreach (long version in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return version;
            }
        }
        finally
        {
            lock (_lock)
            {
                _watchers.Remove(channel);
            }
        }
    }

    /// <summary>Advances the version and tells every watcher. Call under the lock.</summary>
    private void Bump()
    {
        _version++;

        foreach (Channel<long> watcher in _watchers)
        {
            watcher.Writer.TryWrite(_version);
        }
    }

    /// <summary>
    /// Refuses a rule aimed at a field a sealed attribute already speaks to.
    /// </summary>
    /// <remarks>
    /// Delegated to <see cref="SealedFields"/> so the relational and Redis stores perform the
    /// identical check rather than three approximations of it — the conformance suite's
    /// sealed-field test is worth what it looks like only if all three run the same code.
    /// </remarks>
    private void RefuseSealed(PolicyRule rule) =>
        SealedFields.Refuse(rule, _resolveType, nameof(rule));
}
