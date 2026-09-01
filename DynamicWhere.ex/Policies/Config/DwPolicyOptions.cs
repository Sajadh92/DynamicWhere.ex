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
    private string _hashSalt = string.Empty;
    private IServiceProvider? _services;

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

    /// <summary>
    /// The salt mixed into every hashed mask.
    /// </summary>
    /// <remarks>
    /// Lives here rather than on the attribute because an attribute is source code, and a salt
    /// committed to source control is not a salt. Without one, a hash of a national identifier or a
    /// postcode is reversed by hashing a dictionary of candidates and comparing.
    /// <para>
    /// It must stay stable for the life of a deployment: the same value hashes to the same text, so
    /// a caller can group and join by it without learning what it is, and rotating the salt changes
    /// every hashed value at once.
    /// </para>
    /// </remarks>
    public string HashSalt
    {
        get => _hashSalt;
        set
        {
            Guard();
            _hashSalt = value ?? throw new ArgumentNullException(nameof(value));
        }
    }

    /// <summary>
    /// Where <c>[DwMutate]</c> transformers are resolved from, or null to construct them directly.
    /// </summary>
    /// <remarks>
    /// Optional so the core package stays usable without a container. When it is absent a
    /// transformer is built through <c>Activator.CreateInstance</c>, which needs a parameterless
    /// constructor; when it is present the container decides, so a transformer can take
    /// dependencies of its own.
    /// </remarks>
    public IServiceProvider? Services
    {
        get => _services;
        set
        {
            Guard();
            _services = value;
        }
    }

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
