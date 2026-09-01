using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// A policy store end to end, against a real database.
/// </summary>
/// <remarks>
/// The provider suite proves the right fragments are emitted. This one proves they reach a query:
/// a rule written to a store masks a column that carries no attribute at all, and a rule written to
/// a store cannot unmask one that does.
/// </remarks>
[Collection(PolicyCollection.Name)]
public class StoreQueryTests : IDisposable
{
    private const string PersonType = "DynamicWhere.Tests.Policies.Person";

    private readonly PolicyContext _db;

    public StoreQueryTests(PolicyFixture fixture) => _db = fixture.CreateContext();

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private static DwPolicyOptions Options()
    {
        DwPolicyOptions options = new() { HashSalt = "pepper" };

        options.Freeze();

        return options;
    }

    /// <summary>Seeds a store, builds the provider, and prepares a caller against it.</summary>
    private static async Task<(StorePolicyProvider Provider, DwPolicyContext Caller)> Given(
        params PolicyRule[] rules)
    {
        InMemoryPolicyStore store = new();

        store.Seed(rules);

        StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, Options(), autoRefresh: false);

        DwPolicyContext caller = new DwPolicyContext()
            .WithSubject(DwSubjectKind.Role, "Manager")
            .WithSubject(DwSubjectKind.User, "u1");

