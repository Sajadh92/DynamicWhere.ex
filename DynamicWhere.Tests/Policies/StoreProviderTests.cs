using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers the provider that turns store rules into fragments: what reaches the resolver, what does
/// not, and what is refused outright.
/// </summary>
public class StoreProviderTests
{
    private const string StaffType = "DynamicWhere.Tests.Policies.Staff";

    private static PolicyRule Rule(
        DwSubjectKind kind = DwSubjectKind.Role,
        string? key = "Manager",
        string field = "Department",
        PolicyFeature features = PolicyFeature.Select,
        PolicyEffect effect = PolicyEffect.Deny,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? purpose = null) =>
        new(kind, key, StaffType, field, features, effect,
            validFrom: from, validTo: to, purpose: purpose);

    private static DwPolicyOptions Options(TimeSpan? maxAge = null)
    {
        DwPolicyOptions options = new();

        if (maxAge is not null)
        {
            options.MaxSnapshotAge = maxAge.Value;
        }

        options.Freeze();

        return options;
    }

    /// <summary>Builds a provider over a seeded store, with no background refresh.</summary>
    private static async Task<StorePolicyProvider> ProviderOver(params PolicyRule[] rules) =>
        await ProviderOver(null, rules);

    /// <summary>
    /// Builds a provider with a chosen staleness ceiling, for a test that moves the clock further
    /// than the default one allows. Staleness is <see cref="StoreFailureTests"/>' subject, not this
    /// file's.
    /// </summary>
    private static async Task<StorePolicyProvider> ProviderOver(
        TimeSpan? maxAge, params PolicyRule[] rules)
    {
        InMemoryPolicyStore store = new();

        store.Seed(rules);

        return await StorePolicyProvider.CreateAsync(store, Options(maxAge), autoRefresh: false);
    }

    /// <summary>Builds a context and prepares it against one provider.</summary>
    private static async Task<DwPolicyContext> PreparedFor(
        StorePolicyProvider provider, params DwSubject[] subjects)
    {
        DwPolicyContext context = new();

        foreach (DwSubject subject in subjects)
        {
            context.WithSubject(subject.Kind, subject.Identity);
        }

        return await provider.PrepareAsync(context);
    }

    // ---------------------------------------------------------------- the unprepared context

    [Fact]
    public async Task An_unprepared_context_is_refused_rather_than_served_from_the_broad_zone()
    {
        // The whole reason preparation exists. Served from the broad zone alone, a user-level
        // denial simply would not appear, and that is indistinguishable from a caller who has no
        // user rules.
        using StorePolicyProvider provider = await ProviderOver(Rule());

        PolicyException error = Assert.Throws<PolicyException>(
            () => provider.GetFragments(typeof(Staff), new DwPolicyContext()));

        Assert.Equal(PolicyErrorCode.PolicyContextNotPrepared, error.ErrorCode);
    }

    [Fact]
    public async Task An_unprepared_context_is_refused_even_with_no_user_subjects()
    {
        // No carve-out for a caller whose narrow zone would have been empty. That reasoning is
        // correct today and is exactly the shape that has failed open on this branch before, and
        // refusing unconditionally is also what makes the pinned snapshot unconditional.
        using StorePolicyProvider provider = await ProviderOver(Rule(DwSubjectKind.Global, null));

        DwPolicyContext roleOnly = new DwPolicyContext().WithSubject(DwSubjectKind.Role, "Manager");

        Assert.Throws<PolicyException>(() => provider.GetFragments(typeof(Staff), roleOnly));
    }

    [Fact]
    public async Task A_context_prepared_against_one_provider_is_not_prepared_for_another()
    {
        using StorePolicyProvider first = await ProviderOver(Rule());
        using StorePolicyProvider second = await ProviderOver(Rule());

        DwPolicyContext context = await PreparedFor(
            first, new DwSubject(DwSubjectKind.Role, "Manager"));

        await second.PrepareAsync(new DwPolicyContext());

        Assert.NotEmpty(first.GetFragments(typeof(Staff), context));
        Assert.Throws<PolicyException>(() => second.GetFragments(typeof(Staff), context));
    }

