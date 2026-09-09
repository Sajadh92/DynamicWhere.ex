using System.Security.Claims;
using DynamicWhere.ex.Policies.AspNetCore;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The adapter that decides who the caller is, from the claims a host happens to issue.
/// </summary>
/// <remarks>
/// Worth its own suite because nothing here fails loudly. Every other mistake in this layer refuses
/// a query; a claim type that is spelled wrong, or a role that is read once where the caller holds
/// three, produces a caller with fewer subjects than they have — which is a set of rules that
/// silently do not apply. The tests below are therefore mostly about what ends up on the context
/// rather than about what is thrown.
/// </remarks>
public class PolicyClaimsAdapterTests
{
    private static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims, "Test"));

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    // ---- arguments -----------------------------------------------------------------------------

    [Fact]
    public void A_null_principal_is_refused()
    {
        Assert.Throws<ArgumentNullException>(
            () => DwClaimsAdapter.FromClaims(null!, new DwClaimsOptions()));
    }

    [Fact]
    public void Null_options_are_refused()
    {
        Assert.Throws<ArgumentNullException>(
            () => DwClaimsAdapter.FromClaims(Principal(), null!));
    }

    // ---- the anonymous posture -----------------------------------------------------------------

    /// <summary>
    /// An unauthenticated caller carries no user and no role, so every rule written for a subject
    /// fails to match and only global rules apply. That is a coherent posture for a public read
    /// surface and an accident everywhere else, so it has to be asked for.
    /// </summary>
    [Fact]
    public void An_unauthenticated_caller_is_refused_by_default()
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => DwClaimsAdapter.FromClaims(Anonymous(), new DwClaimsOptions()));

        Assert.Contains("AllowAnonymous", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unauthenticated_caller_is_allowed_when_the_host_says_so()
    {
        DwPolicyContext context = DwClaimsAdapter.FromClaims(
            Anonymous(), new DwClaimsOptions { AllowAnonymous = true });

        Assert.Empty(context.Subjects);
    }

    /// <summary>
    /// The refusal is on the identity, not on the claim count. A principal carrying claims but no
    /// authentication type is exactly what an unauthenticated request looks like once something
    /// upstream has attached a few, and it must not be mistaken for a signed-in caller.
    /// </summary>
    [Fact]
    public void Claims_without_an_authentication_type_are_still_anonymous()
    {
        ClaimsPrincipal carrying = new(
            new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Manager") }));

        Assert.Throws<InvalidOperationException>(
            () => DwClaimsAdapter.FromClaims(carrying, new DwClaimsOptions()));
    }

    // ---- who the caller is ---------------------------------------------------------------------

    [Fact]
    public void The_user_is_read_from_either_spelling()
    {
        DwPolicyContext byName = DwClaimsAdapter.FromClaims(
            Principal(new Claim(ClaimTypes.NameIdentifier, "u-1")), new DwClaimsOptions());

        DwPolicyContext bySub = DwClaimsAdapter.FromClaims(
            Principal(new Claim("sub", "u-2")), new DwClaimsOptions());

        Assert.Equal(new[] { "u-1" }, byName.Identities(DwSubjectKind.User));
        Assert.Equal(new[] { "u-2" }, bySub.Identities(DwSubjectKind.User));
    }

    /// <summary>
    /// Every role the caller holds, not the first one found.
    /// </summary>
    /// <remarks>
    /// The reason this matters is the conflict rule: multi-role resolves to the strictest, which
    /// needs all of them present to resolve at all. Taking one would drop the denials attached to
    /// the other two and hand the caller the most permissive of their roles.
    /// </remarks>
    [Fact]
    public void Every_role_is_carried_not_the_first()
    {
        DwPolicyContext context = DwClaimsAdapter.FromClaims(
            Principal(
                new Claim(ClaimTypes.Role, "Support"),
                new Claim(ClaimTypes.Role, "Auditor"),
                new Claim("roles", "Manager")),
            new DwClaimsOptions());

        Assert.Equal(
            new[] { "Support", "Auditor", "Manager" },
            context.Identities(DwSubjectKind.Role));
    }

    /// <summary>
    /// The same identity under two claim types is one subject, not two.
    /// </summary>
    [Fact]
    public void The_same_role_spelled_twice_is_one_subject()
    {
        DwPolicyContext context = DwClaimsAdapter.FromClaims(
            Principal(new Claim(ClaimTypes.Role, "Support"), new Claim("role", "Support")),
            new DwClaimsOptions());

        Assert.Single(context.Identities(DwSubjectKind.Role));
    }

    [Fact]
    public void The_tenant_is_read_from_any_of_its_three_spellings()
    {
        foreach (string claimType in new[] { "tenant", "tenant_id", "tid" })
        {
            DwPolicyContext context = DwClaimsAdapter.FromClaims(
                Principal(new Claim(claimType, "acme")), new DwClaimsOptions());

            Assert.Equal(new[] { "acme" }, context.Identities(DwSubjectKind.Tenant));
        }
    }

    /// <summary>
    /// A claim type the host renamed is read once the host says so, and not before.
    /// </summary>
    [Fact]
    public void An_unlisted_claim_type_contributes_nothing_until_it_is_listed()
    {
        Claim claim = new("employee_number", "e-9");

        Assert.Empty(DwClaimsAdapter
            .FromClaims(Principal(claim), new DwClaimsOptions())
            .Identities(DwSubjectKind.User));

        DwClaimsOptions options = new();
        options.UserClaimTypes.Add("employee_number");

        Assert.Equal(
            new[] { "e-9" },
            DwClaimsAdapter.FromClaims(Principal(claim), options).Identities(DwSubjectKind.User));
    }

    /// <summary>
    /// A dimension this library does not name, mapped to the kind the host chose for it.
    /// </summary>
    [Fact]
    public void An_extra_claim_type_is_mapped_to_the_kind_the_host_gave_it()
    {
        DwClaimsOptions options = new();
        options.SubjectClaimTypes["region"] = DwSubjectKind.Custom;
        options.SubjectClaimTypes["business_unit"] = DwSubjectKind.Tenant;

        DwPolicyContext context = DwClaimsAdapter.FromClaims(
            Principal(new Claim("region", "emea"), new Claim("business_unit", "retail")),
            options);

        Assert.Equal(new[] { "emea" }, context.Identities(DwSubjectKind.Custom));
        Assert.Equal(new[] { "retail" }, context.Identities(DwSubjectKind.Tenant));
    }

    /// <summary>
    /// A blank claim value is skipped rather than becoming a subject with an empty identity.
    /// </summary>
    /// <remarks>
    /// <c>DwSubject</c> refuses a blank identity outright, so letting one through would throw on a
    /// request whose only fault is an identity provider issuing an empty claim.
    /// </remarks>
    [Fact]
    public void A_blank_claim_value_is_skipped_rather_than_thrown_on()
    {
        DwPolicyContext context = DwClaimsAdapter.FromClaims(
            Principal(
                new Claim(ClaimTypes.Role, "   "),
                new Claim(ClaimTypes.Role, "Support")),
            new DwClaimsOptions());

        Assert.Equal(new[] { "Support" }, context.Identities(DwSubjectKind.Role));
    }

    // ---- ambient values ------------------------------------------------------------------------

    [Fact]
    public void A_mapped_claim_becomes_the_ambient_value_a_forced_predicate_reads()
    {
        DwClaimsOptions options = new();
        options.ValueClaimTypes["TenantId"] = "tenant";

        DwPolicyContext context = DwClaimsAdapter.FromClaims(
            Principal(new Claim("tenant", "42")), options);

        Assert.True(context.TryGetValue("TenantId", out object? value));
        Assert.Equal("42", value);
    }

    /// <summary>
    /// A mapped claim the caller does not carry leaves the key absent, not null.
    /// </summary>
    /// <remarks>
    /// The distinction is the whole reason the value is read here rather than defaulted somewhere.
    /// A forced predicate reading a key nothing supplies refuses the query with
    /// <c>MissingContextValue</c>; one reading a key supplied as null injects a comparison against
    /// nothing, which matches no row and reads as an empty result rather than a misconfiguration.
    /// </remarks>
    [Fact]
    public void A_missing_claim_leaves_the_value_absent_rather_than_null()
    {
        DwClaimsOptions options = new();
        options.ValueClaimTypes["TenantId"] = "tenant";

        DwPolicyContext context = DwClaimsAdapter.FromClaims(
            Principal(new Claim(ClaimTypes.NameIdentifier, "u-1")), options);

        Assert.False(context.TryGetValue("TenantId", out _));
    }

    // ---- purpose -------------------------------------------------------------------------------

    /// <summary>
    /// A constant, not a claim: a purpose describes what a request is for, which the caller's
    /// identity does not know.
    /// </summary>
    [Fact]
    public void The_purpose_is_bound_from_the_options()
    {
        Assert.Equal(
            "admin",
            DwClaimsAdapter
                .FromClaims(Principal(), new DwClaimsOptions { AllowAnonymous = true, Purpose = "admin" })
                .Purpose);

        Assert.Null(DwClaimsAdapter
            .FromClaims(Principal(), new DwClaimsOptions { AllowAnonymous = true })
            .Purpose);
    }

    // ---- the async surface ---------------------------------------------------------------------

    /// <summary>
    /// The prepared form builds the same context. Preparation itself is a no-op with no store
    /// configured, and is covered where a store exists.
    /// </summary>
    [Fact]
    public async Task The_prepared_form_describes_the_same_caller()
    {
        DwClaimsOptions options = new();

        DwPolicyContext context = await DwClaimsAdapter.CreateContextAsync(
            Principal(
                new Claim(ClaimTypes.NameIdentifier, "u-1"),
                new Claim(ClaimTypes.Role, "Support"),
                new Claim("tenant", "acme")),
            options);

        Assert.Equal(new[] { "u-1" }, context.Identities(DwSubjectKind.User));
        Assert.Equal(new[] { "Support" }, context.Identities(DwSubjectKind.Role));
        Assert.Equal(new[] { "acme" }, context.Identities(DwSubjectKind.Tenant));
    }

    [Fact]
    public async Task The_extension_method_reaches_the_same_adapter()
    {
        DwPolicyContext context = await Principal(new Claim("sub", "u-7"))
            .ToPolicyContextAsync(new DwClaimsOptions());

        Assert.Equal(new[] { "u-7" }, context.Identities(DwSubjectKind.User));
    }

    /// <summary>
    /// The anonymous refusal holds on the async path too, which is the one a host actually calls.
    /// </summary>
    [Fact]
    public async Task The_prepared_form_refuses_an_unauthenticated_caller()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await DwClaimsAdapter.CreateContextAsync(Anonymous(), new DwClaimsOptions()));
    }
}
