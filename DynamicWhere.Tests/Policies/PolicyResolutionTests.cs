using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers <see cref="PolicyResolver"/> and the models it merges. No database is involved;
/// every test constructs fragments directly or through <see cref="FakePolicyProvider"/>.
/// </summary>
public class PolicyResolutionTests
{
    [Fact]
    public void PolicyFeature_All_covers_every_individual_flag()
    {
        PolicyFeature all = PolicyFeature.All;

        Assert.True(all.HasFlag(PolicyFeature.Where));
        Assert.True(all.HasFlag(PolicyFeature.Select));
        Assert.True(all.HasFlag(PolicyFeature.Order));
        Assert.True(all.HasFlag(PolicyFeature.Group));
        Assert.True(all.HasFlag(PolicyFeature.Aggregate));
        Assert.True(all.HasFlag(PolicyFeature.Segment));
        Assert.Equal(63, (int)all);
    }

    [Fact]
    public void PolicyFeature_flags_are_distinct_powers_of_two()
    {
        int[] singles =
        {
            (int)PolicyFeature.Where, (int)PolicyFeature.Select, (int)PolicyFeature.Order,
            (int)PolicyFeature.Group, (int)PolicyFeature.Aggregate, (int)PolicyFeature.Segment
        };

        Assert.Equal(singles.Length, singles.Distinct().Count());
        Assert.All(singles, v => Assert.Equal(0, v & (v - 1)));
    }

    [Fact]
    public void PolicyEffect_orders_by_authority_so_the_highest_value_wins_a_tie()
    {
        Assert.True(PolicyEffect.Deny > PolicyEffect.Mask);
        Assert.True(PolicyEffect.Mask > PolicyEffect.Allow);
    }

    [Fact]
    public void PolicyLevel_orders_most_authoritative_first()
    {
        Assert.True(PolicyLevel.SealedAttribute < PolicyLevel.DynamicUser);
        Assert.True(PolicyLevel.DynamicUser < PolicyLevel.DynamicRole);
        Assert.True(PolicyLevel.DynamicRole < PolicyLevel.DynamicTenant);
        Assert.True(PolicyLevel.DynamicTenant < PolicyLevel.DynamicGlobal);
        Assert.True(PolicyLevel.DynamicGlobal < PolicyLevel.OverridableAttribute);
    }

    [Fact]
    public void Fake_provider_returns_the_fragments_it_was_given()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        IReadOnlyList<PolicyFragment> fragments =
            provider.GetFragments(typeof(object), new DwPolicyContext());

