using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.ex.Policies.Context;

/// <summary>
/// What one store provider pinned to one caller's context when the context was prepared: the
/// snapshot that provider will read for the life of the context, and that caller's user-level
/// rules.
/// </summary>
/// <remarks>
/// The two travel together because they answer the same question at the same instant. Resolving the
/// narrow zone lazily on the query path would need I/O inside <c>GetFragments</c>, which is
/// synchronous and documented to perform none; re-reading the snapshot per field would let a
/// refresh land between two fields of one query, so a filter could be gated on version 41 and a
/// projection on version 42.
/// <para>
/// The presence of an attachment is itself the signal that somebody looked. A context without one
/// is refused rather than served from the broad zone alone, because a user-level denial that
/// silently does not apply is indistinguishable from a caller who has no user rules.
/// </para>
/// </remarks>
internal sealed class PolicyAttachment
{
    /// <summary>Initializes an attachment.</summary>
    /// <param name="snapshot">The pinned broad zone.</param>
    /// <param name="narrow">This caller's user-level rules.</param>
    internal PolicyAttachment(StoreSnapshot snapshot, NarrowZone narrow)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Narrow = narrow ?? throw new ArgumentNullException(nameof(narrow));
    }

    /// <summary>The broad zone this context reads, fixed for its lifetime.</summary>
    internal StoreSnapshot Snapshot { get; }

    /// <summary>The caller's user-level rules, read once.</summary>
    internal NarrowZone Narrow { get; }
}
