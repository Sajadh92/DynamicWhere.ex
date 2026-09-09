using DynamicWhere.ex.Policies.Discovery;
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
    private StoreFailureMode _storeFailure = StoreFailureMode.LastKnownGood;
    private TimeSpan _maxSnapshotAge = TimeSpan.FromMinutes(15);
    private TimeSpan _refreshInterval = TimeSpan.FromSeconds(30);

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
    /// The types an administrative surface may be asked about, and the names it may ask by.
    /// </summary>
    /// <remarks>
    /// Empty by default, which means nothing can be described. That is the safe default rather than
    /// an inconvenient one: an endpoint that resolved a name straight to a type would let whoever
    /// reaches it enumerate every type the process has loaded.
    /// </remarks>
    public DwEntityCatalog Entities { get; } = new();

    /// <summary>
    /// The salt mixed into every hashed mask.
    /// </summary>
    /// <remarks>
    /// Lives here rather than on the attribute because an attribute is source code, and a salt
    /// committed to source control is not a salt. Without one, a hash of a national identifier or a
    /// postcode is reversed by hashing a dictionary of candidates and comparing.
    /// <para>
    /// Required, not advisory. A query masking a field to a hash is refused with
    /// <c>MissingHashSalt</c> while this is blank, and <c>DwPolicy.ValidateModel(options, types)</c>
    /// reports it at startup. The default is empty and there is deliberately no generated one: a
    /// salt that changed per process would change every hashed value with it, and the whole point of
    /// the strategy is that the same value hashes to the same text.
    /// </para>
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

    /// <summary>
    /// What happens when the policy store cannot be reached after startup.
    /// </summary>
    /// <remarks>
    /// Says nothing about the first load, which throws under every mode. An application that cannot
    /// read its policy at startup does not know whether it is enforcing anything.
    /// </remarks>
    public StoreFailureMode StoreFailure
    {
        get => _storeFailure;
        set
        {
            Guard();
            _storeFailure = value;
        }
    }

    /// <summary>
    /// How old the last successful store load may be before every mode escalates to
    /// <see cref="StoreFailureMode.FailClosed"/>.
    /// </summary>
    /// <remarks>
    /// There is deliberately no value that disables this. The ceiling is what stops "the store died
    /// six hours ago" from silently becoming "we have been honouring revoked grants all afternoon",
    /// and a mode that can be told to trust a snapshot forever is that failure with a setting in
    /// front of it.
    /// <para>
    /// Zero and negative values are both refused. Zero makes every snapshot instantly stale, which
    /// refuses every query; a negative value makes the comparison never fire, which is the
    /// dangerous half of the same mistake. Neither is a posture anyone means to configure.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the value is zero or negative.
    /// </exception>
    public TimeSpan MaxSnapshotAge
    {
        get => _maxSnapshotAge;
        set
        {
            Guard();
            _maxSnapshotAge = Positive(value, nameof(MaxSnapshotAge));
        }
    }

    /// <summary>
    /// How often a store with no change notification of its own is polled for a new version.
    /// </summary>
    /// <remarks>
    /// Ignored by a store that supplies a watch. The poll is a read of a single version value, which
    /// is cheap enough to run indefinitely.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when the value is zero or negative.
    /// </exception>
    public TimeSpan RefreshInterval
    {
        get => _refreshInterval;
        set
        {
            Guard();
            _refreshInterval = Positive(value, nameof(RefreshInterval));
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
        Entities.Freeze();
        IsFrozen = true;
    }

    /// <summary>
    /// Refuses a duration that is not a real interval.
    /// </summary>
    private static TimeSpan Positive(TimeSpan value, string property) =>
        value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(
                property, value, $"{property} must be a positive interval.");

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
