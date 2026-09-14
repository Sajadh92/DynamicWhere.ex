using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Validation;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The four facts Phase 7 adds — label and grouping, allowed values, cost weight, audited features
/// — from the attributes that declare them through to the resolved policy that reports them.
/// </summary>
/// <remarks>
/// They travel the fragment path the effects travel rather than being read straight off the type,
/// because <c>/schema</c> answers per caller: a rule may relabel a field, and a sealed attribute
/// may refuse to let it. Reading attributes directly would answer the same for everyone and quietly
/// ignore the ceiling.
/// </remarks>
public class FieldFactsTests
{
    private static FieldPolicy Resolve<T>(string path)
    {
        PolicyResolver resolver = new(new[] { new AttributePolicyProvider() });

        return resolver.Resolve(typeof(T), path, new DwPolicyContext());
    }

    [Fact]
    public void A_described_field_carries_its_label_and_grouping()
    {
        FieldPolicy policy = Resolve<DescribedEmployee>("Name");

        Assert.Equal("Full name", policy.Label);
        Assert.Equal("As printed on the contract", policy.Description);
        Assert.Equal("Identity", policy.Group);
        Assert.Equal(10, policy.Order);
    }

    [Fact]
    public void An_undescribed_field_carries_nothing()
    {
        FieldPolicy policy = Resolve<DescribedEmployee>("Id");

        Assert.Null(policy.Label);
        Assert.Null(policy.Description);
        Assert.Null(policy.Group);
        Assert.Null(policy.Order);
        Assert.Null(policy.AllowedValues);
    }

    [Fact]
    public void Allowed_values_reach_the_resolved_policy()
    {
        FieldPolicy policy = Resolve<DescribedEmployee>("Status");

        Assert.Equal(new[] { "Active", "Suspended", "Closed" }, policy.AllowedValues);
    }

    /// <summary>
    /// Two attributes on one property, each carrying a different fact. A single winner-takes-all
    /// election between them would let whichever won erase the other, which is why each fact
    /// elects on its own.
    /// </summary>
    [Fact]
    public void Describe_and_allowed_values_on_one_field_both_survive()
    {
        FieldPolicy policy = Resolve<DescribedEmployee>("Status");

        Assert.Equal("Status", policy.Label);
        Assert.Equal("Identity", policy.Group);
        Assert.NotNull(policy.AllowedValues);
    }

    [Fact]
    public void A_cost_weight_reaches_the_resolved_policy()
    {
        Assert.Equal(10, Resolve<DescribedEmployee>("Salary").CostWeight);
    }

    /// <summary>
    /// Null rather than a default of one. The distinction matters to the cost pass: a field nobody
    /// weighed is charged the standing default, which is configuration, not a fact about the field.
    /// </summary>
    [Fact]
    public void An_unweighted_field_carries_no_weight()
    {
        Assert.Null(Resolve<DescribedEmployee>("Id").CostWeight);
    }

    [Fact]
    public void An_audited_field_reports_every_feature_by_default()
    {
        Assert.Equal(PolicyFeature.All, Resolve<DescribedEmployee>("Salary").AuditedFeatures);
    }

    [Fact]
    public void An_audit_narrowed_to_one_feature_reports_only_that_feature()
    {
        FieldPolicy policy = Resolve<DescribedEmployee>("Email");

        Assert.Equal(PolicyFeature.Select, policy.AuditedFeatures);
        Assert.True(policy.IsAudited(PolicyFeature.Select));
        Assert.False(policy.IsAudited(PolicyFeature.Where));
    }

    [Fact]
    public void An_unaudited_field_is_audited_for_nothing()
    {
        FieldPolicy policy = Resolve<DescribedEmployee>("Id");

        Assert.Null(policy.AuditedFeatures);
        Assert.False(policy.IsAudited(PolicyFeature.Select));
    }

