using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Design section 7.2's second mitigation: the k-anonymity floor that keeps
/// <c>AllowAggregate</c> from being an opening rather than a permission.
/// </summary>
/// <remarks>
/// Aggregation over a group of one returns that row's exact value under any function, so permitting
/// a masked field to be aggregated without a floor hands back precisely what the mask was there to
/// hide. The floor suppresses a group smaller than <c>k</c>.
/// <para>
/// It is <b>on by default</b>, at <c>DwCaps.DefaultMinGroupSize</c>. A deployment that wants no
/// floor writes <c>MinGroupSize = 1</c> and gets exactly that. Those are two different
/// instructions, which is why the setting starts unset rather than at one — see
/// <see cref="A_deployment_that_says_nothing_gets_the_safe_floor"/> and
/// <see cref="An_explicit_one_switches_the_floor_off_and_is_honoured"/>.
/// </para>
/// <para>
/// Above one it applies to every grouped summary rather than only those touching a transformed
/// field, because a group of one is a re-identification risk whatever is in it.
/// </para>
/// </remarks>
public class PolicyGroupFloorTests
{
    /// <summary>Two in Engineering, three in Support, one in Legal.</summary>
    private static Staffer[] Rows() => new[]
    {
        new Staffer { Id = 1, Department = "Engineering", Salary = 100, Headcount = 1 },
        new Staffer { Id = 2, Department = "Engineering", Salary = 120, Headcount = 1 },
        new Staffer { Id = 3, Department = "Support", Salary = 60, Headcount = 1 },
        new Staffer { Id = 4, Department = "Support", Salary = 65, Headcount = 1 },
        new Staffer { Id = 5, Department = "Support", Salary = 70, Headcount = 1 },
        new Staffer { Id = 6, Department = "Legal", Salary = 400, Headcount = 1 }
    };

    private static DwPolicyOptions Options(int floor = 1)
    {
        DwPolicyOptions options = new();

        options.Caps.MinGroupSize = floor;
        options.Freeze();

        return options;
    }

    private static PolicyQueryable<Staffer> Query(DwPolicyOptions options) =>
        Rows().AsQueryable().ApplyPolicy(
            new DwPolicyContext(),
            options,
            new PolicyResolver(new[] { new AttributePolicyProvider() }));

