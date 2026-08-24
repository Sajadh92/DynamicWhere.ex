using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers <see cref="AttributePolicyProvider"/>: which fragments reflection produces from a
/// decorated type, and how <c>Overridable</c> maps onto a precedence level.
/// </summary>
public class AttributeProviderTests
{
    private static readonly DwPolicyContext Anyone = new();

    private static IReadOnlyList<PolicyFragment> FragmentsFor<T>() =>
        new AttributePolicyProvider().GetFragments(typeof(T), Anyone);

    [Fact]
    public void A_type_with_no_attributes_produces_no_fragments()
    {
        Assert.Empty(FragmentsFor<PlainProduct>());
    }

    [Fact]
    public void A_denied_property_produces_one_fragment_covering_every_feature()
    {
        PolicyFragment fragment = FragmentsFor<SecuredEmployee>()
            .Single(f => f.FieldPath == "NationalId");

        Assert.Equal(PolicyFeature.All, fragment.Features);
        Assert.Equal(PolicyEffect.Deny, fragment.Effect);
    }

    [Fact]
    public void A_non_overridable_attribute_lands_at_the_sealed_level()
    {
        PolicyFragment fragment = FragmentsFor<SecuredEmployee>()
            .Single(f => f.FieldPath == "NationalId");

        Assert.Equal(PolicyLevel.SealedAttribute, fragment.Level);
        Assert.True(fragment.Source.IsSealed);
    }

    [Fact]
    public void An_overridable_attribute_lands_at_the_overridable_level()
    {
        PolicyFragment fragment = FragmentsFor<SecuredEmployee>()
            .Single(f => f.FieldPath == "Salary");

        Assert.Equal(PolicyLevel.OverridableAttribute, fragment.Level);
        Assert.False(fragment.Source.IsSealed);
    }

    [Fact]
    public void A_composed_deny_carries_exactly_the_features_it_named()
    {
        PolicyFragment fragment = FragmentsFor<SecuredEmployee>()
            .Single(f => f.FieldPath == "InternalNotes");

        Assert.True(fragment.Covers(PolicyFeature.Order));
        Assert.True(fragment.Covers(PolicyFeature.Group));
        Assert.False(fragment.Covers(PolicyFeature.Select));
    }

    [Fact]
    public void The_source_names_the_attribute_that_produced_the_fragment()
    {
        PolicyFragment fragment = FragmentsFor<SecuredEmployee>()
            .Single(f => f.FieldPath == "NationalId");

        Assert.Equal("DwDeniedAttribute", fragment.Source.Origin);
    }

    [Fact]
    public void Undecorated_properties_produce_no_fragments()
    {
        IReadOnlyList<PolicyFragment> fragments = FragmentsFor<SecuredEmployee>();

        Assert.DoesNotContain(fragments, f => f.FieldPath == "Name");
        Assert.DoesNotContain(fragments, f => f.FieldPath == "Id");
    }
}
