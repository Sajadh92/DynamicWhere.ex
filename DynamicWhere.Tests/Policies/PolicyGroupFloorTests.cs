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
/// It is off by default — <c>MinGroupSize</c> of one — so no existing caller changes behaviour. Set
/// above one it applies to every grouped summary rather than only those touching a transformed
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
