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
using DynamicWhere.ex.Policies.Validation;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// The security regression suite. Each test reproduces a disclosure channel from the design's
/// threat analysis and asserts that the mitigation closes it, so removing a mitigation turns a test
/// red rather than quietly reopening an attack.
/// </summary>
/// <remarks>
/// Seven mitigations across the five sections of §7, counted the way the roadmap counts them: §7.1
/// delivered two and §7.2 needs two. Three were brought forward to Phase 3, because they were
/// reachable as soon as injection landed and needed nothing from the mask engine. The rest arrive
/// with the masking and Summary support they depend on.
/// <para>
/// Two of the seven needed no code in this phase at all. §7.3's control is <c>[DwOperators]</c>,
/// shipped in Phase 1, and §7.4's is a warning the startup scan has emitted since Phase 4 — what
/// they lacked was a test that reproduces the attack and shows the control stopping it. A
/// mitigation nobody has attacked is a claim rather than a control.
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
        new() { Tier = tier, DryRun = dryRun, Caps = { MinGroupSize = 1 } };

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

    // ------------------------------------------- 7.2 aggregates over singleton groups (denial)

    private PolicyQueryable<Person> People(DwPolicyOptions options) =>
        _db.People.ApplyPolicy(Caller(), options, Attributes());

    private static DwPolicyOptions Floor(int minGroupSize)
    {
        DwPolicyOptions options = new();

        options.Caps.MinGroupSize = minGroupSize;

        return options;
    }

    private static Summary Oldest(string field = "Age", string alias = "Oldest") => new()
    {
        GroupBy = new GroupBy
        {
            Fields = new List<string> { "Department" },
            AggregateBy = new List<AggregateBy>
            {
                new() { Field = field, Aggregator = Aggregator.Maximum, Alias = alias }
            }
        }
    };

    [Fact]
    public void A_transformed_field_cannot_be_aggregated()
    {
        // MAX runs in SQL against the stored values, before the rounding that hides them. Without
        // this the caller reads the real maximum of a column the policy transforms on the way out.
        PolicyException error = Assert.Throws<PolicyException>(
            () => People(Options(DwTier.Convenience)).ToList(Oldest("Salary", "Top")));

        Assert.Equal(PolicyErrorCode.FieldDeniedForAggregate, error.ErrorCode);
    }

    [Fact]
    public void The_denial_holds_in_both_tiers()
    {
        // Unlike §7.1's rule, this one is not a tier judgement. The value leaks identically to a
        // trusted caller and an untrusted one, because it is SQL doing the leaking.
        Assert.Throws<PolicyException>(
            () => People(Options(DwTier.Strict)).ToList(Oldest("Salary", "Top")));

        Assert.Throws<PolicyException>(
            () => People(Options(DwTier.Convenience)).ToList(Oldest("Salary", "Top")));
    }

    [Fact]
    public void A_field_that_opted_in_is_still_aggregatable()
    {
        // The mitigation is a default, not a prohibition. Age opts in deliberately, which is what
        // makes the singleton-group attack below reachable at all.
        SummaryResult result = People(Options(DwTier.Convenience)).ToList(Oldest());

        Assert.Equal(2, result.Data.Count);
    }

    [Fact]
    public void An_untransformed_field_is_unaffected()
    {
        SummaryResult result = People(Options(DwTier.Convenience))
            .ToList(Oldest("Id", "Highest"));

        Assert.Equal(2, result.Data.Count);
    }

    // -------------------------------------------- 7.2 aggregates over singleton groups (floor)

    [Fact]
    public void A_group_of_one_discloses_that_person_without_a_floor()
    {
        // The attack, run. Sales is Cy and nobody else, so the maximum age of Sales is Cy's age —
        // reached without ever selecting Age, and through a field that opted into aggregation.
        SummaryResult result = People(Floor(1)).ToList(Oldest());

        dynamic sales = result.Data.Single(row => (string)((dynamic)row).Department == "Sales");

        Assert.Equal(25, (int)sales.Oldest);
    }

    [Fact]
    public void The_floor_removes_the_group_that_would_have_disclosed_it()
    {
        SummaryResult result = People(Floor(2)).ToList(Oldest());

        Assert.DoesNotContain(
            result.Data,
            row => (string)((dynamic)row).Department == "Sales");

        Assert.Contains(
            result.Data,
            row => (string)((dynamic)row).Department == "Engineering");
    }

    [Fact]
    public void The_removal_is_recorded_rather_than_silent()
    {
        PolicyQueryable<Person> guarded = People(Floor(2));

        guarded.ToList(Oldest());

        Assert.Contains(
            guarded.LastTrace!.Decisions,
            d => d.Action == PolicyAction.Dropped && d.Feature == PolicyFeature.Aggregate);
    }

    // ------------------------------------------------- 7.3 TotalCount cardinality disclosure

    private static Filter Where(string field, Operator op, object value) => new()
    {
        ConditionGroup = new ConditionGroup
        {
            Sort = 1,
            Conditions =
            {
                new Condition
                {
                    Sort = 1,
                    Field = field,
                    DataType = value is string ? DataType.Text : DataType.Number,
                    Operator = op,
                    Values = { value }
                }
            }
        }
    };

    [Fact]
    public void An_operator_restriction_stops_the_cardinality_probe_before_it_starts()
    {
        // §7.3 is inherent to permitting WHERE on a protected field: TotalCount counts the matches
        // of a range filter without selecting anything, so a caller who may range-filter may count.
        // The control is the operator restriction, which leaves a field confirmable by someone who
        // already holds the value and undiscoverable by someone who does not.
        PolicyException error = Assert.Throws<PolicyException>(
            () => Guarded(DwTier.Convenience).ToList(Where("Badge", Operator.GreaterThan, "E-2")));

        Assert.Equal(PolicyErrorCode.OperatorNotAllowed, error.ErrorCode);
    }

    [Fact]
    public void The_operators_the_restriction_permits_still_work()
    {
        // Confirmation is the point of the permitted set. Refusing everything would be a denial,
        // and this field is not denied.
        FilterResult<Staff> result =
            Guarded(DwTier.Convenience).ToList(Where("Badge", Operator.Equal, "E-1"));

        Assert.Single(result.Data);
    }

    [Fact]
    public void Without_the_restriction_the_count_really_does_disclose()
    {
        // The documented consequence, asserted so that it is a decision on record rather than an
        // oversight. Salary carries no operator restriction, so a range filter counts the high
        // earners while selecting nothing — which is why §7.3 names [DwOperators] as the control
        // and not something the engine applies on its own.
        //
        // Counted against the unguarded query rather than a number written here, so the assertion
        // stays about the disclosure rather than about what the fixture happens to hold.
        int reallyOver = _db.Staff.Count(s => s.Salary > 100000m);

        FilterResult<Staff> result = Guarded(DwTier.Convenience)
            .ToList(Where("Salary", Operator.GreaterThan, 100000));

        Assert.Equal(reallyOver, result.TotalCount);

        // And the field itself never left the database. The count is the whole disclosure: at this
        // threshold it names one person as earning over a hundred thousand, through a column the
        // policy refuses to project.
        Assert.All(result.Data, row => Assert.Equal(0m, row.Salary));
    }

    // ------------------------------------------------- 7.4 order plus paging is a binary search

    [Fact]
    public void The_startup_scan_warns_when_a_masked_field_can_still_be_sorted()
    {
        // Sorting runs against the real value, so paging a masked column ranks the true order and,
        // with range filters, converges on it. The engine does not decide this silently: it warns,
        // and [DwNoOrder] remains the explicit fix.
        PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(Guarded) });

        Assert.True(report.IsValid);
        Assert.Contains(
            report.Warnings,
            w => w.Contains("Guarded.Salary") && w.Contains("ranks the true order"));
    }

    [Fact]
    public void A_masked_field_that_took_the_fix_is_refused_for_sorting()
    {
        // Person.NationalId is masked and carries [DwNoOrder], which is the shape the warning asks
        // for. The refusal is what closes the channel; the warning only points at it.
        Filter filter = new()
        {
            Orders = new List<OrderBy>
            {
                new() { Sort = 1, Field = "NationalId", Direction = Direction.Ascending }
            }
        };

        PolicyException error = Assert.Throws<PolicyException>(
            () => People(Options(DwTier.Strict)).ToList(filter));

        Assert.Equal(PolicyErrorCode.FieldDeniedForOrder, error.ErrorCode);
    }

    [Fact]
    public void A_field_that_took_the_fix_draws_no_warning()
    {
        // The warning has to go quiet once the fix is applied, or it is noise nobody reads and the
        // one that matters is lost in it.
        PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(Person) });

        Assert.DoesNotContain(report.Warnings, w => w.Contains("Person.NationalId"));
    }
}
