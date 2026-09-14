using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Enums;
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

    [Fact]
    public void An_alias_attribute_produces_a_fragment_carrying_the_public_name()
    {
        PolicyFragment fragment = FragmentsFor<AliasedCustomer>()
            .Single(f => f.FieldPath == "Name");

        Assert.Equal("customer_name", fragment.Alias);
    }

    [Fact]
    public void An_alias_on_a_nested_reference_is_pathed_from_the_root()
    {
        PolicyFragment fragment = FragmentsFor<AliasedCustomer>()
            .Single(f => f.Alias == "email_address");

        Assert.Equal("Contact.Email", fragment.FieldPath);
    }

    [Fact]
    public void An_aliased_type_reached_twice_produces_one_fragment_per_path()
    {
        // Neither path is the obvious one, which is why the sanitizer refuses the name rather than
        // picking. The provider's job is only to report both.
        string[] paths = FragmentsFor<TwoContactCustomer>()
            .Where(f => f.Alias == "email_address")
            .Select(f => f.FieldPath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "Home.Email", "Work.Email" }, paths);
    }

    [Fact]
    public void A_forced_predicate_reading_the_context_is_produced()
    {
        PolicyFragment fragment = FragmentsFor<ScopedInvoice>()
            .Single(f => f.FieldPath == "TenantId" && f.Forced is not null);

        Assert.NotNull(fragment.Forced);
        Assert.Equal("TenantId", fragment.Forced!.ContextValue);
        Assert.Equal(Operator.Equal, fragment.Forced.Operator);
        Assert.True(fragment.Forced.ReadsContext);
    }

    [Fact]
    public void A_forced_predicate_infers_its_data_type_from_the_member()
    {
        PolicyFragment tenant = FragmentsFor<ScopedInvoice>()
            .Single(f => f.FieldPath == "TenantId" && f.Forced is not null);
        PolicyFragment deleted = FragmentsFor<ScopedInvoice>()
            .Single(f => f.FieldPath == "IsDeleted" && f.Forced is not null);

        Assert.Equal(DataType.Number, tenant.Forced!.DataType);
        Assert.Equal(DataType.Boolean, deleted.Forced!.DataType);
        Assert.Equal("false", deleted.Forced.Value);
    }

    [Fact]
    public void A_forced_predicate_on_an_unmappable_clr_type_is_refused()
    {
        // Guessing a data type the pipeline is about to validate against can only produce a
        // confusing downstream failure, so the misconfiguration is named where it was made.
        ArgumentException error = Assert.Throws<ArgumentException>(() => FragmentsFor<UnmappableForce>());

        Assert.Contains("Window", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_forced_predicates_on_one_member_produce_two_fragments()
    {
        PolicyFragment[] fragments = FragmentsFor<BoundedWindow>()
            .Where(f => f.FieldPath == "Age" && f.Forced is not null)
            .ToArray();

        Assert.Equal(2, fragments.Length);
        Assert.Contains(fragments, f => f.Forced!.Operator == Operator.GreaterThanOrEqual);
        Assert.Contains(fragments, f => f.Forced!.Operator == Operator.LessThanOrEqual);
    }

    [Fact]
    public void A_required_filter_produces_the_default_operator_set()
    {
        PolicyFragment fragment = FragmentsFor<RequiredScopeLedger>()
            .Single(f => f.FieldPath == "TenantId" && f.RequiredOperators is not null);

        Assert.Equal(DwRequireWhereAttribute.DefaultOperators, fragment.RequiredOperators);
    }

    [Fact]
    public void A_required_filter_honours_an_explicit_operator_set()
    {
        PolicyFragment fragment = FragmentsFor<RequiredScopeLedger>()
            .Single(f => f.FieldPath == "OccurredAt" && f.RequiredOperators is not null);

        Assert.Equal(
            new[] { Operator.GreaterThanOrEqual, Operator.Between },
            fragment.RequiredOperators);
    }

    [Fact]
    public void An_injection_attribute_marked_overridable_lands_at_the_overridable_level()
    {
        PolicyFragment sealedAlias = FragmentsFor<AliasedCustomer>().Single(f => f.FieldPath == "Name");
        PolicyFragment openAlias = FragmentsFor<BoundedWindow>().Single(f => f.FieldPath == "Label");

        Assert.Equal(PolicyLevel.SealedAttribute, sealedAlias.Level);
        Assert.Equal(PolicyLevel.OverridableAttribute, openAlias.Level);
    }

    [Fact]
    public void The_injection_attributes_refuse_nothing_on_their_own()
    {
        // None of the three is an effect. A fragment that denied anything here would take a field
        // away from a caller as a side effect of naming or scoping it.
        PolicyFragment[] fragments = FragmentsFor<ScopedInvoice>()
            .Concat(FragmentsFor<RequiredScopeLedger>())
            .ToArray();

        Assert.All(fragments, f => Assert.Equal(PolicyEffect.Allow, f.Effect));
    }
}
