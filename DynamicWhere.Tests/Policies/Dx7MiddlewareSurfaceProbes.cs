using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Policies.AspNetCore;
using DynamicWhere.ex.Policies.Audit;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace DynamicWhere.Tests.Policies
{
    // =============================================================================================
    // Round 7, documentation review of 3.3.0 at 893cadc.
    //
    // The AspNetCore package's own 3.3.0 release note says "No API or behaviour change in this
    // package", while the core's 3.3.0 note ends the [DwAudit] paragraph with "raise the cap or
    // drain per request with app.UseDwPolicyAudit()". This measures what that package's surface
    // actually does with a request that names no projection.
    // =============================================================================================

    public sealed class Dx7MiddlewareSurfaceProbes
    {
        private readonly ITestOutputHelper _out;

        public Dx7MiddlewareSurfaceProbes(ITestOutputHelper output) => _out = output;

        private sealed class Recorder : IDwAuditSink
        {
            internal List<DwAuditEvent> Written { get; } = new();

            public ValueTask WriteAsync(DwAuditEvent auditEvent, CancellationToken ct = default)
            {
                Written.Add(auditEvent);

                return default;
            }
        }

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

        /// <summary>
        /// Runs a guarded query that names no projection, then drains the context the way the
        /// middleware does for a request.
        /// </summary>
        private static async Task<(List<DwAuditEvent> Written, List<(LogLevel Level, string Message)> Logged)>
            RequestWithNoSelects(bool registerSink)
        {
            DwPolicyContext context = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

            new[] { new Dx7Whole { Id = 1, Name = "a", Email = "a@b" } }
                .AsQueryable()
                .ApplyPolicy(
                    context,
                    new DwPolicyOptions { Tier = DwTier.Convenience, Caps = { MinGroupSize = 1 } },
                    new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }))
                .ToList(new Filter());

            Recorder sink = new();
            ServiceCollection services = new();

            if (registerSink)
            {
                services.AddSingleton<IDwAuditSink>(sink);
            }

            Recording logger = new();
            DefaultHttpContext http = new() { RequestServices = services.BuildServiceProvider() };

            http.Features.Set(context);

            await new DwPolicyAuditMiddleware(_ => Task.CompletedTask, logger).InvokeAsync(http);

            return (sink.Written, logger.Entries);
        }

        /// <summary>
        /// A request that names no projection now reaches the sink this package drains to.
        /// </summary>
        [Fact]
        public async Task A_request_naming_no_projection_reaches_the_sink_this_package_drains_to()
        {
            (List<DwAuditEvent> written, _) = await RequestWithNoSelects(registerSink: true);

            _out.WriteLine($"written: {string.Join("; ", written.Select(e => $"{e.FieldPath}:{e.Feature}"))}");

            DwAuditEvent recorded = Assert.Single(written);

            Assert.Equal("Email", recorded.FieldPath);
            Assert.Equal(PolicyFeature.Select, recorded.Feature);
        }

        /// <summary>
        /// And the same request, on a deployment with no sink, now trips the middleware's warning
        /// where it recorded nothing to warn about before.
        /// </summary>
        [Fact]
        public async Task The_same_request_with_no_sink_now_trips_the_middleware_warning()
        {
            (_, List<(LogLevel Level, string Message)> logged) = await RequestWithNoSelects(registerSink: false);

            foreach ((LogLevel level, string message) in logged)
            {
                _out.WriteLine($"{level}: {message}");
            }

            Assert.Contains(logged, entry => entry.Level == LogLevel.Warning);
        }
    }
}
