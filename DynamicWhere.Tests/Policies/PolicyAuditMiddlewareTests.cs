using System.Security.Claims;
using DynamicWhere.ex.Policies.AspNetCore;
using DynamicWhere.ex.Policies.Audit;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The middleware that writes what a request recorded, and the per-request context it drains.
/// </summary>
/// <remarks>
/// The query path is synchronous and a sink is not, so events accumulate on the context while the
/// request runs and are written afterwards. That arrangement is only complete if something drains
/// them: a context that is never drained loses its events when it is collected, and an audit record
/// silently lost is the failure <c>[DwAudit]</c> exists to prevent. Everything below is about the
/// cases where the drain could quietly not happen.
/// </remarks>
public class PolicyAuditMiddlewareTests
{
    /// <summary>A sink that remembers, and can be told to fail.</summary>
    private sealed class Recorder : IDwAuditSink
    {
        internal List<DwAuditEvent> Written { get; } = new();

        internal bool Fails { get; set; }

        public ValueTask WriteAsync(DwAuditEvent auditEvent, CancellationToken ct = default)
        {
            if (Fails)
            {
                throw new InvalidOperationException("the sink is down");
            }

            Written.Add(auditEvent);

            return default;
        }
    }

    /// <summary>A logger that remembers what it was told, so a warning can be asserted on.</summary>
    private sealed class Recording : ILogger<DwPolicyAuditMiddleware>
    {
        internal List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static DwAuditEvent Event(string field = "Salary") =>
        new(
            DateTimeOffset.UtcNow,
            typeof(Staff).FullName!,
            field,
            PolicyFeature.Select,
            PolicyEffect.Allow,
            Array.Empty<DwSubject>(),
            purpose: null,
            DwTier.Convenience,
            dryRun: false);

    /// <summary>
    /// A request carrying a context with <paramref name="events"/> already recorded on it.
    /// </summary>
    /// <remarks>
    /// Recorded directly rather than by running a guarded query. What the query path records is
    /// covered by <see cref="PolicyAuditTests"/>; what is in question here is only whether the
    /// middleware writes what it finds.
    /// </remarks>
    private static HttpContext Request(IServiceProvider services, params DwAuditEvent[] events)
    {
        DwPolicyContext context = new();

        foreach (DwAuditEvent recorded in events)
        {
            context.TryRecordAudit(recorded, capacity: 100);
        }

        DefaultHttpContext http = new() { RequestServices = services };

        http.Features.Set(context);

        return http;
    }

    private static IServiceProvider Services(IDwAuditSink? sink)
    {
        ServiceCollection services = new();

        if (sink is not null)
        {
            services.AddSingleton(sink);
        }

        return services.BuildServiceProvider();
    }

    // ---- arguments -----------------------------------------------------------------------------

    [Fact]
    public void The_middleware_requires_the_rest_of_the_pipeline()
    {
        Assert.Throws<ArgumentNullException>(() => new DwPolicyAuditMiddleware(null!));
    }

    [Fact]
    public void Wiring_it_into_a_null_pipeline_is_refused()
    {
        Assert.Throws<ArgumentNullException>(
            () => DwPolicyAuditMiddlewareExtensions.UseDwPolicyAudit(null!));
    }

    // ---- the drain -----------------------------------------------------------------------------

    [Fact]
    public async Task What_the_request_recorded_reaches_the_sink()
    {
        Recorder sink = new();

        DwPolicyAuditMiddleware middleware = new(_ => Task.CompletedTask);

        await middleware.InvokeAsync(Request(Services(sink), Event(), Event("NationalId")));

        Assert.Equal(new[] { "Salary", "NationalId" }, sink.Written.Select(e => e.FieldPath));
    }

    /// <summary>
    /// A request that threw still writes what it did before it threw.
    /// </summary>
    /// <remarks>
    /// The drain is in a <c>finally</c> for this case, and this case is the one an audit log is
    /// most often read for. Without it, the accesses a failing request made would be the ones that
    /// go unrecorded.
    /// </remarks>
    [Fact]
    public async Task A_request_that_threw_still_writes_what_it_recorded()
    {
        Recorder sink = new();

        DwPolicyAuditMiddleware middleware =
            new(_ => throw new InvalidOperationException("the handler failed"));

        HttpContext http = Request(Services(sink), Event());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await middleware.InvokeAsync(http));

        Assert.Single(sink.Written);
    }

    /// <summary>
    /// The failure the request threw is the one the caller sees, not anything the drain did.
    /// </summary>
    [Fact]
    public async Task The_drain_does_not_replace_the_failure_the_request_threw()
    {
        Recorder sink = new() { Fails = true };

        DwPolicyAuditMiddleware middleware =
            new(_ => throw new InvalidOperationException("the handler failed"));

        InvalidOperationException surfaced = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await middleware.InvokeAsync(Request(Services(sink), Event())));

