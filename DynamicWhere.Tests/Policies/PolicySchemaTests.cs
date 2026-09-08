using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Discovery;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The schema a front end builds its filter UI from, and the registry that decides which types may
/// be asked about at all.
/// </summary>
/// <remarks>
/// Answered per caller rather than per type. A rule may relabel a field for one role and deny it to
/// another, so a schema computed once and shared would advertise names the caller cannot use and
/// hide ones they can.
/// </remarks>
public class PolicySchemaTests
{
    private static PolicySchema Describe<T>(
        DwPolicyContext? context = null,
        DwPolicyOptions? options = null,
        params IDwPolicyProvider[] extra)
    {
        DwEntityCatalog catalog = new();

        catalog.Expose(typeof(T));
        catalog.Freeze();

        List<IDwPolicyProvider> providers = new() { new AttributePolicyProvider() };

        providers.AddRange(extra);

        return PolicySchemaBuilder.Describe(
            typeof(T),
            catalog,
            context ?? new DwPolicyContext(),
            options ?? Frozen(),
            new PolicyResolver(providers));
    }

    private static DwPolicyOptions Frozen()
    {
        DwPolicyOptions options = new();

        options.Freeze();

        return options;
    }

    private static PolicySchemaField Field(PolicySchema schema, string name) =>
        Assert.Single(schema.Fields, f => f.Name == name);

    // ---- the catalogue -------------------------------------------------------------------------

    [Fact]
    public void An_exposed_type_resolves_by_its_short_name()
    {
        DwEntityCatalog catalog = new();

        catalog.Expose<DescribedEmployee>();
        catalog.Freeze();

        Assert.Equal(typeof(DescribedEmployee), catalog.Resolve("DescribedEmployee"));
        Assert.Equal(typeof(DescribedEmployee), catalog.Resolve("describedemployee"));
    }

    [Fact]
    public void An_exposed_type_resolves_by_a_name_the_host_chose()
    {
        DwEntityCatalog catalog = new();

        catalog.Expose<DescribedEmployee>("staff");
        catalog.Freeze();

        Assert.Equal(typeof(DescribedEmployee), catalog.Resolve("staff"));
    }

    /// <summary>
    /// The registry is the security boundary. Without it, resolving a type by name lets whoever
    /// reaches the endpoint enumerate every type in every loaded assembly.
    /// </summary>
    [Fact]
    public void A_type_nobody_exposed_resolves_to_nothing()
    {
        DwEntityCatalog catalog = new();

        catalog.Expose<DescribedEmployee>();
        catalog.Freeze();

        Assert.Null(catalog.Resolve("PlainProduct"));
        Assert.Null(catalog.Resolve("System.String"));
    }

    /// <summary>
    /// Two types answering to one name is a coin flip that sends an operator to the wrong entity,
    /// so it fails the startup that configured it rather than the request that hits it.
    /// </summary>
    [Fact]
    public void Two_types_claiming_one_name_are_refused()
    {
        DwEntityCatalog catalog = new();

        catalog.Expose<DescribedEmployee>("staff");

        Assert.Throws<ArgumentException>(() => catalog.Expose<PlainProduct>("staff"));
    }

    [Fact]
    public void The_catalogue_cannot_grow_after_startup()
    {
        DwEntityCatalog catalog = new();

        catalog.Freeze();

        Assert.Throws<InvalidOperationException>(() => catalog.Expose<PlainProduct>());
    }

    [Fact]
    public void Describing_a_type_nobody_exposed_is_refused()
    {
        DwEntityCatalog catalog = new();

        catalog.Freeze();

        Assert.Throws<ArgumentException>(() => PolicySchemaBuilder.Describe(
            typeof(PlainProduct), catalog, new DwPolicyContext(), Frozen(),
            new PolicyResolver(new[] { new AttributePolicyProvider() })));
    }

    // ---- what the schema says ------------------------------------------------------------------

    [Fact]
    public void Every_queryable_field_is_listed()
    {
        PolicySchema schema = Describe<DescribedEmployee>();

        Assert.Contains(schema.Fields, f => f.Name == "Id");
        Assert.Contains(schema.Fields, f => f.Name == "Name");
        Assert.Contains(schema.Fields, f => f.Name == "Salary");
    }

    [Fact]
    public void A_field_reports_the_data_type_a_filter_would_use()
    {
        PolicySchema schema = Describe<DescribedEmployee>();

        Assert.Equal(DataType.Text, Field(schema, "Name").DataType);
        Assert.Equal(DataType.Number, Field(schema, "Salary").DataType);
    }

    [Fact]
    public void A_described_field_carries_its_label_and_values()
    {
        PolicySchemaField status = Field(Describe<DescribedEmployee>(), "Status");

        Assert.Equal("Status", status.Label);
        Assert.Equal("Identity", status.Group);
        Assert.Equal(20, status.Order);
        Assert.Equal(new[] { "Active", "Suspended", "Closed" }, status.AllowedValues);
    }

