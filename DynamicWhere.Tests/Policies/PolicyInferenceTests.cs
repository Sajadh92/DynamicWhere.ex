using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
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
/// The security regression suite. Each test reproduces a disclosure channel from the design's
/// threat analysis and asserts that the mitigation closes it, so removing a mitigation turns a test
/// red rather than quietly reopening an attack.
/// </summary>
/// <remarks>
/// Started early. Two of the seven channels were reachable on this branch as soon as injection
/// landed, and both are closed by rules that need nothing from the mask engine — so they were
/// brought forward rather than left until the phase that owns the rest.
/// <para>
/// The remaining five arrive with their mitigations: the singleton-group aggregate and the
/// order-plus-page binary search both need masking to exist first, and <c>MinGroupSize</c> is only
/// half a mitigation without <c>AllowAggregate</c> to bound.
/// </para>
/// </remarks>
[Collection(PolicyCollection.Name)]
public class PolicyInferenceTests : IDisposable
{
    private readonly PolicyContext _db;

    public PolicyInferenceTests(PolicyFixture fixture) => _db = fixture.CreateContext();

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private static DwPolicyContext Caller() =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1").WithValue("TenantId", 5);

    private static PolicyResolver Attributes() =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    private static DwPolicyOptions Options(DwTier tier, bool dryRun = false) =>
        new() { Tier = tier, DryRun = dryRun };

    private PolicyQueryable<Staff> Guarded(DwTier tier, bool dryRun = false) =>
        _db.Staff.ApplyPolicy(Caller(), Options(tier, dryRun), Attributes());

    private static Condition OnSalary() => new()
    {
        Field = "Salary", DataType = DataType.Number,
        Operator = Operator.GreaterThan, Values = { 100000 }
    };

    private static Segment SegmentOn(Condition condition)
    {
        ConditionGroup group = new();

        group.Conditions.Add(condition);

        return new Segment
        {
            ConditionSets = new List<ConditionSet> { new() { Sort = 0, ConditionGroup = group } }
        };
    }

    private static Segment Sanitize(Segment segment, DwTier tier) =>
        FilterSanitizer.Sanitize<Staff>(
            segment, Attributes(), Caller(), Options(tier), new PolicyTrace(tier, dryRun: false));

    // ------------------------------------------------------- 7.1 set operations reconstruct

    [Fact]
    public void Strict_refuses_a_filter_on_a_deny_select_field_inside_a_segment()
    {
        // The attack: Salary is refused for projection but allowed for filtering, so
        // AllStaff EXCEPT (AllStaff WHERE Salary > 100000) returns exactly the people earning under
        // 100k, by name, with the salary column never selected. The value is reconstructed from set
        // membership, which narrowing a projection cannot prevent.
        PolicyException error = Assert.Throws<PolicyException>(
            () => Sanitize(SegmentOn(OnSalary()), DwTier.Strict));

        Assert.Equal(PolicyErrorCode.FieldDeniedForSegment, error.ErrorCode);
        Assert.Equal("Salary", error.FieldPath);
    }

    [Fact]
    public void The_rule_reaches_a_condition_nested_inside_a_subgroup()
    {
        // A gate that looked only at the top level would be bypassed by nesting one group deeper.
        ConditionGroup root = new();

        root.SubConditionGroups.Add(SegmentOn(OnSalary()).ConditionSets![0].ConditionGroup);

        Segment segment = new()
        {
            ConditionSets = new List<ConditionSet> { new() { Sort = 0, ConditionGroup = root } }
        };

        Assert.Throws<PolicyException>(() => Sanitize(segment, DwTier.Strict));
    }

    [Fact]
    public void The_rule_applies_to_every_set_not_only_the_first()
    {
        Segment segment = new()
        {
            ConditionSets = new List<ConditionSet>
            {
                new()
                {
                    Sort = 0,
                    ConditionGroup = new ConditionGroup
                    {
                        Conditions =
                        {
                            new Condition
                            {
                                Field = "Name", DataType = DataType.Text,
                                Operator = Operator.NotEqual, Values = { "zzz" }
                            }
                        }
                    }
                },
                new()
                {
                    Sort = 1, Intersection = Intersection.Except,
                    ConditionGroup = new ConditionGroup { Conditions = { OnSalary() } }
                }
            }
        };

        Assert.Throws<PolicyException>(() => Sanitize(segment, DwTier.Strict));
    }

