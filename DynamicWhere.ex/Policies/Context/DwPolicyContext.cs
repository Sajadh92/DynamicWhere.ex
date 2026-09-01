using System.Collections.Concurrent;
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
