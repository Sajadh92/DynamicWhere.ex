using System.Security.Claims;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Discovery;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DynamicWhere.ex.Policies.AspNetCore;

/// <summary>
/// Design section 5.7's administrative surface, mounted on any ASP.NET Core host.
/// </summary>
/// <remarks>
/// Every endpoint is a thin translation. What each one answers with is computed in the core package
/// — the schema by <c>PolicySchemaBuilder</c>, the explanation by <c>PolicyResolver</c>, the
/// simulation by <c>PolicySimulator</c> — so a host without ASP.NET Core has all of it, and so the
/// answers cannot drift from what enforcement actually does.
/// </remarks>
public static class DwPolicyEndpoints
{
    /// <summary>
    /// Maps the policy administration endpoints.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="configure">Sets the route prefix and the authorization policies.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no authorization posture was chosen. There is deliberately no default.
    /// </exception>
    /// <example>
    /// <code>
    /// app.MapDwPolicyAdmin(o =>
    /// {
    ///     o.ReadPolicy  = "PolicyReader";
    ///     o.WritePolicy = "PolicyAdmin";
    /// });
    /// </code>
    /// </example>
    public static IEndpointRouteBuilder MapDwPolicyAdmin(
        this IEndpointRouteBuilder endpoints, Action<DwPolicyAdminOptions>? configure = null)
    {
        if (endpoints is null)
        {
            throw new ArgumentNullException(nameof(endpoints));
        }

        DwPolicyAdminOptions options = new();

        configure?.Invoke(options);

        // Before a single route is added. A surface that mounts and then refuses at request time
        // fails on the first call rather than the deployment — and for an endpoint nobody is
        // supposed to call, the first call is exactly the one that must not be the discovery.
        options.Validate();

        // Paths are concatenated rather than grouped: MapGroup arrived in .NET 7 and this package
        // targets the same .NET 6 floor as the other three, so a host on the floor would not be able
        // to use it at all.
        string prefix = options.RoutePrefix.TrimEnd('/');

        RouteHandlerBuilder Read(RouteHandlerBuilder route) =>
            options.AllowAnonymousAccess ? route : route.RequireAuthorization(options.ReadPolicy!);

        RouteHandlerBuilder Write(RouteHandlerBuilder route) =>
            options.AllowAnonymousAccess ? route : route.RequireAuthorization(options.WritePolicy!);

        Read(endpoints.MapGet($"{prefix}/schema/{{entity}}", (string entity, HttpContext http) =>
            Schema(entity, http, options)));

        Read(endpoints.MapGet($"{prefix}/rules", (HttpContext http, string? subject) =>
            Rules(http, subject)));

        Write(endpoints.MapPost($"{prefix}/rules", (HttpContext http, RuleRequest rule) =>
            Upsert(http, rule)));

        Write(endpoints.MapDelete($"{prefix}/rules/{{id:guid}}", (HttpContext http, Guid id) =>
            Delete(http, id)));

        Read(endpoints.MapPost($"{prefix}/explain", (HttpContext http, ExplainRequest request) =>
            Explain(http, request, options)));

        Read(endpoints.MapPost($"{prefix}/simulate", (HttpContext http, SimulateRequest request) =>
            Simulate(http, request, options)));

        Read(endpoints.MapGet($"{prefix}/health", (HttpContext http) => Health(http)));

        return endpoints;
    }

    // ------------------------------------------------------------------------------ schema

    private static async Task<IResult> Schema(
        string entity, HttpContext http, DwPolicyAdminOptions options)
    {
        Type? type = DwPolicy.Options.Entities.Resolve(entity);

        // The same answer whether the entity does not exist or merely was not exposed. Telling
        // those apart is precisely what an enumeration attempt is looking for.
        if (type is null)
        {
            return Results.NotFound(new { error = $"No entity named '{entity}'." });
        }

        DwPolicyContext context = await CallerAsync(http, options).ConfigureAwait(false);

        return Results.Ok(PolicySchemaBuilder.Describe(
            type, DwPolicy.Options.Entities, context, DwPolicy.Options, DwPolicy.Resolver));
    }

    // ------------------------------------------------------------------------------- rules