    // ---------------------------------------------------------------- what reaches the resolver

    [Fact]
    public async Task A_rule_reaches_the_resolver_as_a_fragment_at_its_subject_level()
    {
        using StorePolicyProvider provider = await ProviderOver(Rule());

        DwPolicyContext context = await PreparedFor(
            provider, new DwSubject(DwSubjectKind.Role, "Manager"));

        PolicyFragment fragment = Assert.Single(provider.GetFragments(typeof(Staff), context));

        Assert.Equal("Department", fragment.FieldPath);
        Assert.Equal(PolicyLevel.DynamicRole, fragment.Level);
        Assert.Equal(PolicyEffect.Deny, fragment.Effect);
    }

    [Fact]
    public async Task A_rule_for_a_subject_the_caller_does_not_hold_is_left_out()
    {
        using StorePolicyProvider provider = await ProviderOver(Rule(key: "Auditor"));

        DwPolicyContext context = await PreparedFor(
            provider, new DwSubject(DwSubjectKind.Role, "Manager"));

        Assert.Empty(provider.GetFragments(typeof(Staff), context));
    }

    [Fact]
    public async Task A_rule_for_another_entity_is_left_out()
    {
        using StorePolicyProvider provider = await ProviderOver(Rule(DwSubjectKind.Global, null));

        DwPolicyContext context = await PreparedFor(provider);

        Assert.NotEmpty(provider.GetFragments(typeof(Staff), context));
        Assert.Empty(provider.GetFragments(typeof(Office), context));
    }

    [Fact]
    public async Task A_user_rule_arrives_through_the_narrow_zone()
    {
        using StorePolicyProvider provider = await ProviderOver(Rule(DwSubjectKind.User, "alice"));

        DwPolicyContext alice = await PreparedFor(
            provider, new DwSubject(DwSubjectKind.User, "alice"));

        DwPolicyContext bob = await PreparedFor(
            provider, new DwSubject(DwSubjectKind.User, "bob"));

        Assert.Equal(
            PolicyLevel.DynamicUser,
            Assert.Single(provider.GetFragments(typeof(Staff), alice)).Level);

        Assert.Empty(provider.GetFragments(typeof(Staff), bob));
    }

    [Fact]
    public async Task Broad_and_narrow_rules_both_reach_one_caller()
    {
        using StorePolicyProvider provider = await ProviderOver(
            Rule(DwSubjectKind.Role, "Manager"),
            Rule(DwSubjectKind.User, "alice", field: "Notes"));

        DwPolicyContext context = await PreparedFor(
            provider,
            new DwSubject(DwSubjectKind.Role, "Manager"),
            new DwSubject(DwSubjectKind.User, "alice"));

        Assert.Equal(2, provider.GetFragments(typeof(Staff), context).Count);
    }

    // ---------------------------------------------------------------- validity and purpose

    [Fact]
    public async Task A_grant_stops_applying_the_moment_it_expires_rather_than_at_the_next_refresh()
    {
        // Evaluated against the clock per query. Resolved into the snapshot at load, this rule
        // would keep applying for the whole staleness ceiling after it expired.
        DateTimeOffset noon = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        using StorePolicyProvider provider = await ProviderOver(
            TimeSpan.FromDays(3650), Rule(from: noon, to: noon.AddHours(1)));

        DwPolicyContext context = await PreparedFor(
            provider, new DwSubject(DwSubjectKind.Role, "Manager"));

        provider.Clock = () => noon.AddMinutes(30);
        Assert.Single(provider.GetFragments(typeof(Staff), context));

        provider.Clock = () => noon.AddHours(1);
        Assert.Empty(provider.GetFragments(typeof(Staff), context));

        // The same pinned snapshot, the same prepared context, a different answer. Nothing was
        // reloaded in between.
        provider.Clock = () => noon.AddMinutes(-1);
        Assert.Empty(provider.GetFragments(typeof(Staff), context));
    }