    /// <summary>
    /// The facts are not effects. Declaring one must not decide whether the field may be selected,
    /// because a fragment that carries a weight and nothing else would then be granting access.
    /// </summary>
    [Fact]
    public void A_fact_decides_no_feature()
    {
        FieldPolicy policy = Resolve<DescribedEmployee>("Salary");

        Assert.True(policy.Allows(PolicyFeature.Select));
        Assert.True(policy.Allows(PolicyFeature.Where));
        Assert.False(policy.IsSealed);
    }

    // ---- election ----------------------------------------------------------------------------

    private static PolicyFragment Fact(
        string path, PolicyLevel level, FieldFacts facts, int priority = 0) =>
        new(path,
            PolicyFeature.None,
            PolicyEffect.Allow,
            level,
            level == PolicyLevel.SealedAttribute
                ? PolicySource.FromAttribute("DwCost", isSealed: true)
                : PolicySource.FromRule(Guid.NewGuid().ToString("N"), "Role=Ops"),
            priority,
            facts: facts);

    private static FieldPolicy Elect(params PolicyFragment[] fragments)
    {
        PolicyResolver resolver = new(new[] { new FakePolicyProvider(fragments) });

        return resolver.Resolve(typeof(PlainProduct), "Name", new DwPolicyContext());
    }

    /// <summary>
    /// The answer to the question this phase asked: an attribute is sealed unless its author says
    /// otherwise, so a rule cannot undercut a cost weight the source code set.
    /// </summary>
    [Fact]
    public void A_sealed_cost_outranks_a_cheaper_rule()
    {
        FieldPolicy policy = Elect(
            Fact("Name", PolicyLevel.SealedAttribute, FieldFacts.ForCost(10)),
            Fact("Name", PolicyLevel.DynamicRole, FieldFacts.ForCost(1)));

        Assert.Equal(10, policy.CostWeight);
    }

    [Fact]
    public void An_overridable_cost_yields_to_a_rule()
    {
        FieldPolicy policy = Elect(
            Fact("Name", PolicyLevel.OverridableAttribute, FieldFacts.ForCost(10)),
            Fact("Name", PolicyLevel.DynamicRole, FieldFacts.ForCost(1)));

        Assert.Equal(1, policy.CostWeight);
    }

    /// <summary>
    /// Two rules at one level saying different things. Rank cannot separate them, and falling
    /// through to the effect decides nothing on a fragment that carries only a weight — so the
    /// stricter value wins, which for a budget is the larger one.
    /// </summary>
    [Fact]
    public void Two_tied_costs_elect_the_dearer()
    {
        FieldPolicy policy = Elect(
            Fact("Name", PolicyLevel.DynamicRole, FieldFacts.ForCost(3)),
            Fact("Name", PolicyLevel.DynamicRole, FieldFacts.ForCost(7)));

        Assert.Equal(7, policy.CostWeight);
    }

    [Fact]
    public void Two_tied_audits_elect_the_wider_coverage()
    {
        FieldPolicy policy = Elect(
            Fact("Name", PolicyLevel.DynamicRole, FieldFacts.ForAudit(PolicyFeature.Select)),
            Fact("Name", PolicyLevel.DynamicRole, FieldFacts.ForAudit(PolicyFeature.Where)));

        Assert.Equal(PolicyFeature.Select | PolicyFeature.Where, policy.AuditedFeatures);
    }

    /// <summary>
    /// A rule relabelling a field must not silently switch auditing off, so the ceiling holds for
    /// audit exactly as it does for cost.
    /// </summary>
    [Fact]
    public void A_sealed_audit_outranks_a_rule_that_says_nothing_about_it()
    {
        FieldPolicy policy = Elect(
            Fact("Name", PolicyLevel.SealedAttribute, FieldFacts.ForAudit(PolicyFeature.All)),
            Fact("Name", PolicyLevel.DynamicRole, FieldFacts.ForLabel("Renamed")));

        Assert.Equal(PolicyFeature.All, policy.AuditedFeatures);
        Assert.Equal("Renamed", policy.Label);
    }

