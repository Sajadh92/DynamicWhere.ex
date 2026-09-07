using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.AspNetCore;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The administrative surface, driven through a real request pipeline so that routing,
/// authorization and model binding all take part.
/// </summary>
/// <remarks>
/// The endpoints are thin — everything they answer with is computed in the core package and tested
/// there. What is tested here is the part only a pipeline can show: that the routes are where they
/// are supposed to be, that they refuse the callers they are supposed to refuse, and that mounting
/// them without deciding who may reach them is impossible.
/// </remarks>
public class PolicyEndpointTests
{
    private const string Entity = "staff";

    private static readonly object Bootstrap = new();

    /// <summary>
    /// Configures the process-wide policy once, because the endpoints read <c>DwPolicy</c> rather
    /// than taking a posture per call — which is the whole point of that type: a posture a caller
    /// can forget to pass at one call site is not a posture.
    /// </summary>
    private static void Configure()
    {
        lock (Bootstrap)
        {
            if (DwPolicy.IsConfigured)
            {
                return;
            }

            DwPolicyOptions options = new();

            options.Entities.Expose<Staff>(Entity).Expose<Person>();

            DwPolicy.Configure(options);
        }
    }

    /// <summary>Signs every request in as whoever the test asked for.</summary>
    private sealed class StubAuth : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        internal static string? Role { get; set; }

