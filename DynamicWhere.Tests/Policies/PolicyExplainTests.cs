using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The decision chain behind one field, as the explain endpoint reports it: what was decided, what
/// decided it, what it outranked, and what tied with it.
/// </summary>
/// <remarks>
/// Computed by the resolver rather than by the endpoint. Precedence is four comparison rules, and a
/// second implementation of them in a transport would eventually disagree with the first — leaving
/// an operator a decision chain that contradicts the decision, which is worse than no chain at all.
/// </remarks>
public class PolicyExplainTests
{
    private static PolicyExplanation Explain(
        FakePolicyProvider provider, string field = "Name", DwPolicyContext? context = null) =>
        new PolicyResolver(new[] { provider })
            .Explain(typeof(PlainProduct), field, context ?? new DwPolicyContext());

    private static FeatureExplanation For(PolicyExplanation explanation, PolicyFeature feature) =>
        Assert.Single(explanation.Features, f => f.Feature == feature);

    [Fact]
    public void An_explanation_reports_the_decision_it_explains()
    {
        PolicyExplanation explanation = Explain(new FakePolicyProvider()
            .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicRole));

        Assert.Equal("Name", explanation.FieldPath);
        Assert.Equal(typeof(PlainProduct).FullName, explanation.EntityType);
        Assert.Equal(PolicyEffect.Deny, explanation.Policy.EffectFor(PolicyFeature.Where));
    }

    [Fact]
    public void Every_feature_is_accounted_for()
    {
        PolicyExplanation explanation = Explain(new FakePolicyProvider());

        Assert.Equal(6, explanation.Features.Count);
        Assert.All(explanation.Features, f => Assert.Null(f.DecidedBy));
        Assert.All(explanation.Features, f => Assert.Equal(PolicyEffect.Allow, f.Effect));
    }

    [Fact]
    public void The_deciding_source_is_named()
    {
        FeatureExplanation where = For(
            Explain(new FakePolicyProvider()
                .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicRole)),
            PolicyFeature.Where);

        Assert.NotNull(where.DecidedBy);
        Assert.Equal(PolicyLevel.DynamicRole, where.Level);
        Assert.Equal(PolicyEffect.Deny, where.Effect);
    }

    /// <summary>
    /// The "Overrode" line of section 5.7's sample output. A weaker fragment is discarded rather
    /// than merged, and an operator wondering why their rule did nothing needs to see it named.
    /// </summary>
    [Fact]
    public void What_the_winner_outranked_is_reported()
    {
        FeatureExplanation where = For(
            Explain(new FakePolicyProvider()
                .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.SealedAttribute)
                .Add("Name", PolicyFeature.Where, PolicyEffect.Allow, PolicyLevel.DynamicGlobal)),
            PolicyFeature.Where);

        Assert.Equal(PolicyEffect.Deny, where.Effect);
        Assert.Single(where.Overrode);
        Assert.Empty(where.TiedWith);
    }

    /// <summary>
    /// Carried in from Phase 1 Task 11. When two fragments tie on level, specificity, priority and
    /// effect, the resolver keeps whichever it swept first — the decided effect is identical either
    /// way, but naming one of them as "the" reason would credit a rule that contributed no more
    /// than its twin. Both are reported.
    /// </summary>
    [Fact]
    public void Fragments_tied_on_every_pass_are_all_reported()
    {
        FeatureExplanation where = For(
            Explain(new FakePolicyProvider()
                .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicRole)
                .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicRole)),
            PolicyFeature.Where);

        Assert.NotNull(where.DecidedBy);
        Assert.Single(where.TiedWith);
        Assert.True(where.IsAttributionAmbiguous);
    }

    [Fact]
    public void A_lone_winner_is_not_ambiguous()
    {
        FeatureExplanation where = For(
            Explain(new FakePolicyProvider()
                .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicRole)),
            PolicyFeature.Where);

        Assert.False(where.IsAttributionAmbiguous);
        Assert.Empty(where.TiedWith);
    }

    /// <summary>
    /// A tie is broken by the strongest effect before it becomes a tie at all, so two fragments that
    /// disagree are an override rather than an ambiguity.
    /// </summary>
    [Fact]
    public void Two_fragments_disagreeing_at_one_level_are_an_override_not_a_tie()
    {
        FeatureExplanation where = For(
            Explain(new FakePolicyProvider()
                .Add("Name", PolicyFeature.Where, PolicyEffect.Allow, PolicyLevel.DynamicRole)
                .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicRole)),
            PolicyFeature.Where);

        Assert.Equal(PolicyEffect.Deny, where.Effect);
        Assert.Empty(where.TiedWith);
        Assert.Single(where.Overrode);
    }

    /// <summary>
    /// A fragment speaking to another feature is neither an override nor a tie for this one. Listing
    /// it would tell an operator their Select rule lost a contest it never entered.
    /// </summary>
    [Fact]
    public void A_fragment_for_another_feature_is_not_reported_against_this_one()
    {
        FeatureExplanation where = For(
            Explain(new FakePolicyProvider()
                .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicRole)
                .Add("Name", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicGlobal)),
            PolicyFeature.Where);

        Assert.Empty(where.Overrode);
        Assert.Empty(where.TiedWith);
    }

    /// <summary>
    /// A carrier decides nothing, so it appears in no feature's chain. After Phase 7 it covers no
    /// feature at all, which is what stops a sealed alias outranking a runtime denial.
    /// </summary>
    [Fact]
    public void A_carrier_is_reported_against_no_feature()
    {
        PolicyExplanation explanation = Explain(new FakePolicyProvider()
            .AddAlias("Name", "public_name", PolicyLevel.SealedAttribute)
            .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicRole));

        FeatureExplanation where = For(explanation, PolicyFeature.Where);

        Assert.Equal(PolicyEffect.Deny, where.Effect);
        Assert.Empty(where.Overrode);
        Assert.Equal("public_name", explanation.Policy.Alias);
    }

    /// <summary>
    /// A wildcard beaten by a field-specific rule is the ordinary way a broad denial is relaxed one
    /// field at a time, and the explanation has to show which one did it.
    /// </summary>
    [Fact]
    public void A_wildcard_outranked_by_a_named_field_is_reported()
    {
        FeatureExplanation where = For(
            Explain(new FakePolicyProvider()
                .Add(PolicyFragment.Wildcard, PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicGlobal)
                .Add("Name", PolicyFeature.Where, PolicyEffect.Allow, PolicyLevel.DynamicGlobal)),
            PolicyFeature.Where);

        Assert.Equal(PolicyEffect.Allow, where.Effect);
        Assert.Single(where.Overrode);
    }

    [Fact]
    public void The_explanation_carries_the_facts_as_resolved()
    {
        PolicyExplanation explanation =
            new PolicyResolver(new[] { new AttributePolicyProvider() })
                .Explain(typeof(DescribedEmployee), "Salary", new DwPolicyContext());

        Assert.Equal(10, explanation.Policy.CostWeight);
        Assert.True(explanation.Policy.IsAudited(PolicyFeature.Select));
    }

    // ---- guards --------------------------------------------------------------------------------

    [Fact]
    public void Explaining_nothing_is_refused()
    {
        PolicyResolver resolver = new(new[] { new AttributePolicyProvider() });

        Assert.Throws<ArgumentNullException>(
            () => resolver.Explain(null!, "Name", new DwPolicyContext()));

        Assert.Throws<ArgumentNullException>(
            () => resolver.Explain(typeof(PlainProduct), "Name", null!));

        Assert.Throws<ArgumentException>(
            () => resolver.Explain(typeof(PlainProduct), "  ", new DwPolicyContext()));
    }

    /// <summary>
    /// The explanation must agree with the decision, or it is worse than useless. Asserting they
    /// come from the same resolution is the point of computing both here.
    /// </summary>
    [Fact]
    public void The_explanation_agrees_with_the_resolution()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Name", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.SealedAttribute)
            .Add("Name", PolicyFeature.Select, PolicyEffect.Mask, PolicyLevel.DynamicRole)
            .Add("Name", PolicyFeature.Order, PolicyEffect.Allow, PolicyLevel.DynamicGlobal);

        PolicyResolver resolver = new(new[] { provider });
        DwPolicyContext context = new();

        FieldPolicy resolved = resolver.Resolve(typeof(PlainProduct), "Name", context);
        PolicyExplanation explained = resolver.Explain(typeof(PlainProduct), "Name", context);

        foreach (FeatureExplanation feature in explained.Features)
        {
            Assert.Equal(resolved.EffectFor(feature.Feature), feature.Effect);
        }
    }
}
