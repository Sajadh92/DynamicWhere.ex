using DynamicWhere.ex.Enums;
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

    [Fact]
    public void Alias_carries_the_public_name_it_was_given()
    {
        DwAliasAttribute alias = new("customer_name");

        Assert.Equal("customer_name", alias.Name);
    }

    [Fact]
    public void Alias_is_one_per_field()
    {
        // Two names for one field is not a policy question with an answer. The election in
        // ResolveType picks between fragments from different sources; two attributes on the same
        // member at the same level would tie on all four passes and resolve by declaration order.
        AttributeUsageAttribute usage = Usage(typeof(DwAliasAttribute));

        Assert.False(usage.AllowMultiple);
    }

    [Fact]
    public void Require_where_defaults_to_the_positive_membership_operators()
    {
        DwRequireWhereAttribute required = new();

        Assert.Equal(
            new[] { Operator.Equal, Operator.IEqual, Operator.In, Operator.IIn },
            required.Resolve());
    }

    [Fact]
    public void Require_where_honours_an_explicit_operator_set()
    {
        DwRequireWhereAttribute required = new()
        {
            Operators = new[] { Operator.GreaterThanOrEqual, Operator.LessThanOrEqual }
        };

        Assert.Equal(
            new[] { Operator.GreaterThanOrEqual, Operator.LessThanOrEqual },
            required.Resolve());
    }

    [Fact]
    public void Require_where_with_an_empty_operator_set_can_never_be_satisfied()
    {
        // Null and empty mean opposite things, exactly as they do on DwOperatorsAttribute.Allow:
        // null is "nothing was said, use the default", empty is "this set, and it is empty". A
        // requirement no operator satisfies refuses every query on the type, which is loud rather
        // than silent, and it is the direction that cannot grant access by accident.
        DwRequireWhereAttribute required = new() { Operators = Array.Empty<Operator>() };

        Assert.Empty(required.Resolve());
    }

    [Fact]
    public void Require_where_is_one_per_field()
    {
        Assert.False(Usage(typeof(DwRequireWhereAttribute)).AllowMultiple);
    }

    [Fact]
    public void Force_where_carries_its_operator_and_its_constant()
    {
        DwForceWhereAttribute forced = new(Operator.Equal) { Value = "false" };

        Assert.Equal(Operator.Equal, forced.Operator);
        Assert.Equal("false", forced.Value);
        Assert.Null(forced.ContextValue);
    }

    [Fact]
    public void Force_where_reads_an_ambient_value_by_key()
    {
        DwForceWhereAttribute forced = new(Operator.Equal) { ContextValue = "TenantId" };

        Assert.Equal("TenantId", forced.ContextValue);
        Assert.Null(forced.Value);
    }

    [Fact]
    public void Force_where_may_be_stacked_to_express_a_range()
    {
        // Two forced predicates on one field are a range, and they compose by conjunction. This is
        // the one of the three that accumulates rather than electing a winner.
        Assert.True(Usage(typeof(DwForceWhereAttribute)).AllowMultiple);
    }

    [Theory]
    [InlineData(typeof(DwAliasAttribute))]
    [InlineData(typeof(DwRequireWhereAttribute))]
    [InlineData(typeof(DwForceWhereAttribute))]
    public void Every_injection_attribute_is_sealed_by_default(Type attributeType)
    {
        DwPolicyAttribute attribute = (DwPolicyAttribute)(attributeType == typeof(DwAliasAttribute)
            ? new DwAliasAttribute("public_name")
            : attributeType == typeof(DwForceWhereAttribute)
                ? new DwForceWhereAttribute(Operator.Equal) { Value = "1" }
                : new DwRequireWhereAttribute());

        Assert.False(attribute.Overridable);
    }

    /// <summary>Reads the usage declared on one attribute type, ignoring the base class's own.</summary>
    private static AttributeUsageAttribute Usage(Type attributeType) =>
        attributeType
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false)
            .Cast<AttributeUsageAttribute>()
            .Single();
}
