using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DynamicWhere.API.Controllers;
using DynamicWhere.API.Models;
using DynamicWhere.ex.Policies.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The demo API's policy administration controller, driven through MVC against a real PostgreSQL.
/// </summary>
/// <remarks>
/// It is not the package's administrative surface — those endpoints are mounted separately and
/// covered by <see cref="PolicyEndpointTests"/>. This controller is the demonstration next to them,
/// and the only place where the four packages are driven together against a database: it writes a
/// rule through the EF store, refreshes the provider, and reports what the resolver then decides.
/// <para>
/// Worth testing rather than trusting because a demonstration that reports the wrong answer teaches
/// the wrong answer. Its sealed-field endpoint asserts a refusal on behalf of the whole library,
/// and it was constructing a store that could not perform the check.
/// </para>
/// </remarks>
[Collection("PolicyEndpoints")]
public sealed class PolicyAdminControllerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _server =
        new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    private IHost? _host;

    /// <summary>
    /// Starts the database, creates the two policy tables, and mounts the controller.
    /// </summary>
    /// <remarks>
    /// The demo's own <c>Program</c> is deliberately not used: it migrates a seeded sales schema
    /// and configures <c>DwPolicy</c> a second time, which is refused. What is under test is the
    /// controller, so the controller is what is hosted.
    /// </remarks>
    public async Task InitializeAsync()
    {
        PolicyEndpointHost.Configure();

        await _server.StartAsync();

        string connection = _server.GetConnectionString();

        DbContextOptions<DwPolicyDbContext> options =
            new DbContextOptionsBuilder<DwPolicyDbContext>().UseNpgsql(connection).Options;

        using (DwPolicyDbContext database = new(options))
        {
            await database.Database.EnsureCreatedAsync();
        }

        // The controller opens its own short-lived contexts for writes and reads the injected one
        // for the listing, exactly as it does in the demo.
        PolicyAdminController.UseConnection(connection);

        _host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddDbContext<DwPolicyDbContext>(o => o.UseNpgsql(connection));
                    services.AddControllers()
                        .AddApplicationPart(typeof(PolicyAdminController).Assembly);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(e => e.MapControllers());
                }))
            .StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();

            _host.Dispose();
        }

        await _server.DisposeAsync();
    }

    private HttpClient Client => _host!.GetTestClient();

    /// <summary>Reads one of the controller's uniform result envelopes.</summary>
    private static async Task<JsonElement> ResultAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static bool Succeeded(JsonElement result) => result.GetProperty("success").GetBoolean();

    private static string Message(JsonElement result) =>
        result.GetProperty("message").GetString() ?? string.Empty;

    // ---- health --------------------------------------------------------------------------------

    /// <summary>
    /// With no store provider registered, attributes are the whole policy and the endpoint says so
    /// rather than reporting an empty list as health.
    /// </summary>
    [Fact]
    public async Task Health_reports_that_attributes_are_the_whole_policy()
    {
        JsonElement result = await ResultAsync(await Client.GetAsync("/api/PolicyAdmin/health"));

        Assert.True(Succeeded(result));
        Assert.Contains("No store provider", Message(result), StringComparison.Ordinal);
    }

    // ---- rules ---------------------------------------------------------------------------------

    /// <summary>
    /// The listing reads the store, not the attributes. The sealed half is not in it and the
    /// message has to say so, or an operator reads an empty list as an empty policy.
    /// </summary>
    [Fact]
    public async Task The_listing_is_the_dynamic_half_and_says_so()
    {
        JsonElement result = await ResultAsync(await Client.GetAsync("/api/PolicyAdmin/rules"));

        Assert.True(Succeeded(result));
        Assert.Contains("sealed half", Message(result), StringComparison.Ordinal);
    }

    /// <summary>
    /// The demo rule is written, appears in the listing, and is removed again — which is what makes
    /// the demonstration repeatable.
    /// </summary>
    [Fact]
    public async Task The_demo_rule_is_written_listed_and_removed()
    {
        HttpClient client = Client;

        JsonElement written = await ResultAsync(
            await client.PostAsync("/api/PolicyAdmin/rules/deny-position-for-support", null));

        Assert.True(Succeeded(written), Message(written));

        JsonElement listed = await ResultAsync(await client.GetAsync("/api/PolicyAdmin/rules"));

        Assert.Contains(
            listed.GetProperty("output").EnumerateArray(),
            rule => rule.GetProperty("fieldPath").GetString() == "Position");

        JsonElement removed = await ResultAsync(
            await client.DeleteAsync("/api/PolicyAdmin/rules/deny-position-for-support"));

        Assert.True(Succeeded(removed));

        JsonElement after = await ResultAsync(await client.GetAsync("/api/PolicyAdmin/rules"));

        Assert.DoesNotContain(
            after.GetProperty("output").EnumerateArray(),
            rule => rule.GetProperty("fieldPath").GetString() == "Position");
    }

    /// <summary>
    /// Writing the same rule twice replaces it rather than accumulating copies, because the demo
    /// gives it a fixed id.
    /// </summary>
    [Fact]
    public async Task Writing_the_demo_rule_twice_leaves_one_rule()
    {
        HttpClient client = Client;

        await client.PostAsync("/api/PolicyAdmin/rules/deny-position-for-support", null);
        await client.PostAsync("/api/PolicyAdmin/rules/deny-position-for-support", null);

        JsonElement listed = await ResultAsync(await client.GetAsync("/api/PolicyAdmin/rules"));

        Assert.Single(
            listed.GetProperty("output").EnumerateArray()
                .Where(rule => rule.GetProperty("fieldPath").GetString() == "Position"));

        await client.DeleteAsync("/api/PolicyAdmin/rules/deny-position-for-support");
    }

    /// <summary>
    /// A rule aimed at a field the source code seals is refused at the store boundary.
    /// </summary>
    /// <remarks>
    /// The endpoint reports its own verdict, and the failing verdict is a string rather than a
    /// status code — so a test that only checked for a 200 would pass while the demonstration
    /// printed FAILED OPEN. It printed exactly that until the store this controller builds was
    /// given the type resolver <c>SealedFields.Refuse</c> needs, which nothing had exercised.
    /// </remarks>
    [Fact]
    public async Task A_rule_granting_a_sealed_field_is_refused()
    {
        JsonElement result = await ResultAsync(
            await Client.PostAsync("/api/PolicyAdmin/rules/sealed-field-is-refused", null));

        Assert.True(Succeeded(result), Message(result));
        Assert.DoesNotContain("FAILED OPEN", Message(result), StringComparison.Ordinal);
        Assert.Equal(
            "ArgumentException", result.GetProperty("output").GetProperty("refusal").GetString());
    }

    /// <summary>
    /// And the refusal is not merely reported: nothing reached the store.
    /// </summary>
    [Fact]
    public async Task The_refused_rule_is_not_in_the_store()
    {
        HttpClient client = Client;

        await client.PostAsync("/api/PolicyAdmin/rules/sealed-field-is-refused", null);

        JsonElement listed = await ResultAsync(await client.GetAsync("/api/PolicyAdmin/rules"));

        Assert.DoesNotContain(
            listed.GetProperty("output").EnumerateArray(),
            rule => rule.GetProperty("fieldPath").GetString() == "WorkSchedule");
    }

    // ---- explain, schema and simulate ----------------------------------------------------------

    [Fact]
    public async Task Explain_names_the_field_and_its_decisions()
    {
        JsonElement result = await ResultAsync(
            await Client.GetAsync("/api/PolicyAdmin/explain/Position"));

        Assert.True(Succeeded(result));
        Assert.Equal("Position", result.GetProperty("output").GetProperty("fieldPath").GetString());
        Assert.NotEmpty(result.GetProperty("output").GetProperty("features").EnumerateArray());
    }

    /// <summary>
    /// A field the source code refuses outright is sealed, and the explanation says so rather than
    /// reporting it as an ordinary denial a rule could lift.
    /// </summary>
    [Fact]
    public async Task Explaining_a_denied_field_reports_it_as_sealed()
    {
        JsonElement result = await ResultAsync(
            await Client.GetAsync("/api/PolicyAdmin/explain/WorkSchedule"));

        Assert.True(result.GetProperty("output").GetProperty("policy").GetProperty("isSealed").GetBoolean());
    }

    /// <summary>
    /// The schema is built from the entity, so a field that is denied is absent from it and a
    /// filter UI built from it cannot offer one.
    /// </summary>
    [Fact]
    public async Task The_schema_leaves_the_denied_field_out()
    {
        JsonElement result = await ResultAsync(await Client.GetAsync("/api/PolicyAdmin/schema"));

        Assert.True(Succeeded(result));

        List<string?> paths = result.GetProperty("output").GetProperty("fields").EnumerateArray()
            .Select(field => field.GetProperty("path").GetString())
            .ToList();

        Assert.Contains("Department", paths);
        Assert.DoesNotContain("WorkSchedule", paths);
    }

    /// <summary>
    /// A simulation answers what would happen without executing anything, and the denied field the
    /// demo filter asks for is gone from the sanitized clause.
    /// </summary>
    [Fact]
    public async Task Simulate_drops_the_denied_field_without_running_anything()
    {
        JsonElement result = await ResultAsync(
            await Client.PostAsync("/api/PolicyAdmin/simulate", null));

        Assert.True(Succeeded(result));

        JsonElement simulation = result.GetProperty("output");

        Assert.True(simulation.GetProperty("wouldRun").GetBoolean());

        List<string?> selects = simulation.GetProperty("clause").GetProperty("selects")
            .EnumerateArray()
            .Select(select => select.GetString())
            .ToList();

        Assert.Contains("FirstName", selects);
        Assert.DoesNotContain("WorkSchedule", selects);

        // The request itself is echoed unchanged, so the before and the after can be compared —
        // which is the one thing this controller shows that the package endpoints do not.
        Assert.Contains(
            "WorkSchedule",
            result.GetProperty("input").GetProperty("selects").EnumerateArray()
                .Select(select => select.GetString()));
    }
}