        return (provider, await provider.PrepareAsync(caller));
    }

    /// <summary>A resolver over the attributes plus the store.</summary>
    private static PolicyResolver Both(StorePolicyProvider provider) =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider(), provider });

    // -------------------------------------------------------------------- a rule reaches a query

    [Fact]
    public async Task A_store_rule_masks_a_column_that_carries_no_attribute()
    {
        // Person.Department is undecorated. Everything hiding it comes from the store, which is the
        // whole point of the phase: policy that is not in the source code.
        (StorePolicyProvider provider, DwPolicyContext caller) = await Given(
            new PolicyRule(
                DwSubjectKind.Role, "Manager", PersonType, "Department", PolicyFeature.Select,
                PolicyEffect.Mask, transform: new MaskStage(MaskStrategy.Full)));

        using (provider)
        {
            List<Person> people = _db.People
                .ApplyPolicy(caller, Options(), Both(provider))
                .ToList(new Filter())
                .Data;

            Assert.All(people, p => Assert.DoesNotContain("Engineering", p.Department));
            Assert.All(people, p => Assert.NotEmpty(p.Department));
        }
    }

    [Fact]
    public async Task Without_the_rule_the_same_column_comes_back_whole()
    {
        // The control. Without it the masking test would pass against a column that was empty in
        // the fixture all along.
        (StorePolicyProvider provider, DwPolicyContext caller) = await Given();

        using (provider)
        {
            List<Person> people = _db.People
                .ApplyPolicy(caller, Options(), Both(provider))
                .ToList(new Filter())
                .Data;

            Assert.Contains(people, p => p.Department == "Engineering");
        }
    }

    [Fact]
    public async Task A_store_rule_refuses_a_projection()
    {
        (StorePolicyProvider provider, DwPolicyContext caller) = await Given(
            new PolicyRule(
                DwSubjectKind.Role, "Manager", PersonType, "Department", PolicyFeature.Select,
                PolicyEffect.Deny));

        using (provider)
        {
            Filter filter = new() { Selects = new List<string> { "Department" } };

            // A block body, so the delegate is an Action: ToListDynamic returns a
            // FilterResult<dynamic>, and an expression lambda over it binds the Func<Task> overload.
            PolicyException error = Assert.Throws<PolicyException>(
                () =>
                {
                    _ = _db.People
                        .ApplyPolicy(caller, Options(), Both(provider))
                        .ToListDynamic(filter);
                });

            Assert.Equal(PolicyErrorCode.AllSelectsDenied, error.ErrorCode);
        }
    }

    [Fact]
    public async Task A_user_rule_reaches_the_query_through_the_narrow_zone()
    {
        (StorePolicyProvider provider, DwPolicyContext caller) = await Given(
            new PolicyRule(
                DwSubjectKind.User, "u1", PersonType, "Department", PolicyFeature.Select,
                PolicyEffect.Mask, transform: new MaskStage(MaskStrategy.Fixed, text: "hidden")));

        using (provider)
        {
            List<Person> people = _db.People
                .ApplyPolicy(caller, Options(), Both(provider))
                .ToList(new Filter())
                .Data;

            Assert.All(people, p => Assert.Equal("hidden", p.Department));
        }
    }

    // -------------------------------------------------------------------- what a rule cannot do

    [Fact]
    public async Task A_store_rule_cannot_unmask_a_sealed_attribute()
    {
        // Person.NationalId carries [DwMask(Partial, KeepEnd = 4)], which is sealed. A rule that
        // could replace it would make the store a credential — one rogue row and the column is in
        // the clear.
        (StorePolicyProvider provider, DwPolicyContext caller) = await Given(
            new PolicyRule(
                DwSubjectKind.User, "u1", PersonType, "NationalId", PolicyFeature.Select,
                PolicyEffect.Allow, priority: int.MaxValue,
                transform: new MaskStage(MaskStrategy.Fixed, text: "exposed")));

        using (provider)
        {
            // The rule is genuinely in play, not quietly unmatched. Without this the test would
            // pass just as well against a rule aimed at a field that does not exist.
            Assert.Contains(
                provider.GetFragments(typeof(Person), caller),
                f => f.FieldPath == "NationalId" && f.Transform is not null);

            Person ada = _db.People
                .ApplyPolicy(caller, Options(), Both(provider))
                .ToList(new Filter())
                .Data
                .Single(p => p.Name == "Ada");

            Assert.Equal("********2345", ada.NationalId);
        }
    }

    [Fact]
    public async Task An_empty_store_leaves_the_attributes_enforcing_end_to_end()
    {
        // Design section 8.5's "empty policy store" row, against a database. A store that says
        // nothing must not read as a store that permits everything.
        (StorePolicyProvider provider, DwPolicyContext caller) = await Given();

        using (provider)
        {
            Person ada = _db.People
                .ApplyPolicy(caller, Options(), Both(provider))
                .ToList(new Filter())
                .Data
                .Single(p => p.Name == "Ada");

            Assert.Equal("********2345", ada.NationalId);
            Assert.Equal("N/A", ada.Notes);
        }
    }

    [Fact]
    public async Task An_expired_rule_stops_masking_without_anything_being_reloaded()
    {
        DateTimeOffset noon = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        InMemoryPolicyStore store = new();

        store.Seed(new PolicyRule(
            DwSubjectKind.Role, "Manager", PersonType, "Department", PolicyFeature.Select,
            PolicyEffect.Mask, validFrom: noon, validTo: noon.AddHours(1),
            transform: new MaskStage(MaskStrategy.Fixed, text: "hidden")));

        DwPolicyOptions options = new() { HashSalt = "pepper", MaxSnapshotAge = TimeSpan.FromDays(3650) };

        options.Freeze();

        using StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, options, autoRefresh: false);

        DwPolicyContext caller = await provider.PrepareAsync(
            new DwPolicyContext().WithSubject(DwSubjectKind.Role, "Manager"));

        provider.Clock = () => noon.AddMinutes(30);

        Assert.All(
            _db.People.ApplyPolicy(caller, options, Both(provider)).ToList(new Filter()).Data,
            p => Assert.Equal("hidden", p.Department));

        // One second past the window. Same store, same snapshot, same prepared context.
        provider.Clock = () => noon.AddHours(1);

        Assert.Contains(
            _db.People.ApplyPolicy(caller, options, Both(provider)).ToList(new Filter()).Data,
            p => p.Department == "Engineering");
    }

    [Fact]
    public async Task An_unprepared_context_refuses_a_real_query()
    {
        InMemoryPolicyStore store = new();

        store.Seed(new PolicyRule(
            DwSubjectKind.Global, null, PersonType, "Department", PolicyFeature.Select,
            PolicyEffect.Deny));

        using StorePolicyProvider provider = await StorePolicyProvider.CreateAsync(
            store, Options(), autoRefresh: false);

        PolicyException error = Assert.Throws<PolicyException>(
            () => _db.People
                .ApplyPolicy(new DwPolicyContext(), Options(), Both(provider))
                .ToList(new Filter()));

        Assert.Equal(PolicyErrorCode.PolicyContextNotPrepared, error.ErrorCode);
    }
}
