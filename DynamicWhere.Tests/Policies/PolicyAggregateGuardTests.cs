using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Design section 7.2's first mitigation: a field whose value is transformed on its way out may not
/// be aggregated, unless the attribute that transforms it opts in.
/// </summary>
/// <remarks>
/// Aggregation runs in SQL, against the stored values, long before any transform applies. So
/// <c>MAX</c> over a masked salary returns the real maximum and the mask protected nothing — the
/// transform is applied to a column the caller never asked to see.
/// <para>
/// Every transform, not only <c>[DwMask]</c>. A generalized value is rounded on output and exact in
/// the database; a defaulted one is replaced on output and untouched in the database. The spec names
/// masking because it is the common case, and covering only masking would leave
/// <c>[DwGeneralize]</c> — the attribute it recommends for numeric fields, which is to say the ones
/// people aggregate — with no protection at all.
/// </para>
/// </remarks>
public class PolicyAggregateGuardTests
{
    private static FieldPolicy Resolve<T>(string path) =>
        new PolicyResolver(new[] { new AttributePolicyProvider() })
            .Resolve(typeof(T), path, new DwPolicyContext());

    // ---- the denial ----------------------------------------------------------------------------

    [Fact]
    public void A_masked_field_may_not_be_aggregated()
    {
        Assert.False(Resolve<Guarded>("Salary").Allows(PolicyFeature.Aggregate));
    }

    /// <summary>
    /// The case the spec's wording would have missed. Rounding to the nearest ten thousand happens
    /// on output; <c>MAX</c> reads the column.
    /// </summary>
    [Fact]
    public void A_generalized_field_may_not_be_aggregated()
    {
        Assert.False(Resolve<Guarded>("Bonus").Allows(PolicyFeature.Aggregate));
    }

    [Fact]
    public void A_defaulted_field_may_not_be_aggregated()
    {
        Assert.False(Resolve<Guarded>("Bounty").Allows(PolicyFeature.Aggregate));
    }

    [Fact]
    public void A_truncated_field_may_not_be_aggregated()
    {
        Assert.False(Resolve<Guarded>("Summary").Allows(PolicyFeature.Aggregate));
    }

    /// <summary>
    /// The denial is not a blanket refusal of the field. Everything else it was permitted to do it
    /// still does — a mask that also stopped the field being filtered on would be a different
    /// control, and a surprising one.
    /// </summary>
    [Fact]
    public void The_denial_touches_only_aggregation()
    {
        FieldPolicy policy = Resolve<Guarded>("Salary");

        Assert.True(policy.Allows(PolicyFeature.Where));
        Assert.True(policy.Allows(PolicyFeature.Select));
        Assert.True(policy.Allows(PolicyFeature.Group));
    }

    [Fact]
    public void An_untransformed_field_is_unaffected()
    {
        Assert.True(Resolve<Guarded>("Headcount").Allows(PolicyFeature.Aggregate));
    }

    // ---- the opt-in ----------------------------------------------------------------------------

    [Fact]
    public void A_field_that_opts_in_may_be_aggregated()
    {
        Assert.True(Resolve<Guarded>("Tenure").Allows(PolicyFeature.Aggregate));
    }

    /// <summary>
    /// A chain is only as permissive as its strictest stage. Adding a truncation on top of a mask
    /// that allowed aggregation takes the permission away again, which is the direction that is safe
    /// to get wrong.
    /// </summary>
    [Fact]
    public void One_stage_refusing_takes_the_permission_away()
    {
        Assert.False(Resolve<Guarded>("Reference").Allows(PolicyFeature.Aggregate));
    }

    // ---- through a runtime rule ----------------------------------------------------------------

    /// <summary>
    /// A rule may set a transform, so a rule must be able to close this channel too. Enforcing in
    /// the resolver rather than in the attribute provider is what makes that free.
    /// </summary>
    [Fact]
    public void A_transform_a_rule_set_denies_aggregation_as_well()
    {
        FakePolicyProvider rules = new FakePolicyProvider(new[]
        {
            new PolicyFragment(
                "Headcount",
                PolicyFeature.Select,
                PolicyEffect.Mask,
                PolicyLevel.DynamicRole,
                PolicySource.FromRule("r-1", "Role=Ops"),
                transform: new GeneralizeStage(GeneralizeMode.Round, step: 10))
        });

        FieldPolicy policy = new PolicyResolver(
                new IDwPolicyProvider[] { new AttributePolicyProvider(), rules })
            .Resolve(typeof(Guarded), "Headcount", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Aggregate));
    }

    /// <summary>
    /// The chain reports the permission it composed, so the resolver is reading a decision the
    /// transform made rather than making one of its own.
    /// </summary>
    [Fact]
    public void The_chain_composes_the_permission()
    {
        Assert.False(new ValueTransform(mask: new MaskStage(MaskStrategy.Full)).AllowsAggregate);

        Assert.True(new ValueTransform(
            mask: new MaskStage(MaskStrategy.Full, allowAggregate: true)).AllowsAggregate);

        Assert.False(new ValueTransform(
            mask: new MaskStage(MaskStrategy.Full, allowAggregate: true),
            truncate: new TruncateStage(10)).AllowsAggregate);
    }

    /// <summary>An empty chain permits everything, because it transforms nothing.</summary>
    [Fact]
    public void An_empty_chain_permits_aggregation()
    {
        Assert.True(new ValueTransform().AllowsAggregate);
    }
}

/// <summary>
/// A type whose every transform is declared for one purpose: to be aggregated at, and refused.
/// </summary>
internal class Guarded
{
    public int Id { get; set; }

    public string Department { get; set; } = string.Empty;

    /// <summary>Masked, so aggregation is refused.</summary>
    [DwMask(MaskStrategy.Full)]
    public string Salary { get; set; } = string.Empty;

    /// <summary>Rounded on output and exact in the database.</summary>
    [DwGeneralize(GeneralizeMode.Round, Step = 10000)]
    public decimal Bonus { get; set; }

    /// <summary>Replaced on output and untouched in the database.</summary>
    [DwDefault("0")]
    public decimal Bounty { get; set; }

    /// <summary>Capped on output.</summary>
    [DwTruncate(20)]
    public string Summary { get; set; } = string.Empty;

    /// <summary>Nothing transforms it, so nothing stops it being aggregated.</summary>
    public int Headcount { get; set; }

    /// <summary>Masked, and deliberately still aggregatable.</summary>
    [DwMask(MaskStrategy.Full, AllowAggregate = true)]
    public string Tenure { get; set; } = string.Empty;

    /// <summary>A permissive mask under a stage that is not permissive.</summary>
    [DwMask(MaskStrategy.Full, AllowAggregate = true)]
    [DwTruncate(8)]
    public string Reference { get; set; } = string.Empty;
}