    private static Summary Grouped(string aggregated = "Headcount", string alias = "n") =>
        new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Department" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = aggregated, Aggregator = Aggregator.Maximum, Alias = alias }
                }
            }
        };

    private static List<string> Departments(SummaryResult result) =>
        result.Data.Select(row => (string)((dynamic)row).Department).ToList();

    /// <summary>
    /// The column names of a summary row, whichever shape it is. A row is an anonymous type as the
    /// pipeline generated it and an expando once anything rebuilt it, so a test that assumed one of
    /// those would be testing the reshaping rather than the floor.
    /// </summary>
    private static IReadOnlyCollection<string> Columns(object row) =>
        row is IDictionary<string, object?> expando
            ? expando.Keys.ToList()
            : row.GetType()
                .GetProperties()
                .Where(p => p.GetIndexParameters().Length == 0)
                .Select(p => p.Name)
                .ToList();

    // ---- the floor -----------------------------------------------------------------------------

    /// <summary>
    /// Off by default, so nothing an existing caller does changes. A control that refuses working
    /// queries the day a library is upgraded is a control that gets switched off.
    /// </summary>
    [Fact]
    public void With_no_floor_every_group_comes_back()
    {
        List<string> departments = Departments(Query(Options()).ToList(Grouped()));

        Assert.Equal(3, departments.Count);
        Assert.Contains("Legal", departments);
    }

    [Fact]
    public void A_group_below_the_floor_is_suppressed()
    {
        List<string> departments = Departments(Query(Options(floor: 3)).ToList(Grouped()));

        Assert.Equal(new[] { "Support" }, departments);
    }

    [Fact]
    public void A_group_that_meets_the_floor_survives()
    {
        List<string> departments = Departments(Query(Options(floor: 2)).ToList(Grouped()));

        Assert.Contains("Engineering", departments);
        Assert.Contains("Support", departments);
        Assert.DoesNotContain("Legal", departments);
    }

    /// <summary>
    /// A missing group has to be answerable. Without the record, a caller debugging an absent
    /// department cannot tell suppression from there being no such department.
    /// </summary>
    [Fact]
    public void Every_suppression_is_recorded()
    {
        PolicyQueryable<Staffer> query = Query(Options(floor: 3));

        query.ToList(Grouped());

        Assert.Contains(
            query.LastTrace!.Decisions,
            d => d.Action == PolicyAction.Dropped
                && d.Feature == PolicyFeature.Aggregate
                && (d.Reason?.Contains("below the group floor") ?? false));
    }

    // ---- the floor governs the whole answer, not only the page ---------------------------------

    /// <summary>
    /// The count describes the rows that came back.
    /// </summary>
    /// <remarks>
    /// Suppressing after the query left <c>TotalCount</c> and <c>PageCount</c> describing a result
    /// that was never returned — which is a count of exactly the groups the floor exists to hide,
    /// readable by bisecting a Having threshold while every response comes back empty. The floor is
    /// a predicate on the query now, so the count is taken over the groups that reached it.
    /// </remarks>
    [Fact]
    public void The_count_describes_the_groups_that_survived_the_floor()
    {
        SummaryResult result = Query(Options(floor: 3)).ToList(Grouped());

        // Support has three, Engineering two, Legal one.
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(new[] { "Support" }, Departments(result));
    }

    /// <summary>
    /// And a page holds as many rows as it says it does.
    /// </summary>
    /// <remarks>
    /// Suppression ran after Skip and Take, so a page of ten could come back with four and the six
    /// it dropped were never backfilled from the next page. No sequence of requests returned the
    /// surviving groups in full, and a client that stops when a page is short stopped on the first.
    /// </remarks>
    [Fact]
    public void A_page_is_not_thinned_by_the_floor()
    {
        Summary summary = Grouped();

        summary.Page = new PageBy { PageNumber = 1, PageSize = 2 };

        SummaryResult result = Query(Options(floor: 2)).ToList(summary);

        // Engineering and Support reach the floor; Legal does not. Both fit on the page.
        Assert.Equal(2, result.Data.Count);
        Assert.Equal(2, result.TotalCount);
    }

    // ---- the alias the library keeps for itself --------------------------------------------------

    /// <summary>
    /// The reserved alias is refused wherever a caller can write it, not only in the aggregate list.
    /// </summary>
    /// <remarks>
    /// The gate leaves a name it does not recognize alone, and the validator runs after injection —
    /// by which point the alias is a legitimate aggregate. A caller naming it in Having bound to the
    /// count this control keeps for itself, and read the number of suppressed groups straight out of
    /// <c>TotalCount</c>.
    /// </remarks>
    [Fact]
    public void A_having_clause_may_not_name_the_reserved_alias()
    {
        Summary summary = Grouped();

        summary.Having = new ConditionGroup
        {
            Sort = 1,
            Conditions =
            {
                new Condition
                {
                    Sort = 1,
                    Field = GroupFloorAlias,
                    DataType = DataType.Number,
                    Operator = Operator.LessThan,
                    Values = { "5" }
                }
            }
        };

        PolicyException refused = Assert.Throws<PolicyException>(
            () => Query(Options(floor: 2)).ToList(summary));

        Assert.Equal(PolicyErrorCode.GroupTooSmall, refused.ErrorCode);
    }

    [Fact]
    public void An_order_clause_may_not_name_the_reserved_alias_either()
    {
        Summary summary = Grouped();

        summary.Orders = new List<OrderBy>
        {
            new() { Sort = 1, Field = GroupFloorAlias, Direction = Direction.Ascending }
        };

        PolicyException refused = Assert.Throws<PolicyException>(
            () => Query(Options(floor: 2)).ToList(summary));

        Assert.Equal(PolicyErrorCode.GroupTooSmall, refused.ErrorCode);
    }

    /// <summary>
    /// A caller's own Having still applies, alongside the floor rather than instead of it.
    /// </summary>
    [Fact]
    public void The_callers_having_survives_the_injected_one()
    {
        // Salary declares a floor of three of its own, which outranks the global two, so the floor
        // alone leaves Support: three members, topping out at 70.
        Assert.Equal(
            new[] { "Support" },
            Departments(Query(Options(floor: 2)).ToList(Grouped("Salary", "top"))));

        Summary narrowed = Grouped("Salary", "top");

        narrowed.Having = new ConditionGroup
        {
            Sort = 1,
            Conditions =
            {
                new Condition
                {
                    Sort = 1,
                    Field = "top",
                    DataType = DataType.Number,
                    Operator = Operator.GreaterThan,
                    Values = { "100" }
                }
            }
        };

        // The caller's clause is applied as well as the floor's, not instead of it: Support reaches
        // the floor and fails the threshold, so nothing comes back.
        SummaryResult result = Query(Options(floor: 2)).ToList(narrowed);

        Assert.Empty(Departments(result));
        Assert.Equal(0, result.TotalCount);
    }

    private const string GroupFloorAlias = "__dwGroupSize";

    // ---- the injected count --------------------------------------------------------------------

    /// <summary>
    /// The caller asked for a maximum and no count, so the library adds the count it needs and takes
    /// it away again. The result shape is the one that was asked for.
    /// </summary>
    [Fact]
    public void The_count_the_floor_needed_never_reaches_the_caller()
    {
        SummaryResult result = Query(Options(floor: 2)).ToList(Grouped());

        Assert.All(
            result.Data,
            row => Assert.DoesNotContain("__dwGroupSize", Columns(row)));
    }

    /// <summary>
    /// A count the caller asked for is theirs and is left alone, floor or no floor.
    /// </summary>
    [Fact]
    public void A_count_the_caller_asked_for_is_kept()
    {
        Summary summary = Grouped();

        summary.GroupBy!.AggregateBy.Add(
            new AggregateBy { Aggregator = Aggregator.Count, Alias = "people" });

        SummaryResult result = Query(Options(floor: 2)).ToList(summary);

        Assert.All(result.Data, row => Assert.Contains("people", Columns(row)));
        Assert.All(result.Data, row => Assert.DoesNotContain("__dwGroupSize", Columns(row)));
    }

    /// <summary>
    /// Silently overwriting the caller's column would lose whatever they were counting, and the
    /// number they read back would be the library's rather than theirs.
    /// </summary>
    [Fact]
    public void A_caller_who_took_the_reserved_alias_is_refused()
    {
        Summary summary = Grouped();

        summary.GroupBy!.AggregateBy.Add(
            new AggregateBy { Aggregator = Aggregator.Count, Alias = "__dwGroupSize" });

        PolicyException error =
            Assert.Throws<PolicyException>(() => Query(Options(floor: 2)).ToList(summary));

        Assert.Contains("__dwGroupSize", error.SourceOrigin);
    }

    /// <summary>
    /// Nothing is injected when no floor applies, so an ordinary summary generates the query it
    /// always generated.
    /// </summary>
    [Fact]
    public void Nothing_is_injected_when_no_floor_applies()
    {
        SummaryResult result = Query(Options()).ToList(Grouped());

        Assert.All(result.Data, row => Assert.DoesNotContain("__dwGroupSize", Columns(row)));
    }

    // ---- the per-field override ----------------------------------------------------------------

    /// <summary>
    /// The roadmap's decision table: a global option with a per-field attribute override. The field
    /// carries a floor of three, so it holds even though the global setting is off.
    /// </summary>
    [Fact]
    public void A_field_may_raise_the_floor_on_its_own()
    {
        List<string> departments =
            Departments(Query(Options()).ToList(Grouped("Salary", "top")));

        Assert.Equal(new[] { "Support" }, departments);
    }

    /// <summary>
    /// The largest of them wins. A summary touching one especially sensitive column is held to that
    /// column's standard rather than the weakest one present.
    /// </summary>
    [Fact]
    public void The_strictest_floor_in_the_summary_decides()
    {
        Summary summary = Grouped("Headcount", "n");

        summary.GroupBy!.AggregateBy.Add(
            new AggregateBy { Field = "Salary", Aggregator = Aggregator.Maximum, Alias = "top" });

        Assert.Equal(new[] { "Support" }, Departments(Query(Options()).ToList(summary)));
    }

    [Fact]
    public void A_global_floor_larger_than_the_field_wins_instead()
    {
        Assert.Empty(Departments(Query(Options(floor: 4)).ToList(Grouped("Salary", "top"))));
    }

    // ---- the default, and switching it off ------------------------------------------------------

    /// <summary>
    /// A deployment that never mentions the floor gets one, and it is the safe value rather than
    /// the compatible one.
    /// </summary>
    /// <remarks>
    /// The compatibility argument for shipping this off does not survive being looked at. The floor
    /// applies only to a guarded summary, and a guarded summary is new in this release, so there is
    /// no caller anywhere whose results shipping it on can change.
    /// </remarks>
    [Fact]
    public void A_deployment_that_says_nothing_gets_the_safe_floor()
    {
        DwPolicyOptions options = new();

        Assert.Equal(5, DwCaps.DefaultMinGroupSize);
        Assert.Equal(DwCaps.DefaultMinGroupSize, options.Caps.MinGroupSize);
        Assert.False(options.Caps.IsMinGroupSizeSet);
    }

    /// <summary>The default suppresses a real query's small groups without being asked to.</summary>
    [Fact]
    public void The_default_floor_suppresses_the_small_groups_of_an_unconfigured_deployment()
    {
        DwPolicyOptions options = new();

        options.Freeze();

        SummaryResult result = Query(options).ToList(Grouped());

        // Two in Engineering, three in Support, one in Legal. None of them reaches five, so the
        // report is empty — which is the floor doing exactly what it is for on a fixture this
        // small, and why every test in this file that is about something else sets it to one.
        Assert.Empty(result.Data);
    }

    /// <summary>
    /// An explicit one is a deployment saying "no floor", and it is obeyed in production with
    /// nothing refused and nothing warned about.
    /// </summary>
    /// <remarks>
    /// The reason the backing value starts unset instead of at one. If one were the default, the
    /// library could not tell a deliberate opt-out from a deployment that had never heard of the
    /// setting — so any check that refused to start would trap the developer who meant it.
    /// </remarks>
    [Fact]
    public void An_explicit_one_switches_the_floor_off_and_is_honoured()
    {
        DwPolicyOptions options = Options(floor: 1);

        Assert.True(options.Caps.IsMinGroupSizeSet);
        Assert.Equal(1, options.Caps.MinGroupSize);

        SummaryResult result = Query(options).ToList(Grouped());

        // All three groups, the group of one included. Nobody is protected from themselves here,
        // because somebody said so in a sentence.
        Assert.Equal(3, result.Data.Count);
    }

    /// <summary>Setting the default's own value still counts as having set it.</summary>
    /// <remarks>
    /// A deployment that writes five means five, and would go on meaning five if a later release
    /// changed the default. Reporting it as unset would make that upgrade change a decision
    /// somebody had already made.
    /// </remarks>
    [Fact]
    public void Setting_the_floor_to_the_default_value_is_still_setting_it()
    {
        DwPolicyOptions options = new();

        options.Caps.MinGroupSize = DwCaps.DefaultMinGroupSize;

        Assert.True(options.Caps.IsMinGroupSizeSet);
        Assert.Equal(DwCaps.DefaultMinGroupSize, options.Caps.MinGroupSize);
    }

    // ---- the cap's own guards ------------------------------------------------------------------

    [Fact]
    public void A_floor_below_one_is_refused()
    {
        DwPolicyOptions options = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Caps.MinGroupSize = 0);
    }

    [Fact]
    public void The_floor_cannot_be_changed_after_startup()
    {
        Assert.Throws<InvalidOperationException>(() => Options(floor: 3).Caps.MinGroupSize = 5);
    }

    /// <summary>
    /// A dry run records what it would have suppressed and hands the rows back, exactly as every
    /// other decision does under that posture.
    /// </summary>
    [Fact]
    public void A_dry_run_records_the_suppression_without_making_it()
    {
        DwPolicyOptions options = new() { DryRun = true };

        options.Caps.MinGroupSize = 3;
        options.Freeze();

        PolicyQueryable<Staffer> query = Query(options);
        SummaryResult result = query.ToList(Grouped());

        Assert.Equal(3, result.Data.Count);
        Assert.Contains(query.LastTrace!.Decisions, d => d.Action == PolicyAction.Dropped);
    }

    /// <summary>
    /// There is no ungrouped summary to floor. The pipeline requires at least one grouping field,
    /// so "one group over everything" is a shape this library cannot be asked for — which is worth
    /// a test, because the floor would have nothing to say about it if it could.
    /// </summary>
    [Fact]
    public void A_summary_must_group_by_something()
    {
        Summary summary = new()
        {
            GroupBy = new GroupBy
            {
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = "Headcount", Aggregator = Aggregator.Maximum, Alias = "n" }
                }
            }
        };

        Assert.Throws<LogicException>(() => Query(Options(floor: 20)).ToList(summary));
    }

    // ---- a grouping with no aggregates ---------------------------------------------------------

    [Fact]
    public void A_group_by_with_no_aggregates_does_not_throw_when_the_floor_is_set()
    {
        // GroupBy.AggregateBy is declared non-nullable with a default, but JSON carrying
        // "aggregateBy": null overwrites the initializer — which GroupBy.Clone already accounts for,
        // and which every one of the six AggregateBy dereferences in FilterSanitizer null-checks.
        // GroupFloor.Inject was the one that did not, so a grouped summary with no aggregates threw
        // NullReferenceException the moment MinGroupSize was raised above one.
        Summary summary = new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Department" },
                AggregateBy = null!
            }
        };

        SummaryResult result = Query(Options(floor: 3)).ToList(summary);

        // Support has three and survives; Engineering has two and Legal one, so both are suppressed.
        Assert.Equal(new[] { "Support" }, Departments(result));
    }

    [Fact]
    public void A_group_by_with_an_empty_aggregate_list_is_still_floored()
    {
        Summary summary = new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Department" },
                AggregateBy = new List<AggregateBy>()
            }
        };

        SummaryResult result = Query(Options(floor: 3)).ToList(summary);

        Assert.Equal(new[] { "Support" }, Departments(result));
    }

}