    [Fact]
    public void A_field_reports_what_the_caller_may_do_with_it()
    {
        PolicySchemaField salary = Field(Describe<DescribedEmployee>(), "Salary");

        Assert.True(salary.CanWhere);
        Assert.True(salary.CanSelect);
        Assert.True(salary.CanOrder);
        Assert.Equal(10, salary.CostWeight);
    }

    [Fact]
    public void A_field_denied_for_one_feature_is_listed_without_it()
    {
        PolicySchemaField notes = Field(Describe<SecuredEmployee>(), "InternalNotes");

        Assert.True(notes.CanWhere);
        Assert.False(notes.CanOrder);
        Assert.False(notes.CanGroup);
    }

    /// <summary>
    /// The one thing section 5.7 insists on. A field an operator can never grant must not be
    /// advertised as though they could.
    /// </summary>
    [Fact]
    public void A_field_denied_for_everything_never_appears()
    {
        PolicySchema schema = Describe<SecuredEmployee>();

        Assert.DoesNotContain(schema.Fields, f => f.Path == "NationalId");
        Assert.DoesNotContain(schema.Fields, f => f.Name == "NationalId");
    }

    /// <summary>
    /// A masked field is usable — the query runs and the value comes back transformed — so it is
    /// listed and marked. Omitting it because a sealed attribute speaks to it would hide a field the
    /// caller can legitimately filter, sort and read.
    /// </summary>
    [Fact]
    public void A_masked_field_appears_and_says_so()
    {
        PolicySchemaField id = Field(Describe<Person>(), "NationalId");

        Assert.True(id.IsMasked);
        Assert.True(id.CanSelect);
    }

    [Fact]
    public void An_operator_restriction_is_advertised()
    {
        PolicySchemaField national = Field(Describe<SecuredAccount>(), "NationalId");

        Assert.Equal(new[] { Operator.Equal, Operator.In }, national.AllowedOperators);
    }

    [Fact]
    public void A_required_filter_is_advertised()
    {
        Assert.True(Field(Describe<RequiredScopeLedger>(), "TenantId").IsRequiredInWhere);
    }

    // ---- naming --------------------------------------------------------------------------------

    /// <summary>
    /// Hiding the internal name is this endpoint's job. Phase 3 shipped the alias as an added
    /// spelling and left the schema to decide which one the public vocabulary is.
    /// </summary>
    [Fact]
    public void An_aliased_field_is_advertised_under_its_alias()
    {
        PolicySchema schema = Describe<AliasedCustomer>();

        Assert.Contains(schema.Fields, f => f.Name == "customer_name");
        Assert.DoesNotContain(schema.Fields, f => f.Name == "Name");
    }

    /// <summary>
    /// The vocabulary varies by caller, which is why the schema is resolved rather than cached per
    /// type: a rule may name a field for one role and not another.
    /// </summary>
    [Fact]
    public void A_rule_can_rename_a_field_for_one_caller()
    {
        FakePolicyProvider rules = new FakePolicyProvider()
            .AddAlias("Name", "product_title", PolicyLevel.DynamicRole);

        PolicySchema renamed = Describe<PlainProduct>(extra: rules);
        PolicySchema plain = Describe<PlainProduct>();

        Assert.Contains(renamed.Fields, f => f.Name == "product_title");
        Assert.Contains(plain.Fields, f => f.Name == "Name");
    }

    // ---- the walk ------------------------------------------------------------------------------

    [Fact]
    public void A_navigation_is_walked()
    {
        PolicySchema schema = Describe<SecuredEmployee>();

        Assert.Contains(schema.Fields, f => f.Path == "Contact.Phone");
    }

    /// <summary>
    /// Bounded by the same cap the sanitizer refuses on, so the schema cannot advertise a path a
    /// query would then reject for being too deep.
    /// </summary>
    [Fact]
    public void The_walk_stops_at_the_navigation_cap()
    {
        DwPolicyOptions options = new();

        options.Caps.MaxNavigationDepth = 1;
        options.Freeze();

        PolicySchema schema = Describe<SecuredEmployee>(options: options);

        Assert.Contains(schema.Fields, f => f.Path == "Name");
        Assert.DoesNotContain(schema.Fields, f => f.Path.Contains('.'));
    }

    /// <summary>
    /// A self-referencing type would otherwise walk forever. The depth cap is what stops it, so a
    /// schema of one is finite rather than merely slow.
    /// </summary>
    [Fact]
    public void A_self_referencing_type_terminates()
    {
        PolicySchema schema = Describe<PlainNode>();

        Assert.Contains(schema.Fields, f => f.Path == "Label");
        Assert.Contains(schema.Fields, f => f.Path == "Next.Label");
        Assert.All(schema.Fields, f => Assert.True(f.Path.Split('.').Length <= 4));
    }

