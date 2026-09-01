using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers the immutable zones a store loads into, and the split between them.
/// </summary>
public class StoreSnapshotTests
{
    private const string Entity = "DynamicWhere.Tests.Policies.Staff";

    private static PolicyRule Rule(
        DwSubjectKind kind = DwSubjectKind.Role,
        string? key = "Manager",
        string entity = Entity,
        bool enabled = true) =>
        new(kind, key, entity, "Salary", PolicyFeature.Select, PolicyEffect.Deny, enabled: enabled);

    [Fact]
    public void Rules_are_indexed_by_the_full_name_the_rule_stores()
    {
        StoreSnapshot snapshot = new(41, DateTimeOffset.UtcNow, new[] { Rule() });

        Assert.Single(snapshot.For(typeof(Staff)));
        Assert.Empty(snapshot.For(typeof(Office)));
        Assert.Equal(41, snapshot.Version);
    }

    [Fact]
    public void An_entity_name_matches_case_insensitively()
    {
        // Hand-written configuration and database collations both vary. A rule that fails to match
        // its own type is a denial that does nothing.
        StoreSnapshot snapshot = new(
            1, DateTimeOffset.UtcNow, new[] { Rule(entity: Entity.ToUpperInvariant()) });

        Assert.Single(snapshot.For(typeof(Staff)));
    }

    [Fact]
    public void Disabled_rules_are_dropped_at_load()
    {
        // Safe to bake in: enablement changes only through a write, and a write bumps the version.
        StoreSnapshot snapshot = new(
            1, DateTimeOffset.UtcNow, new[] { Rule(enabled: false), Rule() });

        Assert.Equal(1, snapshot.Count);
    }

    [Fact]
    public void A_user_rule_in_the_broad_zone_is_refused_rather_than_dropped()
    {
        // Dropping it would turn a store's zone-split bug into a user-level denial that quietly
        // stops applying — which is precisely the failure this phase exists to make impossible.
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new StoreSnapshot(
                1, DateTimeOffset.UtcNow, new[] { Rule(DwSubjectKind.User, "u1") }));

        Assert.Contains("narrow zone", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_broad_kind_is_accepted()
    {
        PolicyRule[] rules =
        {
            Rule(DwSubjectKind.Global, null),
            Rule(DwSubjectKind.Tenant, "acme"),
            Rule(DwSubjectKind.Role, "Manager"),
            Rule(DwSubjectKind.Custom, "region-eu")
        };

        Assert.Equal(4, new StoreSnapshot(1, DateTimeOffset.UtcNow, rules).Count);
    }

    [Fact]
    public void A_null_rule_is_refused()
    {
        Assert.Throws<ArgumentException>(
            () => new StoreSnapshot(1, DateTimeOffset.UtcNow, new PolicyRule?[] { null }!));
    }

    [Fact]
    public void The_empty_snapshot_is_stale_under_every_ceiling()
    {
        // It stands for "no load has succeeded", which is not the same as "the store is empty" —
        // an empty store loads a real snapshot with a real timestamp.
        Assert.True(StoreSnapshot.Empty.IsStale(DateTimeOffset.UtcNow, TimeSpan.FromDays(365000)));
        Assert.Equal(0, StoreSnapshot.Empty.Version);
    }

    [Fact]
    public void Staleness_is_measured_from_the_load()
    {
        DateTimeOffset loaded = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        StoreSnapshot snapshot = new(1, loaded, Array.Empty<PolicyRule>());

        Assert.False(snapshot.IsStale(loaded.AddMinutes(15), TimeSpan.FromMinutes(15)));
        Assert.True(snapshot.IsStale(loaded.AddMinutes(16), TimeSpan.FromMinutes(15)));
    }

    // ---------------------------------------------------------------- narrow zone

    [Fact]
    public void The_narrow_zone_holds_user_rules_only()
    {
        NarrowZone zone = new(7, new[] { Rule(DwSubjectKind.User, "u1") });

        Assert.Single(zone.For(typeof(Staff)));
        Assert.Equal(7, zone.Version);

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new NarrowZone(7, new[] { Rule(DwSubjectKind.Role, "Manager") }));

        Assert.Contains("broad zone", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_empty_narrow_zone_is_a_real_answer()
    {
        // "This caller has no user rules" is a legitimate result. It is distinguishable from "no
        // narrow zone was ever loaded" because the latter has no zone at all, not an empty one.
        Assert.Equal(0, NarrowZone.Empty.Count);
        Assert.Empty(NarrowZone.Empty.For(typeof(Staff)));
    }

    [Fact]
    public void The_narrow_zone_drops_disabled_rules_too()
    {
        NarrowZone zone = new(
            1,
            new[]
            {
                new PolicyRule(
                    DwSubjectKind.User, "u1", Entity, "Salary", PolicyFeature.Select,
                    PolicyEffect.Deny, enabled: false)
            });

        Assert.Equal(0, zone.Count);
    }
}