        Assert.Equal("the handler failed", surfaced.Message);
    }

    [Fact]
    public async Task A_request_that_recorded_nothing_does_not_reach_the_sink()
    {
        Recorder sink = new();

        DwPolicyAuditMiddleware middleware = new(_ => Task.CompletedTask);

        await middleware.InvokeAsync(Request(Services(sink)));

        Assert.Empty(sink.Written);
    }

    /// <summary>
    /// A request that never built a context is the ordinary case for every route that queries
    /// nothing, and must pass through untouched.
    /// </summary>
    [Fact]
    public async Task A_request_with_no_context_passes_through()
    {
        bool ran = false;

        DwPolicyAuditMiddleware middleware = new(_ =>
        {
            ran = true;

            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext { RequestServices = Services(null) });

        Assert.True(ran);
    }

    // ---- the two ways a record can be lost -----------------------------------------------------

    /// <summary>
    /// No sink registered is a configuration mistake that would otherwise be silent, so it is
    /// logged rather than passed over.
    /// </summary>
    [Fact]
    public async Task Events_with_nowhere_to_go_are_reported()
    {
        Recording log = new();

        DwPolicyAuditMiddleware middleware = new(_ => Task.CompletedTask, log);

        await middleware.InvokeAsync(Request(Services(null), Event()));

        (LogLevel Level, string Message) warning = Assert.Single(log.Entries);

        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("IDwAuditSink", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A sink failure is logged and swallowed, because the response has already been written by
    /// this point and there is nothing left to fail into.
    /// </summary>
    [Fact]
    public async Task A_failing_sink_is_reported_rather_than_thrown_at_a_finished_response()
    {
        Recording log = new();

        DwPolicyAuditMiddleware middleware = new(_ => Task.CompletedTask, log);

        await middleware.InvokeAsync(Request(Services(new Recorder { Fails = true }), Event()));

        (LogLevel Level, string Message) failure = Assert.Single(log.Entries);

        Assert.Equal(LogLevel.Error, failure.Level);
    }

    /// <summary>
    /// The events a failing sink never accepted stay on the buffer, which is what would let a host
    /// retry them.
    /// </summary>
    [Fact]
    public async Task A_failing_sink_leaves_the_events_where_they_were()
    {
        DwPolicyContext context = new();

        context.TryRecordAudit(Event(), capacity: 100);

        DefaultHttpContext http = new() { RequestServices = Services(new Recorder { Fails = true }) };

        http.Features.Set(context);

        await new DwPolicyAuditMiddleware(_ => Task.CompletedTask).InvokeAsync(http);

        Assert.Single(context.PendingAuditEvents);
    }

    /// <summary>
    /// Draining empties the buffer, so a second drain cannot write the same access twice.
    /// </summary>
    [Fact]
    public async Task A_written_event_is_not_written_again()
    {
        Recorder sink = new();

        DwPolicyContext context = new();

        context.TryRecordAudit(Event(), capacity: 100);

        DefaultHttpContext http = new() { RequestServices = Services(sink) };

        http.Features.Set(context);

        DwPolicyAuditMiddleware middleware = new(_ => Task.CompletedTask);

        await middleware.InvokeAsync(http);
        await middleware.InvokeAsync(http);

        Assert.Single(sink.Written);
        Assert.Empty(context.PendingAuditEvents);
    }
}

/// <summary>
/// The per-request context accessor the endpoints and the drain both read.
/// </summary>
/// <remarks>
/// It lives on <c>HttpContext.Features</c> rather than in dependency injection because the drain
/// has to find the same instance the queries used. Its one interesting property is therefore
/// identity, not equality.
/// </remarks>
public class PolicyHttpContextTests
{
    private static DefaultHttpContext Request(params Claim[] claims)
    {
        DefaultHttpContext http = new()
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };

        return http;
    }

    [Fact]
    public async Task A_null_request_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await DwPolicyHttpContextExtensions
                .GetPolicyContextAsync(null!, new DwClaimsOptions()));
    }

    [Fact]
    public async Task Null_options_are_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await Request().GetPolicyContextAsync(null!));
    }

    [Fact]
    public async Task The_context_describes_the_requests_own_principal()
    {
        DwPolicyContext context = await Request(new Claim(ClaimTypes.Role, "Support"))
            .GetPolicyContextAsync(new DwClaimsOptions());

        Assert.Equal(new[] { "Support" }, context.Identities(DwSubjectKind.Role));
    }

    /// <summary>
    /// The same instance, not an equal one. A second context would carry its own audit buffer, and
    /// whichever of the two the drain did not find would lose its events.
    /// </summary>
    [Fact]
    public async Task A_second_call_returns_the_first_context()
    {
        DefaultHttpContext http = Request(new Claim(ClaimTypes.NameIdentifier, "u-1"));

        DwPolicyContext first = await http.GetPolicyContextAsync(new DwClaimsOptions());
        DwPolicyContext second = await http.GetPolicyContextAsync(new DwClaimsOptions());

        Assert.Same(first, second);
    }

    /// <summary>
    /// The middleware finds what the accessor stored, which is the whole reason the accessor
    /// stores it.
    /// </summary>
    [Fact]
    public async Task What_the_accessor_stored_is_what_the_middleware_drains()
    {
        DefaultHttpContext http = Request(new Claim(ClaimTypes.NameIdentifier, "u-1"));

        DwPolicyContext built = await http.GetPolicyContextAsync(new DwClaimsOptions());

        Assert.Same(built, http.Features.Get<DwPolicyContext>());
    }
}
