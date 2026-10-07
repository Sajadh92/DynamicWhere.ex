using System.Net;
using System.Text.Json;
using DynamicWhere.API.Controllers;
using DynamicWhere.API.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// One PostgreSQL, one data source and one host for every scenario of <see cref="DemoApi350Tests"/>.
/// </summary>
/// <remarks>
/// Shared on purpose. EF Core builds an internal service provider for each distinct set of options, a new
/// data source and a new host each counting as one, and refuses to build a twenty-first in the same
/// process with <c>ManyServiceProvidersCreatedWarning</c>. A container per case, as a plain
/// <c>IAsyncLifetime</c> test class gets, used up that budget for every suite after it. The seed runs
/// through the host's own context, so the suite adds one provider in all.
/// </remarks>
public sealed class DemoApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _server = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private NpgsqlDataSource? _source;

    internal IHost Host { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        PolicyEndpointHost.Configure();

        await _server.StartAsync();

        // The demo's JSON columns hold dictionaries and lists, which Npgsql maps only when asked to.
        NpgsqlDataSourceBuilder builder = new(_server.GetConnectionString());
        builder.EnableDynamicJson();
        _source = builder.Build();

        Host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddDbContext<AppDbContext>(o => o.UseNpgsql(_source));
                    services.AddControllers()
                        .AddApplicationPart(typeof(PolicyTestController).Assembly);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }))
            .StartAsync();

        using IServiceScope scope = Host.Services.CreateScope();

        AppDbContext database = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await database.Database.EnsureCreatedAsync();
        await DataSeeder.SeedDataAsync(database);
    }

    public async Task DisposeAsync()
    {
        if (Host is not null)
        {
            await Host.StopAsync();

            Host.Dispose();
        }

        if (_source is not null)
        {
            await _source.DisposeAsync();
        }

        await _server.DisposeAsync();
    }
}

/// <summary>
/// The demo API's 3.5.0 scenarios, run on the demo's own model and seed against a real PostgreSQL.
/// </summary>
/// <remarks>
/// Each scenario asserts its own outcome and reports it as <c>success</c>, the house shape of the demo's
/// test controllers, so running them here puts the demonstration of a release behind CI rather than behind
/// somebody remembering to call it. A demonstration that reports the wrong answer teaches the wrong answer.
/// <para>
/// The demo's own <c>Program</c> is not used, for the reason <see cref="PolicyAdminControllerTests"/>
/// gives: it configures <c>DwPolicy</c> a second time. Nor are its migrations: they were written under
/// Npgsql's legacy timestamp behaviour, which the demo switches on process-wide and a shared test process
/// must not. The schema is created from the model instead, which maps every date as the seed writes it.
/// These scenarios read nothing of the posture the assembly's lacks: two row types without transforms, and
/// a summary refused before any posture is read.
/// </para>
/// </remarks>
[Collection("PolicyEndpoints")]
public sealed class DemoApi350Tests : IClassFixture<DemoApiFixture>
{
    private readonly DemoApiFixture _demo;

    public DemoApi350Tests(DemoApiFixture demo) => _demo = demo;

    [Theory]
    [InlineData("/api/PolicyTest/refuse-selects/selects-refused")]
    [InlineData("/api/PolicyTest/refuse-selects/whole-cards")]
    [InlineData("/api/PolicyTest/refuse-selects/dry-run-traced")]
    [InlineData("/api/PolicyTest/summary/no-groupby-is-malformed")]
    [InlineData("/api/PolicyTest/tiebreak/pages-are-total")]
    [InlineData("/api/SummaryTest/summary/no-groupby-is-malformed")]
    public async Task Every_350_scenario_reports_success(string path)
    {
        HttpResponseMessage response = await _demo.Host.GetTestClient().GetAsync(path);
        string body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");

        JsonElement result = JsonDocument.Parse(body).RootElement;

        Assert.True(result.GetProperty("success").GetBoolean(), result.GetProperty("message").GetString());
    }
}
