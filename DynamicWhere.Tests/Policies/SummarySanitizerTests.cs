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
/// Covers the gate applied to a <see cref="Summary"/>.
/// </summary>
/// <remarks>
/// A summary is the shape where field paths stop being the only vocabulary. <c>GroupBy.Fields</c>
/// and <c>AggregateBy.Field</c> are property paths, but <c>Having</c>'s condition fields and
/// <c>Orders</c>'s fields are aggregate aliases or dot-stripped group-by fields, and the validator
/// checks them against a set of names rather than against reflection. Sending one of those through
/// <c>Validate&lt;T&gt;()</c> would reject a query that is valid today; skipping them would let an
/// alias read a field whose own policy refuses it.
/// </remarks>
public class SummarySanitizerTests
{
    private static PolicyResolver AttributeResolver() =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    private static DwPolicyContext Caller() =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    private static (Summary Result, PolicyTrace Trace) Guard<T>(Summary summary, DwTier tier)
        where T : class
    {
        PolicyTrace trace = new(tier, dryRun: false);

        Summary result = FilterSanitizer.Sanitize<T>(
            summary, AttributeResolver(), Caller(), new DwPolicyOptions { Tier = tier }, trace);

        return (result, trace);
    }

    private static Summary GroupedBy(string field, params AggregateBy[] aggregates) => new()
    {
        GroupBy = new GroupBy
        {
            Fields = new List<string> { field },
            AggregateBy = new List<AggregateBy>(aggregates)
        }
    };

    [Fact]
    public void Grouping_by_a_refused_field_throws_in_both_tiers()
    {
        // InternalNotes carries [DwDeny(Order | Group)]. Grouping cannot be dropped the way a sort
        // can: removing a grouping key collapses rows together and changes every aggregate in the
        // result rather than merely returning them unordered.
        foreach (DwTier tier in new[] { DwTier.Convenience, DwTier.Strict })
        {
            PolicyException exception = Assert.Throws<PolicyException>(
                () => Guard<SecuredEmployee>(GroupedBy("InternalNotes"), tier));

            Assert.Equal(PolicyErrorCode.FieldDeniedForGroup, exception.ErrorCode);
            Assert.Equal("InternalNotes", exception.FieldPath);
        }
    }

    [Fact]
    public void Aggregating_a_refused_field_throws_in_both_tiers()
    {
        Summary summary = GroupedBy(
            "Name",
            new AggregateBy { Field = "NationalId", Alias = "Ids", Aggregator = Aggregator.CountDistinct });

        foreach (DwTier tier in new[] { DwTier.Convenience, DwTier.Strict })
        {
            PolicyException exception = Assert.Throws<PolicyException>(
                () => Guard<SecuredEmployee>(summary, tier));

            Assert.Equal(PolicyErrorCode.FieldDeniedForAggregate, exception.ErrorCode);
            Assert.Equal("NationalId", exception.FieldPath);
        }
    }

    [Fact]
    public void A_field_denied_only_for_projection_can_still_be_aggregated()
    {
        // Salary carries [DwNoSelect]. Refusing to show a column is not the same as refusing to
        // total it, and conflating the two would make the feature flags meaningless.
        Summary summary = GroupedBy(
            "Name",
            new AggregateBy { Field = "Salary", Alias = "TotalPay", Aggregator = Aggregator.Sumation });

        (Summary result, PolicyTrace trace) = Guard<SecuredEmployee>(summary, DwTier.Strict);

        Assert.Equal("Salary", result.GroupBy!.AggregateBy[0].Field);
        Assert.Empty(trace.Decisions);
    }

    [Fact]
    public void A_count_with_no_field_has_no_underlying_policy_and_is_allowed()
    {
        Summary summary = GroupedBy(
            "Name",
            new AggregateBy { Alias = "Rows", Aggregator = Aggregator.Count });

        (Summary result, _) = Guard<SecuredEmployee>(summary, DwTier.Strict);

        Assert.Equal("Rows", result.GroupBy!.AggregateBy[0].Alias);
    }

    [Fact]
    public void The_summarys_where_clause_is_gated_like_any_other_filter()
    {
        Summary summary = GroupedBy("Name");
        summary.ConditionGroup = new ConditionGroup
        {
            Conditions =
            {
                new Condition
                {
                    Field = "Contact.Email",
                    DataType = DataType.Text,
                    Operator = Operator.Equal,
                    Values = { "a@b.c" }
                }
            }
        };

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<SecuredEmployee>(summary, DwTier.Convenience));

        Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, exception.ErrorCode);
    }

    [Fact]
    public void Group_and_aggregate_paths_come_back_canonical()
    {
        Summary summary = GroupedBy(
            "  name  ",
            new AggregateBy { Field = "salary", Alias = "TotalPay", Aggregator = Aggregator.Sumation });

        (Summary result, _) = Guard<SecuredEmployee>(summary, DwTier.Strict);

        Assert.Equal("Name", result.GroupBy!.Fields[0]);
        Assert.Equal("Salary", result.GroupBy.AggregateBy[0].Field);
    }

    [Fact]
    public void The_callers_summary_is_never_touched()
    {
        Summary original = GroupedBy(
            "name",
            new AggregateBy { Field = "salary", Alias = "TotalPay", Aggregator = Aggregator.Sumation });

        Guard<SecuredEmployee>(original, DwTier.Strict);

        Assert.Equal("name", original.GroupBy!.Fields[0]);
        Assert.Equal("salary", original.GroupBy.AggregateBy[0].Field);
    }

    [Fact]
    public void A_summary_over_a_type_with_no_attributes_passes_through()
    {
        Summary summary = GroupedBy(
            "Name",
            new AggregateBy { Field = "Id", Alias = "Ids", Aggregator = Aggregator.CountDistinct });

        (Summary result, PolicyTrace trace) = Guard<PlainProduct>(summary, DwTier.Strict);

        Assert.Equal("Name", result.GroupBy!.Fields[0]);
        Assert.Empty(trace.Decisions);
    }
}
