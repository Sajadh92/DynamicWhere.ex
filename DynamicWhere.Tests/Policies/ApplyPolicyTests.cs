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
/// Covers the handle returned by <c>ApplyPolicy</c>.
/// </summary>
/// <remarks>
/// These run against an in-memory queryable, which is enough to prove the handle sanitizes,
/// delegates, and reports. The database-backed behaviour has its own suite.
/// </remarks>
public class ApplyPolicyTests
{
    private static IQueryable<SecuredEmployee> People() => new List<SecuredEmployee>
    {
        new() { Id = 1, Name = "Ada", NationalId = "AAA", Salary = 100m, InternalNotes = "n1" },
        new() { Id = 2, Name = "Bo", NationalId = "BBB", Salary = 200m, InternalNotes = "n2" }
    }.AsQueryable();

    private static DwPolicyContext Caller() =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    private static DwPolicyOptions Posture(DwTier tier) => new() { Tier = tier };

    private static PolicyResolver Resolver() =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    private static PolicyQueryable<SecuredEmployee> Guarded(DwTier tier = DwTier.Convenience) =>
        People().ApplyPolicy(Caller(), Posture(tier), Resolver());

    [Fact]
    public void A_guarded_query_returns_rows_and_the_record_of_what_was_decided()
    {
        FilterResult<SecuredEmployee> result = Guarded().ToList(new Filter());

        Assert.Equal(2, result.Data.Count);
        Assert.NotNull(result.Policy);
        Assert.Equal(DwTier.Convenience, result.Policy!.Tier);
    }

    [Fact]
    public void A_denied_field_does_not_reach_the_caller()
    {
        // No projection was asked for, so the whole entity would come back. The synthesized
        // projection is what keeps NationalId and Salary out of it.
        FilterResult<SecuredEmployee> result = Guarded().ToList(new Filter());

        Assert.All(result.Data, row => Assert.Equal(string.Empty, row.NationalId));
        Assert.All(result.Data, row => Assert.Equal(0m, row.Salary));
        Assert.All(result.Data, row => Assert.NotEqual(string.Empty, row.Name));
    }

    [Fact]
    public void The_drop_is_recorded_where_a_caller_can_find_it()
    {
        FilterResult<SecuredEmployee> result = Guarded().ToList(new Filter());

        Assert.Contains(
            result.Policy!.Decisions,
            d => d.FieldPath == "NationalId" && d.Action == PolicyAction.Dropped);
    }

    [Fact]
    public void An_unguarded_query_on_a_type_that_does_not_require_policy_is_untouched()
    {
        // The point of the comparison: the policy layer changes what a *guarded* call returns and
        // nothing else. SecuredEmployee cannot stand in here, because it carries
        // [DwEntity(RequirePolicy = true)] and refuses an unguarded read outright.
        IQueryable<PlainProduct> products = new List<PlainProduct>
        {
            new() { Id = 1, Name = "Widget" }
        }.AsQueryable();

        FilterResult<PlainProduct> result = products.ToList(new Filter());

        Assert.Equal("Widget", result.Data[0].Name);
        Assert.Null(result.Policy);
    }

    [Fact]
    public void A_refused_filter_throws_out_of_the_handle()
    {
        Filter filter = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = "NationalId",
                        DataType = DataType.Text,
                        Operator = Operator.Equal,
                        Values = { "AAA" }
                    }
                }
            }
        };

        PolicyException exception = Assert.Throws<PolicyException>(() => Guarded().ToList(filter));

        Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, exception.ErrorCode);
    }

    [Fact]
    public void The_callers_filter_survives_the_round_trip_unmodified()
    {
        Filter filter = new() { Selects = new List<string> { "name" } };

        Guarded().ToList(filter);

        Assert.Equal("name", filter.Selects![0]);
    }

    [Fact]
    public void A_filter_the_policy_allows_returns_exactly_what_was_asked_for()
    {
        Filter filter = new()
        {
            Selects = new List<string> { "Id", "Name" },
            Orders = new List<OrderBy> { new() { Field = "Name", Direction = Direction.Descending } }
        };

        FilterResult<SecuredEmployee> result = Guarded(DwTier.Strict).ToList(filter);

        Assert.Equal(new[] { "Bo", "Ada" }, result.Data.Select(r => r.Name));
        Assert.Empty(result.Policy!.Decisions);
    }

    [Fact]
    public void Paging_information_survives_the_guarded_path()
    {
        Filter filter = new()
        {
            Orders = new List<OrderBy> { new() { Field = "Id" } },
            Page = new PageBy { PageNumber = 1, PageSize = 1 }
        };

        FilterResult<SecuredEmployee> result = Guarded().ToList(filter);

        Assert.Single(result.Data);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.PageCount);
    }

    [Fact]
    public void The_handle_refuses_a_null_query_or_context()
    {
        IQueryable<SecuredEmployee> query = People();

        Assert.Throws<ArgumentNullException>(() => query.ApplyPolicy(null!, Posture(DwTier.Strict), Resolver()));
        Assert.Throws<ArgumentNullException>(() => ((IQueryable<SecuredEmployee>)null!).ApplyPolicy(Caller(), Posture(DwTier.Strict), Resolver()));
    }

    [Fact]
    public void The_trace_reports_the_tier_the_query_actually_ran_under()
    {
        FilterResult<SecuredEmployee> result = Guarded(DwTier.Strict).ToList(
            new Filter { Selects = new List<string> { "Name" } });

        Assert.Equal(DwTier.Strict, result.Policy!.Tier);
        Assert.False(result.Policy.DryRun);
    }

    // ------------------------------------------------------------ configuration

    [Fact]
    public void The_default_posture_is_frozen_so_it_cannot_be_edited_at_request_time()
    {
        Assert.True(DwPolicy.Options.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => DwPolicy.Options.Tier = DwTier.Strict);
    }

    [Fact]
    public void The_default_configuration_enforces_the_attributes_rather_than_nothing()
    {
        // A policy layer that does nothing until someone remembers to switch it on is worse than
        // none at all: the attributes in the source read as though they are already in force.
        FilterResult<SecuredEmployee> result = People().ApplyPolicy(Caller()).ToList(new Filter());

        Assert.All(result.Data, row => Assert.Equal(string.Empty, row.NationalId));
    }
}
