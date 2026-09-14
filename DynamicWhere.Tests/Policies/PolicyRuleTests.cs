using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers the store boundary: everything a rule must be refused for before it can reach the
/// resolver.
/// </summary>
/// <remarks>
/// This is the fail-open surface the whole phase exists to close. A rule arriving from a database
/// column or a JSON document has every field defaulted rather than absent, and three of the four
/// enums involved default to their most permissive member. A rule of all zeroes reads as "grant
/// everyone everything, above sealed" — so each guard here is checked on its own, and each is
/// mutation-checked by deleting it and confirming this file goes red.
/// </remarks>
public class PolicyRuleTests
{
    private const string Entity = "DynamicWhere.Tests.Policies.Staff";

    /// <summary>Builds a rule that is valid in every respect, for a test to spoil one part of.</summary>
    private static PolicyRule Valid(
        DwSubjectKind kind = DwSubjectKind.Role,
        string? subjectKey = "Manager",
        string entityType = Entity,
        string fieldPath = "Salary",
        PolicyFeature features = PolicyFeature.Select,
        PolicyEffect effect = PolicyEffect.Deny) =>
        new(kind, subjectKey, entityType, fieldPath, features, effect);

    // ---------------------------------------------------------------- the four zero defaults

    [Fact]
    public void A_level_is_derived_from_the_subject_and_never_accepted()
    {
        // The trap this phase was told to close. PolicyLevel has no member at zero, so a level read
        // from a column defaults to 0 and outranks SealedAttribute = 1. Deriving it from the
        // subject means zero is unreachable rather than guarded against.
        Assert.Equal(PolicyLevel.DynamicGlobal, Valid(DwSubjectKind.Global, null).Level);
        Assert.Equal(PolicyLevel.DynamicTenant, Valid(DwSubjectKind.Tenant, "acme").Level);
        Assert.Equal(PolicyLevel.DynamicRole, Valid(DwSubjectKind.Role, "Manager").Level);
        Assert.Equal(PolicyLevel.DynamicUser, Valid(DwSubjectKind.User, "u1").Level);

        // A caller-defined dimension resolves at the tenant level, as DwSubjectKind documents.
        Assert.Equal(PolicyLevel.DynamicTenant, Valid(DwSubjectKind.Custom, "region-eu").Level);
    }

    [Fact]
    public void Every_derived_level_is_below_a_sealed_attribute()
    {
        foreach (DwSubjectKind kind in Enum.GetValues<DwSubjectKind>())
        {
            PolicyRule rule = Valid(kind, kind == DwSubjectKind.Global ? null : "x");

            Assert.True(
                rule.Level > PolicyLevel.SealedAttribute,
                $"{kind} resolved to {rule.Level}, which a sealed attribute would not outrank.");
        }
    }

