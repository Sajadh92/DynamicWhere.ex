using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers dry run, the mode that records every decision and enforces none of them.
/// </summary>
/// <remarks>
/// This is how a policy is rolled out onto a live application without breaking it. Turning
/// enforcement on blind means discovering the over-strict rule from a support ticket; running it
/// unenforced first means discovering it from the trace, before anyone is refused. The comparison
/// that matters is therefore not "does it record something" but "is the query it produces
/// identical to the unguarded one" — anything less and the rehearsal is not a rehearsal.
/// </remarks>
public class DryRunTests
{
    private static IQueryable<OpenLedger> Ledger() => new List<OpenLedger>
    {
        new() { Id = 1, Reference = "INV-1", Amount = 10m },
        new() { Id = 2, Reference = "INV-2", Amount = 20m }
    }.AsQueryable();

    private static PolicyResolver Resolver() =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    private static FilterResult<OpenLedger> Run(Filter filter, DwTier tier, bool global, bool perCaller)
    {
        DwPolicyContext caller = new DwPolicyContext()
            .WithSubject(DwSubjectKind.User, "u1");

        caller.DryRun = perCaller;

        DwPolicyOptions options = new() { Tier = tier, DryRun = global };

        return Ledger().ApplyPolicy(caller, options, Resolver()).ToList(filter);
    }

    [Fact]
    public void A_dry_run_returns_exactly_what_the_unguarded_query_returns()
    {
        FilterResult<OpenLedger> unguarded = Ledger().ToList(new Filter());
        FilterResult<OpenLedger> rehearsed = Run(new Filter(), DwTier.Strict, global: true, perCaller: false);

        Assert.Equal(
            unguarded.Data.Select(r => (r.Id, r.Reference, r.Amount)),
            rehearsed.Data.Select(r => (r.Id, r.Reference, r.Amount)));
    }

    [Fact]
    public void Enforced_the_same_query_loses_the_denied_field()
    {
        // The contrast that makes the test above mean something.
        FilterResult<OpenLedger> enforced = Run(new Filter(), DwTier.Strict, global: false, perCaller: false);

        Assert.All(enforced.Data, row => Assert.Equal(0m, row.Amount));
    }

    [Fact]
    public void The_decisions_are_recorded_in_full_even_though_nothing_was_enforced()
    {
        FilterResult<OpenLedger> rehearsed = Run(new Filter(), DwTier.Strict, global: true, perCaller: false);

        // Dropped rather than Denied even in the strict tier: this decision comes from projection
        // synthesis, which never throws, because the caller named no projection to be refused.
        Assert.Contains(
            rehearsed.Policy!.Decisions,
            d => d.FieldPath == "Amount" && d.Action == PolicyAction.Dropped);

        Assert.True(rehearsed.Policy.DryRun);
    }

    [Fact]
    public void A_refusal_that_would_throw_does_not()
    {
        Filter filter = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = "Amount",
                        DataType = DataType.Number,
                        Operator = Operator.GreaterThan,
                        Values = { 5 }
                    }
                }
            }
        };

        // Enforced, a denied filter throws in either tier -- that is the asymmetry the design rests
        // on. Rehearsed, it runs and says so.
        Assert.Throws<PolicyException>(
            () => Run(filter, DwTier.Convenience, global: false, perCaller: false));

        FilterResult<OpenLedger> rehearsed = Run(filter, DwTier.Convenience, global: true, perCaller: false);

        Assert.Equal(2, rehearsed.Data.Count);
        Assert.Contains(
            rehearsed.Policy!.Decisions,
            d => d.FieldPath == "Amount" && d.Feature == PolicyFeature.Where);
    }

    [Fact]
    public void A_single_caller_can_rehearse_while_everyone_else_is_enforced()
    {
        // Per-context dry run is what makes the rollout incremental. A global-only switch would
        // force it to be all-or-nothing, which for a policy layer means never turning it on.
        FilterResult<OpenLedger> canary = Run(new Filter(), DwTier.Strict, global: false, perCaller: true);
        FilterResult<OpenLedger> everyone = Run(new Filter(), DwTier.Strict, global: false, perCaller: false);

        Assert.All(canary.Data, row => Assert.NotEqual(0m, row.Amount));
        Assert.All(everyone.Data, row => Assert.Equal(0m, row.Amount));
    }

    [Fact]
    public void A_dropped_sort_stays_in_place_during_a_rehearsal()
    {
        Filter filter = new()
        {
            Orders = new List<OrderBy> { new() { Field = "Amount", Direction = Direction.Descending } }
        };

        FilterResult<OpenLedger> rehearsed = Run(filter, DwTier.Convenience, global: true, perCaller: false);

        Assert.Equal(new[] { 20m, 10m }, rehearsed.Data.Select(r => r.Amount));
        Assert.Contains(rehearsed.Policy!.Decisions, d => d.Feature == PolicyFeature.Order);
    }

    [Fact]
    public void A_cap_is_recorded_rather_than_enforced()
    {
        Filter filter = new() { Page = new PageBy { PageNumber = 1, PageSize = 5000 } };

        Assert.Throws<PolicyException>(
            () => Run(filter, DwTier.Convenience, global: false, perCaller: false));

        FilterResult<OpenLedger> rehearsed = Run(filter, DwTier.Convenience, global: true, perCaller: false);

        Assert.Contains(
            rehearsed.Policy!.Decisions,
            d => d.Reason is not null && d.Reason.Contains("MaxPageSize", StringComparison.Ordinal));
    }

    [Fact]
    public void A_rehearsal_that_refuses_every_projection_field_still_returns_rows()
    {
        // Enforced, this is AllSelectsDenied. Rehearsed it has to return something, or the mode
        // cannot be used to rehearse the one case most likely to break a caller.
        Filter filter = new() { Selects = new List<string> { "Amount" } };

        Assert.Throws<PolicyException>(
            () => Run(filter, DwTier.Convenience, global: false, perCaller: false));

        FilterResult<OpenLedger> rehearsed = Run(filter, DwTier.Convenience, global: true, perCaller: false);

        Assert.Equal(2, rehearsed.Data.Count);
    }
}