    private static async Task<IResult> Rules(HttpContext http, string? subject)
    {
        IDwPolicyStore? store = http.RequestServices.GetService<IDwPolicyStore>();

        if (store is null)
        {
            return NoStore();
        }

        (DwSubjectKind Kind, string? Key)? filter =
            string.IsNullOrWhiteSpace(subject) ? null : ParseSubject(subject!);

        // Both zones are indexed by entity type and neither hands out everything it holds, so the
        // listing walks the exposed types. That is the stricter reading as well as the only
        // available one: a rule about a type the host never exposed is not this surface's to show.
        Type[] entities = DwPolicy.Options.Entities.ToArray();

        List<PolicyRule> rules = new();

        // A user subject lives in the narrow zone rather than the snapshot — section 5.3 splits it
        // out so it is fetched per caller — so it is read from where it actually is instead of
        // being filtered out of a snapshot that never held it.
        if (filter is { Kind: DwSubjectKind.User })
        {
            NarrowZone narrow = await store
                .LoadNarrowAsync(new[] { filter.Value.Key ?? string.Empty }, http.RequestAborted)
                .ConfigureAwait(false);

            foreach (Type entity in entities)
            {
                rules.AddRange(narrow.For(entity));
            }
        }
        else
        {
            StoreSnapshot snapshot = await store.LoadAsync(http.RequestAborted).ConfigureAwait(false);

            foreach (Type entity in entities)
            {
                foreach (PolicyRule rule in snapshot.For(entity))
                {
                    if (filter is null || Matches(rule, filter.Value.Kind, filter.Value.Key))
                    {
                        rules.Add(rule);
                    }
                }
            }
        }

        return Results.Ok(rules.Select(Describe).ToList());
    }

    private static async Task<IResult> Upsert(HttpContext http, RuleRequest rule)
    {
        IDwPolicyWritableStore? store = http.RequestServices.GetService<IDwPolicyWritableStore>();

        if (store is null)
        {
            return NoStore(writable: true);
        }

        if (rule is null)
        {
            return Results.BadRequest(new { error = "A rule is required." });
        }

        PolicyRule built;

        // Every refusal PolicyRule makes at its boundary — an unknown subject kind, a short entity
        // name, a validity window that closes before it opens, an effect with no feature to apply it
        // to — surfaces here as a bad request rather than a stack trace.
        try
        {
            built = rule.ToRule(http.User);
        }
        catch (Exception malformed)
            when (malformed is ArgumentException or FormatException or OverflowException)
        {
            return Results.BadRequest(new { error = malformed.Message });
        }

        // The sealed-field refusal every store already shares. It is repeated here so the operator
        // is told immediately, and it is not the guarantee: a sealed attribute outranks every
        // dynamic level whatever any store did or did not check.
        try
        {
            SealedFields.Refuse(built, DwPolicy.Options.Entities.Resolve, nameof(rule));
        }
        catch (ArgumentException refused)
        {
            return Results.BadRequest(new { error = refused.Message });
        }

        PolicyRule stored = await store.UpsertAsync(built, http.RequestAborted).ConfigureAwait(false);

        return Results.Ok(Describe(stored));
    }

    private static async Task<IResult> Delete(HttpContext http, Guid id)
    {
        IDwPolicyWritableStore? store = http.RequestServices.GetService<IDwPolicyWritableStore>();

        if (store is null)
        {
            return NoStore(writable: true);
        }

        await store.DeleteAsync(id, http.RequestAborted).ConfigureAwait(false);

        return Results.NoContent();
    }

    // ----------------------------------------------------------------------------- explain

    private static async Task<IResult> Explain(
        HttpContext http, ExplainRequest request, DwPolicyAdminOptions options)
    {
        Type? type = DwPolicy.Options.Entities.Resolve(request?.Entity);

        if (type is null)
        {
            return Results.NotFound(new { error = $"No entity named '{request?.Entity}'." });
        }

        DwPolicyContext context = await CallerAsync(http, options).ConfigureAwait(false);

        PolicySchema schema = PolicySchemaBuilder.Describe(
            type, DwPolicy.Options.Entities, context, DwPolicy.Options, DwPolicy.Resolver);

        if (!string.IsNullOrWhiteSpace(request!.Field))
        {
            // Resolved against the entity's real fields rather than passed straight through. A path
            // nothing defines carries no fragment, and a field with no fragment is permitted — so a
            // typo would be explained as "allowed for everything", which is a confident answer about
            // a field that does not exist. Aliases are accepted here for the same reason they are
            // accepted in a filter: it is the name the caller was told to use.
            PolicySchemaField? named = schema.Fields.FirstOrDefault(field =>
                string.Equals(field.Path, request.Field, StringComparison.OrdinalIgnoreCase)
                || string.Equals(field.Name, request.Field, StringComparison.OrdinalIgnoreCase));

            if (named is null)
            {
                return Results.NotFound(new
                {
                    error = $"'{request.Field}' is not a field of '{request.Entity}' that this "
                        + "caller can use."
                });
            }

            return Results.Ok(new[] { Describe(DwPolicy.Resolver.Explain(type, named.Path, context)) });
        }

        return Results.Ok(schema.Fields
            .Select(field => Describe(DwPolicy.Resolver.Explain(type, field.Path, context)))
            .ToList());
    }

