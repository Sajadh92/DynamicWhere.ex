using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DynamicWhere.ex.Policies.AspNetCore;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Storage;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The administrative surface at its edges: the postures it refuses to mount under, the verbs and
/// ids it does not answer to, and the requests it has to turn away rather than fail on.
/// </summary>
/// <remarks>
/// Separate from <see cref="PolicyEndpointTests"/>, which covers the path each endpoint is for.
/// What is here is the surrounding shape — and it is worth its own suite because an admin endpoint
/// is reached by whoever is probing it long before it is reached by an operator.
/// </remarks>
[Collection("PolicyEndpoints")]
public class PolicyEndpointHardeningTests
{
    private const string Entity = PolicyEndpointHost.Entity;

    private static Task<IHost> HostAsync(
        IDwPolicyWritableStore? store = null,
        Action<DwPolicyAdminOptions>? configure = null,
        string? asRole = "PolicyAdmin") =>
        PolicyEndpointHost.StartAsync(store, configure, asRole);

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static RuleRequest Rule(string field = "Department") =>
        new(null, "Role", "Manager", typeof(Staff).FullName!, field, "Select", "Deny");

    // ---- mounting ------------------------------------------------------------------------------

    /// <summary>
    /// The mirror of the read-only case. Write is the half that rewrites what every caller may
    /// see, so leaving it unnamed is the more dangerous of the two omissions, not the lesser.
    /// </summary>
    [Fact]
    public async Task Mounting_with_only_a_write_policy_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => HostAsync(configure: o => o.ReadPolicy = null));
    }

    /// <summary>
    /// Whitespace is not a policy name. It would satisfy a null check and name nothing.
    /// </summary>
    [Fact]
    public async Task A_blank_policy_name_is_not_a_policy()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => HostAsync(configure: o => o.WritePolicy = "   "));
    }

    [Fact]
    public async Task Mounting_without_a_route_prefix_is_refused()
    {
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => HostAsync(configure: o => o.RoutePrefix = string.Empty));

        Assert.Contains("route prefix", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mounting_on_nothing_is_refused()
    {
        Assert.Throws<ArgumentNullException>(
            () => DwPolicyEndpoints.MapDwPolicyAdmin(null!, _ => { }));
    }

    /// <summary>
    /// A prefix written with a trailing slash mounts where the operator meant, not one segment
    /// below it.
    /// </summary>
    [Fact]
    public async Task A_trailing_slash_in_the_prefix_is_trimmed()
    {
        using IHost host = await HostAsync(configure: o => o.RoutePrefix = "/admin/policy/");

        Assert.Equal(
            HttpStatusCode.OK,
            (await host.GetTestClient().GetAsync("/admin/policy/health")).StatusCode);
    }

    /// <summary>
    /// The anonymous escape hatch opens the writes as well as the reads.
    /// </summary>
    /// <remarks>
    /// Pinned because it is the half a reader of the option name would not assume. It is documented
    /// as being for a deployment where something in front of the application authorizes, and a host
    /// that sets it expecting only <c>/health</c> to open has published <c>POST /rules</c>.
    /// </remarks>
    [Fact]
    public async Task An_anonymous_mount_opens_the_writes_too()
    {
        using IHost host = await HostAsync(
            new InMemoryPolicyStore(),
            o =>
            {
                o.ReadPolicy = null;
                o.WritePolicy = null;
                o.AllowAnonymousAccess = true;
            },
            asRole: null);

        HttpResponseMessage written =
            await host.GetTestClient().PostAsJsonAsync("/dw-policies/rules", Rule());

        Assert.Equal(HttpStatusCode.OK, written.StatusCode);
    }

    // ---- authorization -------------------------------------------------------------------------

    /// <summary>
    /// Deleting a rule is a write. A reader reaching it would be able to remove any control in the
    /// application while holding only the permission to look at them.
    /// </summary>
    [Fact]
    public async Task A_reader_may_not_delete()
    {
        using IHost host = await HostAsync(new InMemoryPolicyStore(), asRole: "PolicyReader");

        HttpResponseMessage response =
            await host.GetTestClient().DeleteAsync($"/dw-policies/rules/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_reader_may_read_schema_explain_and_simulate()
    {
        using IHost host = await HostAsync(asRole: "PolicyReader");

        HttpClient client = host.GetTestClient();

        Assert.Equal(
            HttpStatusCode.OK, (await client.GetAsync($"/dw-policies/schema/{Entity}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/dw-policies/explain", new { entity = Entity })).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PostAsJsonAsync(
                "/dw-policies/simulate",
                new SimulateRequest(Entity, new DynamicWhere.ex.Classes.Complex.Filter()))).StatusCode);
    }

    // ---- routing -------------------------------------------------------------------------------

    /// <summary>
    /// The surface answers to the verbs it maps and to no others. A rule is replaced by posting it
    /// with its id, and a PUT that quietly did nothing would read as a rule that had been changed.
    /// </summary>
    [Fact]
    public async Task An_unmapped_verb_is_not_answered()
    {
        using IHost host = await HostAsync(new InMemoryPolicyStore());

        HttpResponseMessage response = await host.GetTestClient().PutAsJsonAsync(
            "/dw-policies/rules", Rule());

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    /// <summary>
    /// An id that is not a rule id does not reach the store at all: the route constraint declines
    /// it, so the handler never runs on a value it would have to interpret.
    /// </summary>
    [Fact]
    public async Task An_id_that_is_not_a_guid_is_not_routed()
    {
        using IHost host = await HostAsync(new InMemoryPolicyStore());

        HttpResponseMessage response =
            await host.GetTestClient().DeleteAsync("/dw-policies/rules/not-a-rule-id");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// Deleting a rule that is not there succeeds. An operator removing a control twice, or two
    /// operators removing it at once, have both got what they asked for.
    /// </summary>
    [Fact]
    public async Task Deleting_a_rule_that_is_not_there_is_still_no_content()
    {
        using IHost host = await HostAsync(new InMemoryPolicyStore());

        HttpResponseMessage response =
            await host.GetTestClient().DeleteAsync($"/dw-policies/rules/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ---- a host with no store ------------------------------------------------------------------

    /// <summary>
    /// The write half of the no-store answer, which says something different from the read half:
    /// a host can register a readable store that cannot be written to.
    /// </summary>
    [Fact]
    public async Task A_host_with_no_writable_store_says_so_on_both_writes()
    {
        using IHost host = await HostAsync();

        HttpClient client = host.GetTestClient();

        HttpResponseMessage upsert = await client.PostAsJsonAsync("/dw-policies/rules", Rule());

        Assert.Equal(HttpStatusCode.NotImplemented, upsert.StatusCode);
        Assert.Contains("writable", await upsert.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Equal(
            HttpStatusCode.NotImplemented,
            (await client.DeleteAsync($"/dw-policies/rules/{Guid.NewGuid()}")).StatusCode);
    }

    // ---- what a body can say ------------------------------------------------------------------

    [Fact]
    public async Task A_body_that_is_not_json_is_a_bad_request()
    {
        using IHost host = await HostAsync(new InMemoryPolicyStore());

        HttpResponseMessage response = await host.GetTestClient().PostAsync(
            "/dw-policies/rules",
            new StringContent("{ not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A window that closes before it opens is refused rather than stored. Stored, it reads to an
    /// operator as a grant that was configured and to an auditor as a control in force, and it is
    /// neither.
    /// </summary>
    [Fact]
    public async Task A_validity_window_that_closes_before_it_opens_is_refused()
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
                effect = "Deny",
                validFrom = DateTimeOffset.UtcNow.AddDays(1),
                validTo = DateTimeOffset.UtcNow
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// A rule stored without being applied. The listing has to report that, or an operator reading
    /// it counts a control that is switched off among the ones in force.
    /// </summary>
    [Fact]
    public async Task A_rule_can_be_stored_without_being_enabled()
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
                enabled = false
            }));

        Assert.False(written.GetProperty("enabled").GetBoolean());
    }

    /// <summary>
    /// A rule that applies to everybody needs no key, and every other kind does.
    /// </summary>
    [Fact]
    public async Task A_global_rule_needs_no_key_and_a_role_rule_does()
    {
        using IHost host = await HostAsync(new InMemoryPolicyStore());

        HttpClient client = host.GetTestClient();

        JsonElement global = await JsonAsync(await client.PostAsJsonAsync(
            "/dw-policies/rules",
            new RuleRequest(
                null, "Global", null, typeof(Staff).FullName!, "Department", "Select", "Deny")));

        Assert.Equal("Global", global.GetProperty("subjectKind").GetString());

        HttpResponseMessage keyless = await client.PostAsJsonAsync(
            "/dw-policies/rules",
            new RuleRequest(
                null, "Role", null, typeof(Staff).FullName!, "Department", "Select", "Deny"));

        Assert.Equal(HttpStatusCode.BadRequest, keyless.StatusCode);
    }

    // ---- filtering the listing -----------------------------------------------------------------

    /// <summary>
    /// A subject filter naming only a kind matches every rule of that kind.
    /// </summary>
    [Fact]
    public async Task A_subject_filter_with_no_key_matches_the_whole_kind()
    {
        using IHost host = await HostAsync(new InMemoryPolicyStore());

        HttpClient client = host.GetTestClient();

        await client.PostAsJsonAsync("/dw-policies/rules", Rule());

        JsonElement byKind = await JsonAsync(await client.GetAsync("/dw-policies/rules?subject=Role"));

        Assert.Single(byKind.EnumerateArray());
    }

    /// <summary>
    /// A kind nothing defines filters to nothing rather than falling through to everything.
    /// </summary>
    /// <remarks>
    /// The fail-open reading of an unparsable filter is to ignore it, which would answer a
    /// misspelled query with the entire rule set — the map an operator is least entitled to by
    /// accident.
    /// </remarks>
    [Fact]
    public async Task An_unknown_subject_kind_matches_nothing_rather_than_everything()
    {
        using IHost host = await HostAsync(new InMemoryPolicyStore());

        HttpClient client = host.GetTestClient();

        await client.PostAsJsonAsync("/dw-policies/rules", Rule());

        JsonElement none = await JsonAsync(
            await client.GetAsync("/dw-policies/rules?subject=Wizard:merlin"));

        Assert.Empty(none.EnumerateArray());
    }

    /// <summary>
    /// A rule about a type the host never exposed is not this surface's to show, so the listing
    /// walks the exposed types rather than handing out everything a store holds.
    /// </summary>
    [Fact]
    public async Task A_rule_about_an_unexposed_type_is_not_listed()
    {
        InMemoryPolicyStore store = new();

        await store.UpsertAsync(
            new PolicyRule(
                DwSubjectKind.Role,
                "Manager",
                typeof(Office).FullName!,
                "City",
                PolicyFeature.Select,
                PolicyEffect.Deny),
            CancellationToken.None);

        using IHost host = await HostAsync(store);

        JsonElement listed = await JsonAsync(
            await host.GetTestClient().GetAsync("/dw-policies/rules"));

        Assert.Empty(listed.EnumerateArray());
    }

    // ---- explain and simulate ------------------------------------------------------------------

    [Fact]
    public async Task Explaining_nothing_is_not_found()
    {
        using IHost host = await HostAsync();

        HttpResponseMessage response = await host.GetTestClient()
            .PostAsJsonAsync("/dw-policies/explain", new { entity = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Simulating_without_a_filter_is_a_bad_request()
    {
        using IHost host = await HostAsync();

        HttpResponseMessage response = await host.GetTestClient()
            .PostAsJsonAsync("/dw-policies/simulate", new SimulateRequest(Entity, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Simulating_against_an_entity_nobody_exposed_is_not_found()
    {
        using IHost host = await HostAsync();

        HttpResponseMessage response = await host.GetTestClient()
            .PostAsJsonAsync("/dw-policies/simulate", new { entity = "NoSuchThing", filter = new { } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The entity resolves by its public name and by the full type name a rule is matched on, so an
    /// operator holding a rule can look up the schema of what it targets.
    /// </summary>
    [Fact]
    public async Task An_entity_resolves_by_public_name_and_by_type_name()
    {
        using IHost host = await HostAsync();

        HttpClient client = host.GetTestClient();

        Assert.Equal(
            HttpStatusCode.OK, (await client.GetAsync($"/dw-policies/schema/{Entity}")).StatusCode);

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync($"/dw-policies/schema/{typeof(Staff).FullName}")).StatusCode);
    }
}
