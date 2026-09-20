using DynamicWhere.ex.Policies.Audit;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Discovery;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Validation;

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
    private static IReadOnlyList<StorePolicyProvider> _stores = Array.Empty<StorePolicyProvider>();

    // The kinds of policy source the configured call supplied, in order. Read only when a second
    // call asks whether it is asking for the same posture.
    private static Type[] _providerTypes = Array.Empty<Type>();

    private static bool _configured;

    /// <summary>
    /// The enforcement posture in force. Frozen — change it through <see cref="Configure"/>.
    /// </summary>
    public static DwPolicyOptions Options => _options;

    /// <summary>True once <see cref="Configure"/> has been called.</summary>
    public static bool IsConfigured => _configured;

    /// <summary>The resolver every guarded query consults.</summary>
    public static PolicyResolver Resolver => _resolver;

    /// <summary>
    /// The store-backed providers configured, in the order they were supplied.
    /// </summary>
    /// <remarks>
    /// Exposed for a health endpoint, which reports each one's version, age and last error. There is
    /// nothing to enforce with here: a provider hands out fragments and the resolver decides, so
    /// reading this cannot change what any query is permitted to do.
    /// </remarks>
    public static IReadOnlyList<StorePolicyProvider> StoreProviders => _stores;

    /// <summary>
    /// Sets the posture and the runtime policy sources, during startup.
    /// </summary>
    /// <param name="options">The posture. Frozen by this call.</param>
    /// <param name="providers">
    /// Runtime policy sources, in any order. The attribute provider is always present and does not
    /// need to be listed.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when called again with a posture that differs from the one in force.
    /// </exception>
    /// <remarks>
    /// The first call decides. A later call carrying the same posture is a no-op, and one carrying a
    /// different posture is refused: the posture is read by every request thread without
    /// synchronization, and a tier that can change while requests are in flight is one that can be
    /// relaxed by a code path nobody expected to be security-relevant.
    /// <para>
    /// The comparison covers everything that decides what a query is permitted to do: the tier, the
    /// dry-run and trace flags, refusal auditing, the hash salt, the store-failure mode, both
    /// intervals, every cap, the exposed entity catalogue, and the provider types supplied. It does
    /// not cover the objects a host builds for itself — the token vault, the service provider and
    /// the provider instances — because a second host builds its own and comparing them by
    /// reference would make every second call a refusal. Those stay as the first call left them,
    /// which is what makes a second host safe to start and what makes it the first host's vault and
    /// container that the layer keeps using. Start a second host only where that is what you want:
    /// an integration suite running many <c>WebApplicationFactory</c> hosts over one composition
    /// root, which is the case this exists for.
    /// </para>
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
                if (!SamePosture(_options, options, providers))
                {
                    throw new InvalidOperationException(
                        "Policy is already configured with a different posture. The enforcement " +
                        "posture is read by every request thread and cannot change once the " +
                        "application is serving. Configuring the same posture again is allowed, so " +
                        "a second host over one composition root starts without a check of its own.");
                }

                // Frozen so a caller holding this instance cannot go on setting values that no
                // longer decide anything. The posture in force is unchanged and stays the frozen
                // one the first call installed.
                options.Freeze();

                return;
            }

            options.Freeze();

            IDwPolicyProvider[] supplied = providers ?? Array.Empty<IDwPolicyProvider>();

            _options = options;
            _resolver = CreateResolver(supplied);
            _stores = supplied.OfType<StorePolicyProvider>().ToList();
            _providerTypes = ProviderTypes(supplied);
            _configured = true;
        }
    }

    /// <summary>
    /// True when a second configuration asks for exactly what is already in force.
    /// </summary>
    /// <remarks>
    /// Every value that decides what a query may do is compared, so a posture that differs in any of
    /// them is refused rather than quietly ignored. The three things not compared are the objects a
    /// host constructs for itself — the token vault, the service provider and the provider instances
    /// — since a second host builds its own and no two are ever the same reference. Provider
    /// <i>types</i> are compared, in order, so a host that adds or drops a source is still refused.
    /// </remarks>
    private static bool SamePosture(DwPolicyOptions inForce, DwPolicyOptions asked, IDwPolicyProvider[]? providers)
    {
        if (ReferenceEquals(inForce, asked))
        {
            // The same instance carries the same values by definition, and says nothing about the
            // sources. Handing the posture in force back with a store provider beside it is a call
            // asking for that source, and answering "same posture" would drop it: the resolver is
            // never rebuilt, the source is never consulted, and every rule in it — a denial
            // included — quietly does not apply.
            return SameProviders(providers);
        }

        if (inForce.Tier != asked.Tier
            || inForce.DryRun != asked.DryRun
            || inForce.TraceInResult != asked.TraceInResult
            || inForce.AuditRefusals != asked.AuditRefusals
            || !string.Equals(inForce.HashSalt, asked.HashSalt, StringComparison.Ordinal)
            || inForce.StoreFailure != asked.StoreFailure
            || inForce.MaxSnapshotAge != asked.MaxSnapshotAge
            || inForce.RefreshInterval != asked.RefreshInterval)
        {
            return false;
        }

        // IncludeTraceInResult is compared by the value that applies, not by whether somebody wrote
        // it down: it defaults to the tier's own answer, and the tiers are equal by the line above,
        // so a host writing that answer out enforces exactly what a host leaving it null does. The
        // same rule as MinGroupSize below.
        if (!SameCaps(inForce.Caps, asked.Caps) || !SameCatalog(inForce.Entities, asked.Entities))
        {
            return false;
        }

        return SameProviders(providers);
    }

    /// <summary>
    /// True when two cap sets hold the same numbers.
    /// </summary>
    /// <remarks>
    /// The floor that applies, not whether somebody wrote it down. <c>IsMinGroupSizeSet</c> tells a
    /// deliberate opt-out from a deployment that never heard of the control, and nothing in
    /// enforcement reads it — a host binding the documented appsettings sample, which writes
    /// <c>"MinGroupSize": 5</c>, enforces exactly what a host on the defaults does, and refusing the
    /// second one would refuse the case this feature exists for.
    /// </remarks>
    private static bool SameCaps(DwCaps inForce, DwCaps asked) =>
        inForce.MaxPageSize == asked.MaxPageSize
        && inForce.DefaultPageSize == asked.DefaultPageSize
        && inForce.MaxConditions == asked.MaxConditions
        && inForce.MaxConditionDepth == asked.MaxConditionDepth
        && inForce.MaxConditionSets == asked.MaxConditionSets
        && inForce.MaxConditionValues == asked.MaxConditionValues
        && inForce.MaxAggregates == asked.MaxAggregates
        && inForce.MaxOrderFields == asked.MaxOrderFields
        && inForce.MaxNavigationDepth == asked.MaxNavigationDepth
        && inForce.MaxQueryCost == asked.MaxQueryCost
        && inForce.DefaultFieldCost == asked.DefaultFieldCost
        && inForce.MaxAuditEvents == asked.MaxAuditEvents
        && inForce.MinGroupSize == asked.MinGroupSize
        && inForce.SchemaDepth == asked.SchemaDepth
        && inForce.SchemaCycleLimit == asked.SchemaCycleLimit
        && inForce.MaxSchemaFields == asked.MaxSchemaFields;

    /// <summary>True when both catalogues expose the same types and answer to the same names.</summary>
    /// <remarks>
    /// Every name, not only the last one each type was exposed under. A type exposed twice keeps
    /// both names resolvable while <c>Entities</c> reports one, so two catalogues can report the
    /// same pairs and still answer differently to an administrative request.
    /// </remarks>
    private static bool SameCatalog(DwEntityCatalog inForce, DwEntityCatalog asked) => inForce.SameAs(asked);

    /// <summary>
    /// True when the second call supplies the same kinds of policy source, in the same order.
    /// </summary>
    /// <remarks>
    /// Types, not instances. A store provider reads its rules from wherever it was built to read
    /// them, and nothing here can compare two stores; what it can refuse is a second call that adds
    /// a source, drops one, or reorders them, which is the difference that changes what the resolver
    /// decides. The attribute provider is left out of both sides, since it is added either way.
    /// </remarks>
    private static bool SameProviders(IDwPolicyProvider[]? asked)
    {
        Type[] wanted = ProviderTypes(asked);

        if (_providerTypes.Length != wanted.Length)
        {
            return false;
        }

        for (int i = 0; i < wanted.Length; i++)
        {
            if (_providerTypes[i] != wanted[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The kinds of policy source a call supplies, in order, as the resolver takes them.</summary>
    private static Type[] ProviderTypes(IDwPolicyProvider[]? providers) =>
        (providers ?? Array.Empty<IDwPolicyProvider>())
        .Where(provider => provider is not null and not AttributePolicyProvider)
        .Select(provider => provider.GetType())
        .ToArray();

    /// <summary>
    /// Prepares a context for use, once per request, before its first query.
    /// </summary>
    /// <param name="context">The caller's context.</param>
    /// <param name="ct">Cancels the loads.</param>
    /// <returns>The same context, prepared, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context"/> is null.</exception>
    /// <remarks>
    /// Every configured store provider pins its current snapshot to the context and fetches this
    /// caller's user-level rules. Everything downstream then resolves synchronously against what was
    /// read here, and every query the context makes sees one coherent version of the policy.
    /// <para>
    /// A context that skips this is refused with <c>PolicyContextNotPrepared</c>: by
    /// <c>ApplyPolicy(context)</c>, store or no store, and by any store provider that sees it. It could
    /// only be served from the broad zone, where a denial written for one user does not appear — and a
    /// denial that silently does not apply is the failure this whole layer exists to prevent.
    /// </para>
    /// <para>
    /// Cheap when no store is configured: with nothing but attributes in force there is nothing to pin
    /// and nothing is read, but the context is still marked prepared.
    /// </para>
    /// <para>
    /// A user-level rule the store holds but cannot read throws here, for every request of the caller it
    /// names, whatever <c>StoreFailure</c> says: it is read only for that caller, never by the load that
    /// startup and refresh run.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// DwPolicyContext ctx = await DwPolicy.PrepareAsync(
    ///     new DwPolicyContext()
    ///         .WithSubject(DwSubjectKind.Tenant, tenantId)
    ///         .WithSubject(DwSubjectKind.User, userId));
    /// </code>
    /// </example>
    public static async ValueTask<DwPolicyContext> PrepareAsync(
        DwPolicyContext context, CancellationToken ct = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        IReadOnlyList<StorePolicyProvider> stores = _stores;

        for (int i = 0; i < stores.Count; i++)
        {
            await stores[i].PrepareAsync(context, ct).ConfigureAwait(false);
        }

        // Recorded even when no store pinned anything. With attributes alone there is nothing to
        // read, but the guarded surface refuses a context that never came through here, so that a
        // deployment behaves the same before and after it gains a store.
        context.MarkPrepared();

        return context;
    }

    /// <summary>
    /// Writes everything a context recorded to a sink, and empties its buffer.
    /// </summary>
    /// <param name="context">The caller's context, after its queries have run.</param>
    /// <param name="sink">Where the events go.</param>
    /// <param name="ct">Cancels the writes.</param>
    /// <returns>How many events were written.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="context"/> or <paramref name="sink"/> is null.
    /// </exception>
    /// <remarks>
    /// Call it once per request, after the response. The query path is synchronous and a sink is
    /// not, so events accumulate on the context while queries run and are written here — the only
    /// arrangement that neither loses records to a fire-and-forget call nor blocks a request thread
    /// on I/O.
    /// <para>
    /// A sink that throws part way leaves everything it never saw on the buffer and the failure is
    /// rethrown, so a host can retry or log without the events having been silently consumed. It
    /// does not retry on the caller's behalf: how many times to try writing an audit record, and
    /// how long to hold a request open doing it, are the host's decisions.
    /// </para>
    /// <para>
    /// The sink is passed rather than read from configuration so that a scoped one works — an audit
    /// sink writing through a per-request database context is the ordinary case, and a singleton
    /// held in options could not be one.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// await DwPolicy.DrainAuditAsync(ctx, sink);
    /// </code>
    /// </example>
    public static async ValueTask<int> DrainAuditAsync(
        DwPolicyContext context, IDwAuditSink sink, CancellationToken ct = default)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (sink is null)
        {
            throw new ArgumentNullException(nameof(sink));
        }

        DwAuditEvent[] events = context.TakeAuditEvents();

        for (int i = 0; i < events.Length; i++)
        {
            try
            {
                await sink.WriteAsync(events[i], ct).ConfigureAwait(false);
            }
            catch
            {
                // Everything from the one that failed onward, in order. The events already written
                // are not returned: a sink that accepted one and is asked for it again would record
                // the same access twice, and a duplicated audit entry is its own kind of wrong
                // answer.
                context.ReturnAuditEvents(events[i..]);

                throw;
            }
        }

        return events.Length;
    }

    /// <summary>
    /// Checks a policy model and throws when it cannot work, listing everything wrong at once.
    /// </summary>
    /// <param name="types">The entity and DTO types to inspect.</param>
    /// <returns>
    /// The report, so a host that would rather log the warnings than ignore them can read them.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the model contains anything that will fail a query.
    /// </exception>
    /// <remarks>
    /// Call it during startup, beside <see cref="Configure"/>. Attribute misuse then surfaces at a
    /// deployment rather than on a caller's request at three in the morning, which is the whole
    /// reason it exists.
    /// <para>
    /// Explicit about which types it inspects rather than scanning loaded assemblies: an application
    /// knows its entity types, and a scan would wander into every referenced package looking for
    /// attributes that are not there.
    /// </para>
    /// </remarks>
    public static PolicyModelReport ValidateModel(params Type[] types) =>
        ValidateModel(null, types);

    /// <summary>
    /// Checks a policy model against the posture it will run under, and throws when it cannot work.
    /// </summary>
    /// <param name="options">
    /// The options about to be passed to <see cref="Configure"/>, or null to check the attributes
    /// alone.
    /// </param>
    /// <param name="types">The entity and DTO types to inspect.</param>
    /// <returns>The report, so a host can log the warnings rather than ignore them.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the model contains anything that will fail a query.
    /// </exception>
    /// <remarks>
    /// Takes the options rather than reading <see cref="Options"/>, because a host validates before
    /// it configures — reading the static here would check the model against the default posture and
    /// report a salt that is one line from being set.
    /// </remarks>
    public static PolicyModelReport ValidateModel(DwPolicyOptions? options, params Type[] types)
    {
        PolicyModelReport report =
            PolicyModelValidator.Inspect(types ?? Array.Empty<Type>(), options);

        if (!report.IsValid)
        {
            throw new InvalidOperationException(
                "The policy model cannot work as declared:" + Environment.NewLine
                + string.Join(Environment.NewLine, report.Errors.Select(e => "  - " + e)));
        }

        return report;
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