/// <summary>
/// The suppressor itself, driven directly, for the cases a query cannot arrange.
/// </summary>
/// <remarks>
/// A mutation check found nothing covering what happens when the size column cannot be read — the
/// behaviour was documented in a remark and asserted nowhere, which is the shape the branch has
/// learned to distrust: a test that passes either way proves nothing, and so does a comment.
/// </remarks>
public class PolicyGroupFloorReadingTests
{
    private static SummaryResult Result(params object[] rows) =>
        new() { Data = rows.ToList() };

    private static PolicyTrace Trace() => new(DwTier.Convenience, dryRun: false);

    /// <summary>
    /// Fail closed. The column is one this library added for itself, so failing to find it means the
    /// floor has no idea how large the group is — and answering anyway is the disclosure the floor
    /// exists to prevent.
    /// </summary>
    [Fact]
    public void A_row_with_no_size_column_is_dropped()
    {
        SummaryResult result = Result(new { Department = "Legal", top = 400 });
        PolicyTrace trace = Trace();

        int dropped = ResultTransformer.Suppress(result, floor: 3, dryRun: false, trace);

        Assert.Equal(1, dropped);
        Assert.Empty(result.Data);
        Assert.Contains(
            trace.Decisions,
            d => d.Reason!.Contains("could not be read"));
    }

