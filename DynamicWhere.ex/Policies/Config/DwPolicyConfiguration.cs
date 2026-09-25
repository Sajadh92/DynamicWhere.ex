using System.Reflection;
using DynamicWhere.ex.Policies.Resolution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicWhere.ex.Policies.Config;

/// <summary>
/// Reads the enforcement posture from configuration.
/// </summary>
/// <remarks>
/// Every value on <see cref="DwPolicyOptions"/> and <c>DwCaps</c> is a plain settable property, so
/// a deployment can move its posture out of source and into whatever configuration provider it
/// already runs — a file, environment variables, a key vault.
/// <para>
/// Three things cannot come from configuration, because they are objects rather than values: the
/// entity catalogue, the token vault and the service provider. They stay in code, which is what the
/// callback on <see cref="AddDwPolicies"/> is for.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// {
///   "DynamicWhere": {
///     "Policies": {
///       "Tier": "Strict",
///       "StoreFailure": "LastKnownGood",
///       "MaxSnapshotAge": "00:15:00",
///       "Caps": {
///         "MaxPageSize": 1000,
///         "MinGroupSize": 5,
///         "SchemaDepth": 2,
///         "SchemaCycleLimit": 2,
///         "MaxSchemaFields": 2000
///       }
///     }
///   }
/// }
/// </code>
/// </example>
public static class DwPolicyConfiguration
{
    /// <summary>
    /// Binds a configuration section onto a posture, refusing any key it does not recognise.
    /// </summary>
    /// <param name="options">The posture to fill in. Must not be frozen.</param>
    /// <param name="section">The section to read. An absent one leaves every default in place.</param>
    /// <returns>The same instance, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the section names a key no property answers to, and when a value is refused by
    /// the property it is written to.
    /// </exception>
    /// <remarks>
    /// <b>An unrecognised key is an error, not a shrug.</b> The binder's own default is to ignore a
    /// key nothing matches, which would let <c>MinGropSize</c> sit in a file doing nothing while the
    /// deployment believes it has set a floor. That is the fail-open shape this whole layer is built
    /// against, so binding runs with <c>ErrorOnUnknownConfiguration</c> and a typo refuses to start.
    /// <para>
    /// Every setter's own validation still applies: a cap below one, a snapshot age that is not a
    /// positive interval, and a hash salt shorter than the minimum are all refused here exactly as
    /// they are refused in code.
    /// </para>
    /// <para>
    /// The group floor's opt-out survives this unchanged, because it lives in the setter rather
    /// than in the default. Saying nothing about <c>MinGroupSize</c> leaves it unset, which reads as
    /// the safe default; writing <c>1</c> records a deliberate choice and switches the floor off.
    /// </para>
    /// <para>
    /// <c>HashSalt</c> binds like anything else, and configuration is the right channel for it —
    /// through user secrets, an environment variable or a vault. A salt committed to
    /// <c>appsettings.json</c> is not a salt, and nothing here can tell the difference, so that one
    /// is left to the reader.
    /// </para>
    /// </remarks>
    public static DwPolicyOptions Bind(this DwPolicyOptions options, IConfiguration section)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (section is null)
        {
            throw new ArgumentNullException(nameof(section));
        }

        try
        {
            section.Bind(options, binder => binder.ErrorOnUnknownConfiguration = true);
        }
        catch (TargetInvocationException refused) when (refused.InnerException is { } setter)
        {
            // The binder calls each setter by reflection, so a value the property refuses arrived wrapped:
            // a cap below one as a TargetInvocationException, the same cap inside a purpose as an
            // InvalidOperationException. Reported one way, with the property's own reason inside.
            throw new InvalidOperationException(
                $"A configured policy value was refused: {setter.Message}", setter);
        }

        return options;
    }

    /// <summary>
    /// Configures the policy layer from a configuration section, then from code.
    /// </summary>
    /// <param name="services">The container. The frozen posture is registered in it.</param>
    /// <param name="section">The section holding the posture.</param>
    /// <param name="configure">
    /// Runs after the section is bound, for everything configuration cannot carry: the entity
    /// catalogue, the token vault, the service provider.
    /// </param>
    /// <param name="providers">
    /// The rule providers. <c>AttributePolicyProvider</c> is added whether or not it appears here.
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the collection or the section is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the policy layer is already configured with a <i>different</i> posture, or when
    /// the section names a key nothing answers to.
    /// </exception>
    /// <remarks>
    /// Configuration binds first and the callback runs second, so code has the last word. That is
    /// the order that lets a deployment supply values while the application supplies objects, and
    /// it means a line somebody wrote deliberately is never quietly overwritten by a file.
    /// <para>
    /// The posture is frozen by <c>DwPolicy.Configure</c> before this returns, and registered as a
    /// singleton so anything resolving <see cref="DwPolicyOptions"/> reads the same frozen instance
    /// the query path reads. There is no second posture anywhere.
    /// </para>
    /// <para>
    /// Calling this a second time with the same posture is a no-op, and the container is given the
    /// posture already in force. That is what lets an integration suite start many
    /// <c>WebApplicationFactory</c> hosts over one composition root without a lock or an
    /// <c>IsConfigured</c> check of its own — the check and the act are one step inside the
    /// package. A second call asking for a <i>different</i> posture still throws, and what counts as
    /// different is listed on <see cref="DwPolicy.Configure"/>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddDwPolicies(
        this IServiceCollection services,
        IConfiguration section,
        Action<DwPolicyOptions>? configure = null,
        params IDwPolicyProvider[] providers)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        DwPolicyOptions options = new DwPolicyOptions().Bind(section);

        configure?.Invoke(options);

        DwPolicy.Configure(options, providers ?? Array.Empty<IDwPolicyProvider>());

        // The posture in force, which is this call's instance on the first call and the first
        // call's on any later one. Registering the instance built here instead would hand the
        // container a posture the query path does not read.
        services.AddSingleton(DwPolicy.Options);

        return services;
    }
}
