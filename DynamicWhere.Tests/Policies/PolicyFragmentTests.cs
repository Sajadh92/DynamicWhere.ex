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
    public void Attribute_source_records_the_attribute_name_and_whether_it_was_sealed()
    {
        PolicySource source = PolicySource.FromAttribute("DwDeniedAttribute", isSealed: true);

        Assert.Equal("DwDeniedAttribute", source.Origin);
        Assert.True(source.IsSealed);
        Assert.Null(source.RuleId);
    }
}
