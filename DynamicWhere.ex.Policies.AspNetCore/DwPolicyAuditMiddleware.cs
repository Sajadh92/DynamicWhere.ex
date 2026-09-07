using DynamicWhere.ex.Policies.Audit;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DynamicWhere.ex.Policies.AspNetCore;

/// <summary>
/// Carries one policy context for the life of a request, and writes what it recorded to the audit
/// sink once the response is done.
/// </summary>
/// <remarks>
/// The query path is synchronous and a sink is not, so events accumulate on the context while the
/// request runs. Draining them here is what makes that arrangement complete: a context that is
/// never drained loses its events when it is collected, and an audit record that is silently lost
/// is the failure <c>[DwAudit]</c> exists to prevent.
/// <para>
/// After the response rather than before it. The sink may be slow, and a caller should not wait on
/// a write they are not the beneficiary of — but the request has not finished until the record is
/// written, so a host that needs the two decoupled should give the sink a queue of its own.
/// </para>
/// </remarks>
public sealed class DwPolicyAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DwPolicyAuditMiddleware>? _log;

    /// <summary>Initializes the middleware.</summary>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="log">Where a sink failure is reported, or null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="next"/> is null.</exception>
    public DwPolicyAuditMiddleware(RequestDelegate next, ILogger<DwPolicyAuditMiddleware>? log = null)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _log = log;
    }

    /// <summary>Runs the request, then drains whatever it recorded.</summary>
    /// <param name="http">The request.</param>
    public async Task InvokeAsync(HttpContext http)
    {
        try
        {
            await _next(http).ConfigureAwait(false);
        }
        finally
        {
            await DrainAsync(http).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes what the request recorded, if it recorded anything and a sink is registered.
    /// </summary>
    /// <remarks>
    /// In a <c>finally</c>, so a request that threw still writes what it did before it threw. That
    /// is the case an audit log is most often read for.
    /// <para>
    /// A sink failure is logged and swallowed. The response has already been written by this point,
    /// so there is nothing left to fail into — rethrowing would replace a completed response with a
    /// connection reset and still not save the record. What it must not do is pass silently, which
    /// is why a logger that is absent is itself worth noticing.
    /// </para>
    /// </remarks>
    private async ValueTask DrainAsync(HttpContext http)
    {
        DwPolicyContext? context = http.Features.Get<DwPolicyContext>();

        if (context is null || context.PendingAuditEvents.Count == 0)
        {
            return;
        }

        IDwAuditSink? sink = http.RequestServices.GetService<IDwAuditSink>();

        if (sink is null)
        {
            _log?.LogWarning(
                "{Count} policy audit events were recorded and no IDwAuditSink is registered, so "
                + "they were discarded. Register one, or remove [DwAudit] from the fields that "
                + "produced them.",
                context.PendingAuditEvents.Count);

            return;
        }

        try
        {
            await DwPolicy.DrainAuditAsync(context, sink, http.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            _log?.LogError(
                failure,
                "The policy audit sink refused {Count} events; they remain buffered on a context "
                + "that is about to be discarded.",
                context.PendingAuditEvents.Count);
        }
    }
}

/// <summary>
/// Wires the audit drain into a request pipeline.
/// </summary>
public static class DwPolicyAuditMiddlewareExtensions
{
    /// <summary>
    /// Drains the request's policy audit events to the registered sink once the response is done.
    /// </summary>
    /// <param name="app">The pipeline.</param>
    /// <returns>The same pipeline, for chaining.</returns>
    /// <remarks>
    /// Add it early — before authentication and routing — so that it wraps everything that could
    /// touch an audited field. It reads a context the application stored on
    /// <c>HttpContext.Features</c>, and does nothing when there is none.
    /// </remarks>
    public static IApplicationBuilder UseDwPolicyAudit(this IApplicationBuilder app) =>
        app is null
            ? throw new ArgumentNullException(nameof(app))
            : app.UseMiddleware<DwPolicyAuditMiddleware>();
}

/// <summary>
/// Stores and retrieves the policy context a request is being served under.
/// </summary>
/// <remarks>
/// On <c>HttpContext.Features</c> rather than in dependency injection, because the context is built
/// per request from the request's own principal and the drain has to find the same instance the
/// queries used. A scoped service would work too; a feature is one fewer registration a host can
/// forget, and forgetting it here means the audit log quietly stops.
/// </remarks>
public static class DwPolicyHttpContextExtensions
{
    /// <summary>
    /// Builds a policy context for the request's principal, stores it on the request, and returns
    /// it. Returns the same instance on a second call.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <param name="options">Which claims describe the caller.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    public static async ValueTask<DwPolicyContext> GetPolicyContextAsync(
        this HttpContext http, DwClaimsOptions options)
    {
        if (http is null)
        {
            throw new ArgumentNullException(nameof(http));
        }

        DwPolicyContext? existing = http.Features.Get<DwPolicyContext>();

        if (existing is not null)
        {
            return existing;
        }

        DwPolicyContext context = await DwClaimsAdapter
            .CreateContextAsync(http.User, options, http.RequestAborted)
            .ConfigureAwait(false);

        http.Features.Set(context);

        return context;
    }
}