        Assert.Single(fragments);
        Assert.Equal("Salary", fragments[0].FieldPath);
        Assert.Equal(PolicyLevel.DynamicRole, fragments[0].Level);
    }

    private static PolicyResolver Resolver(params IDwPolicyProvider[] providers) => new(providers);

    [Fact]
    public void A_single_deny_fragment_denies_that_feature_and_leaves_others_alone()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
        Assert.True(policy.Allows(PolicyFeature.Where));
        Assert.True(policy.Allows(PolicyFeature.Order));
    }

    [Fact]
    public void A_field_with_no_fragments_allows_everything()
    {
        FieldPolicy policy = Resolver(new FakePolicyProvider())
            .Resolve(typeof(object), "Name", new DwPolicyContext());

        Assert.True(policy.Allows(PolicyFeature.Where));
        Assert.True(policy.Allows(PolicyFeature.Select));
        Assert.Empty(policy.Sources);
    }

    [Fact]
    public void A_multi_feature_fragment_applies_to_every_feature_it_names()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select | PolicyFeature.Order, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
        Assert.False(policy.Allows(PolicyFeature.Order));
        Assert.True(policy.Allows(PolicyFeature.Group));
    }

    [Fact]
    public void Fragments_for_other_fields_are_ignored()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.All, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Name", new DwPolicyContext());

        Assert.True(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void A_padded_field_path_still_matches_its_fragments()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "  Salary  ", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
        Assert.Equal("Salary", policy.FieldPath);
    }

    [Fact]
    public void A_blank_field_path_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            Resolver(new FakePolicyProvider()).Resolve(typeof(object), "  ", new DwPolicyContext()));
    }

    [Fact]
    public void A_role_rule_replaces_an_overridable_attribute_default()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.OverridableAttribute)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.True(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void A_user_rule_beats_a_role_rule()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicUser);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void A_global_rule_loses_to_a_tenant_rule()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicGlobal)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicTenant);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.True(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void Levels_are_decided_per_feature_not_once_for_the_whole_field()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Where, PolicyEffect.Deny, PolicyLevel.DynamicUser)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.OverridableAttribute)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Where));
        Assert.True(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void An_exact_field_rule_beats_a_wildcard_rule_at_the_same_level()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("*", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole)
            .Add("Name", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Name", new DwPolicyContext());

        Assert.True(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void A_wildcard_still_applies_to_fields_with_no_exact_rule()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("*", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole)
            .Add("Name", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void A_wildcard_at_a_higher_level_still_beats_an_exact_rule_at_a_lower_one()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("*", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicUser)
            .Add("Name", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Name", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void Higher_priority_wins_within_a_level()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole, priority: 1)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole, priority: 10);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.True(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void Deny_beats_allow_when_two_roles_tie_on_priority()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void Deny_beats_mask_and_mask_beats_allow_on_a_three_way_tie()
    {
        FakePolicyProvider maskOverAllow = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Mask, PolicyLevel.DynamicRole);

        FieldPolicy masked = Resolver(maskOverAllow)
            .Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.Equal(PolicyEffect.Mask, masked.EffectFor(PolicyFeature.Select));

        FakePolicyProvider denyOverMask = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Mask, PolicyLevel.DynamicRole)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy denied = Resolver(denyOverMask)
            .Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.Equal(PolicyEffect.Deny, denied.EffectFor(PolicyFeature.Select));
    }

    [Fact]
    public void Priority_is_compared_before_effect()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole, priority: 1)
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Allow, PolicyLevel.DynamicRole, priority: 5);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.True(policy.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void No_runtime_rule_can_loosen_a_sealed_attribute()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("NationalId", PolicyFeature.All, PolicyEffect.Deny, PolicyLevel.SealedAttribute)
            .Add("NationalId", PolicyFeature.All, PolicyEffect.Allow, PolicyLevel.DynamicUser, priority: 999);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "NationalId", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
        Assert.False(policy.Allows(PolicyFeature.Where));
        Assert.True(policy.IsSealed);
    }

    [Fact]
    public void A_sealed_attribute_on_one_feature_leaves_other_features_open_to_rules()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.SealedAttribute)
            .Add("Salary", PolicyFeature.Order, PolicyEffect.Allow, PolicyLevel.DynamicRole)
            .Add("Salary", PolicyFeature.Order, PolicyEffect.Deny, PolicyLevel.OverridableAttribute);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
        Assert.True(policy.Allows(PolicyFeature.Order));
        Assert.True(policy.IsSealed);
    }

    [Fact]
    public void A_policy_decided_only_by_rules_is_not_marked_sealed()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.IsSealed);
    }

    [Fact]
    public void Every_provider_contributes_not_only_the_first()
    {
        FakePolicyProvider selectRules = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FakePolicyProvider orderRules = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Order, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(selectRules, orderRules)
            .Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
        Assert.False(policy.Allows(PolicyFeature.Order));
        Assert.True(policy.Allows(PolicyFeature.Where));
    }

    [Fact]
    public void The_sealed_flag_is_derived_from_the_level_of_the_fragment_that_won()
    {
        FieldPolicy fromAttribute = Resolver(new FakePolicyProvider()
                .Add("NationalId", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.SealedAttribute))
            .Resolve(typeof(object), "NationalId", new DwPolicyContext());

        FieldPolicy fromRule = Resolver(new FakePolicyProvider()
                .Add("NationalId", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole))
            .Resolve(typeof(object), "NationalId", new DwPolicyContext());

        Assert.True(fromAttribute.IsSealed);
        Assert.False(fromRule.IsSealed);
    }

    [Fact]
    public void One_fragment_deciding_two_features_is_recorded_as_one_source()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("Salary", PolicyFeature.Select | PolicyFeature.Order, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), "Salary", new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
        Assert.False(policy.Allows(PolicyFeature.Order));
        Assert.Single(policy.Sources);
    }

    [Theory]
    // Every row here is a fragment path and a lookup path that name the same field. Both sides must
    // reduce to the same canonical spelling, in both directions, or the Deny fragment is missed --
    // and a field with no matching fragment is allowed.
    [InlineData("Customer. Name", "Customer.Name")]
    [InlineData("Customer.Name", "Customer. Name")]
    [InlineData("Customer .Name", "Customer.Name")]
    [InlineData("Customer.Name", "Customer .Name")]
    [InlineData("Customer..Name", "Customer.Name")]
    [InlineData("Customer.Name", "Customer..Name")]
    [InlineData(" Customer . Name ", "Customer.Name")]
    [InlineData("Customer.Name", " Customer . Name ")]
    public void A_nested_path_matches_however_its_separators_are_spaced(string fragmentPath, string lookupPath)
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add(fragmentPath, PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy policy = Resolver(provider).Resolve(typeof(object), lookupPath, new DwPolicyContext());

        Assert.False(policy.Allows(PolicyFeature.Select));
        Assert.Equal("Customer.Name", policy.FieldPath);
    }

    [Fact]
    public void A_padded_wildcard_still_addresses_every_field()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add(" * ", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.DynamicRole);

        FieldPolicy topLevel = Resolver(provider).Resolve(typeof(object), "Name", new DwPolicyContext());
        FieldPolicy nested = Resolver(provider).Resolve(typeof(object), "Customer.Name", new DwPolicyContext());

        Assert.False(topLevel.Allows(PolicyFeature.Select));
        Assert.False(nested.Allows(PolicyFeature.Select));
    }

    [Fact]
    public void A_path_of_nothing_but_separators_is_rejected_at_both_entry_points()
    {
        Assert.Throws<ArgumentException>(() =>
            new PolicyFragment("...", PolicyFeature.All, PolicyEffect.Deny,
                PolicyLevel.DynamicRole, PolicySource.FromRule("r1", "Role:Manager")));

        Assert.Throws<ArgumentException>(() =>
            Resolver(new FakePolicyProvider()).Resolve(typeof(object), "...", new DwPolicyContext()));
    }

    [Fact]
    public void A_null_entity_type_is_rejected_rather_than_resolved_against_no_fragments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Resolver(new FakePolicyProvider()).Resolve(null!, "Salary", new DwPolicyContext()));
    }

    [Fact]
    public void A_null_context_is_rejected_rather_than_resolved_against_no_fragments()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Resolver(new FakePolicyProvider()).Resolve(typeof(object), "Salary", null!));
    }

    [Fact]
    public void A_provider_returning_null_fails_closed_and_is_named_in_the_message()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Resolver(new NullReturningProvider()).Resolve(typeof(object), "Salary", new DwPolicyContext()));

        Assert.Contains(nameof(NullReturningProvider), error.Message);
    }

    [Fact]
    public void A_provider_returning_a_null_fragment_fails_closed_and_is_named_in_the_message()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            Resolver(new NullFragmentProvider()).Resolve(typeof(object), "Salary", new DwPolicyContext()));

        Assert.Contains(nameof(NullFragmentProvider), error.Message);
    }

    [Fact]
    public void A_null_provider_in_the_sequence_is_rejected_when_the_resolver_is_built()
    {
        ArgumentException error =
            Assert.Throws<ArgumentException>(() => Resolver(new FakePolicyProvider(), null!));

        Assert.Contains("1", error.Message);
    }

    /// <summary>
    /// A provider that returns null rather than an empty list. Legal against the compiler only
    /// because nullable annotations are advisory, and exactly what a third-party implementation
    /// might do by accident.
    /// </summary>
    private sealed class NullReturningProvider : IDwPolicyProvider
    {
        /// <inheritdoc />
        public IReadOnlyList<PolicyFragment> GetFragments(Type entityType, DwPolicyContext context) => null!;
    }

    /// <summary>A provider whose returned list holds a null element.</summary>
    private sealed class NullFragmentProvider : IDwPolicyProvider
    {
        /// <inheritdoc />
        public IReadOnlyList<PolicyFragment> GetFragments(Type entityType, DwPolicyContext context) =>
            new PolicyFragment[] { null! };
    }
}
