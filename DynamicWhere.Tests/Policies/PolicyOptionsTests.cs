using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers <see cref="DwPolicyOptions"/> defaults and the freeze that makes the enforcement posture
/// immutable once the application has started.
/// </summary>
public class PolicyOptionsTests
{
    [Fact]
    public void Defaults_are_the_convenience_tier_with_dry_run_off()
    {
        DwPolicyOptions options = new();

        Assert.Equal(DwTier.Convenience, options.Tier);
        Assert.False(options.DryRun);
    }

    [Fact]
    public void Default_caps_are_set_rather_than_unbounded()
    {
        DwCaps caps = new DwPolicyOptions().Caps;

        Assert.Equal(1000, caps.MaxPageSize);
        Assert.Equal(50, caps.MaxConditions);
        Assert.Equal(10, caps.MaxOrderFields);
        Assert.Equal(4, caps.MaxNavigationDepth);
    }

    [Fact]
    public void Freezing_prevents_any_later_change_to_the_posture()
    {
        DwPolicyOptions options = new() { Tier = DwTier.Strict };

        options.Freeze();

        Assert.Throws<InvalidOperationException>(() => options.Tier = DwTier.Convenience);
        Assert.Throws<InvalidOperationException>(() => options.DryRun = true);
        Assert.Equal(DwTier.Strict, options.Tier);
    }

    [Fact]
    public void Caps_are_frozen_along_with_the_options_that_hold_them()
    {
        DwPolicyOptions options = new();

        options.Freeze();

        Assert.Throws<InvalidOperationException>(() => options.Caps.MaxPageSize = 5);
    }

    [Fact]
    public void Freezing_twice_is_harmless()
    {
        DwPolicyOptions options = new();

        options.Freeze();
        options.Freeze();

        Assert.True(options.IsFrozen);
    }

    [Fact]
    public void A_cap_below_one_is_rejected()
    {
        DwCaps caps = new DwPolicyOptions().Caps;

        Assert.Throws<ArgumentOutOfRangeException>(() => caps.MaxPageSize = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => caps.MaxConditions = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => caps.MaxOrderFields = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => caps.MaxNavigationDepth = -5);
    }

    [Fact]
    public void Store_failure_defaults_to_last_known_good_bounded_by_a_ceiling()
    {
        DwPolicyOptions options = new();

        Assert.Equal(StoreFailureMode.LastKnownGood, options.StoreFailure);
        Assert.Equal(TimeSpan.FromMinutes(15), options.MaxSnapshotAge);
        Assert.Equal(TimeSpan.FromSeconds(30), options.RefreshInterval);
    }

    [Fact]
    public void The_staleness_ceiling_cannot_be_disabled()
    {
        DwPolicyOptions options = new();

        // Zero means every snapshot is instantly stale, and a negative value means the comparison
        // never fires again. The second is the dangerous reading, and neither is a configuration
        // anyone wants, so both are refused rather than interpreted.
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MaxSnapshotAge = TimeSpan.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => options.MaxSnapshotAge = TimeSpan.FromMinutes(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => options.RefreshInterval = TimeSpan.Zero);
    }

    [Fact]
    public void The_store_posture_is_frozen_with_the_rest()
    {
        DwPolicyOptions options = new();

        options.Freeze();

        Assert.Throws<InvalidOperationException>(
            () => options.StoreFailure = StoreFailureMode.FailClosed);
        Assert.Throws<InvalidOperationException>(
            () => options.MaxSnapshotAge = TimeSpan.FromMinutes(1));
        Assert.Throws<InvalidOperationException>(
            () => options.RefreshInterval = TimeSpan.FromMinutes(1));
    }
}