    /// <summary>
    /// A size column holding something that is not a number is no more readable than a missing one,
    /// and is treated the same way rather than throwing on a caller's query.
    /// </summary>
    [Fact]
    public void A_row_whose_size_is_not_a_number_is_dropped()
    {
        SummaryResult result = Result(new { Department = "Legal", __dwGroupSize = "many" });

        Assert.Equal(1, ResultTransformer.Suppress(result, floor: 3, dryRun: false, Trace()));
        Assert.Empty(result.Data);
    }

    [Fact]
    public void A_row_that_meets_the_floor_is_kept()
    {
        SummaryResult result = Result(new { Department = "Support", __dwGroupSize = 3 });

        Assert.Equal(0, ResultTransformer.Suppress(result, floor: 3, dryRun: false, Trace()));
        Assert.Single(result.Data);
    }

    /// <summary>A floor of one suppresses nothing, so an unreadable column costs nothing either.</summary>
    [Fact]
    public void No_floor_reads_nothing_and_drops_nothing()
    {
        SummaryResult result = Result(new { Department = "Legal" });

        Assert.Equal(0, ResultTransformer.Suppress(result, floor: 1, dryRun: false, Trace()));
        Assert.Single(result.Data);
    }
}


/// <summary>
/// A type grouped over in the floor tests. <c>Salary</c> carries a per-field floor so the override
/// can be exercised without touching the global setting.
/// </summary>
internal class Staffer
{
    public int Id { get; set; }