    [Fact]
    public void An_unmapped_subject_kind_is_refused()
    {
        // An integer column holding anything outside the enum. Left alone it would fall through the
        // subject-to-level mapping, and the tempting default arm is Global — a rule meant for one
        // role silently applying to everyone.
        Assert.Throws<ArgumentOutOfRangeException>(() => Valid((DwSubjectKind)99, "x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Valid((DwSubjectKind)(-1), "x"));
    }

    [Fact]
    public void An_unmapped_effect_is_refused()
    {
        // PolicyEffect.Allow is zero, so an absent or unparsed Effect column is a grant. The
        // roadmap names only the level trap; this is the same shape on the axis that decides
        // whether the rule permits or refuses.
        Assert.Throws<ArgumentOutOfRangeException>(() => Valid(effect: (PolicyEffect)7));
        Assert.Throws<ArgumentOutOfRangeException>(() => Valid(effect: (PolicyEffect)(-3)));
    }

    [Fact]
    public void A_rule_that_speaks_to_no_feature_is_refused()
    {
        // PolicyFeature.None is zero, and Covers() is false for every feature against it — so a
        // Deny with an absent Feature column loses every election silently and the field is
        // allowed. Inert is indistinguishable from absent, which is why it cannot be stored.
        Assert.Throws<ArgumentException>(() => Valid(features: PolicyFeature.None));
    }

    [Fact]
    public void A_feature_carrying_an_unknown_bit_is_refused()
    {
        // Flags cannot use Enum.IsDefined: Where | Select is 3 and is defined nowhere. The check is
        // a mask against All, so a bit no version of this library knows cannot ride along.
        Assert.Throws<ArgumentException>(() => Valid(features: (PolicyFeature)1024));
        Assert.Throws<ArgumentException>(
            () => Valid(features: PolicyFeature.Select | (PolicyFeature)1024));
    }

    [Fact]
    public void A_combination_of_known_features_is_accepted()
    {
        PolicyRule rule = Valid(features: PolicyFeature.Where | PolicyFeature.Select);

        Assert.True(rule.Features.HasFlag(PolicyFeature.Where));
        Assert.True(rule.Features.HasFlag(PolicyFeature.Select));
    }

    [Fact]
    public void The_all_zero_rule_is_refused()
    {
        // The row a zeroed record or an empty JSON document produces: Global subject, Allow effect,
        // no features. Read literally it grants everyone everything. It must not construct.
        Assert.ThrowsAny<ArgumentException>(() => new PolicyRule(
            default, null, Entity, "Salary", default, default));
    }

    // ---------------------------------------------------------------- naming axes

    [Fact]
    public void An_entity_named_without_its_namespace_is_refused()
    {
        // Matching is on FullName. A short name would simply never match, and a rule that never
        // matches is a denial that does nothing — so it is refused at the boundary rather than
        // stored and quietly ignored.
        ArgumentException error = Assert.Throws<ArgumentException>(() => Valid(entityType: "Staff"));

        Assert.Contains("full name", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_blank_entity_or_field_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Valid(entityType: "  "));
        Assert.Throws<ArgumentException>(() => Valid(fieldPath: "  "));
        Assert.Throws<ArgumentException>(() => Valid(fieldPath: "..."));
    }

    [Fact]
    public void A_field_path_is_normalized_the_way_every_other_path_is()
    {
        // One normalizer, shared with PolicyFragment. A rule stored under one spelling and looked
        // up under another does not match, and a fragment that does not match is access granted.
        PolicyRule rule = Valid(fieldPath: " Contact . Email ");

        Assert.Equal("Contact.Email", rule.FieldPath);
    }

    [Fact]
    public void A_subject_other_than_global_requires_an_identity()
    {
        Assert.Throws<ArgumentException>(() => Valid(DwSubjectKind.Role, null));
        Assert.Throws<ArgumentException>(() => Valid(DwSubjectKind.Tenant, "   "));
    }

    [Fact]
    public void A_global_rule_carries_no_identity_even_when_one_is_supplied()
    {
        Assert.Null(Valid(DwSubjectKind.Global, "ignored").SubjectKey);
    }

    // ---------------------------------------------------------------- wildcard refusals

    [Fact]
    public void The_wildcard_refuses_an_alias_a_requirement_and_a_transform()
    {
        Assert.Throws<ArgumentException>(() => new PolicyRule(
            DwSubjectKind.Role, "Manager", Entity, "*", PolicyFeature.Select, PolicyEffect.Allow,
            alias: "Anything"));

        Assert.Throws<ArgumentException>(() => new PolicyRule(
            DwSubjectKind.Role, "Manager", Entity, "*", PolicyFeature.Where, PolicyEffect.Allow,
            requiredOperators: new[] { Operator.Equal }));

        Assert.Throws<ArgumentException>(() => new PolicyRule(
            DwSubjectKind.Role, "Manager", Entity, "*", PolicyFeature.Select, PolicyEffect.Mask,
            transform: new MaskStage(MaskStrategy.Full)));
    }

    [Fact]
    public void The_wildcard_still_carries_an_effect_across_every_field()
    {
        PolicyRule rule = new(
            DwSubjectKind.Role, "Guest", Entity, "*", PolicyFeature.All, PolicyEffect.Deny);

        Assert.True(rule.ToFragment().IsWildcard);
    }

    // ---------------------------------------------------------------- validity window

    [Fact]
    public void A_window_that_closes_before_it_opens_is_refused()
    {
        // Such a rule can never apply. Stored, it reads as a grant that was configured and as a
        // denial that is in force, and it is neither.
        DateTimeOffset now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentException>(() => new PolicyRule(
            DwSubjectKind.Role, "Manager", Entity, "Salary", PolicyFeature.Select,
            PolicyEffect.Allow, validFrom: now, validTo: now.AddDays(-1)));
    }

    [Fact]
    public void Validity_is_a_half_open_interval_evaluated_against_a_clock()
    {
        DateTimeOffset noon = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        PolicyRule rule = new(
            DwSubjectKind.Role, "Contractor", Entity, "Salary", PolicyFeature.Select,
            PolicyEffect.Allow, validFrom: noon, validTo: noon.AddHours(1));

        Assert.False(rule.AppliesAt(noon.AddSeconds(-1)));
        Assert.True(rule.AppliesAt(noon));
        Assert.True(rule.AppliesAt(noon.AddMinutes(59)));

        // Exclusive end: a grant that expires at one o'clock is gone at one o'clock, not at one
        // o'clock and a tick.
        Assert.False(rule.AppliesAt(noon.AddHours(1)));
    }

    [Fact]
    public void A_rule_with_no_window_always_applies()
    {
        PolicyRule rule = Valid();

        Assert.True(rule.AppliesAt(DateTimeOffset.MinValue));
        Assert.True(rule.AppliesAt(DateTimeOffset.MaxValue));
    }

    // ---------------------------------------------------------------- subject and purpose

    [Fact]
    public void Subject_matching_is_case_insensitive_as_the_context_compares()
    {
        // A rule targeting Role:Manager must match a token that says "manager". Comparing ordinally
        // means the rule does not apply, and a denial that does not apply is access granted.
        DwPolicyContext context = new DwPolicyContext().WithSubject(DwSubjectKind.Role, "manager");

        Assert.True(Valid(DwSubjectKind.Role, "MANAGER").MatchesSubject(context));
        Assert.False(Valid(DwSubjectKind.Role, "Auditor").MatchesSubject(context));
    }

    [Fact]
    public void A_global_rule_matches_every_caller()
    {
        Assert.True(Valid(DwSubjectKind.Global, null).MatchesSubject(new DwPolicyContext()));
    }

    [Fact]
    public void A_rule_bound_to_a_purpose_applies_only_when_the_caller_declares_it()
    {
        PolicyRule rule = new(
            DwSubjectKind.Role, "Manager", Entity, "Salary", PolicyFeature.Select,
            PolicyEffect.Allow, purpose: "Billing");

        Assert.True(rule.MatchesPurpose(new DwPolicyContext { Purpose = "billing" }));
        Assert.False(rule.MatchesPurpose(new DwPolicyContext { Purpose = "Reporting" }));
        Assert.False(rule.MatchesPurpose(new DwPolicyContext()));

        // A rule naming no purpose is unconditional, which is how a denial that must always hold is
        // written.
        Assert.True(Valid().MatchesPurpose(new DwPolicyContext { Purpose = "anything" }));
    }

    // ---------------------------------------------------------------- conversion

    [Fact]
    public void A_rule_converts_to_a_fragment_carrying_its_level_and_source()
    {
        PolicyRule rule = new(
            DwSubjectKind.Role, "Manager", Entity, "Salary", PolicyFeature.Select,
            PolicyEffect.Deny, priority: 7);

        PolicyFragment fragment = rule.ToFragment();

        Assert.Equal("Salary", fragment.FieldPath);
        Assert.Equal(PolicyEffect.Deny, fragment.Effect);
        Assert.Equal(PolicyLevel.DynamicRole, fragment.Level);
        Assert.Equal(7, fragment.Priority);
        Assert.False(fragment.Source.IsSealed);
        Assert.Equal(rule.Id.ToString(), fragment.Source.RuleId);
        Assert.Equal("Role:Manager", fragment.Source.Subject);
    }

    [Fact]
    public void A_fragment_from_a_rule_can_never_be_sealed()
    {
        // The one guarantee the whole level ordering exists to provide: nothing a store holds can
        // reach the authority of a compile-time attribute marked Overridable = false.
        foreach (DwSubjectKind kind in Enum.GetValues<DwSubjectKind>())
        {
            PolicyFragment fragment =
                Valid(kind, kind == DwSubjectKind.Global ? null : "x").ToFragment();

            Assert.False(fragment.Source.IsSealed);
            Assert.NotEqual(PolicyLevel.SealedAttribute, fragment.Level);
        }
    }

    [Fact]
    public void The_typed_carriers_survive_conversion()
    {
        PolicyRule rule = new(
            DwSubjectKind.Tenant, "acme", Entity, "Salary", PolicyFeature.Select, PolicyEffect.Mask,
            transform: new TruncateStage(4), allowedOperators: new[] { Operator.Equal },
            alias: "Pay");

        PolicyFragment fragment = rule.ToFragment();

        Assert.Equal(TransformKind.Truncate, fragment.Transform?.Kind);
        Assert.Equal(new[] { Operator.Equal }, fragment.AllowedOperators);
        Assert.Equal("Pay", fragment.Alias);
    }

    [Fact]
    public void Disabled_is_carried_rather_than_silently_dropped()
    {
        PolicyRule off = new(
            DwSubjectKind.Role, "Manager", Entity, "Salary", PolicyFeature.Select,
            PolicyEffect.Deny, enabled: false);

        Assert.False(off.Enabled);
        Assert.True(Valid().Enabled);
    }
}
