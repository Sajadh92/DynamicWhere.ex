using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.ex.Policies.Config;

/// <summary>
/// The application-wide policy configuration that <c>ApplyPolicy</c> reads from.
/// </summary>
/// <remarks>
/// The enforcement posture is not a per-call parameter, so it has to live somewhere a call site can
/// reach without being handed it — a posture a developer can forget to pass at one call site is not
/// a posture at all. This is that place: set once during startup, frozen, and read from every
/// guarded query afterwards.
/// <para>
/// Left unconfigured, the defaults enforce the attributes in the source code at the convenience
/// tier. That is deliberately a working configuration rather than an inert one: a policy layer that
/// silently does nothing until someone remembers to switch it on is worse than no policy layer,
/// because the attributes in the code read as though they are in force.
/// </para>
/// </remarks>
public static class DwPolicy
{
    private static readonly object Lock = new();

    private static DwPolicyOptions _options = CreateDefaultOptions();
    private static PolicyResolver _resolver = CreateResolver(Array.Empty<IDwPolicyProvider>());
    private static bool _configured;

    /// <summary>
    /// The enforcement posture in force. Frozen — change it through <see cref="Configure"/>.
    /// </summary>
    public static DwPolicyOptions Options => _options;

    /// <summary>True once <see cref="Configure"/> has been called.</summary>
    public static bool IsConfigured => _configured;

    /// <summary>The resolver every guarded query consults.</summary>
    internal static PolicyResolver Resolver => _resolver;

    /// <summary>
    /// Sets the posture and the runtime policy sources, once, during startup.
    /// </summary>
    /// <param name="options">The posture. Frozen by this call.</param>
    /// <param name="providers">
    /// Runtime policy sources, in any order. The attribute provider is always present and does not
    /// need to be listed.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when called more than once.</exception>
    /// <remarks>
    /// Refused after the first call. The posture is read by every request thread without
    /// synchronization, and a tier that can change while requests are in flight is one that can be
    /// relaxed by a code path nobody expected to be security-relevant.
    /// <para>
    /// <see cref="AttributePolicyProvider"/> is added whether or not it appears in
    /// <paramref name="providers"/>. Attributes are the sealed level, and a configuration that
    /// omitted them would let a runtime store grant access the source code refuses — which is the
    /// one thing the level ordering exists to prevent.
    /// </para>
    /// </remarks>
    public static void Configure(DwPolicyOptions options, params IDwPolicyProvider[] providers)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        lock (Lock)
        {
            if (_configured)
            {
                throw new InvalidOperationException(
                    "Policy is already configured. The enforcement posture is read by every request " +
                    "thread and cannot change once the application is serving.");
            }

            options.Freeze();

            _options = options;
            _resolver = CreateResolver(providers ?? Array.Empty<IDwPolicyProvider>());
            _configured = true;
        }
    }

    /// <summary>
    /// Builds a resolver over the attribute provider plus whatever else was supplied.
    /// </summary>
    private static PolicyResolver CreateResolver(IReadOnlyList<IDwPolicyProvider> providers)
    {
        List<IDwPolicyProvider> all = new() { new AttributePolicyProvider() };

        foreach (IDwPolicyProvider provider in providers)
        {
            if (provider is not null and not AttributePolicyProvider)
            {
                all.Add(provider);
            }
        }

        return new PolicyResolver(all);
    }

    /// <summary>
    /// Builds the default posture, already frozen so it cannot be edited at request time.
    /// </summary>
    private static DwPolicyOptions CreateDefaultOptions()
    {
        DwPolicyOptions options = new();

        options.Freeze();

        return options;
    }
}
