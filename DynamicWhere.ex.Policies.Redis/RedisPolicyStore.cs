using System.Runtime.CompilerServices;
using System.Threading.Channels;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Storage;
using StackExchange.Redis;

namespace DynamicWhere.ex.Policies.Redis;

/// <summary>
/// A policy store held in Redis, with pub/sub invalidation.
/// </summary>
/// <remarks>
/// Design section 5.4 gives Redis the millisecond invalidation path: a write publishes on
/// <c>dw:policy:version</c> and every instance reloads. It is held to the same conformance suite as
/// the in-memory and relational stores.
/// <para>
/// <b>The publish is not the mechanism; it is the fast path.</b> Redis pub/sub is fire-and-forget
/// and delivers to whoever is connected at that instant, so an instance that was reconnecting when
/// a message went out would otherwise serve the previous policy until something else happened to
/// change. <see cref="GetVersionAsync"/> reads the counter for real, and <c>StorePolicyProvider</c>
/// polls it behind the watch, which bounds a dropped message at one refresh interval rather than
/// forever. A store that implemented only the watch would take that backstop away without changing
/// anything visible.
/// </para>
/// <para>
/// The store does not own the multiplexer and does not dispose it: a connection to Redis is an
/// application-wide resource, and this is one of its users.
/// </para>
/// </remarks>
public sealed class RedisPolicyStore : IDwPolicyWritableStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisPolicyKeys _keys;
    private readonly Func<string, Type?>? _resolveType;

    /// <summary>
    /// Initializes the store.
    /// </summary>
    /// <param name="redis">A connected multiplexer. Not owned, and not disposed.</param>
    /// <param name="prefix">
    /// The prefix every key and the channel share, or null for <c>dw:policy</c>. Give two
    /// applications sharing one Redis two prefixes.
    /// </param>
    /// <param name="resolveType">
    /// Turns a rule's entity name into a type, so <see cref="UpsertAsync"/> can refuse a rule
    /// targeting a field a sealed attribute already speaks to. Optional.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="redis"/> is null.</exception>
    public RedisPolicyStore(
        IConnectionMultiplexer redis, string? prefix = null, Func<string, Type?>? resolveType = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _keys = new RedisPolicyKeys(prefix);
        _resolveType = resolveType;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">
    /// Thrown when any stored rule cannot be read. The load fails rather than skipping it: a
    /// skipped rule is a control that is no longer enforced, and nothing would report the
    /// difference.
    /// </exception>
    public async ValueTask<StoreSnapshot> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        IDatabase db = _redis.GetDatabase();

        // The counter is read before the rules, and the order is load-bearing. Read after, a write
        // landing in between would stamp this snapshot with a version newer than the rules it
        // holds — and the poll compares versions, so it would see no change and serve the previous
        // policy indefinitely. Read first, the same race stamps a version older than the rules,
        // which costs one redundant reload and nothing else.
        long version = await ReadVersionAsync(db).ConfigureAwait(false);

        HashEntry[] entries = await db.HashGetAllAsync(_keys.Broad).ConfigureAwait(false);

        return new StoreSnapshot(version, DateTimeOffset.UtcNow, Read(entries));
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="userIdentities"/> is null.
    /// </exception>
    public async ValueTask<NarrowZone> LoadNarrowAsync(
        IReadOnlyList<string> userIdentities, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (userIdentities is null)
        {
            throw new ArgumentNullException(nameof(userIdentities));
        }

        if (userIdentities.Count == 0)
        {
            return NarrowZone.Empty;
        }

        IDatabase db = _redis.GetDatabase();

        long version = await ReadVersionAsync(db).ConfigureAwait(false);

        List<PolicyRule> rules = new();

        // One hash per identity, not one scan of everything. A caller holds a handful of user
        // identities and the store may hold a million, which is the whole reason section 5.3 splits
        // the zones.
        foreach (string identity in userIdentities)
        {
            string? key = _keys.User(identity);

            if (key is null)
            {
                continue;
            }

            rules.AddRange(Read(await db.HashGetAllAsync(key).ConfigureAwait(false)));
        }

        return new NarrowZone(version, rules);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A real read of the counter, never a stub. This is what the provider polls behind the watch,
    /// and it is the only thing standing between a dropped pub/sub message and an instance serving
    /// a withdrawn policy until it happens to be restarted.
    /// </remarks>
    public async ValueTask<long> GetVersionAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return await ReadVersionAsync(_redis.GetDatabase()).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<long>? WatchAsync(CancellationToken ct) => Watch(ct);

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the rule targets a sealed field.</exception>
    public async ValueTask<PolicyRule> UpsertAsync(PolicyRule rule, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        SealedFields.Refuse(rule, _resolveType, nameof(rule));

        IDatabase db = _redis.GetDatabase();

        RedisValue field = rule.Id.ToString();

        string target = rule.IsBroad
            ? _keys.Broad
            : _keys.User(rule.SubjectKey)
                ?? throw new ArgumentException(
                    "A user-level rule requires a subject key to be stored under.", nameof(rule));

        // Where the rule lived before, if anywhere. A rule re-upserted under a different subject
        // moves hash, and leaving the old copy behind would apply it to both callers — over-applying
        // an Allow is a grant nobody wrote.
        RedisValue previous = await db.HashGetAsync(_keys.Owner, field).ConfigureAwait(false);

        ITransaction write = db.CreateTransaction();

        if (previous.HasValue && previous != target)
        {
            _ = write.HashDeleteAsync(previous.ToString(), field);
        }

        _ = write.HashSetAsync(target, field, PolicyRuleDocument.ToJson(rule));
        _ = write.HashSetAsync(_keys.Owner, field, target);

        // Inside the transaction with the rule, so the two cannot come apart. The publish below is
        // deliberately outside it: a process that dies after the commit and before the publish has
        // still moved the counter, and the poll is what turns that into a reload.
        Task<long> bumped = write.StringIncrementAsync(_keys.Version);

        await write.ExecuteAsync().ConfigureAwait(false);

        await AnnounceAsync(await bumped.ConfigureAwait(false)).ConfigureAwait(false);

        return rule;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The counter moves whether or not a rule was removed. A caller that deleted a rule someone
    /// else had already deleted still expects the version it reads next to reflect the state it
    /// asked for.
    /// </remarks>
    public async ValueTask DeleteAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        IDatabase db = _redis.GetDatabase();

        RedisValue field = id.ToString();
        RedisValue owner = await db.HashGetAsync(_keys.Owner, field).ConfigureAwait(false);

        ITransaction write = db.CreateTransaction();

        if (owner.HasValue)
        {
            _ = write.HashDeleteAsync(owner.ToString(), field);
            _ = write.HashDeleteAsync(_keys.Owner, field);
        }

        Task<long> bumped = write.StringIncrementAsync(_keys.Version);

        await write.ExecuteAsync().ConfigureAwait(false);

        await AnnounceAsync(await bumped.ConfigureAwait(false)).ConfigureAwait(false);
    }

    /// <summary>Reads the counter, or zero when nothing has been written yet.</summary>
    /// <remarks>
    /// Zero for an untouched store is honest rather than convenient: the first write leaves it at
    /// one, so the version still moves forward on the write an instance is waiting to notice.
    /// </remarks>
    private async Task<long> ReadVersionAsync(IDatabase db)
    {
        RedisValue value = await db.StringGetAsync(_keys.Version).ConfigureAwait(false);

        return value.HasValue && long.TryParse(value.ToString(), out long version) ? version : 0;
    }

    /// <summary>Turns stored documents into rules, refusing any that cannot be read.</summary>
    private static List<PolicyRule> Read(HashEntry[] entries)
    {
        List<PolicyRule> rules = new(entries.Length);

        foreach (HashEntry entry in entries)
        {
            // No try/catch. A document that cannot be read is a rule enforcing less than it says,
            // and skipping it would leave the snapshot loaded, the version advanced and the
            // instance healthy with one denial quietly gone.
            rules.Add(PolicyRuleDocument.ToRule(entry.Value.ToString()));
        }

        return rules;
    }

    /// <summary>Announces a new version, best effort.</summary>
    /// <remarks>
    /// A failure here is not a failed write: the counter has already moved, so every instance will
    /// notice on its next poll. Throwing would tell an operator their rule was not stored when it
    /// was.
    /// </remarks>
    private async Task AnnounceAsync(long version)
    {
        try
        {
            await _redis.GetSubscriber()
                .PublishAsync(RedisChannel.Literal(_keys.Channel), version)
                .ConfigureAwait(false);
        }
        catch (RedisException)
        {
            // Deliberately swallowed. See above: the poll is the guarantee, this is the latency.
        }
    }

    /// <summary>Yields a version each time a write announces one.</summary>
    /// <remarks>
    /// Unbounded, and it drops nothing, because a version is a wake-up rather than a state: the
    /// provider reloads whatever the store currently holds. Falling behind therefore costs an extra
    /// reload, never a missed change.
    /// </remarks>
    private async IAsyncEnumerable<long> Watch([EnumeratorCancellation] CancellationToken ct)
    {
        Channel<long> queue = Channel.CreateUnbounded<long>(
            new UnboundedChannelOptions { SingleReader = true });

        ChannelMessageQueue subscription = await _redis.GetSubscriber()
            .SubscribeAsync(RedisChannel.Literal(_keys.Channel))
            .ConfigureAwait(false);

        subscription.OnMessage(message =>
        {
            if (long.TryParse(message.Message.ToString(), out long version))
            {
                queue.Writer.TryWrite(version);
            }
        });

        try
        {
            await foreach (long version in queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return version;
            }
        }
        finally
        {
            await subscription.UnsubscribeAsync().ConfigureAwait(false);
        }
    }
}
