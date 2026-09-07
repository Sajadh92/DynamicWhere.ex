using System.Collections.Concurrent;
using DynamicWhere.ex.Policies.Audit;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Context;

/// <summary>
/// Describes who is asking, and carries the ambient values a policy may need. Framework-agnostic
/// by design: the core library has no dependency on ASP.NET Core, and an adapter from
/// <c>ClaimsPrincipal</c> ships separately.
/// </summary>
/// <remarks>
/// Build one per request through an asynchronous factory so that any user-level rules are
/// preloaded; every downstream query then resolves policy synchronously against in-memory state.
/// </remarks>
public sealed class DwPolicyContext
{
    private readonly List<DwSubject> _subjects = new();
    private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<object, PolicyAttachment> _attachments = new();

    // Guarded, unlike the subjects and values above. Those are written while the context is being
    // built and only read afterwards; this one is written from the query path, and one context can
    // legitimately serve two queries at once — a plain List appended from two threads corrupts
    // rather than merely races.
    private readonly object _auditLock = new();
    private readonly List<DwAuditEvent> _audit = new();

    /// <summary>The principal dimensions describing the caller.</summary>
    public IReadOnlyList<DwSubject> Subjects => _subjects;

    /// <summary>
    /// When true, no policy decision throws or drops anything. Every decision is still recorded,
    /// which lets one canary subject run unenforced while everyone else is enforced.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// An optional declared purpose for the query, for purpose-bound rules.
    /// </summary>
    public string? Purpose { get; set; }

    /// <summary>
    /// Adds a subject. Adding the same kind and identity twice is a no-op.
    /// </summary>
    /// <returns>This context, for chaining.</returns>
    public DwPolicyContext WithSubject(DwSubjectKind kind, string identity)
    {
        DwSubject subject = new(kind, identity);

        if (!_subjects.Contains(subject))
        {
            _subjects.Add(subject);
        }

        return this;
    }

    /// <summary>
    /// Sets an ambient value, replacing any existing value under the same key.
    /// </summary>
    /// <returns>This context, for chaining.</returns>
    public DwPolicyContext WithValue(string key, object? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("An ambient value requires a key.", nameof(key));
        }

        _values[key] = value;

        return this;
    }

    /// <summary>
    /// Returns every identity held for one kind. Empty when the caller holds none.
    /// </summary>
    public IEnumerable<string> Identities(DwSubjectKind kind) =>
        _subjects.Where(s => s.Kind == kind).Select(s => s.Identity);

    /// <summary>
    /// Looks up an ambient value.
    /// </summary>
    public bool TryGetValue(string key, out object? value) => _values.TryGetValue(key, out value);

    /// <summary>
    /// The audit events recorded so far and not yet written to a sink.
    /// </summary>
    /// <remarks>
    /// A copy. The subject of an audit is the same caller whose access it records, and handing back
    /// the live list would let them delete the evidence before it reached a sink.
    /// <para>
    /// Drain it once per request through <c>DwPolicy.DrainAuditAsync</c>. A context is built per
    /// request and discarded with it, so events left here when it is collected are simply lost —
    /// which is why the buffer is bounded and the bound refuses the query rather than dropping the
    /// record.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DwAuditEvent> PendingAuditEvents
    {
        get
        {
            lock (_auditLock)
            {
                return _audit.ToArray();
            }
        }
    }

    /// <summary>
    /// Records an access, refusing once the buffer is full.
    /// </summary>
    /// <param name="auditEvent">What happened.</param>
    /// <param name="capacity">The most events this context may hold undrained.</param>
    /// <returns>False when the buffer is full and nothing was recorded.</returns>
    /// <remarks>
    /// Reports the overflow rather than throwing, so the caller decides what a full buffer means.
    /// It means the query is refused: an audited field whose log has quietly stopped being written
    /// is the one outcome the attribute exists to make impossible.
    /// </remarks>
    internal bool TryRecordAudit(DwAuditEvent auditEvent, int capacity)
    {
        lock (_auditLock)
        {
            if (_audit.Count >= capacity)
            {
                return false;
            }

            _audit.Add(auditEvent);

            return true;
        }
    }

    /// <summary>
    /// Removes and returns everything recorded so far.
    /// </summary>
    /// <remarks>
    /// Taken rather than copied-then-cleared, so two concurrent drains cannot each write the same
    /// event to the sink.
    /// </remarks>
    internal DwAuditEvent[] TakeAuditEvents()
    {
        lock (_auditLock)
        {
            DwAuditEvent[] taken = _audit.ToArray();

            _audit.Clear();

            return taken;
        }
    }

    /// <summary>
    /// Puts events back on the buffer, at the front, after a sink failed to accept them.
    /// </summary>
    /// <param name="unwritten">What the sink never saw, in order.</param>
    /// <remarks>
    /// At the front, so a retry writes them before anything recorded since — an audit log is read
    /// in order, and reordering it around a transient failure makes it harder to follow for no
    /// gain. The capacity bound is not applied here: these were already counted once, and refusing
    /// to take back what this library just removed would be losing them itself.
    /// </remarks>
    internal void ReturnAuditEvents(IReadOnlyList<DwAuditEvent> unwritten)
    {
        lock (_auditLock)
        {
            _audit.InsertRange(0, unwritten);
        }
    }

    /// <summary>
    /// Records what a store provider pinned to this context when it was prepared.
    /// </summary>
    /// <remarks>
    /// Keyed on the provider, because two store providers each pin their own snapshot and neither
    /// may read the other's. Written during preparation, before the context is used, and replaced
    /// wholesale if the context is prepared again.
    /// </remarks>
    /// <param name="provider">The provider doing the pinning.</param>
    /// <param name="attachment">The snapshot and narrow zone it read.</param>
    internal void Attach(object provider, PolicyAttachment attachment) =>
        _attachments[provider] = attachment;

    /// <summary>
    /// Returns what a provider pinned, or null when it never prepared this context.
    /// </summary>
    /// <remarks>
    /// Null is what a store provider refuses on. It cannot be confused with "this caller has no
    /// rules", which is an attachment holding empty zones.
    /// </remarks>
    /// <param name="provider">The provider asking.</param>
    internal PolicyAttachment? AttachmentFor(object provider) =>
        _attachments.TryGetValue(provider, out PolicyAttachment? attachment) ? attachment : null;
}