        // The three-argument base constructor. The one that also takes an ISystemClock is obsolete
        // on this target, and the branch holds a zero-warning build.
        public StubAuth(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            List<Claim> claims = new() { new Claim(ClaimTypes.NameIdentifier, "operator-1") };

            if (Role is not null)
            {
                claims.Add(new Claim(ClaimTypes.Role, Role));
            }

            ClaimsPrincipal principal = new(new ClaimsIdentity(claims, "Stub"));

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, "Stub")));
        }
    }

    private static async Task<IHost> HostAsync(
        IDwPolicyWritableStore? store = null,
        Action<DwPolicyAdminOptions>? configure = null,
        string? asRole = "PolicyAdmin")
    {
        Configure();

        StubAuth.Role = asRole;

        IHost host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAuthentication("Stub")
                        .AddScheme<AuthenticationSchemeOptions, StubAuth>("Stub", _ => { });

                    services.AddAuthorization(auth =>
                    {
                        auth.AddPolicy("PolicyReader", p => p.RequireRole("PolicyReader", "PolicyAdmin"));
                        auth.AddPolicy("PolicyAdmin", p => p.RequireRole("PolicyAdmin"));
                    });

                    if (store is not null)
                    {
                        services.AddSingleton<IDwPolicyStore>(store);
                        services.AddSingleton(store);
                    }
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e => e.MapDwPolicyAdmin(o =>
                    {
                        o.ReadPolicy = "PolicyReader";
                        o.WritePolicy = "PolicyAdmin";
                        configure?.Invoke(o);
                    }));
                }))
            .StartAsync();

        return host;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static RuleRequest Rule(string field = "Department") =>
        new(null, "Role", "Manager", typeof(Staff).FullName!, field, "Select", "Deny");

    // ---- mounting ------------------------------------------------------------------------------

    /// <summary>
    /// The decision this phase opened with. POST /rules changes what every caller in the
    /// application may see, so there is no default to fall back to — and failing at startup rather
    /// than on the first call matters for an endpoint nobody is supposed to call, where the first
    /// call would be the discovery.
    /// </summary>
    [Fact]
    public async Task Mounting_without_an_authorization_policy_is_refused()
    {
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => HostAsync(configure: o =>
            {
                o.ReadPolicy = null;
                o.WritePolicy = null;
            }));

        Assert.Contains("refuse to map", error.Message);
    }

    [Fact]
    public async Task Mounting_with_only_a_read_policy_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => HostAsync(configure: o => o.WritePolicy = null));
    }

    /// <summary>
    /// The opt-out exists so that "no authorization" is a decision somebody made rather than a
    /// field somebody forgot.
    /// </summary>
    [Fact]
    public async Task Mounting_anonymously_is_possible_but_deliberate()
    {
        using IHost host = await HostAsync(configure: o =>
        {
            o.ReadPolicy = null;
            o.WritePolicy = null;
            o.AllowAnonymousAccess = true;
        });

        HttpResponseMessage response =
            await host.GetTestClient().GetAsync("/dw-policies/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- authorization -------------------------------------------------------------------------

    /// <summary>
    /// Read and write are separate policies. The population who may inspect a policy is not the
    /// population who may rewrite the application's access control.
    /// </summary>
    [Fact]
    public async Task A_reader_may_read_and_may_not_write()
    {
        using IHost host = await HostAsync(new InMemoryPolicyStore(), asRole: "PolicyReader");

        HttpClient client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/dw-policies/rules")).StatusCode);

        HttpResponseMessage written =
            await client.PostAsJsonAsync("/dw-policies/rules", Rule());

        Assert.Equal(HttpStatusCode.Forbidden, written.StatusCode);
    }

    [Fact]
    public async Task A_caller_with_no_role_reaches_nothing()
    {
        using IHost host = await HostAsync(new InMemoryPolicyStore(), asRole: null);

        HttpClient client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/dw-policies/rules")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/dw-policies/health")).StatusCode);
    }

    // ---- schema --------------------------------------------------------------------------------

    [Fact]
    public async Task An_exposed_entity_can_be_described()
    {
        using IHost host = await HostAsync();

        JsonElement body = await JsonAsync(await host.GetTestClient()
            .GetAsync($"/dw-policies/schema/{Entity}"));

        Assert.Equal(Entity, body.GetProperty("entity").GetString());
        Assert.NotEmpty(body.GetProperty("fields").EnumerateArray());
    }

    /// <summary>
    /// The same answer whether the entity does not exist or merely was not exposed. Telling those
    /// apart is exactly what an enumeration attempt is looking for.
    /// </summary>
    [Fact]
    public async Task An_entity_nobody_exposed_is_not_found()
    {
        using IHost host = await HostAsync();

        HttpClient client = host.GetTestClient();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync("/dw-policies/schema/System.String")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync("/dw-policies/schema/NoSuchThing")).StatusCode);
    }

    /// <summary>
    /// Section 5.7's guarantee, over HTTP: a field an operator can never be granted is not
    /// advertised as though they could be.
    /// </summary>
    [Fact]
    public async Task A_sealed_denied_field_is_absent_from_the_schema()
    {
        using IHost host = await HostAsync();

        JsonElement body = await JsonAsync(await host.GetTestClient()
            .GetAsync($"/dw-policies/schema/{Entity}"));

        Assert.DoesNotContain(
            body.GetProperty("fields").EnumerateArray(),
            f => f.GetProperty("path").GetString() == "NationalId");
    }

    // ---- rules ---------------------------------------------------------------------------------

    [Fact]
    public async Task A_rule_can_be_written_read_back_and_deleted()
    {
        InMemoryPolicyStore store = new();

        using IHost host = await HostAsync(store);

        HttpClient client = host.GetTestClient();

        JsonElement written = await JsonAsync(
            await client.PostAsJsonAsync("/dw-policies/rules", Rule()));

        Guid id = written.GetProperty("id").GetGuid();

        JsonElement listed = await JsonAsync(await client.GetAsync("/dw-policies/rules"));

        Assert.Contains(listed.EnumerateArray(), r => r.GetProperty("id").GetGuid() == id);

        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/dw-policies/rules/{id}")).StatusCode);

        JsonElement empty = await JsonAsync(await client.GetAsync("/dw-policies/rules"));

        Assert.Empty(empty.EnumerateArray());
    }

    /// <summary>
    /// The audit columns have existed since Phase 5 and nothing has ever populated them. This is
    /// the first place a principal is actually known.
    /// </summary>
    [Fact]
    public async Task Writing_a_rule_records_who_wrote_it()
    {
        using IHost host = await HostAsync(new InMemoryPolicyStore());

        JsonElement written = await JsonAsync(await host.GetTestClient()
            .PostAsJsonAsync("/dw-policies/rules", Rule()));

        Assert.Equal("operator-1", written.GetProperty("createdBy").GetString());
        Assert.Equal("operator-1", written.GetProperty("updatedBy").GetString());
        Assert.NotEqual(default, written.GetProperty("updatedAt").GetDateTimeOffset());
    }

    /// <summary>
    /// The attribution comes from the principal and from nowhere else. A request body that names
    /// somebody else as the author is not honoured — an audit column a client can set is not an
    /// audit column.
    /// </summary>
    [Fact]
    public async Task A_client_cannot_choose_who_wrote_a_rule()
    {
        using IHost host = await HostAsync(new InMemoryPolicyStore());

        JsonElement written = await JsonAsync(await host.GetTestClient().PostAsJsonAsync(
            "/dw-policies/rules",
            new
            {
                subjectKind = "Role",
                subjectKey = "Manager",
                entityType = typeof(Staff).FullName,
                fieldPath = "Department",
                features = "Select",
                effect = "Deny",
                createdBy = "somebody-else",
                updatedBy = "somebody-else"
            }));

        Assert.Equal("operator-1", written.GetProperty("createdBy").GetString());
        Assert.Equal("operator-1", written.GetProperty("updatedBy").GetString());
    }

    /// <summary>
    /// Enumerations arrive by name, never by number. Allow, Global, Equal and Text are all zero, so
    /// a body that omitted a field or sent a stray zero would otherwise read as a grant to everyone.
    /// </summary>
    [Fact]
    public async Task An_effect_written_as_a_number_is_refused()
    {
        using IHost host = await HostAsync(new InMemoryPolicyStore());

        HttpResponseMessage response = await host.GetTestClient().PostAsJsonAsync(
            "/dw-policies/rules",
            new
            {
                subjectKind = "Role",
                subjectKey = "Manager",
                entityType = typeof(Staff).FullName,
                fieldPath = "Department",
                features = "Select",
                effect = "0"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A rule the domain boundary refuses comes back as a bad request rather than a stack trace.
    /// </summary>
    [Fact]
    public async Task A_malformed_rule_is_a_bad_request()
    {
        using IHost host = await HostAsync(new InMemoryPolicyStore());

        HttpResponseMessage response = await host.GetTestClient().PostAsJsonAsync(
            "/dw-policies/rules",
            new
            {
                subjectKind = "Role",
                subjectKey = "Manager",
                entityType = "Staff",
                fieldPath = "Department",
                features = "Select",
                effect = "Deny"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("full name", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Inherited from the store contract rather than reimplemented: SealedFields.Refuse is shared
    /// by all three stores and the endpoint calls the same one.
    /// </summary>
    [Fact]
    public async Task A_rule_aimed_at_a_sealed_field_is_refused()
    {
        using IHost host = await HostAsync(new InMemoryPolicyStore());

        HttpResponseMessage response = await host.GetTestClient()
            .PostAsJsonAsync("/dw-policies/rules", Rule("NationalId"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sealed", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The key is shown as the operator typed it. Both stores match on a lowercased copy so a rule
    /// applies identically on a case-sensitive PostgreSQL and a case-insensitive SQL Server, and
    /// echoing the normalized form back would be confusing and is not what was written.
    /// </summary>
    [Fact]
    public async Task A_subject_key_is_reported_as_it_was_typed()
    {
        using IHost host = await HostAsync(new InMemoryPolicyStore());

        JsonElement written = await JsonAsync(await host.GetTestClient()
            .PostAsJsonAsync("/dw-policies/rules", Rule()));

        Assert.Equal("Manager", written.GetProperty("subjectKey").GetString());
    }

    [Fact]
    public async Task Rules_can_be_filtered_by_subject()
    {
        InMemoryPolicyStore store = new();

        using IHost host = await HostAsync(store);

        HttpClient client = host.GetTestClient();

        await client.PostAsJsonAsync("/dw-policies/rules", Rule());

        JsonElement matched = await JsonAsync(
            await client.GetAsync("/dw-policies/rules?subject=Role:manager"));

        Assert.Single(matched.EnumerateArray());

        JsonElement other = await JsonAsync(
            await client.GetAsync("/dw-policies/rules?subject=Role:Auditor"));

        Assert.Empty(other.EnumerateArray());
    }

    [Fact]
    public async Task A_host_with_no_store_says_so_rather_than_failing()
    {
        using IHost host = await HostAsync();

        Assert.Equal(
            HttpStatusCode.NotImplemented,
            (await host.GetTestClient().GetAsync("/dw-policies/rules")).StatusCode);
    }

    // ---- explain and simulate ------------------------------------------------------------------

    [Fact]
    public async Task Explain_returns_the_decision_chain()
    {
        using IHost host = await HostAsync();

        JsonElement body = await JsonAsync(await host.GetTestClient().PostAsJsonAsync(
            "/dw-policies/explain", new { entity = Entity, field = "Department" }));

        JsonElement field = Assert.Single(body.EnumerateArray());

        Assert.Equal("Department", field.GetProperty("field").GetString());
        Assert.Equal(6, field.GetProperty("features").GetArrayLength());
    }

    [Fact]
    public async Task Explain_without_a_field_covers_every_field_the_caller_can_use()
    {
        using IHost host = await HostAsync();

        JsonElement body = await JsonAsync(await host.GetTestClient().PostAsJsonAsync(
            "/dw-policies/explain", new { entity = Entity }));

        Assert.True(body.GetArrayLength() > 1);
    }

    [Fact]
    public async Task Simulate_returns_a_sanitized_filter_without_executing()
    {
        using IHost host = await HostAsync();

        Filter filter = new() { Selects = new List<string> { "Name", "NationalId" } };

        JsonElement body = await JsonAsync(await host.GetTestClient().PostAsJsonAsync(
            "/dw-policies/simulate", new SimulateRequest(Entity, filter)));

        Assert.True(body.GetProperty("wouldRun").GetBoolean());

        List<string?> selects = body.GetProperty("filter").GetProperty("selects")
            .EnumerateArray().Select(s => s.GetString()).ToList();

        Assert.Contains("Name", selects);
        Assert.DoesNotContain("NationalId", selects);
    }

    [Fact]
    public async Task Simulate_reports_a_refusal_as_an_answer()
    {
        using IHost host = await HostAsync();

        Filter filter = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Sort = 1,
                Conditions =
                {
                    new Condition
                    {
                        Sort = 1,
                        Field = "NationalId",
                        DataType = DataType.Text,
                        Operator = Operator.Equal,
                        Values = { "x" }
                    }
                }
            }
        };

        JsonElement body = await JsonAsync(await host.GetTestClient().PostAsJsonAsync(
            "/dw-policies/simulate", new SimulateRequest(Entity, filter)));

        Assert.False(body.GetProperty("wouldRun").GetBoolean());
        Assert.Equal(
            "FieldDeniedForWhere",
            body.GetProperty("refusal").GetProperty("code").GetString());
    }

    // ---- health --------------------------------------------------------------------------------

    [Fact]
    public async Task Health_reports_the_posture()
    {
        using IHost host = await HostAsync();

        JsonElement body = await JsonAsync(await host.GetTestClient().GetAsync("/dw-policies/health"));

        Assert.True(body.GetProperty("healthy").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("tier").GetString()));
    }

    /// <summary>
    /// The route prefix is configurable, so a host already using <c>/dw-policies</c> for something
    /// else can mount this elsewhere.
    /// </summary>
    [Fact]
    public async Task The_route_prefix_is_configurable()
    {
        using IHost host = await HostAsync(configure: o => o.RoutePrefix = "/admin/policy");

        HttpClient client = host.GetTestClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin/policy/health")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/dw-policies/health")).StatusCode);
    }
}
