using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers the attribute surface: the composable primitive, the six named subclasses, and the
/// sealed-by-default rule that keeps a runtime rule from loosening a compile-time denial.
/// </summary>
public class PolicyAttributeTests
{
    [Fact]
    public void Deny_is_sealed_unless_explicitly_made_overridable()
    {
        DwDenyAttribute sealedByDefault = new(PolicyFeature.Select);
        DwDenyAttribute opened = new(PolicyFeature.Select) { Overridable = true };

        Assert.False(sealedByDefault.Overridable);
        Assert.True(opened.Overridable);
    }

    [Theory]
    [InlineData(typeof(DwDeniedAttribute), PolicyFeature.All)]
    [InlineData(typeof(DwNoWhereAttribute), PolicyFeature.Where)]
    [InlineData(typeof(DwNoSelectAttribute), PolicyFeature.Select)]
    [InlineData(typeof(DwNoOrderAttribute), PolicyFeature.Order)]
    [InlineData(typeof(DwNoGroupAttribute), PolicyFeature.Group)]
    [InlineData(typeof(DwNoAggregateAttribute), PolicyFeature.Aggregate)]
    public void Each_sugar_attribute_denies_exactly_its_feature(Type attributeType, PolicyFeature expected)
    {
        DwDenyAttribute attribute = (DwDenyAttribute)Activator.CreateInstance(attributeType)!;

        Assert.Equal(expected, attribute.Features);
    }

    [Fact]
    public void Attributes_may_be_stacked_on_one_property()
    {
        AttributeUsageAttribute usage = typeof(DwPolicyAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: true)
            .Cast<AttributeUsageAttribute>()
            .Single();

        Assert.True(usage.AllowMultiple);
        Assert.True(usage.ValidOn.HasFlag(AttributeTargets.Property));
    }

    [Fact]
    public void Entity_attribute_defaults_to_not_requiring_a_policy()
    {
        DwEntityAttribute relaxed = new();
        DwEntityAttribute guarded = new() { RequirePolicy = true };

        Assert.False(relaxed.RequirePolicy);
        Assert.True(guarded.RequirePolicy);
    }
}
