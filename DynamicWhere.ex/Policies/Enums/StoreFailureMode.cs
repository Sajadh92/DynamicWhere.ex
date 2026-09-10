namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// What the library does when a policy store cannot be reached after the application has started.
/// </summary>
/// <remarks>
/// A startup load failure is not covered by any of these. Booting into an unknown policy state
/// means the process cannot know whether it is enforcing anything, so the first load throws
/// regardless of this setting and there is no provider to configure.
/// <para>
/// Every mode is bounded by <c>DwPolicyOptions.MaxSnapshotAge</c>. Without that ceiling,
/// <see cref="LastKnownGood"/> turns "the store died six hours ago" into "we have been honouring
/// revoked grants all afternoon".
/// </para>
/// </remarks>
public enum StoreFailureMode
{
    /// <summary>
    /// Keep serving the last snapshot that loaded, until it grows older than the ceiling.
    /// </summary>
    /// <remarks>
    /// The default, and deliberately so rather than by falling out of being zero: a transient
    /// network fault should not take an application down, and the ceiling bounds how long a stale
    /// answer can be given.
    /// </remarks>
    LastKnownGood = 0,

    /// <summary>
    /// Refuse every guarded query until a load succeeds.
    /// </summary>
    /// <remarks>
    /// Implemented by throwing rather than by emitting a blanket denial. A denial competes in the
    /// ordinary election, where a sealed <c>[DwOperators]</c> allowance outranks it, so every field
    /// carrying an operator restriction would stay filterable in exactly the state this mode exists
    /// to refuse. Nothing outranks an exception.
    /// </remarks>
    FailClosed = 1,

    /// <summary>
    /// Drop the store's rules and enforce the compile-time attributes alone.
    /// </summary>
    /// <remarks>
    /// For a deployment whose attributes carry the whole policy and whose store only ever relaxes
    /// them. It is not a safe default: a store holding a denial the attributes do not is silently
    /// ignored under this mode.
    /// </remarks>
    StaticOnly = 2
}