    // ---------------------------------------------------------------------------- simulate

    private static async Task<IResult> Simulate(
        HttpContext http, SimulateRequest request, DwPolicyAdminOptions options)
    {
        Type? type = DwPolicy.Options.Entities.Resolve(request?.Entity);

        if (type is null)
        {
            return Results.NotFound(new { error = $"No entity named '{request?.Entity}'." });
        }

        if (request!.Filter is null)
        {
            return Results.BadRequest(new { error = "A filter is required." });
        }

        DwPolicyContext context = await CallerAsync(http, options).ConfigureAwait(false);

        PolicySimulation<Filter> simulation;

        // A policy refusal is an answer and comes back inside the result. A filter the pipeline
        // itself cannot parse is not — it is a malformed request, and without this it would leave
        // as a five-hundred, which reads to an operator as the endpoint being broken rather than
        // their filter.
        try
        {
            simulation = PolicySimulator.Simulate(
                type, request.Filter, context, DwPolicy.Options, DwPolicy.Resolver);
        }
        catch (LogicException malformed)
        {
            return Results.BadRequest(new { error = malformed.Message });
        }

        return Results.Ok(new
        {
            wouldRun = simulation.WouldRun,
            filter = simulation.Clause,
            refusal = simulation.Refusal is null
                ? null
                : new
                {
                    code = simulation.Refusal.ErrorCode.ToString(),
                    field = simulation.Refusal.FieldPath,
                    feature = simulation.Refusal.Feature.ToString(),
                    reason = simulation.Refusal.SourceOrigin
                },
            trace = simulation.Trace.Decisions
                .Select(d => new
                {
                    field = d.FieldPath,
                    feature = d.Feature.ToString(),
                    action = d.Action.ToString(),
                    reason = d.Reason
                })
                .ToList()
        });
    }

    // ------------------------------------------------------------------------------ health