    public string Department { get; set; } = string.Empty;

    /// <summary>Aggregatable, but only over a group of three or more.</summary>
    [DwGeneralize(GeneralizeMode.Round, Step = 10, AllowAggregate = true, MinGroupSize = 3)]
    public decimal Salary { get; set; }

    /// <summary>Nothing transforms it, so it carries no floor of its own.</summary>
    public int Headcount { get; set; }
}

/// <summary>
/// The two fields Phase 8 added to every transform stage, through the serializer a rule's transform
/// travels in.
/// </summary>
/// <remarks>
/// Found by the reading pass rather than by a test. A rule may set a transform, so a rule may set an
/// aggregation permission and a group floor — and <c>PolicyPayload</c> was written a phase before
/// either existed. Losing <c>AllowAggregate</c> is fail-closed and merely wrong; losing
/// <c>MinGroupSize</c> is fail-open, because the floor an operator set for that field silently
/// becomes no floor at all.
/// </remarks>
public class PolicyPayloadFloorTests
{
    private static TransformStage RoundTrip(TransformStage stage) =>
        PolicyPayload.ToStage(PolicyPayload.ToJson(stage));

    [Fact]
    public void A_masks_aggregation_permission_survives()
    {
        Assert.True(RoundTrip(new MaskStage(MaskStrategy.Full, allowAggregate: true)).AllowAggregate);
    }