    /// <summary>
    /// Each fact elects independently, so a rule setting one of them leaves the rest where they
    /// were. The alternative loses an attribute's allowed values the moment a rule renames it.
    /// </summary>
    [Fact]
    public void A_rule_setting_one_fact_leaves_the_others_alone()
    {
        FieldPolicy policy = Elect(
            Fact("Name", PolicyLevel.OverridableAttribute,
                new FieldFacts(
                    label: "Salary",
                    description: "Monthly gross",
                    group: "Compensation",
                    order: 10,
                    allowedValues: new[] { "A", "B" },
                    costWeight: 4,
                    auditedFeatures: PolicyFeature.All)),
            Fact("Name", PolicyLevel.DynamicRole, FieldFacts.ForLabel("Pay")));

        Assert.Equal("Pay", policy.Label);
        Assert.Equal("Monthly gross", policy.Description);
        Assert.Equal("Compensation", policy.Group);
        Assert.Equal(10, policy.Order);
        Assert.Equal(new[] { "A", "B" }, policy.AllowedValues);
        Assert.Equal(4, policy.CostWeight);
        Assert.Equal(PolicyFeature.All, policy.AuditedFeatures);
    }

    // ---- construction ------------------------------------------------------------------------

    [Fact]
    public void A_negative_cost_weight_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FieldFacts.ForCost(-1));
    }

    /// <summary>
    /// Zero is a real answer — a field the budget should not charge for — so it is not refused.
    /// </summary>
    [Fact]
    public void A_zero_cost_weight_is_accepted()
    {
        Assert.Equal(0, FieldFacts.ForCost(0).CostWeight);
    }

    [Fact]
    public void An_audit_of_no_features_is_refused()
    {
        Assert.Throws<ArgumentException>(() => FieldFacts.ForAudit(PolicyFeature.None));
    }

    [Fact]
    public void A_blank_allowed_value_is_refused()
    {
        Assert.Throws<ArgumentException>(
            () => new FieldFacts(allowedValues: new[] { "Active", "  " }));
    }

    [Fact]
    public void A_facts_object_that_says_nothing_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new FieldFacts());
    }

    /// <summary>
    /// The wildcard carries a label for the same reason it cannot carry an alias: one name cannot
    /// stand for every field of a type.
    /// </summary>
    [Fact]
    public void A_label_on_the_wildcard_is_refused()
    {
        Assert.Throws<ArgumentException>(
            () => Fact(PolicyFragment.Wildcard, PolicyLevel.DynamicRole, FieldFacts.ForLabel("x")));
    }

    /// <summary>
    /// A cost on the wildcard is meaningful and permitted: "every field of this type is expensive"
    /// is a budget an operator can reasonably set.
    /// </summary>
    [Fact]
    public void A_cost_on_the_wildcard_is_accepted()
    {
        PolicyFragment fragment =
            Fact(PolicyFragment.Wildcard, PolicyLevel.DynamicRole, FieldFacts.ForCost(3));

        Assert.True(fragment.IsWildcard);
    }
}

/// <summary>
/// The startup scan's half of the four new attributes. Every refusal here also exists at fragment
/// construction; what this adds is the moment — a deployment rather than a caller's query — and a
/// message naming the type and the member.
/// </summary>
public class FieldFactsValidationTests
{
    [Fact]
    public void A_bare_describe_fails_the_scan()
    {
        PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(BareDescribe) });

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("BareDescribe.Name") && e.Contains("DwDescribe"));
    }

    [Fact]
    public void An_empty_allowed_value_list_fails_the_scan()
    {
        PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(EmptyValues) });

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("nothing to choose from"));
    }

    [Fact]
    public void A_negative_cost_fails_the_scan()
    {
        PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(NegativeCost) });

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("buy budget back"));
    }

    [Fact]
    public void An_audit_of_nothing_fails_the_scan()
    {
        PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(AuditOfNothing) });

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains("records nothing"));
    }

    /// <summary>
    /// The scan reports every problem at once rather than the first, so one deployment fixes them
    /// all. Four separate types would each pass through a different branch.
    /// </summary>
    [Fact]
    public void A_well_formed_model_passes()
    {
        PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(DescribedEmployee) });

        Assert.True(report.IsValid);
    }
}