    /// <summary>
    /// The other half of the same rule, on a type where every field is refused: the schema is empty
    /// rather than listing fields with nothing permitted on them.
    /// </summary>
    [Fact]
    public void A_type_whose_every_field_is_denied_describes_nothing()
    {
        Assert.Empty(Describe<SecuredNode>().Fields);
    }

    [Fact]
    public void Fields_come_back_grouped_and_ordered_as_declared()
    {
        PolicySchema schema = Describe<DescribedEmployee>();

        List<PolicySchemaField> identity =
            schema.Fields.Where(f => f.Group == "Identity").ToList();

        Assert.Equal(new[] { "Name", "Status" }, identity.Select(f => f.Name));
    }

    // ---- the audit is not advertised -----------------------------------------------------------

    /// <summary>
    /// Which fields are watched is not a fact a caller needs, and it is one an attacker would use
    /// to pick the fields nobody is looking at.
    /// </summary>
    [Fact]
    public void The_schema_does_not_say_which_fields_are_audited()
    {
        Assert.DoesNotContain(
            typeof(PolicySchemaField).GetProperties(),
            p => p.Name.Contains("Audit", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// The catalogue is asked for a type by two different names by two different callers, and both have
/// to be answered or one of them fails open.
/// </summary>
public class DwEntityCatalogNameTests
{
    private static DwEntityCatalog Exposed()
    {
        DwEntityCatalog catalog = new();

        catalog.Expose<DescribedEmployee>("staff");
        catalog.Freeze();

        return catalog;
    }

    /// <summary>
    /// A rule carries <c>Type.FullName</c> by design, so the sealed-field check on a write has no
    /// public name to look the type up by. Resolving only public names left that check resolving
    /// nothing and accepting silently.
    /// </summary>
    [Fact]
    public void An_exposed_type_resolves_by_its_full_name()
    {
        Assert.Equal(
            typeof(DescribedEmployee),
            Exposed().Resolve(typeof(DescribedEmployee).FullName));
    }

    [Fact]
    public void The_public_name_still_resolves()
    {
        Assert.Equal(typeof(DescribedEmployee), Exposed().Resolve("staff"));
    }

    /// <summary>
    /// Resolving a full name must not become a way to reach a type nobody exposed, which would undo
    /// the boundary the catalogue exists to be.
    /// </summary>
    [Fact]
    public void A_full_name_nobody_exposed_still_resolves_to_nothing()
    {
        Assert.Null(Exposed().Resolve(typeof(PlainProduct).FullName));
        Assert.Null(Exposed().Resolve("System.String"));
    }
}

/// <summary>
/// The reading pass on this phase's own work. Twenty defects of the fail-open shape across six
/// phases, and not one of them found by a test that already existed.
/// </summary>
public class PolicySchemaReadingTests
{
    private static PolicySchema Describe<T>()
    {
        DwEntityCatalog catalog = new();

        catalog.Expose(typeof(T));
        catalog.Freeze();

        DwPolicyOptions options = new();

        options.Freeze();

        return PolicySchemaBuilder.Describe(
            typeof(T),
            catalog,
            new DwPolicyContext(),
            options,
            new PolicyResolver(new[] { new AttributePolicyProvider() }));
    }

    /// <summary>
    /// A nested field's policy has to be resolved against the entity the walk started from, because
    /// that is what the path is rooted in. Resolving it against the type that declares the member
    /// finds no fragment for <c>Contact.Email</c> — the fragments for that path belong to the root —
    /// and a field with no fragment is permitted. The schema would then advertise a denied field as
    /// filterable, which is the advertisement an operator builds a UI from.
    /// </summary>
    [Fact]
    public void A_nested_field_reports_the_denial_written_against_the_root()
    {
        PolicySchema schema = Describe<SecuredEmployee>();

        PolicySchemaField email = Assert.Single(schema.Fields, f => f.Path == "Contact.Email");

        Assert.False(email.CanWhere);
        Assert.True(email.CanSelect);
    }

    [Fact]
    public void The_entity_catalogue_cannot_be_cast_back_and_mutated()
    {
        // Freezing the catalogue is pointless if the dictionary behind it is reachable: a caller
        // could expose a type after Freeze and past the duplicate-name check, and the surface an
        // administrative endpoint answers about would move while the application was serving.
        var catalogue = new DwEntityCatalog();

        catalogue.Expose<SecuredEmployee>("Employee");
        catalogue.Freeze();

        Assert.Throws<InvalidCastException>(() => (Dictionary<Type, string>)catalogue.Entities);
    }
}
