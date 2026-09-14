using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Storage;
using Microsoft.Extensions.Configuration;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers reading the enforcement posture from configuration.
/// </summary>
/// <remarks>
/// The point of binding is that a deployment can move its posture out of source without losing any
/// of the refusals that make the posture worth having. So most of these are about what binding
/// refuses rather than what it accepts: an unknown key, a cap below one, a salt too short. A binder
/// that silently shrugged at any of them would be a configuration file that looks like it is
/// enforcing something.
/// </remarks>
public class PolicyConfigurationTests
{
    private static IConfiguration Section(params (string Key, string Value)[] values)
    {
        Dictionary<string, string?> settings = new();

        foreach ((string key, string value) in values)
        {
            settings[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    // ---- what it reads -------------------------------------------------------------------------

    [Fact]
    public void A_section_sets_the_caps_it_names()
    {
        DwPolicyOptions options = new DwPolicyOptions().Bind(Section(
            ("Caps:MaxPageSize", "25"),
            ("Caps:SchemaDepth", "3"),
            ("Caps:SchemaCycleLimit", "4"),
            ("Caps:MaxSchemaFields", "100")));

        Assert.Equal(25, options.Caps.MaxPageSize);
        Assert.Equal(3, options.Caps.SchemaDepth);
        Assert.Equal(4, options.Caps.SchemaCycleLimit);
        Assert.Equal(100, options.Caps.MaxSchemaFields);
    }

    [Fact]
    public void A_section_sets_the_posture_it_names()
    {
        DwPolicyOptions options = new DwPolicyOptions().Bind(Section(
            ("Tier", "Strict"),
            ("DryRun", "true"),
            ("StoreFailure", "FailClosed"),
            ("MaxSnapshotAge", "00:05:00"),
            ("RefreshInterval", "00:00:10")));

        Assert.Equal(DwTier.Strict, options.Tier);
        Assert.True(options.DryRun);
        Assert.Equal(StoreFailureMode.FailClosed, options.StoreFailure);
        Assert.Equal(TimeSpan.FromMinutes(5), options.MaxSnapshotAge);
        Assert.Equal(TimeSpan.FromSeconds(10), options.RefreshInterval);
    }

    [Fact]
    public void An_empty_section_leaves_every_default_in_place()
    {
        DwPolicyOptions options = new DwPolicyOptions().Bind(Section());

        Assert.Equal(DwTier.Convenience, options.Tier);
        Assert.Equal(2, options.Caps.SchemaDepth);
        Assert.Equal(DwCaps.DefaultMinGroupSize, options.Caps.MinGroupSize);
        Assert.False(options.Caps.IsMinGroupSizeSet);
    }

    // ---- what it refuses -----------------------------------------------------------------------

    /// <summary>
    /// A key nothing answers to refuses to start, rather than being ignored.
    /// </summary>
    /// <remarks>
    /// The binder's own default is to shrug at an unmatched key, which would let a misspelling sit
    /// in a file doing nothing while the deployment believes it has set a control. That is the
    /// fail-open shape this whole layer is built against: the floor would be off, the file would say
    /// it was on, and nothing anywhere would disagree.
    /// </remarks>
    [Fact]
    public void A_key_nothing_answers_to_refuses_to_start()
    {
        Assert.ThrowsAny<InvalidOperationException>(
            () => new DwPolicyOptions().Bind(Section(("Caps:MinGropSize", "20"))));

        Assert.ThrowsAny<InvalidOperationException>(
            () => new DwPolicyOptions().Bind(Section(("Teir", "Strict"))));
    }

    /// <summary>Every setter's own validation still applies to a value that arrived from a file.</summary>
    [Fact]
    public void A_value_the_property_refuses_is_refused_here_too()
    {
        // A cap below one.
        Assert.ThrowsAny<Exception>(
            () => new DwPolicyOptions().Bind(Section(("Caps:MaxPageSize", "0"))));

        // A salt short enough to be brute-forced offline.
        Assert.ThrowsAny<Exception>(
            () => new DwPolicyOptions().Bind(Section(("HashSalt", "pepper"))));

        // A snapshot age that is not a positive interval.
        Assert.ThrowsAny<Exception>(
            () => new DwPolicyOptions().Bind(Section(("MaxSnapshotAge", "00:00:00"))));
    }

    [Fact]
    public void A_frozen_posture_cannot_be_bound_over()
    {
        DwPolicyOptions options = new();

        options.Freeze();

        Assert.ThrowsAny<Exception>(() => options.Bind(Section(("Caps:MaxPageSize", "25"))));
    }

    // ---- the group floor's opt-out ---------------------------------------------------------------

    /// <summary>
    /// The floor's two instructions survive the round trip through a file, because the distinction
    /// lives in the setter rather than in the default.
    /// </summary>
    /// <remarks>
    /// Saying nothing leaves it unset, which reads as the safe value. Writing one records a
    /// deliberate choice and switches the floor off. A deployment gets both sentences from
    /// configuration exactly as it gets them from code, which is the property that would have been
    /// quietly lost if the default had been a constant the binder overwrote.
    /// </remarks>
    [Fact]
    public void The_group_floor_can_be_switched_off_from_configuration()
    {
        DwPolicyOptions silent = new DwPolicyOptions().Bind(Section());

        Assert.Equal(5, silent.Caps.MinGroupSize);
        Assert.False(silent.Caps.IsMinGroupSizeSet);

        DwPolicyOptions off = new DwPolicyOptions().Bind(Section(("Caps:MinGroupSize", "1")));

        Assert.Equal(1, off.Caps.MinGroupSize);
        Assert.True(off.Caps.IsMinGroupSizeSet);

        DwPolicyOptions stricter = new DwPolicyOptions().Bind(Section(("Caps:MinGroupSize", "10")));

        Assert.Equal(10, stricter.Caps.MinGroupSize);
        Assert.True(stricter.Caps.IsMinGroupSizeSet);
    }
}
