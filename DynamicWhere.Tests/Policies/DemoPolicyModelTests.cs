using System.Reflection;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Enums;
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

}
