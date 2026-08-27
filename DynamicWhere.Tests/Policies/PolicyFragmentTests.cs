using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers <see cref="PolicyFragment"/> construction and its field-path matching, which decides
/// whether a wildcard rule applies to a given field.
/// </summary>
public class PolicyFragmentTests
{
    private static PolicyFragment Frag(string path, PolicyFeature features, PolicyEffect effect) =>
        new(path, features, effect, PolicyLevel.DynamicRole, PolicySource.FromRule("r1", "Role:Manager"));

    [Fact]
    public void Exact_path_matches_only_itself()
    {
        PolicyFragment fragment = Frag("Salary", PolicyFeature.Select, PolicyEffect.Deny);

        Assert.True(fragment.Matches("Salary"));
        Assert.False(fragment.Matches("Name"));
        Assert.False(fragment.IsWildcard);
    }

    [Fact]
    public void Path_matching_is_case_insensitive()
    {
        PolicyFragment fragment = Frag("Salary", PolicyFeature.Select, PolicyEffect.Deny);

        Assert.True(fragment.Matches("salary"));
        Assert.True(fragment.Matches("SALARY"));
    }

    [Fact]
    public void Wildcard_matches_every_path()
    {
        PolicyFragment fragment = Frag("*", PolicyFeature.All, PolicyEffect.Deny);

        Assert.True(fragment.IsWildcard);
        Assert.True(fragment.Matches("Salary"));
        Assert.True(fragment.Matches("ContactInfo.Email"));
    }

    [Fact]
    public void Fragment_reports_whether_it_speaks_to_a_feature()
    {
        PolicyFragment fragment =
            Frag("Salary", PolicyFeature.Select | PolicyFeature.Order, PolicyEffect.Deny);

        Assert.True(fragment.Covers(PolicyFeature.Select));
        Assert.True(fragment.Covers(PolicyFeature.Order));
        Assert.False(fragment.Covers(PolicyFeature.Where));
    }

    [Fact]
    public void Fragment_requires_a_field_path()
    {
        Assert.Throws<ArgumentException>(() =>
            new PolicyFragment(" ", PolicyFeature.All, PolicyEffect.Deny,
                PolicyLevel.DynamicGlobal, PolicySource.FromRule("r1", "Global")));
    }

