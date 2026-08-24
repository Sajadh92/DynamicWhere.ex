using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Config;

/// <summary>
/// The enforcement posture, fixed once at startup and read from this single instance at every
/// decision point.
/// </summary>
/// <remarks>
/// The tier is deliberately not a per-call parameter. A posture a developer can forget to pass at
/// one call site is not a posture at all, so it is set once, frozen, and consulted from here.
/// </remarks>
public sealed class DwPolicyOptions
{
    private DwTier _tier = DwTier.Convenience;
    private bool _dryRun;

    /// <summary>The enforcement tier.</summary>
    public DwTier Tier
    {
        get => _tier;
        set
        {
            Guard();
            _tier = value;
        }
    }

    /// <summary>
    /// When true, no decision throws or drops anything anywhere in the application; every decision
    /// is still recorded. Individual callers can opt into dry-run without this by setting it on
    /// their own context, which is how a single canary subject is rolled out.
    /// </summary>
    public bool DryRun
    {
        get => _dryRun;
        set
        {
            Guard();
            _dryRun = value;
        }
    }

    /// <summary>Numeric limits applied to every guarded query.</summary>
    public DwCaps Caps { get; } = new();

    /// <summary>True once <see cref="Freeze"/> has been called.</summary>
    public bool IsFrozen { get; private set; }

    /// <summary>
    /// Prevents any further change. Calling it more than once is harmless.
    /// </summary>
    /// <remarks>
    /// Not thread-safe. Call it during startup, before this instance is shared with request
    /// threads. The caps are frozen before <see cref="IsFrozen"/> is set so that a caller racing
    /// the freeze is refused rather than allowed through: the remaining window can only produce a
    /// spurious rejection, never a write that slips past.
    /// </remarks>
    public void Freeze()
    {
        Caps.Freeze();
        IsFrozen = true;
    }

    /// <summary>
    /// Rejects a change made after startup.
    /// </summary>
    private void Guard()
    {
        if (IsFrozen)
        {
            throw new InvalidOperationException("Policy options cannot be changed after startup.");
        }
    }
}