    [Fact]
    public void A_masks_group_floor_survives()
    {
        Assert.Equal(
            7,
            RoundTrip(new MaskStage(MaskStrategy.Full, allowAggregate: true, minGroupSize: 7))
                .MinGroupSize);
    }

    [Fact]
    public void A_generalizations_floor_survives()
    {
        TransformStage read = RoundTrip(
            new GeneralizeStage(GeneralizeMode.Round, step: 10, allowAggregate: true, minGroupSize: 4));

        Assert.True(read.AllowAggregate);
        Assert.Equal(4, read.MinGroupSize);
    }

    [Fact]
    public void A_truncations_floor_survives()
    {
        TransformStage read = RoundTrip(new TruncateStage(8, null, allowAggregate: true, minGroupSize: 3));

        Assert.True(read.AllowAggregate);
        Assert.Equal(3, read.MinGroupSize);
    }

    [Fact]
    public void A_formats_floor_survives()
    {
        TransformStage read = RoundTrip(new FormatStage("N2", allowAggregate: true, minGroupSize: 2));

        Assert.True(read.AllowAggregate);
        Assert.Equal(2, read.MinGroupSize);
    }

    [Fact]
    public void A_replacements_floor_survives()
    {
        TransformStage read = RoundTrip(new DefaultStage("0", true, allowAggregate: true, minGroupSize: 6));

        Assert.True(read.AllowAggregate);
        Assert.Equal(6, read.MinGroupSize);
    }