    [Fact]
    public void Fragment_requires_a_source()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PolicyFragment("Salary", PolicyFeature.All, PolicyEffect.Deny,
                PolicyLevel.DynamicGlobal, null!));
    }

    [Fact]
    public void Normalizing_a_null_path_names_the_parameter_rather_than_dereferencing_it()
    {
        ArgumentNullException error =
            Assert.Throws<ArgumentNullException>(() => PolicyFragment.NormalizePath(null!));

        Assert.Equal("fieldPath", error.ParamName);
    }

    [Fact]
    public void Attribute_source_records_the_attribute_name_and_whether_it_was_sealed()
    {
        PolicySource source = PolicySource.FromAttribute("DwDeniedAttribute", isSealed: true);

        Assert.Equal("DwDeniedAttribute", source.Origin);
        Assert.True(source.IsSealed);
        Assert.Null(source.RuleId);
    }

    [Fact]
    public void Attribute_source_requires_an_attribute_name()
    {
        Assert.Throws<ArgumentException>(() => PolicySource.FromAttribute(null!, isSealed: true));
        Assert.Throws<ArgumentException>(() => PolicySource.FromAttribute(string.Empty, isSealed: true));
        Assert.Throws<ArgumentException>(() => PolicySource.FromAttribute(" ", isSealed: true));
    }

    [Fact]
    public void Rule_source_requires_a_rule_id()
    {
        Assert.Throws<ArgumentException>(() => PolicySource.FromRule(null!, "Role:Manager"));
        Assert.Throws<ArgumentException>(() => PolicySource.FromRule(string.Empty, "Role:Manager"));
        Assert.Throws<ArgumentException>(() => PolicySource.FromRule(" ", "Role:Manager"));
    }

    [Fact]
    public void Rule_source_requires_a_subject()
    {
        Assert.Throws<ArgumentException>(() => PolicySource.FromRule("r1", null!));
        Assert.Throws<ArgumentException>(() => PolicySource.FromRule("r1", string.Empty));
        Assert.Throws<ArgumentException>(() => PolicySource.FromRule("r1", " "));
    }

    [Fact]
    public void A_fragment_may_carry_an_alias()
    {
        PolicyFragment fragment = Alias("Customer.Name", "customer_name");

        Assert.Equal("customer_name", fragment.Alias);
    }

    [Fact]
    public void An_alias_is_trimmed_the_way_a_field_path_is()
    {
        Assert.Equal("customer_name", Alias("Customer.Name", "  customer_name  ").Alias);
    }

    [Fact]
    public void A_blank_alias_is_refused()
    {
        // Null means "this fragment says nothing about naming". A blank string is a mistake, and
        // treating it as silence would make a misconfigured rule invisible.
        Assert.Throws<ArgumentException>(() => Alias("Customer.Name", string.Empty));
        Assert.Throws<ArgumentException>(() => Alias("Customer.Name", "   "));
    }

    [Fact]
    public void An_alias_may_not_look_like_a_navigation_path()
    {
        // A dotted alias is indistinguishable from a real nested path at the point a caller's name
        // is resolved, so one could shadow a genuine navigation the caller meant.
        Assert.Throws<ArgumentException>(() => Alias("Customer.Name", "customer.name"));
    }

    [Fact]
    public void An_alias_may_not_be_the_wildcard()
    {
        Assert.Throws<ArgumentException>(() => Alias("Customer.Name", PolicyFragment.Wildcard));
    }

    [Fact]
    public void A_fragment_may_carry_a_forced_predicate()
    {
        ForcedPredicate forced = ForcedPredicate.FromContext(
            "TenantId", Operator.Equal, DataType.Number, "TenantId");

        PolicyFragment fragment = new(
            "TenantId", PolicyFeature.Where, PolicyEffect.Allow, PolicyLevel.SealedAttribute,
            PolicySource.FromAttribute("DwForceWhereAttribute", isSealed: true),
            forced: forced);

        Assert.Same(forced, fragment.Forced);
    }

    [Fact]
    public void A_fragment_may_carry_the_operators_that_satisfy_a_requirement()
    {
        PolicyFragment fragment = new(
            "TenantId", PolicyFeature.Where, PolicyEffect.Allow, PolicyLevel.SealedAttribute,
            PolicySource.FromAttribute("DwRequireWhereAttribute", isSealed: true),
            requiredOperators: new[] { Operator.Equal });

        Assert.Equal(new[] { Operator.Equal }, fragment.RequiredOperators);
    }

    [Fact]
    public void A_forced_predicate_from_a_constant_carries_no_context_key()
    {
        ForcedPredicate forced = ForcedPredicate.FromConstant(
            "IsDeleted", Operator.Equal, DataType.Boolean, "false");

        Assert.Equal("IsDeleted", forced.FieldPath);
        Assert.Equal("false", forced.Value);
        Assert.Null(forced.ContextValue);
        Assert.False(forced.ReadsContext);
    }

    [Fact]
    public void A_forced_predicate_from_the_context_carries_no_constant()
    {
        ForcedPredicate forced = ForcedPredicate.FromContext(
            "TenantId", Operator.Equal, DataType.Number, "TenantId");

        Assert.Equal("TenantId", forced.ContextValue);
        Assert.Null(forced.Value);
        Assert.True(forced.ReadsContext);
    }

    [Fact]
    public void A_forced_predicate_needs_a_field_and_a_value()
    {
        Assert.Throws<ArgumentException>(
            () => ForcedPredicate.FromConstant(" ", Operator.Equal, DataType.Boolean, "false"));
        Assert.Throws<ArgumentException>(
            () => ForcedPredicate.FromConstant("IsDeleted", Operator.Equal, DataType.Boolean, null!));
        Assert.Throws<ArgumentException>(
            () => ForcedPredicate.FromContext("TenantId", Operator.Equal, DataType.Number, " "));
    }

    /// <summary>Builds a fragment whose only interesting property is its alias.</summary>
    private static PolicyFragment Alias(string fieldPath, string alias) =>
        new(fieldPath, PolicyFeature.Where, PolicyEffect.Allow, PolicyLevel.SealedAttribute,
            PolicySource.FromAttribute("DwAliasAttribute", isSealed: true), alias: alias);
}
