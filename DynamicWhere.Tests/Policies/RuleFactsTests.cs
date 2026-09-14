using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The four facts, written by a rule rather than declared by an attribute: through the rule's own
/// guards, through the document both stores serialize, and back out as a resolved policy.
/// </summary>
/// <remarks>
/// Two of the four are enforcement rather than decoration. A cost weight lost in transit
/// under-charges the query and slips it under the cap; a lost audit flag means the access happened
/// and nothing was written down. Both fail open and both fail quietly, which is why each is
/// mutation-checked on its own rather than covered by one round-trip assertion.
/// </remarks>
public class RuleFactsTests
{
    private const string Entity = "DynamicWhere.Tests.Policies.PlainProduct";

    private static PolicyRule Rule(
        FieldFacts facts,
        PolicyFeature features = PolicyFeature.None,
        PolicyEffect effect = PolicyEffect.Allow,
        string field = "Name") =>
        new(DwSubjectKind.Role, "Manager", Entity, field, features, effect, facts: facts);

    // ---- the rule's own guards -----------------------------------------------------------------

    /// <summary>
    /// A rule that states facts speaks to no feature, because it decides nothing. The refusal that
    /// used to make that impossible was written when a rule could only decide.
    /// </summary>
    [Fact]
    public void A_rule_may_state_facts_and_no_feature()
    {
        PolicyRule rule = Rule(FieldFacts.ForCost(7));

        Assert.Equal(PolicyFeature.None, rule.Features);
        Assert.Equal(7, rule.Facts?.CostWeight);
    }

