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
        PolicySchemaRequest? request = null,
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
            new PolicyResolver(providers),
            request);
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
    /// A self-referencing type would otherwise walk forever, and the assertion that it does not has
    /// to name the level it stops at.
    /// </summary>
    /// <remarks>
    /// The previous version of this asserted no path exceeded four segments, which the default depth
    /// of two makes true whatever the walk does — it passed with the cycle guard removed and with
    /// the depth limit removed. Two exact paths, one present and one absent, is what it takes for
    /// the test to have an opinion.
    /// </remarks>
    [Fact]
    public void A_self_referencing_type_terminates()
    {
        PolicySchema schema = Describe<PlainNode>();

        Assert.Contains(schema.Fields, f => f.Path == "Label");
        Assert.Contains(schema.Fields, f => f.Path == "Next.Label");
        Assert.DoesNotContain(schema.Fields, f => f.Path == "Next.Next.Label");
        Assert.Equal(2, schema.Fields.Count);
    }

    // ---- depth ---------------------------------------------------------------------------------

    /// <summary>A deployment that says nothing gets the entity and one level of navigation.</summary>
    [Fact]
    public void The_default_walk_covers_two_levels()
    {
        DwPolicyOptions options = new();

        Assert.Equal(2, options.Caps.SchemaDepth);

        PolicySchema schema = Describe<SecuredEmployee>();

        Assert.Contains(schema.Fields, f => f.Path == "Name");
        Assert.Contains(schema.Fields, f => f.Path == "Contact.Phone");
        Assert.Equal(2, schema.Depth);
    }

    [Fact]
    public void A_depth_of_one_describes_the_entity_alone()
    {
        PolicySchema schema = Describe<SecuredEmployee>(request: new PolicySchemaRequest { Depth = 1 });

        Assert.Contains(schema.Fields, f => f.Path == "Name");
        Assert.DoesNotContain(schema.Fields, f => f.Path.Contains('.'));
        Assert.Equal(1, schema.Depth);
    }

    /// <summary>
    /// A depth beyond what a query may reach is clamped rather than refused, and the response says
    /// what it used.
    /// </summary>
    /// <remarks>
    /// This is what removes the need for a sentinel meaning "as deep as possible". A caller sends a
    /// large number, reads <c>Depth</c> back, and learns the ceiling from <c>MaxDepth</c> without
    /// ever having to know it in advance.
    /// </remarks>
    [Fact]
    public void A_depth_beyond_the_query_cap_is_clamped_and_reported()
    {
        PolicySchema schema = Describe<PlainNode>(request: new PolicySchemaRequest { Depth = 99 });

        Assert.Equal(4, schema.MaxDepth);
        Assert.Equal(4, schema.Depth);
    }

    [Fact]
    public void A_depth_below_one_is_refused()
    {
        // Zero levels describes nothing, and answering with an empty list would read as a caller
        // who may use no fields rather than a request that asked for none.
        Assert.Throws<ArgumentException>(
            () => Describe<SecuredEmployee>(request: new PolicySchemaRequest { Depth = 0 }));
    }

    // ---- the cycle guard -----------------------------------------------------------------------

    /// <summary>
    /// A type may appear twice on one path and not three times, which is what makes a
    /// self-referencing entity describable rather than combinatorial.
    /// </summary>
    [Fact]
    public void A_type_may_not_repeat_beyond_the_limit()
    {
        DwPolicyOptions permissive = new();

        permissive.Caps.SchemaCycleLimit = 3;
        permissive.Freeze();

        PolicySchema deeper = Describe<PlainNode>(
            options: permissive, request: new PolicySchemaRequest { Depth = 4 });

        // Three occurrences allowed, so the third level appears and the fourth does not.
        Assert.Contains(deeper.Fields, f => f.Path == "Next.Next.Label");
        Assert.DoesNotContain(deeper.Fields, f => f.Path == "Next.Next.Next.Label");

        // And at the shipped limit of two, the same request stops a level earlier. The depth is
        // identical in both runs, so the guard is the only thing that differs.
        PolicySchema shipped = Describe<PlainNode>(request: new PolicySchemaRequest { Depth = 4 });

        Assert.DoesNotContain(shipped.Fields, f => f.Path == "Next.Next.Label");
    }

    /// <summary>
    /// The guard counts within the view a request asked for, so drilling into a branch shows what
    /// the entity-rooted walk stopped short of.
    /// </summary>
    /// <remarks>
    /// The alternative — counting from the entity — would make this request describe nothing, and a
    /// front end would be offering a node that opens onto an empty response. Every path stays
    /// reachable precisely because the count resets.
    /// </remarks>
    [Fact]
    public void The_guard_counts_within_the_view_so_drilling_makes_progress()
    {
        Assert.DoesNotContain(Describe<PlainNode>().Fields, f => f.Path == "Next.Next.Label");

        PolicySchema drilled = Describe<PlainNode>(
            request: new PolicySchemaRequest { Paths = new[] { "Next" } });

        Assert.Contains(drilled.Fields, f => f.Path == "Next.Label");
        Assert.Contains(drilled.Fields, f => f.Path == "Next.Next.Label");
    }

    /// <summary>
    /// A requested root arrives with its own type already counted once, not at zero.
    /// </summary>
    /// <remarks>
    /// The reset that makes drilling work is a reset to one, not to nothing. Seeding an empty count
    /// would give a requested subtree one more level of self-reference than the same subtree gets
    /// from the entity, so the same path would describe two different things depending on how you
    /// arrived at it. Found by mutation: emptying the seed turned no test red until this one.
    /// </remarks>
    [Fact]
    public void A_requested_root_counts_its_own_type_once()
    {
        PolicySchema schema = Describe<PlainNode>(
            request: new PolicySchemaRequest { Paths = new[] { "Next" }, Depth = 3 });

        Assert.Contains(schema.Fields, f => f.Path == "Next.Label");
        Assert.Contains(schema.Fields, f => f.Path == "Next.Next.Label");
        Assert.DoesNotContain(schema.Fields, f => f.Path == "Next.Next.Next.Label");
        Assert.Equal(2, schema.Fields.Count);
    }

    // ---- paths ---------------------------------------------------------------------------------

    [Fact]
    public void A_path_roots_the_walk()
    {
        PolicySchema schema = Describe<SecuredEmployee>(
            request: new PolicySchemaRequest { Paths = new[] { "Contact" } });

        Assert.Contains(schema.Fields, f => f.Path == "Contact.Phone");
        Assert.DoesNotContain(schema.Fields, f => f.Path == "Name");
        Assert.Equal(new[] { "Contact" }, schema.Roots);
    }

    [Fact]
    public void Two_paths_are_answered_in_one_response()
    {
        PolicySchema schema = Describe<PlainNode>(
            request: new PolicySchemaRequest { Paths = new[] { "Next", "Next.Next" }, Depth = 1 });

        Assert.Contains(schema.Fields, f => f.Path == "Next.Label");
        Assert.Contains(schema.Fields, f => f.Path == "Next.Next.Label");
        Assert.DoesNotContain(schema.Fields, f => f.Path == "Label");
    }

    /// <summary>Two roots that reach the same field list it once.</summary>
    /// <remarks>
    /// Without this a picker would show one column twice, and the caller would have no way to tell
    /// which of the two was the real one. It is also why the fields are flat with a parent rather
    /// than nested: a nested document would have to choose an owner for the duplicate.
    /// </remarks>
    [Fact]
    public void Overlapping_paths_list_a_field_once()
    {
        PolicySchema schema = Describe<PlainNode>(
            request: new PolicySchemaRequest { Paths = new[] { "Next", "Next.Next" } });

        Assert.Single(schema.Fields, f => f.Path == "Next.Next.Label");
    }

    [Fact]
    public void A_path_naming_a_field_rather_than_a_navigation_is_refused()
    {
        // Refused rather than reported as missing. The caller is holding a real name and using it
        // in the wrong place, and "no such thing" is a lie they would act on.
        ArgumentException refused = Assert.Throws<ArgumentException>(
            () => Describe<SecuredEmployee>(
                request: new PolicySchemaRequest { Paths = new[] { "Name" } }));

        Assert.Contains("rather than a related entity", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_path_naming_nothing_resolves_to_nothing()
    {
        Assert.Null(PolicySchemaBuilder.ResolveNavigation(
            typeof(SecuredEmployee),
            "NotAnything",
            new DwPolicyContext(),
            Frozen(),
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() })));
    }

    [Fact]
    public void A_path_already_at_the_navigation_cap_is_refused()
    {
        // Four navigations deep with a cap of four leaves no level to describe, so the request is
        // refused rather than answered with an empty list that reads like a denial.
        Assert.Throws<ArgumentException>(
            () => Describe<PlainNode>(
                request: new PolicySchemaRequest { Paths = new[] { "Next.Next.Next.Next" } }));
    }

    [Fact]
    public void A_navigation_is_addressable_by_its_alias()
    {
        PolicySchema schema = Describe<AliasedNavigationRow>(
            request: new PolicySchemaRequest { Paths = new[] { "details" } });

        Assert.Contains(schema.Fields, f => f.Path == "Contact.Phone");
        Assert.Equal(new[] { "Contact" }, schema.Roots);
    }

    [Fact]
    public void An_unrooted_request_echoes_no_roots()
    {
        Assert.Empty(Describe<SecuredEmployee>().Roots);
    }

    // ---- nodes ---------------------------------------------------------------------------------

    [Fact]
    public void A_walked_navigation_is_a_node_that_says_it_was_walked()
    {
        PolicySchemaNode node = Assert.Single(
            Describe<SecuredEmployee>().Nodes, n => n.Path == "Contact");

        Assert.True(node.Expanded);
        Assert.Null(node.Parent);
        Assert.Equal(2, node.Depth);
    }

    /// <summary>
    /// A navigation the walk stopped at is still listed, which is what a front end draws an
    /// unopened branch from.
    /// </summary>
    [Fact]
    public void A_navigation_left_alone_is_a_node_that_says_so()
    {
        PolicySchemaNode node = Assert.Single(
            Describe<PlainNode>().Nodes, n => n.Path == "Next.Next");

        Assert.False(node.Expanded);
        Assert.Equal("Next", node.Parent);
        Assert.Equal(3, node.Depth);
    }

    /// <summary>
    /// A node reports what expanding it would actually yield, which accounts for the cycle guard as
    /// well as the query cap.
    /// </summary>
    [Fact]
    public void A_node_reports_how_much_lies_beneath_it()
    {
        // Contact holds only simple members, so there is nothing under it to ask for.
        Assert.Equal(
            0,
            Assert.Single(Describe<SecuredEmployee>().Nodes, n => n.Path == "Contact").RemainingDepth);

        // Both self-referencing nodes report one, and the number is the answer to "what do I get if
        // I open this". Asking for a path resets the guard's count, so the reply is measured the
        // same way — a node that inherited the current walk's count would report nothing beneath it
        // and then hand back a field the moment somebody clicked it.
        Assert.Equal(
            1,
            Assert.Single(Describe<PlainNode>().Nodes, n => n.Path == "Next").RemainingDepth);

        Assert.Equal(
            1,
            Assert.Single(Describe<PlainNode>().Nodes, n => n.Path == "Next.Next").RemainingDepth);

        // And it is honoured: opening the node returns the level it promised.
        Assert.Contains(
            Describe<PlainNode>(request: new PolicySchemaRequest { Paths = new[] { "Next.Next" } })
                .Fields,
            f => f.Path == "Next.Next.Next.Label");
    }

    /// <summary>
    /// A node whose type the catalogue never exposed carries no entity name.
    /// </summary>
    /// <remarks>
    /// The catalogue exists so a caller cannot enumerate the application's types by asking about
    /// them, and a nested navigation does not need its type exposed for the walk to pass through it.
    /// Naming one here would hand back exactly the fact the catalogue withholds.
    /// </remarks>
    [Fact]
    public void A_node_whose_type_nobody_exposed_is_unnamed()
    {
        Assert.Null(
            Assert.Single(Describe<SecuredEmployee>().Nodes, n => n.Path == "Contact").Entity);

        // The self-referencing case is the contrast: the walk exposes PlainNode as the entity, so
        // its own navigation resolves to a name.
        Assert.NotNull(
            Assert.Single(Describe<PlainNode>().Nodes, n => n.Path == "Next").Entity);
    }

    // ---- parents -------------------------------------------------------------------------------

    [Fact]
    public void A_field_hangs_under_the_navigation_that_carries_it()
    {
        PolicySchema schema = Describe<SecuredEmployee>();

        Assert.Null(Assert.Single(schema.Fields, f => f.Path == "Name").Parent);
        Assert.Equal("Contact", Assert.Single(schema.Fields, f => f.Path == "Contact.Phone").Parent);
    }

    // ---- the field cap -------------------------------------------------------------------------

    /// <summary>
    /// The cap cuts the list short and says so, rather than throwing.
    /// </summary>
    /// <remarks>
    /// A field picker missing its tail can tell its user; a failed request can only say nothing. It
    /// is a ceiling rather than a shape — the depth and the cycle guard are what keep an ordinary
    /// response small, and this exists so no combination of paths and depth can ask for an
    /// unbounded one.
    /// </remarks>
    [Fact]
    public void Reaching_the_field_cap_truncates_and_says_so()
    {
        DwPolicyOptions capped = new();

        capped.Caps.MaxSchemaFields = 2;
        capped.Freeze();

        PolicySchema schema = Describe<SecuredEmployee>(options: capped);

        Assert.True(schema.Truncated);
        Assert.Equal(2, schema.Fields.Count);

        // And an ordinary request is never truncated, so the flag means something when it is set.
        Assert.False(Describe<SecuredEmployee>().Truncated);
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