    /// <summary>
    /// A stage that said nothing about either reads back saying nothing about either — denied for
    /// aggregation, and setting no floor of its own.
    /// </summary>
    [Fact]
    public void A_stage_that_sets_neither_reads_back_with_neither()
    {
        TransformStage read = RoundTrip(new MaskStage(MaskStrategy.Full));

        Assert.False(read.AllowAggregate);
        Assert.Equal(0, read.MinGroupSize);
    }

    /// <summary>
    /// End to end: a rule that permits aggregation with a floor of its own, through the document
    /// both stores hold, into the resolved policy.
    /// </summary>
    [Fact]
    public void A_rule_can_permit_aggregation_with_a_floor()
    {
        PolicyRule rule = new(
            DwSubjectKind.Role, "Analyst",
            "DynamicWhere.Tests.Policies.Staffer", "Headcount",
            PolicyFeature.Select, PolicyEffect.Mask,
            transform: new GeneralizeStage(
                GeneralizeMode.Round, step: 10, allowAggregate: true, minGroupSize: 5));

        PolicyRule read = PolicyRuleDocument.ToRule(PolicyRuleDocument.ToJson(rule));

        Assert.True(read.Transform!.AllowAggregate);
        Assert.Equal(5, read.Transform.MinGroupSize);
    }
}

/// <summary>
/// The order the two summary passes run in, which the reading pass found mattered.
/// </summary>
public class PolicyGroupFloorOrderTests
{
    /// <summary>Two bands that collide once rounded, each holding one row.</summary>
    private static Banded[] Rows() => new[]
    {
        new Banded { Id = 1, Band = 100m },
        new Banded { Id = 2, Band = 149m }
    };

    private static Summary ByBand() => new()
    {
        GroupBy = new GroupBy
        {
            Fields = new List<string> { "Band" },
            AggregateBy = new List<AggregateBy>
            {
                new() { Field = "Id", Aggregator = Aggregator.Maximum, Alias = "top" }
            }
        }
    };

    private static PolicyQueryable<Banded> Query(int floor)
    {
        DwPolicyOptions options = new();

        options.Caps.MinGroupSize = floor;
        options.Freeze();

        return Rows().AsQueryable().ApplyPolicy(
            new DwPolicyContext(),
            options,
            new PolicyResolver(new[] { new AttributePolicyProvider() }));
    }

    /// <summary>
    /// Without a floor the collision is real and refusing is right: two rounded keys are the same
    /// key, and their aggregates cannot be added together without inventing a figure.
    /// </summary>
    [Fact]
    public void Colliding_keys_still_refuse_when_no_floor_applies()
    {
        PolicyException error = Assert.Throws<PolicyException>(() => Query(1).ToList(ByBand()));

        Assert.Equal(PolicyErrorCode.AmbiguousGroupKey, error.ErrorCode);
    }

    /// <summary>
    /// With a floor that removes both groups there is nothing left to collide. Refusing here would
    /// deny the caller a result because of groups they were never allowed to see — the suppression
    /// has to happen before anything else has an opinion about those rows.
    /// </summary>
    [Fact]
    public void A_collision_between_groups_the_floor_removes_does_not_refuse()
    {
        SummaryResult result = Query(2).ToList(ByBand());

        Assert.Empty(result.Data);
    }
}

/// <summary>A type grouped by a value that is rounded on its way out.</summary>
internal class Banded
{
    public int Id { get; set; }

    /// <summary>Rounded to the nearest hundred, so 100 and 149 become one key.</summary>
    [DwGeneralize(GeneralizeMode.Round, Step = 100)]
    public decimal Band { get; set; }
}

