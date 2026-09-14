using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Four ways the store layer could have handed back a denial that does not apply, each found by
/// reading the code rather than by a test failing.
/// </summary>
/// <remarks>
/// The recurring defect on this branch is fail-open, and it has been found by review more often
/// than by tests — thirteen times across four phases, none of them by a test that already existed.
/// These are this phase's.
/// </remarks>
public class StoreHardeningTests
{
    private const string StaffType = "DynamicWhere.Tests.Policies.Staff";

    private static DwPolicyOptions Options()
    {
        DwPolicyOptions options = new();

        options.Freeze();

        return options;
    }

    private static async Task<StorePolicyProvider> ProviderOver(params PolicyRule[] rules)
    {
        InMemoryPolicyStore store = new();

        store.Seed(rules);

        return await StorePolicyProvider.CreateAsync(store, Options(), autoRefresh: false);
    }

    // ------------------------------------------------------- 1. the context mutated after preparing

    [Fact]
    public async Task A_user_subject_added_after_preparing_is_refused()
    {
        // The narrow zone is read once, for the identities the context held at that moment. Adding
        // a user afterwards would leave that user's rules unread, and a user-level denial that goes
        // unread is access granted — the exact shape preparation exists to prevent, arriving through
        // the one door still open.
        using StorePolicyProvider provider = await ProviderOver(
            new PolicyRule(
                DwSubjectKind.User, "alice", StaffType, "Salary", PolicyFeature.Select,
                PolicyEffect.Deny));

        DwPolicyContext context = await provider.PrepareAsync(new DwPolicyContext());

        context.WithSubject(DwSubjectKind.User, "alice");

        PolicyException error = Assert.Throws<PolicyException>(
            () => provider.GetFragments(typeof(Staff), context));

        Assert.Equal(PolicyErrorCode.PolicyContextNotPrepared, error.ErrorCode);

        // Preparing again reads the zone for who the caller now is, and the denial applies.
        await provider.PrepareAsync(context);

        Assert.Single(provider.GetFragments(typeof(Staff), context));
    }

    [Fact]
    public void Coverage_compares_identities_rather_than_counting_them()
    {
        // Driven against the attachment directly, because DwPolicyContext can only gain subjects
        // and a count check catches a gain just as well. One identity swapped for another is the
        // case a count cannot see, and it is reachable the moment anything can remove a subject.
        PolicyAttachment attachment = new(
            StoreSnapshot.Empty, DateTimeOffset.UtcNow, NarrowZone.Empty, new[] { "bob" });

        Assert.False(attachment.Covers(new[] { "alice" }));
        Assert.True(attachment.Covers(new[] { "BOB" }));
        Assert.False(attachment.Covers(new[] { "bob", "alice" }));

        // Losing one is safe: the zone holds rules for a subject the caller no longer claims, and
        // those fail the ordinary subject match.
        Assert.True(attachment.Covers(Array.Empty<string>()));
    }

    [Fact]
    public async Task Adding_a_role_after_preparing_is_still_allowed()
    {
        // Only the narrow zone is read ahead of time. A role is matched live against the pinned
        // broad zone, so a role added later applies immediately and refusing it would be a cost
        // with nothing bought.
        using StorePolicyProvider provider = await ProviderOver(
            new PolicyRule(
                DwSubjectKind.Role, "Manager", StaffType, "Salary", PolicyFeature.Select,
                PolicyEffect.Deny));

        DwPolicyContext context = await provider.PrepareAsync(new DwPolicyContext());

        Assert.Empty(provider.GetFragments(typeof(Staff), context));

        context.WithSubject(DwSubjectKind.Role, "Manager");

        Assert.Single(provider.GetFragments(typeof(Staff), context));
    }

    // ------------------------------------------------------------- 2. a snapshot that can be edited

    [Fact]
    public void A_snapshot_cannot_be_emptied_by_a_caller_who_casts_it()
    {
        // The same defect AttributePolicyProvider guards against by wrapping its result. A snapshot
        // is shared by every request thread for as long as it is current, so one cast and one
        // Clear() would remove a denial process-wide.
        StoreSnapshot snapshot = new(
            1,
            DateTimeOffset.UtcNow,
            new[]
            {
                new PolicyRule(
                    DwSubjectKind.Global, null, StaffType, "Salary", PolicyFeature.Select,
                    PolicyEffect.Deny)
            });

        IReadOnlyList<PolicyRule> rules = snapshot.For(typeof(Staff));

        Assert.Single(rules);
        Assert.IsNotType<List<PolicyRule>>(rules);
        Assert.Throws<NotSupportedException>(() => ((IList<PolicyRule>)rules).Clear());
        Assert.Single(snapshot.For(typeof(Staff)));
    }

    [Fact]
    public void A_narrow_zone_cannot_be_emptied_either()
    {
        NarrowZone zone = new(
            1,
            new[]
            {
                new PolicyRule(
                    DwSubjectKind.User, "alice", StaffType, "Salary", PolicyFeature.Select,
                    PolicyEffect.Deny)
            });

        Assert.Throws<NotSupportedException>(
            () => ((IList<PolicyRule>)zone.For(typeof(Staff))).Clear());
    }

    // ------------------------------------------------------------ 4. operator lists held by reference

    [Fact]
    public void A_rule_copies_the_operator_lists_it_was_given()
    {
        // A rule lives in a snapshot for as long as that snapshot is current. Holding the caller's
        // array means whoever built the rule can widen the permitted set afterwards, from anywhere,
        // with nothing reloaded and nothing logged.
        Operator[] allowed = { Operator.Equal };
        Operator[] required = { Operator.Equal };

        PolicyRule rule = new(
            DwSubjectKind.Global, null, StaffType, "Badge", PolicyFeature.Where,
            PolicyEffect.Allow, allowedOperators: allowed, requiredOperators: required);

        allowed[0] = Operator.Contains;
        required[0] = Operator.Contains;

        Assert.Equal(Operator.Equal, rule.AllowedOperators![0]);
        Assert.Equal(Operator.Equal, rule.RequiredOperators![0]);
    }

    [Fact]
    public void An_empty_required_list_stays_empty_rather_than_becoming_no_requirement()
    {
        // Null and empty differ: null is "no requirement", empty is "a requirement nothing
        // satisfies". Copying must not turn one into the other.
        PolicyRule rule = new(
            DwSubjectKind.Global, null, StaffType, "Badge", PolicyFeature.Where,
            PolicyEffect.Allow, requiredOperators: Array.Empty<Operator>());

        Assert.NotNull(rule.RequiredOperators);
        Assert.Empty(rule.RequiredOperators!);
        Assert.Null(rule.AllowedOperators);
    }
}
