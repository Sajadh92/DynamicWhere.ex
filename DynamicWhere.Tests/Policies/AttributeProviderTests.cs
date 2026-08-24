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

    [Fact]
    public void Attributes_on_a_nested_reference_type_produce_dotted_paths()
    {
        PolicyFragment fragment = FragmentsFor<SecuredEmployee>()
            .Single(f => f.FieldPath == "Contact.Email");

        Assert.True(fragment.Covers(PolicyFeature.Where));
        Assert.Equal(PolicyLevel.SealedAttribute, fragment.Level);
    }

    [Fact]
    public void Undecorated_nested_properties_produce_no_fragments()
    {
        Assert.DoesNotContain(FragmentsFor<SecuredEmployee>(), f => f.FieldPath == "Contact.Phone");
    }

    [Fact]
    public void A_self_referencing_type_terminates_instead_of_recursing_forever()
    {
        IReadOnlyList<PolicyFragment> fragments = FragmentsFor<SecuredNode>();

        Assert.Contains(fragments, f => f.FieldPath == "Secret");
        Assert.Contains(fragments, f => f.FieldPath == "Next.Secret");
    }

    [Fact]
    public void Nesting_stops_at_the_configured_depth()
    {
        IReadOnlyList<PolicyFragment> fragments = FragmentsFor<SecuredNode>();

        Assert.All(fragments, f => Assert.True(f.FieldPath.Count(c => c == '.') < AttributePolicyProvider.MaxDepth));
    }

    [Fact]
    public void The_returned_fragments_cannot_be_cast_back_and_mutated()
    {
        IReadOnlyList<PolicyFragment> fragments = FragmentsFor<SecuredEmployee>();

        Assert.Null(fragments as List<PolicyFragment>);
        Assert.Throws<NotSupportedException>(() => ((IList<PolicyFragment>)fragments).Clear());
    }

    [Fact]
    public void Attributes_beneath_an_array_typed_navigation_produce_dotted_paths()
    {
        PolicyFragment fragment = FragmentsFor<SecuredInvoiceDto>()
            .Single(f => f.FieldPath == "Lines.Cost");

        Assert.Equal(PolicyFeature.All, fragment.Features);
        Assert.Equal(PolicyLevel.SealedAttribute, fragment.Level);
    }

    [Fact]
    public void An_array_of_a_primitive_is_a_value_rather_than_a_navigation()
    {
        IReadOnlyList<PolicyFragment> fragments = FragmentsFor<SecuredInvoiceDto>();

        Assert.DoesNotContain(fragments, f => f.FieldPath.StartsWith("Signature."));
    }

    [Fact]
    public void Attributes_beneath_an_interface_typed_navigation_produce_dotted_paths()
    {
        PolicyFragment fragment = FragmentsFor<SecuredCustomerDto>()
            .Single(f => f.FieldPath == "Contact.Email");

        Assert.Equal(PolicyFeature.All, fragment.Features);
        Assert.Equal(PolicyLevel.SealedAttribute, fragment.Level);
    }

    [Fact]
    public void Attributes_beneath_a_struct_typed_navigation_produce_dotted_paths()
    {
        PolicyFragment fragment = FragmentsFor<SecuredCustomerDto>()
            .Single(f => f.FieldPath == "Audit.ChangedBy");

        Assert.Equal(PolicyFeature.All, fragment.Features);
        Assert.Equal(PolicyLevel.SealedAttribute, fragment.Level);
    }

    [Fact]
    public void Undecorated_members_beneath_a_followed_navigation_produce_no_fragments()
    {
        IReadOnlyList<PolicyFragment> fragments = FragmentsFor<SecuredCustomerDto>();

        Assert.DoesNotContain(fragments, f => f.FieldPath == "Contact.Phone");
        Assert.DoesNotContain(fragments, f => f.FieldPath == "Audit.ChangedAt");
    }

    [Fact]
    public void A_cyclic_entity_graph_terminates_instead_of_recursing_forever()
    {
        Assert.Empty(FragmentsFor<Blog>());
    }

    [Fact]
    public void Attributes_beneath_a_custom_collection_navigation_produce_dotted_paths()
    {
        PolicyFragment fragment = FragmentsFor<SecuredPagedInvoiceDto>()
            .Single(f => f.FieldPath == "Lines.Cost");

        Assert.Equal(PolicyFeature.All, fragment.Features);
        Assert.Equal(PolicyLevel.SealedAttribute, fragment.Level);
    }

    [Fact]
    public void A_custom_collection_of_simple_values_is_a_value_rather_than_a_navigation()
    {
        IReadOnlyList<PolicyFragment> fragments = FragmentsFor<SecuredPagedInvoiceDto>();

        Assert.DoesNotContain(fragments, f => f.FieldPath.StartsWith("Reviewers."));
    }

    [Fact]
    public void A_keyed_collection_is_a_value_rather_than_a_navigation()
    {
        IReadOnlyList<PolicyFragment> fragments = FragmentsFor<SecuredPagedInvoiceDto>();

        Assert.DoesNotContain(fragments, f => f.FieldPath.StartsWith("LinesBySku."));
    }

    [Fact]
    public void A_string_property_is_never_walked_as_a_collection_of_characters()
    {
        IReadOnlyList<PolicyFragment> fragments = FragmentsFor<SecuredEmployee>();

        Assert.DoesNotContain(fragments, f => f.FieldPath.StartsWith("Name."));
        Assert.DoesNotContain(fragments, f => f.FieldPath.StartsWith("NationalId."));
    }

    [Fact]
    public void Attributes_beneath_an_array_of_a_custom_collection_produce_dotted_paths()
    {
        PolicyFragment fragment = FragmentsFor<SecuredJaggedInvoiceDto>()
            .Single(f => f.FieldPath == "Lines.Cost");

        Assert.Equal(PolicyFeature.All, fragment.Features);
        Assert.Equal(PolicyLevel.SealedAttribute, fragment.Level);
    }

    [Fact]
    public void Attributes_beneath_a_collection_of_collections_produce_dotted_paths()
    {
        PolicyFragment fragment = FragmentsFor<SecuredJaggedInvoiceDto>()
            .Single(f => f.FieldPath == "Batches.Cost");

        Assert.Equal(PolicyFeature.All, fragment.Features);
        Assert.Equal(PolicyLevel.SealedAttribute, fragment.Level);
    }

    [Fact]
    public void Attributes_beneath_a_jagged_array_produce_dotted_paths()
    {
        PolicyFragment fragment = FragmentsFor<SecuredJaggedInvoiceDto>()
            .Single(f => f.FieldPath == "Grid.Cost");

        Assert.Equal(PolicyFeature.All, fragment.Features);
        Assert.Equal(PolicyLevel.SealedAttribute, fragment.Level);
    }

    [Fact]
    public void An_array_of_a_collection_of_simple_values_is_a_value_rather_than_a_navigation()
    {
        IReadOnlyList<PolicyFragment> fragments = FragmentsFor<SecuredJaggedInvoiceDto>();

        Assert.DoesNotContain(fragments, f => f.FieldPath.StartsWith("Reviewers."));
    }

    [Fact]
    public void A_jagged_array_of_simple_values_is_a_value_rather_than_a_navigation()
    {
        IReadOnlyList<PolicyFragment> fragments = FragmentsFor<SecuredJaggedInvoiceDto>();

        Assert.DoesNotContain(fragments, f => f.FieldPath.StartsWith("Signatures."));
        Assert.DoesNotContain(fragments, f => f.FieldPath.StartsWith("Labels."));
    }

    [Fact]
    public void A_type_that_enumerates_itself_terminates_and_is_still_walked()
    {
        IReadOnlyList<PolicyFragment> fragments = FragmentsFor<SecuredRecursiveDto>();

        Assert.Contains(fragments, f => f.FieldPath == "Children.Secret");
    }

    [Fact]
    public void A_two_step_collection_cycle_terminates_and_is_still_walked()
    {
        IReadOnlyList<PolicyFragment> fragments = FragmentsFor<SecuredRecursiveDto>();

        Assert.Contains(fragments, f => f.FieldPath is "Cycle.SecretA" or "Cycle.SecretB");
    }
}
