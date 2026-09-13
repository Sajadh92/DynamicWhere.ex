using System.Reflection;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Discovery;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Tokens;
using DynamicWhere.ex.Policies.Validation;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// What the demo API's own model has to keep saying.
/// </summary>
/// <remarks>
/// The demo is the only place this branch drives the four packages together against a real
/// PostgreSQL, and its sixteen policy endpoints are run by hand rather than by this suite. So the
/// attributes those endpoints depend on are asserted here, where a change that dropped one is
/// caught by a test run rather than by somebody opening the demo.
/// <para>
/// Excluded from the EF Core 6.0.22 floor leg with the rest of the demo, which the floor leg drops
/// along with the EF Core 8 graph it pulls in.
/// </para>
/// </remarks>
public class DemoPolicyModelTests
{
    /// <summary>
    /// The demo API's own entity tokenizes its employee code, and the attributes that make that
    /// safe are all present.
    /// </summary>
    /// <remarks>
    /// The demo is the only place this branch exercises the policy layer against a real PostgreSQL,
    /// and its sixteen policy endpoints are driven by hand rather than by this suite. So the model
    /// itself is asserted here: without this, a change to <c>Employee</c> that dropped the mask, or
    /// added it without <c>[DwNoOrder]</c>, would be caught by nobody until somebody opened the
    /// demo.
    /// </remarks>
    [Fact]
    public void The_demo_employee_code_is_tokenized_and_unsortable()
    {
        System.Reflection.PropertyInfo code =
            typeof(DynamicWhere.API.Models.Employee).GetProperty("EmployeeCode")!;

        DwMaskAttribute mask = Assert.Single(code.GetCustomAttributes<DwMaskAttribute>(true));

        Assert.Equal(MaskStrategy.Tokenize, mask.Strategy);

        // Scoped to its own path. Naming a scope would make the code joinable to any other field
        // sharing it, which nothing in the demo wants.
        Assert.Null(mask.TokenScope);

        // Sorting runs against the stored value, so paging a tokenized column in order ranks the
        // real codes. This is the pairing the startup scan asks for, on a field it now applies to.
        Assert.Contains(
            code.GetCustomAttributes<DwDenyAttribute>(inherit: true),
            deny => (deny.Features & PolicyFeature.Order) == PolicyFeature.Order);
    }

    /// <summary>The demo model passes its own startup scan once a vault is configured.</summary>
    /// <remarks>
    /// The other half. A tokenizing attribute with no vault is an error the scan reports, and the
    /// demo configures one — so this asserts the pair rather than either half alone.
    /// </remarks>
    [Fact]
    public void The_demo_model_scans_clean_with_a_vault_and_reports_without_one()
    {
        DwPolicyOptions configured = new()
        {
            HashSalt = "dynamicwhere-demo-salt",
            TokenVault = new InMemoryTokenVault()
        };

        PolicyModelReport withVault = PolicyModelValidator.Inspect(
            new[] { typeof(DynamicWhere.API.Models.Employee) }, configured);

        Assert.DoesNotContain(
            withVault.Errors, e => e.Contains("TokenVault", StringComparison.Ordinal));

        PolicyModelReport without = PolicyModelValidator.Inspect(
            new[] { typeof(DynamicWhere.API.Models.Employee) },
            new DwPolicyOptions { HashSalt = "dynamicwhere-demo-salt" });

        Assert.Contains(without.Errors, e => e.Contains("TokenVault", StringComparison.Ordinal));
    }


    // ---- the schema the demo entity produces ----------------------------------------------------

    private static PolicySchema Describe(PolicySchemaRequest? request = null, int? cycleLimit = null)
    {
        DwEntityCatalog catalogue = new();

        catalogue.Expose(typeof(DynamicWhere.API.Models.Employee), "Employee");
        catalogue.Freeze();

        DwPolicyOptions options = new();

        if (cycleLimit is not null)
        {
            options.Caps.SchemaCycleLimit = cycleLimit.Value;
        }

        options.Freeze();

        return PolicySchemaBuilder.Describe(
            typeof(DynamicWhere.API.Models.Employee),
            catalogue,
            new DwPolicyContext(),
            options,
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }),
            request);
    }

    /// <summary>
    /// The entity that motivated the whole redesign, counted exactly.
    /// </summary>
    /// <remarks>
    /// Thirteen simple properties, four leaf navigations carrying twenty fields between them, and
    /// two navigations back to itself. Under the old walk that came to 335 fields — correct, and
    /// unusable as a field picker. These are the numbers the design was agreed on, asserted rather
    /// than described, because the whole argument for the defaults rests on them.
    /// </remarks>
    [Theory]
    [InlineData(1, 13)]
    [InlineData(2, 59)]
    [InlineData(3, 99)]
    [InlineData(99, 99)]
    public void The_demo_entity_describes_the_agreed_number_of_fields(int depth, int expected)
    {
        Assert.Equal(expected, Describe(new PolicySchemaRequest { Depth = depth }).Fields.Count);
    }

    /// <summary>The default is two levels, so an unasked request is the fifty-nine.</summary>
    [Fact]
    public void The_demo_entity_defaults_to_two_levels()
    {
        PolicySchema schema = Describe();

        Assert.Equal(59, schema.Fields.Count);
        Assert.Equal(2, schema.Depth);
        Assert.Equal(4, schema.MaxDepth);
        Assert.False(schema.Truncated);
    }

    /// <summary>
    /// The cycle guard is what holds a full-depth request to ninety-nine rather than 335, and
    /// raising it restores the exhaustive listing exactly.
    /// </summary>
    /// <remarks>
    /// Asserted because it is the one claim in the design that a reader cannot check by reading the
    /// code: the 236 paths the guard removes are all repeats like Manager.Manager.Email, and they
    /// stay queryable and stay reachable by asking for the subtree.
    /// </remarks>
    [Fact]
    public void Raising_the_cycle_limit_restores_the_exhaustive_listing()
    {
        Assert.Equal(
            335,
            Describe(new PolicySchemaRequest { Depth = 99 }, cycleLimit: 4).Fields.Count);
    }

    /// <summary>Drilling into the manager describes it as though it were the entity.</summary>
    [Fact]
    public void The_manager_subtree_is_described_on_request()
    {
        PolicySchema schema = Describe(new PolicySchemaRequest { Paths = new[] { "Manager" } });

        Assert.Equal(59, schema.Fields.Count);
        Assert.Equal(new[] { "Manager" }, schema.Roots);
        Assert.All(schema.Fields, f => Assert.StartsWith("Manager.", f.Path, StringComparison.Ordinal));
    }
}
