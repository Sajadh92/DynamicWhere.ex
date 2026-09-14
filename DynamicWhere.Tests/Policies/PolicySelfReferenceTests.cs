using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers the two carriers on a type reachable from itself.
/// </summary>
/// <remarks>
/// The attribute walk is depth-capped with no visited-type guard, deliberately: suppressing
/// <c>Manager.Salary</c> would leave a path a caller can genuinely filter on unprotected. That is
/// right for anything which <em>decides</em>. It was wrong for the two carriers that name a field
/// publicly or demand something of the caller, because a bidirectional navigation turned one
/// declaration into fifteen paths and made both features unusable.
/// <para>
/// Both failed closed — <c>AmbiguousFieldName</c> and <c>RequiredFilterMissing</c> — which is why
/// the existing suite was green throughout. Nothing here is a leak; all of it is a feature that did
/// not work on the commonest model shape there is, found by running the demo API against a real
/// database rather than by a test.
/// </para>
/// </remarks>
public class PolicySelfReferenceTests
{
    private static DwPolicyContext Caller() =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    private static DwPolicyOptions Options() => new() { Tier = DwTier.Convenience };

    private static Filter Sanitize<T>(Filter filter) where T : class =>
        FilterSanitizer.Sanitize<T>(
            filter,
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }),
            Caller(),
            Options(),
            new PolicyTrace(DwTier.Convenience, dryRun: false));

    private static Condition On(string field, string value = "x") =>
        new() { Field = field, DataType = DataType.Text, Operator = Operator.Equal, Values = { value } };

    private static Filter Where(params Condition[] conditions)
    {
        var group = new ConditionGroup { Connector = Connector.And };

        for (int i = 0; i < conditions.Length; i++)
        {
            conditions[i].Sort = i + 1;
            group.Conditions.Add(conditions[i]);
        }

        return new Filter { ConditionGroup = group };
    }

    // ------------------------------------------------------------------------------ the alias

    [Fact]
    public void An_alias_on_a_self_referencing_type_resolves_to_the_root_path()
    {
        Filter sanitized = Sanitize<SelfReferencingStaff>(
            Where(On("code", "A-1"), On("Division", "Eng")));

        // Flattened because injecting the forced predicate nests the caller's own group beneath a
        // new root, which is how a conjunction is guaranteed to survive whatever the caller wrote.
        Condition resolved = Flatten(sanitized.ConditionGroup!)
            .Single(c => c.Field == nameof(SelfReferencingStaff.StaffCode));

        Assert.Equal("A-1", resolved.Values[0]);
    }

    [Fact]
    public void The_alias_is_not_refused_as_ambiguous_by_its_own_reflections()
    {
        // Manager.StaffCode, Reports.StaffCode, Manager.Reports.StaffCode and the rest are all the
        // same declaration seen through the navigation graph. Before the fix each one was a
        // candidate and the name matched fifteen paths.
        Exception? refusal = Record.Exception(
            () => Sanitize<SelfReferencingStaff>(Where(On("code", "A-1"), On("Division", "Eng"))));

        Assert.Null(refusal);
    }

    [Fact]
    public void An_alias_reached_only_through_navigations_is_still_ambiguous()
    {
        // The other side of the rule. Two different members declare "reference", so neither is the
        // declaration site and preferring one would discard a spelling somebody wrote.
        PolicyException refusal = Assert.Throws<PolicyException>(
            () => Sanitize<TwiceAliasedLedger>(Where(On("reference", "INV-1"))));

        Assert.Equal(PolicyErrorCode.AmbiguousFieldName, refusal.ErrorCode);
    }

    [Fact]
    public void A_nested_alias_still_resolves_through_its_navigation()
    {
        // The capability the blunt version of this fix would have removed: aliasing a member of a
        // nested type has to keep working.
        Filter sanitized = Sanitize<AliasedCustomer>(Where(On("customer_name", "ACME")));

        Assert.NotNull(sanitized.ConditionGroup);
    }

    // ------------------------------------------------------------------------- the requirement

    [Fact]
    public void A_requirement_on_a_self_referencing_type_is_satisfied_at_the_root()
    {
        Filter sanitized = Sanitize<SelfReferencingStaff>(Where(On("Division", "Eng")));

        Assert.NotNull(sanitized.ConditionGroup);
    }

    [Fact]
    public void The_requirement_does_not_reappear_behind_every_navigation()
    {
        // Before the fix this threw RequiredFilterMissing naming Manager.Division, and adding that
        // condition only produced a demand for Manager.Manager.Division.
        Exception? refusal = Record.Exception(
            () => Sanitize<SelfReferencingStaff>(Where(On("Division", "Eng"))));

        Assert.Null(refusal);
    }

    [Fact]
    public void The_requirement_still_refuses_a_query_that_omits_it()
    {
        // The mutation check's target. Root-only must not become none-at-all: a requirement that
        // stopped being enforced would fail open, and this is the assertion that goes red if the
        // depth guard is widened to drop the root fragment too.
        PolicyException refusal = Assert.Throws<PolicyException>(
            () => Sanitize<SelfReferencingStaff>(Where(On("Salary", "1"))));

        Assert.Equal(PolicyErrorCode.RequiredFilterMissing, refusal.ErrorCode);
    }

    [Fact]
    public void A_requirement_on_a_type_with_no_self_reference_is_unchanged()
    {
        PolicyException refusal = Assert.Throws<PolicyException>(
            () => Sanitize<RequiredScopeLedger>(Where(On("Amount", "1"))));

        Assert.Equal(PolicyErrorCode.RequiredFilterMissing, refusal.ErrorCode);
    }

    // -------------------------------------------------------------------- the forced predicate

    [Fact]
    public void A_forced_predicate_on_a_self_referencing_type_is_injected_once()
    {
        Filter sanitized = Sanitize<SelfReferencingStaff>(Where(On("Division", "Eng")));

        List<string> forced = Flatten(sanitized.ConditionGroup!)
            .Select(c => c.Field)
            .Where(f => f is not null
                        && f.EndsWith(nameof(SelfReferencingStaff.IsActive), StringComparison.Ordinal))
            .Select(f => f!)
            .ToList();

        Assert.Equal(new[] { nameof(SelfReferencingStaff.IsActive) }, forced);
    }

    [Fact]
    public void A_forced_predicate_does_not_reappear_behind_every_navigation()
    {
        // This is the one that failed silently. Manager.IsActive AND Manager.Manager.IsActive AND
        // Manager.Manager.Manager.IsActive is a conjunction almost no row satisfies, so a good query
        // came back empty instead of refused — fewer rows, which is why nothing caught it.
        List<string> forced = Flatten(Sanitize<SelfReferencingStaff>(Where(On("Division", "Eng"))).ConditionGroup!)
            .Select(c => c.Field)
            .Where(f => f is not null && f.Contains('.'))
            .Select(f => f!)
            .ToList();

        Assert.Empty(forced);
    }

    [Fact]
    public void A_forced_predicate_one_navigation_away_still_reaches_its_path()
    {
        // The capability the cycle guard must not cost: the tenant column lives on the buyer, and
        // the scope is worthless unless it reaches Buyer.TenantId.
        Filter sanitized = FilterSanitizer.Sanitize<ScopedOrder>(
            Where(On("Total", "1")),
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }),
            Caller().WithValue("TenantId", 7),
            Options(),
            new PolicyTrace(DwTier.Convenience, dryRun: false));

        Assert.Contains(
            Flatten(sanitized.ConditionGroup!),
            c => c.Field == "Buyer.TenantId");
    }

    [Fact]
    public void A_type_seen_on_one_branch_still_carries_on_another()
    {
        // The cycle guard has to be unwound, not just set. Branch.Scope is walked first and marks
        // DiamondScope; if that mark survives the return, the direct Scope beside it reads as a
        // reflection and its forced predicate vanishes — reintroducing the silent empty result the
        // guard was added to remove.
        Filter sanitized = FilterSanitizer.Sanitize<DiamondRoot>(
            Where(On("Id", "1")),
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }),
            Caller().WithValue("TenantId", 7),
            Options(),
            new PolicyTrace(DwTier.Convenience, dryRun: false));

        List<string> scoped = Flatten(sanitized.ConditionGroup!)
            .Select(c => c.Field)
            .Where(f => f is not null && f.EndsWith("TenantId", StringComparison.Ordinal))
            .Select(f => f!)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new[] { "Branch.Scope.TenantId", "Scope.TenantId" }, scoped);
    }

    private static List<Condition> Flatten(ConditionGroup group)
    {
        List<Condition> all = new(group.Conditions);

        foreach (ConditionGroup nested in group.SubConditionGroups)
        {
            all.AddRange(Flatten(nested));
        }

        return all;
    }

    // ------------------------------------------------------- what must keep propagating

    [Fact]
    public void A_denial_still_reaches_the_same_member_through_a_navigation()
    {
        // The reason the walk has no visited-type guard, and the reason this fix is two carriers
        // rather than a change to the walk's termination. A caller can filter Manager.Secret, so the
        // denial has to be there.
        PolicyException refusal = Assert.Throws<PolicyException>(
            () => Sanitize<SecuredNode>(Where(On("Next.Secret"))));

        Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, refusal.ErrorCode);
    }
}
