using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// A forced null check on a date member that is reached through an optional navigation.
/// </summary>
/// <remarks>
/// The scope is carried onto every type that reaches the member, as <c>Clearance.GrantedAt</c> on a
/// <see cref="Visit"/>. The builder used to decide the guard from the member's type alone, and a
/// <c>DateTime</c> cannot be null, so the forced <c>IsNotNull</c> was emitted as <c>true</c>: every
/// visit came back, cleared or not, and a forced <c>IsNull</c> returned nothing. A scope that stops
/// filtering is the failure this layer exists to prevent, so this runs on both test legs.
/// </remarks>
public class ForcedDateScopeTests
{
    private static PolicyQueryable<T> Guarded<T>(IEnumerable<T> rows) where T : class
    {
        DwPolicyOptions options = new();

        options.Freeze();

        return rows.AsQueryable().ApplyPolicy(
            new DwPolicyContext(),
            options,
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));
    }

    [Fact]
    public void A_forced_IsNotNull_behind_an_optional_navigation_keeps_filtering()
    {
        Visit[] visits =
        {
            new() { Id = 1, Clearance = new Clearance { Id = 1, GrantedAt = new DateTime(2026, 1, 1) } },
            new() { Id = 2, Clearance = null }
        };

        FilterResult<Visit> result = Guarded(visits).ToList(new Filter());

        Assert.Equal(new[] { 1 }, result.Data.Select(visit => visit.Id));
    }

    [Fact]
    public void A_forced_IsNull_behind_an_optional_navigation_keeps_its_rows()
    {
        Shipment[] shipments =
        {
            new() { Id = 1, Cancellation = new Cancellation { Id = 1, CancelledAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) } },
            new() { Id = 2, Cancellation = null }
        };

        FilterResult<Shipment> result = Guarded(shipments).ToList(new Filter());

        Assert.Equal(new[] { 2 }, result.Data.Select(shipment => shipment.Id));
    }
}

/// <summary>A visit, which only counts once its clearance has been granted.</summary>
internal class Visit
{
    public int Id { get; set; }

    public Clearance? Clearance { get; set; }
}

internal class Clearance
{
    public int Id { get; set; }

    [DwForceWhere(Operator.IsNotNull)]
    public DateTime GrantedAt { get; set; }
}

/// <summary>A shipment, which only counts while nothing has cancelled it.</summary>
internal class Shipment
{
    public int Id { get; set; }

    public Cancellation? Cancellation { get; set; }
}

internal class Cancellation
{
    public int Id { get; set; }

    [DwForceWhere(Operator.IsNull)]
    public DateTimeOffset CancelledAt { get; set; }
}