    [Fact]
    public async Task A_purpose_bound_rule_applies_only_when_the_caller_declares_it()
    {
        using StorePolicyProvider provider = await ProviderOver(Rule(purpose: "Billing"));

        DwPolicyContext declared = await PreparedFor(
            provider, new DwSubject(DwSubjectKind.Role, "Manager"));

        declared.Purpose = "billing";

        Assert.Single(provider.GetFragments(typeof(Staff), declared));

        DwPolicyContext silent = await PreparedFor(
            provider, new DwSubject(DwSubjectKind.Role, "Manager"));

        Assert.Empty(provider.GetFragments(typeof(Staff), silent));
    }

    // ---------------------------------------------------------------- against the real resolver

    [Fact]
    public async Task A_store_rule_cannot_loosen_a_sealed_attribute()
    {
        // The guarantee the whole level ordering exists for, now reachable for the first time
        // because a real store can finally supply the competing fragment.
        using StorePolicyProvider provider = await ProviderOver(
            new PolicyRule(
                DwSubjectKind.User, "alice", StaffType, "NationalId", PolicyFeature.All,
                PolicyEffect.Allow, priority: int.MaxValue));

        DwPolicyContext context = await PreparedFor(
            provider, new DwSubject(DwSubjectKind.User, "alice"));

        PolicyResolver resolver = new(new IDwPolicyProvider[]
        {
            new AttributePolicyProvider(), provider
        });

        FieldPolicy policy = resolver.Resolve(typeof(Staff), "NationalId", context);

        Assert.False(policy.Allows(PolicyFeature.Select));
        Assert.True(policy.IsSealed);
    }

    [Fact]
    public async Task A_store_rule_replaces_an_overridable_attribute()
    {
        // The other half: an attribute marked Overridable is a default, and the store is what
        // overrides it. SecuredEmployee.Salary carries [DwNoSelect(Overridable = true)].
        using StorePolicyProvider provider = await ProviderOver(
            new PolicyRule(
                DwSubjectKind.Role, "Manager", "DynamicWhere.Tests.Policies.SecuredEmployee",
                "Salary", PolicyFeature.Select, PolicyEffect.Allow));

        DwPolicyContext context = await PreparedFor(
            provider, new DwSubject(DwSubjectKind.Role, "Manager"));

        PolicyResolver resolver = new(new IDwPolicyProvider[]
        {
            new AttributePolicyProvider(), provider
        });

        Assert.True(resolver.Resolve(typeof(SecuredEmployee), "Salary", context)
            .Allows(PolicyFeature.Select));

        // Without the rule the attribute's default stands.
        Assert.False(new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() })
            .Resolve(typeof(SecuredEmployee), "Salary", new DwPolicyContext())
            .Allows(PolicyFeature.Select));
    }

    [Fact]
    public async Task An_empty_store_leaves_the_attributes_enforcing()
    {
        // Design section 8.5's "empty policy store" row. A store that says nothing must not read as
        // a store that permits everything.
        using StorePolicyProvider provider = await ProviderOver();

        DwPolicyContext context = await PreparedFor(provider);

        PolicyResolver resolver = new(new IDwPolicyProvider[]
        {
            new AttributePolicyProvider(), provider
        });

        Assert.False(resolver.Resolve(typeof(Staff), "NationalId", context)
            .Allows(PolicyFeature.Select));
    }

    // ---------------------------------------------------------------- arguments

    [Fact]
    public async Task Null_arguments_are_refused()
    {
        using StorePolicyProvider provider = await ProviderOver();

        DwPolicyContext context = await PreparedFor(provider);

        Assert.Throws<ArgumentNullException>(() => provider.GetFragments(null!, context));
        Assert.Throws<ArgumentNullException>(() => provider.GetFragments(typeof(Staff), null!));

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await provider.PrepareAsync(null!));

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await StorePolicyProvider.CreateAsync(null!, Options()));

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await StorePolicyProvider.CreateAsync(new InMemoryPolicyStore(), null!));
    }
}