    private static IResult Health(HttpContext http)
    {
        List<object> stores = new();

        foreach (StorePolicyProvider provider in DwPolicy.StoreProviders)
        {
            stores.Add(new
            {
                version = provider.Version,
                loadedAt = provider.LoadedAt,
                ageSeconds = Math.Round(provider.Age.TotalSeconds, 3),
                degraded = provider.IsDegraded,

                // The message, never the exception. A stack trace names assemblies, file paths and
                // connection details, and this endpoint is read by whoever can reach it.
                lastError = provider.LastError?.Message
            });
        }

        bool healthy = stores.Count == 0 || DwPolicy.StoreProviders.All(p => !p.IsDegraded);

        object body = new
        {
            healthy,
            configured = DwPolicy.IsConfigured,
            tier = DwPolicy.Options.Tier.ToString(),
            dryRun = DwPolicy.Options.DryRun,
            maxSnapshotAge = DwPolicy.Options.MaxSnapshotAge,
            stores
        };

        return healthy ? Results.Ok(body) : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    // ------------------------------------------------------------------------------ shared

    /// <summary>
    /// Builds the context an explanation or a simulation is answered for.
    /// </summary>
    /// <remarks>
    /// The operator's own principal, not an impersonated one. Answering "what would <em>this other
    /// caller</em> see" from a subject list the request supplies would let whoever reaches the
    /// endpoint enumerate the policy for every subject in the system, which is the map an attacker
    /// wants most.
    /// </remarks>
    private static ValueTask<DwPolicyContext> CallerAsync(
        HttpContext http, DwPolicyAdminOptions options) =>
        http.GetPolicyContextAsync(options.Claims);

    /// <summary>
    /// True when a rule targets the requested subject.
    /// </summary>
    /// <remarks>
    /// Keys are compared through the same normalizer both stores write with, so a filter for
    /// <c>Role:manager</c> finds a rule written as <c>Role:Manager</c> — the two are the same rule,
    /// and a listing that disagreed with the matcher would tell an operator a control does not exist
    /// while it is being enforced.
    /// </remarks>
    private static bool Matches(PolicyRule rule, DwSubjectKind kind, string? key) =>
        rule.SubjectKind == kind
        && (key is null
            || string.Equals(
                PolicyRule.NormalizeSubjectKey(rule.SubjectKey),
                PolicyRule.NormalizeSubjectKey(key),
                StringComparison.Ordinal));

    /// <summary>Splits a <c>Kind:Key</c> subject filter.</summary>
    private static (DwSubjectKind Kind, string? Key) ParseSubject(string subject)
    {
        int separator = subject.IndexOf(':');

        string kind = separator < 0 ? subject : subject[..separator];
        string? key = separator < 0 ? null : subject[(separator + 1)..];

        return (Enum.TryParse(kind, ignoreCase: true, out DwSubjectKind parsed)
                ? parsed
                : DwSubjectKind.Custom,
            string.IsNullOrWhiteSpace(key) ? null : key);
    }

    /// <summary>
    /// Renders a rule for an operator.
    /// </summary>
    /// <remarks>
    /// <c>SubjectKey</c> is reported as it was typed, not as it is matched. Both stores normalize
    /// the key to lower case so a rule applies identically on a case-sensitive PostgreSQL and a
    /// case-insensitive SQL Server, and showing an operator a lowercased copy of what they wrote
    /// would be confusing and is not what is stored.
    /// </remarks>
    private static object Describe(PolicyRule rule) => new
    {
        id = rule.Id,
        subjectKind = rule.SubjectKind.ToString(),
        subjectKey = rule.SubjectKey,
        level = rule.Level.ToString(),
        entityType = rule.EntityType,
        fieldPath = rule.FieldPath,
        features = rule.Features.ToString(),
        effect = rule.Effect.ToString(),
        priority = rule.Priority,
        enabled = rule.Enabled,
        validFrom = rule.ValidFrom,
        validTo = rule.ValidTo,
        purpose = rule.Purpose,
        alias = rule.Alias,
        allowedOperators = rule.AllowedOperators?.Select(o => o.ToString()).ToList(),
        requiredOperators = rule.RequiredOperators?.Select(o => o.ToString()).ToList(),
        facts = rule.Facts is null
            ? null
            : new
            {
                label = rule.Facts.Label,
                description = rule.Facts.Description,
                group = rule.Facts.Group,
                order = rule.Facts.Order,
                allowedValues = rule.Facts.AllowedValues,
                cost = rule.Facts.CostWeight,
                audit = rule.Facts.AuditedFeatures?.ToString()
            },
        createdBy = rule.CreatedBy,
        createdAt = rule.CreatedAt,
        updatedBy = rule.UpdatedBy,
        updatedAt = rule.UpdatedAt
    };

    /// <summary>Renders one field's decision chain.</summary>
    private static object Describe(PolicyExplanation explanation) => new
    {
        field = explanation.FieldPath,
        entityType = explanation.EntityType,
        name = explanation.Policy.Alias ?? explanation.FieldPath,
        isSealed = explanation.Policy.IsSealed,
        features = explanation.Features.Select(feature => new
        {
            feature = feature.Feature.ToString(),
            effect = feature.Effect.ToString(),
            decidedBy = feature.DecidedBy?.ToString(),
            level = feature.Level?.ToString(),

            // Reported rather than reduced to the winner. Fragments equal on all four passes are
            // equal in force, and crediting one would name a rule that contributed no more than
            // its twin.
            attributionAmbiguous = feature.IsAttributionAmbiguous,
            tiedWith = feature.TiedWith.Select(s => s.ToString()).ToList(),
            overrode = feature.Overrode.Select(s => s.ToString()).ToList()
        })
        .ToList()
    };

    private static IResult NoStore(bool writable = false) =>
        Results.Json(
            new
            {
                error = writable
                    ? "No writable policy store is registered, so there is nothing to write to."
                    : "No policy store is registered, so there are no runtime rules to read."
            },
            statusCode: StatusCodes.Status501NotImplemented);
}

/// <summary>
/// A rule as an operator writes it over HTTP.
/// </summary>
/// <remarks>
/// A purpose-built contract rather than <c>PolicyRule</c> itself, for two reasons. The domain type
/// takes twenty-two constructor arguments and refuses most of them, which is not a shape a
/// serializer can bind; and, more to the point, it carries the audit columns — so binding it
/// directly would let a client post a <c>createdBy</c> of their choosing and forge the attribution
/// of a rule they wrote. Those four fields do not appear here at all: they come from the principal
/// and from nowhere else.
/// <para>
/// Enumerations are named, never numbered, exactly as they are in a store. <c>Allow</c>,
/// <c>Global</c>, <c>Equal</c> and <c>Text</c> are all zero, so a body that omitted a field or sent
/// a stray <c>0</c> would otherwise read as a grant to everyone.
/// </para>
/// </remarks>
/// <param name="Id">The rule to replace, or null to create one.</param>
/// <param name="SubjectKind">Who the rule applies to: Global, Tenant, Role, User or Custom.</param>
/// <param name="SubjectKey">Which one, or null for Global.</param>
/// <param name="EntityType">The entity's full type name, as rules are matched on it.</param>
/// <param name="FieldPath">The field, or <c>*</c> for every field.</param>
/// <param name="Features">The features this rule speaks to, by name.</param>
/// <param name="Effect">Allow, Mask or Deny, by name.</param>
/// <param name="Priority">Tiebreak within a level. Higher wins.</param>
/// <param name="Enabled">False to store the rule without applying it.</param>
/// <param name="ValidFrom">When the rule starts applying, or null.</param>
/// <param name="ValidTo">When it stops, or null.</param>
/// <param name="Purpose">The declared purpose this rule is bound to, or null.</param>
/// <param name="Alias">The public name to give the field, or null.</param>
public sealed record RuleRequest(
    Guid? Id,
    string? SubjectKind,
    string? SubjectKey,
    string? EntityType,
    string? FieldPath,
    string? Features,
    string? Effect,
    int Priority = 0,
    bool Enabled = true,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidTo = null,
    string? Purpose = null,
    string? Alias = null)
{
    /// <summary>
    /// Builds the rule, stamping the principal into the audit columns.
    /// </summary>
    /// <param name="principal">Whoever is writing it.</param>
    /// <exception cref="ArgumentException">Thrown when the rule is one the boundary refuses.</exception>
    /// <remarks>
    /// Through <see cref="PolicyRule"/>'s constructor, so every refusal that boundary makes still
    /// holds for a rule arriving over HTTP — which is the least trusted way one can arrive.
    /// </remarks>
    internal PolicyRule ToRule(ClaimsPrincipal principal)
    {
        string? who = principal.Identity?.Name
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? principal.FindFirst("sub")?.Value;

        DateTimeOffset now = DateTimeOffset.UtcNow;

        return new PolicyRule(
            PolicyRuleDocument.ToEnum<DwSubjectKind>(SubjectKind, nameof(SubjectKind)),
            SubjectKey,
            EntityType ?? throw new ArgumentException("A rule requires an entity type."),
            FieldPath ?? throw new ArgumentException("A rule requires a field path."),
            PolicyRuleDocument.ToFeatures(Features),
            PolicyRuleDocument.ToEnum<PolicyEffect>(Effect, nameof(Effect)),
            Priority,
            Enabled,
            ValidFrom,
            ValidTo,
            Purpose,
            transform: null,
            allowedOperators: null,
            alias: Alias,
            forced: null,
            requiredOperators: null,
            facts: null,
            id: Id,
            createdBy: who,
            createdAt: now,
            updatedBy: who,
            updatedAt: now);
    }
}

/// <summary>What to explain.</summary>
/// <param name="Entity">The public name of the entity.</param>
/// <param name="Field">One field, or null to explain every field the caller can use.</param>
public sealed record ExplainRequest(string? Entity, string? Field);

/// <summary>What to simulate.</summary>
/// <param name="Entity">The public name of the entity.</param>
/// <param name="Filter">The filter to run through the policy without executing it.</param>
public sealed record SimulateRequest(string? Entity, Filter? Filter);