    /// <summary>
    /// The original refusal, still standing for the case it was written for: a rule that neither
    /// decides anything nor carries anything is stored, applies to nothing, and reads as enforced.
    /// </summary>
    [Fact]
    public void A_rule_that_states_nothing_at_all_is_refused()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            new PolicyRule(
                DwSubjectKind.Role, "Manager", Entity, "Name",
                PolicyFeature.None, PolicyEffect.Allow));

        Assert.Contains("at least one feature", error.Message);
    }

    /// <summary>
    /// The dangerous shape: an operator means to deny a field, mistypes the features, and the rule
    /// happens to carry an alias — so it is accepted and the denial silently does nothing.
    /// </summary>
    [Fact]
    public void A_denial_that_names_no_feature_is_refused_even_when_it_carries_something()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            Rule(FieldFacts.ForCost(7), PolicyFeature.None, PolicyEffect.Deny));

        Assert.Contains("Deny", error.Message);
    }

    [Fact]
    public void A_mask_that_names_no_feature_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
            Rule(FieldFacts.ForCost(7), PolicyFeature.None, PolicyEffect.Mask));
    }

    /// <summary>
    /// The other carriers gain the same freedom, and for the same reason: naming a field decides
    /// nothing, so a rule doing only that has no feature to name.
    /// </summary>
    [Fact]
    public void A_rule_carrying_only_an_alias_may_name_no_feature()
    {
        PolicyRule rule = new(
            DwSubjectKind.Role, "Manager", Entity, "Name",
            PolicyFeature.None, PolicyEffect.Allow, alias: "public_name");

        Assert.Equal("public_name", rule.Alias);
    }

    [Fact]
    public void A_rule_may_still_decide_a_feature_and_state_facts_at_once()
    {
        PolicyRule rule = Rule(FieldFacts.ForAudit(PolicyFeature.All), PolicyFeature.Where, PolicyEffect.Deny);

        Assert.Equal(PolicyEffect.Deny, rule.Effect);
        Assert.Equal(PolicyFeature.All, rule.Facts?.AuditedFeatures);
    }

    [Fact]
    public void A_description_on_the_wildcard_is_refused()
    {
        Assert.Throws<ArgumentException>(() =>
            Rule(FieldFacts.ForLabel("Everything"), field: PolicyFragment.Wildcard));
    }

    /// <summary>
    /// "Every field of this type is expensive" is a budget an operator reasonably sets, so unlike a
    /// label a weight on the wildcard is kept.
    /// </summary>
    [Fact]
    public void A_cost_on_the_wildcard_is_accepted()
    {
        PolicyRule rule = Rule(FieldFacts.ForCost(3), field: PolicyFragment.Wildcard);

        Assert.Equal(3, rule.Facts?.CostWeight);
    }

    // ---- through the fragment ------------------------------------------------------------------

    [Fact]
    public void The_facts_reach_the_fragment_the_rule_becomes()
    {
        Assert.Equal(7, Rule(FieldFacts.ForCost(7)).ToFragment().Facts?.CostWeight);
    }

    /// <summary>
    /// End to end through a store: a rule relabels a field for one role and reweighs it, and the
    /// resolved policy reports both.
    /// </summary>
    [Fact]
    public async Task A_stored_rule_relabels_and_reweighs_a_field()
    {
        InMemoryPolicyStore store = new();

        await store.UpsertAsync(
            Rule(new FieldFacts(label: "Product name", costWeight: 4)), CancellationToken.None);

        StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, new ex.Policies.Config.DwPolicyOptions(), autoRefresh: false);

        DwPolicyContext context = new DwPolicyContext().WithSubject(DwSubjectKind.Role, "Manager");

        await provider.PrepareAsync(context);

        PolicyResolver resolver = new(new IDwPolicyProvider[] { provider });
        FieldPolicy policy = resolver.Resolve(typeof(PlainProduct), "Name", context);

        Assert.Equal("Product name", policy.Label);
        Assert.Equal(4, policy.CostWeight);

        provider.Dispose();
    }

    // ---- through the document ------------------------------------------------------------------

    private static PolicyRule RoundTrip(PolicyRule rule) =>
        PolicyRuleDocument.ToRule(PolicyRuleDocument.ToJson(rule));

    [Fact]
    public void A_label_survives_the_document()
    {
        Assert.Equal("Product name", RoundTrip(Rule(FieldFacts.ForLabel("Product name"))).Facts?.Label);
    }

    [Fact]
    public void A_description_survives_the_document()
    {
        PolicyRule read = RoundTrip(Rule(new FieldFacts(description: "What it is called")));

        Assert.Equal("What it is called", read.Facts?.Description);
    }

    [Fact]
    public void A_group_and_order_survive_the_document()
    {
        PolicyRule read = RoundTrip(Rule(new FieldFacts(group: "Identity", order: 20)));

        Assert.Equal("Identity", read.Facts?.Group);
        Assert.Equal(20, read.Facts?.Order);
    }

    /// <summary>
    /// Zero is a real order and must not be confused with an absent one, the way an absent weight
    /// is not a weight of zero.
    /// </summary>
    [Fact]
    public void An_order_of_zero_survives_the_document()
    {
        Assert.Equal(0, RoundTrip(Rule(new FieldFacts(order: 0))).Facts?.Order);
    }

    [Fact]
    public void An_allowed_value_list_survives_the_document()
    {
        PolicyRule read = RoundTrip(Rule(new FieldFacts(allowedValues: new[] { "A", "B", "C" })));

        Assert.Equal(new[] { "A", "B", "C" }, read.Facts?.AllowedValues);
    }

    [Fact]
    public void A_cost_weight_survives_the_document()
    {
        Assert.Equal(9, RoundTrip(Rule(FieldFacts.ForCost(9))).Facts?.CostWeight);
    }

    /// <summary>
    /// Zero is a field the budget does not charge for. Read back as absent it would fall to the
    /// standing default and start charging, which is the wrong direction only in that it refuses a
    /// query the operator meant to allow — but it is still not what was written.
    /// </summary>
    [Fact]
    public void A_cost_weight_of_zero_survives_the_document()
    {
        Assert.Equal(0, RoundTrip(Rule(FieldFacts.ForCost(0))).Facts?.CostWeight);
    }

    [Fact]
    public void An_audit_survives_the_document()
    {
        PolicyRule read = RoundTrip(Rule(FieldFacts.ForAudit(PolicyFeature.Select | PolicyFeature.Where)));

        Assert.Equal(PolicyFeature.Select | PolicyFeature.Where, read.Facts?.AuditedFeatures);
    }

    /// <summary>
    /// Every enumeration is written by name and read by name, because every zero member of every
    /// one of them is the permissive reading.
    /// </summary>
    [Fact]
    public void An_audit_is_written_by_name()
    {
        string json = PolicyRuleDocument.ToJson(Rule(FieldFacts.ForAudit(PolicyFeature.Select)));

        Assert.Contains("Select", json);
        Assert.DoesNotContain("\"audit\":2", json);
    }

    [Fact]
    public void An_audit_written_as_a_number_is_refused()
    {
        string json = PolicyRuleDocument.ToJson(Rule(FieldFacts.ForAudit(PolicyFeature.Select)));

        Assert.Throws<ArgumentException>(
            () => PolicyRuleDocument.ToRule(json.Replace("\"Select\"", "\"2\"")));
    }

    [Fact]
    public void A_rule_with_no_facts_writes_none_and_reads_none()
    {
        PolicyRule rule = new(
            DwSubjectKind.Role, "Manager", Entity, "Name", PolicyFeature.Where, PolicyEffect.Deny);

        Assert.Null(RoundTrip(rule).Facts);
    }

    /// <summary>
    /// All seven at once, so that a writer dropping one is caught even when the others are present.
    /// </summary>
    [Fact]
    public void Every_fact_survives_the_document_together()
    {
        PolicyRule read = RoundTrip(Rule(new FieldFacts(
            label: "Product name",
            description: "What it is called",
            group: "Identity",
            order: 20,
            allowedValues: new[] { "A", "B" },
            costWeight: 9,
            auditedFeatures: PolicyFeature.All)));

        Assert.Equal("Product name", read.Facts?.Label);
        Assert.Equal("What it is called", read.Facts?.Description);
        Assert.Equal("Identity", read.Facts?.Group);
        Assert.Equal(20, read.Facts?.Order);
        Assert.Equal(new[] { "A", "B" }, read.Facts?.AllowedValues);
        Assert.Equal(9, read.Facts?.CostWeight);
        Assert.Equal(PolicyFeature.All, read.Facts?.AuditedFeatures);
    }

    /// <summary>
    /// The features are written by name too, and <c>None</c> is now a value a rule can legitimately
    /// hold — so it has to survive rather than being read as a defaulted zero.
    /// </summary>
    [Fact]
    public void A_rule_speaking_to_no_feature_survives_the_document()
    {
        PolicyRule read = RoundTrip(Rule(FieldFacts.ForCost(9)));

        Assert.Equal(PolicyFeature.None, read.Features);
        Assert.Equal(9, read.Facts?.CostWeight);
    }

    [Fact]
    public void A_malformed_facts_block_is_refused()
    {
        string json = PolicyRuleDocument.ToJson(Rule(FieldFacts.ForCost(9)));

        Assert.Throws<ArgumentException>(
            () => PolicyRuleDocument.ToRule(json.Replace("\"facts\":{", "\"facts\":[{")));
    }
}