    [Fact]
    public void The_convenience_tier_still_allows_it_because_the_tier_is_the_control()
    {
        // Deliberate. The convenience tier's caller is the project's own front end, and this rule
        // refuses a filter that is legitimate outside a set operation. The threat model, not the
        // engine, decides which posture pays that cost.
        Segment result = Sanitize(SegmentOn(OnSalary()), DwTier.Convenience);

        Assert.Single(result.ConditionSets![0].ConditionGroup.Conditions);
    }

    [Fact]
    public void The_same_filter_outside_a_segment_is_still_allowed_in_strict()
    {
        // The rule is about set membership, not about the field. Refusing Salary everywhere would
        // be a different policy, and one the caller can already express with DwNoWhere.
        Filter filter = new() { ConditionGroup = new ConditionGroup { Conditions = { OnSalary() } } };

        Filter result = FilterSanitizer.Sanitize<Staff>(
            filter, Attributes(), Caller(), Options(DwTier.Strict),
            new PolicyTrace(DwTier.Strict, dryRun: false));

        Assert.Single(result.ConditionGroup!.Conditions);
    }

    [Fact]
    public void A_field_allowed_for_projection_is_unaffected_inside_a_segment()
    {
        Segment segment = SegmentOn(new Condition
        {
            Field = "Department", DataType = DataType.Text,
            Operator = Operator.Equal, Values = { "Engineering" }
        });

        Assert.Single(Sanitize(segment, DwTier.Strict).ConditionSets![0].ConditionGroup.Conditions);
    }

    // ------------------------------------------------- 7.5 getQueryString leaks the generated SQL

    [Fact]
    public void Strict_refuses_to_hand_back_the_generated_sql()
    {
        // The SQL names the columns of denied fields and spells out every injected predicate, so
        // returning it hands a semi-trusted caller both the schema and the shape of their own cage.
        PolicyException error = Assert.Throws<PolicyException>(
            () => Guarded(DwTier.Strict).ToList(new Filter(), getQueryString: true));

        Assert.Equal(PolicyErrorCode.QueryStringDenied, error.ErrorCode);
    }

    [Fact]
    public async Task Every_method_that_can_return_the_sql_refuses_in_strict()
    {
        // One guarded method that forgot the check is the whole mitigation gone, so the surface is
        // asserted as a whole rather than one method at a time.
        PolicyQueryable<Staff> guarded = Guarded(DwTier.Strict);

        Assert.Throws<PolicyException>(() => guarded.ToList(new Filter(), true));
        Assert.Throws<PolicyException>(() => guarded.ToListDynamic(new Filter(), true));
        await Assert.ThrowsAsync<PolicyException>(() => guarded.ToListAsync(new Filter(), true));
        await Assert.ThrowsAsync<PolicyException>(() => guarded.ToListAsyncDynamic(new Filter(), true));

        Summary summary = new() { GroupBy = new GroupBy { Fields = new List<string> { "Department" } } };

        Assert.Throws<PolicyException>(() => guarded.ToList(summary, true));
        await Assert.ThrowsAsync<PolicyException>(() => guarded.ToListAsync(summary, true));
    }

    [Fact]
    public void The_convenience_tier_still_returns_the_sql()
    {
        FilterResult<Staff> result = Guarded(DwTier.Convenience).ToList(new Filter(), getQueryString: true);

        Assert.False(string.IsNullOrWhiteSpace(result.QueryString));
    }

    [Fact]
    public void A_strict_query_that_does_not_ask_for_the_sql_is_unaffected()
    {
        FilterResult<Staff> result = Guarded(DwTier.Strict).ToList(new Filter());

        Assert.Equal(3, result.Data.Count);
        Assert.Null(result.QueryString);
    }

    [Fact]
    public void A_dry_run_records_the_refusal_rather_than_throwing()
    {
        // Dry run overrides the whole table, here as everywhere. An operator running a canary needs
        // to know the request would have been refused.
        PolicyQueryable<Staff> guarded = Guarded(DwTier.Strict, dryRun: true);

        FilterResult<Staff> result = guarded.ToList(new Filter(), getQueryString: true);

        Assert.NotNull(result.QueryString);
        Assert.Contains(
            guarded.LastTrace!.Decisions,
            d => d.Action == PolicyAction.Denied && d.Feature == PolicyFeature.None);
    }
}
