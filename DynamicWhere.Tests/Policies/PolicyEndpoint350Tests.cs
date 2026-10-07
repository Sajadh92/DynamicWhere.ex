using System.Net.Http.Json;
using System.Text.Json;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.AspNetCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// What 3.5.0 changed on the administrative surface, read through a real request pipeline: the JSON an
/// operator's tooling actually receives from <c>POST /schema</c> and <c>POST /simulate</c>.
/// </summary>
/// <remarks>
/// The core tests prove the decisions; these prove they reach the wire under the names the documentation
/// gives them, which only a serialized response can show.
/// </remarks>
[Collection("PolicyEndpoints")]
public class PolicyEndpoint350Tests
{
    private static Task<IHost> HostAsync() => PolicyEndpointHost.StartAsync();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static Task<HttpResponseMessage> SimulateAsync(IHost host, string entity, Filter filter) =>
        host.GetTestClient().PostAsJsonAsync("/dw-policies/simulate", new SimulateRequest(entity, filter));

    /// <summary>An order as the wire carries it: the field, and the direction by name or by number.</summary>
    private static string Order(JsonElement order)
    {
        JsonElement direction = order.GetProperty("direction");
        bool descending = direction.ValueKind == JsonValueKind.Number
            ? direction.GetInt32() == (int)Direction.Descending
            : direction.GetString() == nameof(Direction.Descending);

        return order.GetProperty("field").GetString() + (descending ? " desc" : string.Empty);
    }

    [Fact]
    public async Task Schema_says_once_for_the_entity_that_it_refuses_selects()
    {
        using IHost host = await HostAsync();

        JsonElement card = await JsonAsync(await host.GetTestClient().PostAsJsonAsync(
            "/dw-policies/schema", new { entity = PolicyBootstrap.CardRowName }));
        JsonElement staff = await JsonAsync(await host.GetTestClient().PostAsJsonAsync(
            "/dw-policies/schema", new { entity = PolicyEndpointHost.Entity }));

        Assert.True(card.GetProperty("refusesSelects").GetBoolean());
        Assert.False(staff.GetProperty("refusesSelects").GetBoolean());

        // A field's canSelect keeps meaning that its value comes back.
        Assert.Contains(
            card.GetProperty("fields").EnumerateArray(),
            field => field.GetProperty("path").GetString() == "Title" && field.GetProperty("canSelect").GetBoolean());
    }

    [Fact]
    public async Task Simulate_answers_a_selects_for_such_an_entity_with_SelectsRefused()
    {
        using IHost host = await HostAsync();

        JsonElement body = await JsonAsync(await SimulateAsync(
            host, PolicyBootstrap.CardRowName, new Filter { Selects = new List<string> { "Title" } }));

        Assert.False(body.GetProperty("wouldRun").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("filter").ValueKind);

        JsonElement refusal = body.GetProperty("refusal");

        Assert.Equal("SelectsRefused", refusal.GetProperty("code").GetString());
        Assert.Equal("*", refusal.GetProperty("field").GetString());
        Assert.Equal("Select", refusal.GetProperty("feature").GetString());
        Assert.Equal("DwEntityAttribute(RefuseSelects = true)", refusal.GetProperty("reason").GetString());
        Assert.Contains(
            body.GetProperty("trace").EnumerateArray(),
            decision => decision.GetProperty("field").GetString() == "*"
                        && decision.GetProperty("feature").GetString() == "Select"
                        && decision.GetProperty("action").GetString() == "Denied");

        // With no Selects the same entity simulates as a query that runs.
        JsonElement whole = await JsonAsync(await SimulateAsync(host, PolicyBootstrap.CardRowName, new Filter()));

        Assert.True(whole.GetProperty("wouldRun").GetBoolean());
    }

    [Fact]
    public async Task Simulate_shows_the_tiebreak_after_the_callers_orders()
    {
        using IHost host = await HostAsync();

        Filter filter = new()
        {
            Orders = new List<OrderBy> { new() { Sort = 7, Field = "Status", Direction = Direction.Ascending } }
        };

        JsonElement body = await JsonAsync(await SimulateAsync(host, PolicyBootstrap.QueueRowName, filter));

        Assert.True(body.GetProperty("wouldRun").GetBoolean());

        List<JsonElement> orders = body.GetProperty("filter").GetProperty("orders").EnumerateArray()
            .OrderBy(order => order.GetProperty("sort").GetInt32())
            .ToList();

        // Rank no caller may order by, so it is left out and recorded; Code and Id follow the caller's order.
        Assert.Equal(new[] { "Status", "Code desc", "Id" }, orders.Select(Order));
        Assert.Equal(new[] { 0, 1, 2 }, orders.Select(order => order.GetProperty("sort").GetInt32()));
        Assert.Contains(
            body.GetProperty("trace").EnumerateArray(),
            decision => decision.GetProperty("field").GetString() == "Rank"
                        && decision.GetProperty("action").GetString() == "Dropped"
                        && decision.GetProperty("reason").GetString()!.StartsWith("left out of the default order", StringComparison.Ordinal));

        // The caller's own filter is what was sent, and a caller who names a default field keeps it.
        JsonElement named = await JsonAsync(await SimulateAsync(host, PolicyBootstrap.QueueRowName, new Filter
        {
            Orders = new List<OrderBy> { new() { Sort = 1, Field = "Id", Direction = Direction.Descending } }
        }));

        Assert.Equal(
            new[] { "Id desc", "Code desc" },
            named.GetProperty("filter").GetProperty("orders").EnumerateArray()
                .OrderBy(order => order.GetProperty("sort").GetInt32())
                .Select(Order));
    }
}
