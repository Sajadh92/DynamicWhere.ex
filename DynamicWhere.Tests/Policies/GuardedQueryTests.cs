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
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// End-to-end guarded queries against a real database.
/// </summary>
/// <remarks>
/// The sanitizer suite proves the gate decides correctly as a pure function. This one proves the
/// decisions survive translation to SQL — that a dropped column is genuinely absent from the query
/// rather than merely absent from a list, and that the guarded path leaves the change tracker
/// clean.
/// </remarks>
[Collection(PolicyCollection.Name)]
public class GuardedQueryTests : IDisposable
{
    private readonly PolicyContext _db;

    public GuardedQueryTests(PolicyFixture fixture) => _db = fixture.CreateContext();

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private static DwPolicyContext Caller() =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    private static PolicyResolver Resolver() =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    private PolicyQueryable<Staff> Guarded(DwTier tier = DwTier.Convenience) =>
        _db.Staff.ApplyPolicy(
            Caller(), new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } }, Resolver());

    // ------------------------------------------------------------------ projection

    [Fact]
    public void A_refused_column_never_reaches_the_database_query()
    {
        FilterResult<Staff> result = Guarded().ToList(
            new Filter { Selects = new List<string> { "Name", "Salary" } },
            getQueryString: true);

        Assert.DoesNotContain("Salary", result.QueryString!, StringComparison.Ordinal);
        Assert.Contains("Name", result.QueryString!, StringComparison.Ordinal);
        Assert.All(result.Data, row => Assert.Equal(0m, row.Salary));
    }

    [Fact]
    public void A_query_asking_for_no_projection_still_loses_the_refused_columns()
    {
        FilterResult<Staff> result = Guarded().ToList(new Filter(), getQueryString: true);

        Assert.DoesNotContain("NationalId", result.QueryString!, StringComparison.Ordinal);
        Assert.DoesNotContain("Salary", result.QueryString!, StringComparison.Ordinal);
        Assert.Contains("Name", result.QueryString!, StringComparison.Ordinal);
        Assert.Equal(3, result.Data.Count);
    }

    [Fact]
    public void Strict_refuses_a_projection_the_convenience_tier_would_trim()
    {
        Filter filter = new() { Selects = new List<string> { "Name", "Salary" } };

        FilterResult<Staff> trimmed = Guarded().ToList(filter);

        Assert.Equal(3, trimmed.Data.Count);

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guarded(DwTier.Strict).ToList(filter));

        Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, exception.ErrorCode);
    }

    // ---------------------------------------------------------------------- filter

    [Fact]
    public void A_refused_filter_throws_in_both_tiers_before_the_database_is_touched()
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
                        Values = { "AAA-111" }
                    }
                }
            }
        };

        foreach (DwTier tier in new[] { DwTier.Convenience, DwTier.Strict })
        {
            PolicyException exception = Assert.Throws<PolicyException>(() => Guarded(tier).ToList(filter));

            Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, exception.ErrorCode);
        }
    }

    [Fact]
    public void An_allowed_filter_reaches_the_database_and_narrows_the_result()
    {
        Filter filter = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = "Department",
                        DataType = DataType.Text,
                        Operator = Operator.Equal,
                        Values = { "Engineering" }
                    }
                }
            }
        };

        FilterResult<Staff> result = Guarded().ToList(filter);

        Assert.Equal(2, result.Data.Count);
        Assert.All(result.Data, row => Assert.Equal("Engineering", row.Department));
    }

    [Fact]
    public void A_field_refused_only_for_projection_can_still_filter()
    {
        Filter filter = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = "Salary",
                        DataType = DataType.Number,
                        Operator = Operator.GreaterThan,
                        Values = { 90000 }
                    }
                }
            }
        };

        FilterResult<Staff> result = Guarded().ToList(filter);

        Assert.Equal(2, result.Data.Count);
        Assert.All(result.Data, row => Assert.Equal(0m, row.Salary));
    }

    [Fact]
    public void A_restricted_operator_is_refused_against_the_real_column()
    {
        Filter searching = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = "Badge",
                        DataType = DataType.Text,
                        Operator = Operator.Contains,
                        Values = { "E-" }
                    }
                }
            }
        };

        Assert.Throws<PolicyException>(() => Guarded().ToList(searching));

        Filter confirming = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = "Badge",
                        DataType = DataType.Text,
                        Operator = Operator.Equal,
                        Values = { "E-1" }
                    }
                }
            }
        };

        Assert.Single(Guarded().ToList(confirming).Data);
    }

    // ----------------------------------------------------------------------- order

    [Fact]
    public void A_refused_sort_is_dropped_then_refused_by_tier()
    {
        Filter filter = new()
        {
            Orders = new List<OrderBy> { new() { Field = "Notes" } }
        };

        Assert.Equal(3, Guarded().ToList(filter).Data.Count);
        Assert.Throws<PolicyException>(() => Guarded(DwTier.Strict).ToList(filter));
    }

    // ------------------------------------------------------------------------ caps

    [Fact]
    public void A_page_over_the_cap_is_refused_before_the_query_runs()
    {
        DwPolicyOptions options = new() { Tier = DwTier.Convenience };

        options.Caps.MaxPageSize = 2;

        PolicyQueryable<Staff> handle = _db.Staff.ApplyPolicy(Caller(), options, Resolver());

        PolicyException exception = Assert.Throws<PolicyException>(
            () => handle.ToList(new Filter { Page = new PageBy { PageNumber = 1, PageSize = 50 } }));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
    }

    // -------------------------------------------------------------- require policy

    [Fact]
    public void An_unguarded_read_of_a_guarded_entity_is_refused()
    {
        PolicyException exception = Assert.Throws<PolicyException>(
            () => _db.Staff.ToList(new Filter()));

        Assert.Equal(PolicyErrorCode.PolicyRequired, exception.ErrorCode);
    }

    [Fact]
    public void An_entity_without_the_requirement_is_read_unguarded_as_before()
    {
        FilterResult<Office> result = _db.Offices.ToList(new Filter());

        Assert.Equal(2, result.Data.Count);
        Assert.Null(result.Policy);
    }

    // ------------------------------------------------------------------- tracking

    [Fact]
    public void A_guarded_query_leaves_nothing_in_the_change_tracker()
    {
        // Not an optimization. Masking mutates materialized entities, and a tracked one would write
        // the mask back on the next SaveChanges -- replacing real data with the placeholder that
        // was only ever meant for display. Offices is used because nothing on it is denied, so the
        // whole entity materializes and tracking would actually apply.
        _db.Offices.ApplyPolicy(Caller(), new DwPolicyOptions(), Resolver()).ToList(new Filter());

        Assert.Empty(_db.ChangeTracker.Entries());
    }

    [Fact]
    public void The_same_query_unguarded_does_track_its_entities()
    {
        // The contrast that makes the assertion above mean something: without AsNoTracking these
        // entities are tracked, so the guarded path is doing the work rather than the provider.
        _db.Offices.ToList(new Filter());

        Assert.NotEmpty(_db.ChangeTracker.Entries());
        Assert.All(_db.ChangeTracker.Entries(), e => Assert.Equal(EntityState.Unchanged, e.State));
    }

    // --------------------------------------------------------------------- summary

    [Fact]
    public void A_summary_aggregating_a_projection_refused_field_still_totals_it()
    {
        Summary summary = new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Department" },
                AggregateBy = new List<AggregateBy>
                {
                    // CountDistinct rather than Sumation: SQLite refuses to aggregate a decimal
                    // with SUM. The point being made is about the policy, not the aggregator.
                    new() { Field = "Salary", Alias = "PayBands", Aggregator = Aggregator.CountDistinct }
                }
            }
        };

        SummaryResult result = Guarded().ToList(summary);

        Assert.Equal(2, result.Data.Count);
        Assert.NotNull(result.Policy);
    }

    [Fact]
    public void A_summary_grouping_by_a_refused_key_is_returned_as_a_refusal()
    {
        Summary summary = new()
        {
            GroupBy = new GroupBy { Fields = new List<string> { "Notes" } }
        };

        PolicyException exception = Assert.Throws<PolicyException>(() => Guarded().ToList(summary));

        Assert.Equal(PolicyErrorCode.FieldDeniedForGroup, exception.ErrorCode);
    }

    // ------------------------------------------------------------------- isolation

    [Fact]
    public void The_callers_filter_comes_back_exactly_as_it_went_in()
    {
        // The unguarded path rewrites the caller's filter in place. The guarded one must not, or a
        // policy-narrowed filter reused on a second query would carry the first query's narrowing.
        Filter filter = new()
        {
            Selects = new List<string> { "name", "salary" },
            Orders = new List<OrderBy> { new() { Field = "department" } }
        };

        Guarded().ToList(filter);

        Assert.Equal(new[] { "name", "salary" }, filter.Selects!);
        Assert.Equal("department", filter.Orders![0].Field);
    }

    [Fact]
    public async Task The_asynchronous_path_enforces_the_same_way()
    {
        FilterResult<Staff> result = await Guarded().ToListAsync(new Filter());

        Assert.Equal(3, result.Data.Count);
        Assert.All(result.Data, row => Assert.Equal(string.Empty, row.NationalId));
        Assert.NotNull(result.Policy);
    }

    // ------------------------------------------------- injection and aliases, end to end

    private static DwPolicyContext Tenant(int id) =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1").WithValue("TenantId", id);

    private PolicyQueryable<Invoice> Books(int tenant = 5, DwTier tier = DwTier.Convenience) =>
        _db.Invoices.ApplyPolicy(Tenant(tenant), new DwPolicyOptions { Tier = tier }, Resolver());

    [Fact]
    public void The_injected_predicate_reaches_the_generated_sql()
    {
        FilterResult<Invoice> result = Books().ToList(new Filter(), getQueryString: true);

        Assert.Contains("TenantId", result.QueryString!, StringComparison.Ordinal);
        Assert.Contains("IsVoid", result.QueryString!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_injected_predicate_actually_removes_rows()
    {
        // Four invoices exist. Tenant 5 owns two of them and one of those is void, so the scope
        // leaves exactly one.
        FilterResult<Invoice> result = Books().ToList(new Filter());

        Invoice only = Assert.Single(result.Data);

        Assert.Equal(1, only.Id);
        Assert.Equal(4, _db.Invoices.Count());
    }

    [Fact]
    public void A_different_caller_sees_a_different_set_of_rows()
    {
        List<int> mine = Books(5).ToList(new Filter()).Data.ConvertAll(i => i.Id);
        List<int> theirs = Books(9).ToList(new Filter()).Data.ConvertAll(i => i.Id);

        Assert.Equal(new[] { 1 }, mine);
        Assert.Equal(new[] { 3, 4 }, theirs);
    }

    [Fact]
    public void A_caller_cannot_widen_the_scope_by_filtering_for_another_tenant()
    {
        // The caller's own term is conjoined with the forced one, so asking for tenant 9 while
        // scoped to tenant 5 returns nothing rather than tenant 9's rows.
        Filter filter = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = "TenantId", DataType = DataType.Number,
                        Operator = Operator.Equal, Values = { 9 }
                    }
                }
            }
        };

        Assert.Empty(Books().ToList(filter).Data);
    }

    [Fact]
    public void An_or_filter_cannot_escape_the_scope()
    {
        // The shape the wrap exists for. Merged into the caller's group this would read
        // (Number = INV-3 OR Number = INV-1 OR TenantId = 5) and return tenant 9's rows.
        Filter filter = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.Or,
                Conditions =
                {
                    new Condition
                    {
                        Sort = 0, Field = "Number", DataType = DataType.Text,
                        Operator = Operator.Equal, Values = { "INV-3" }
                    },
                    new Condition
                    {
                        Sort = 1, Field = "Number", DataType = DataType.Text,
                        Operator = Operator.Equal, Values = { "INV-1" }
                    }
                }
            }
        };

        Invoice only = Assert.Single(Books().ToList(filter).Data);

        Assert.Equal(1, only.Id);
    }

    [Fact]
    public void An_aliased_filter_returns_exactly_what_the_internal_path_returns()
    {
        Filter ByName(string field) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = field, DataType = DataType.Text,
                        Operator = Operator.Equal, Values = { "INV-1" }
                    }
                }
            }
        };

        List<int> byAlias = Books().ToList(ByName("reference")).Data.ConvertAll(i => i.Id);
        List<int> byPath = Books().ToList(ByName("Number")).Data.ConvertAll(i => i.Id);

        Assert.Equal(byPath, byAlias);
        Assert.Equal(new[] { 1 }, byAlias);
    }

    [Fact]
    public void An_aliased_projection_reaches_the_column_it_names()
    {
        FilterResult<Invoice> result = Books().ToList(
            new Filter { Selects = new List<string> { "Id", "reference" } },
            getQueryString: true);

        Assert.Contains("Number", result.QueryString!, StringComparison.Ordinal);
        Assert.Equal("INV-1", Assert.Single(result.Data).Number);
    }

    [Fact]
    public void An_aliased_sort_reaches_the_column_it_names()
    {
        FilterResult<Invoice> result = Books(9).ToList(new Filter
        {
            Orders = new List<OrderBy> { new() { Field = "reference", Direction = Direction.Descending } }
        });

        Assert.Equal(new[] { 3, 4 }, result.Data.ConvertAll(i => i.Id));
    }

    [Fact]
    public void A_required_filter_is_refused_before_any_sql_is_built()
    {
        PolicyQueryable<Journal> journals = _db.Journals.ApplyPolicy(
            Tenant(5), new DwPolicyOptions(), Resolver());

        PolicyException error = Assert.Throws<PolicyException>(() => journals.ToList(new Filter()));

        Assert.Equal(PolicyErrorCode.RequiredFilterMissing, error.ErrorCode);
    }

    [Fact]
    public void A_required_filter_supplied_by_the_caller_lets_the_query_through()
    {
        PolicyQueryable<Journal> journals = _db.Journals.ApplyPolicy(
            Tenant(5), new DwPolicyOptions(), Resolver());

        FilterResult<Journal> result = journals.ToList(new Filter
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = "TenantId", DataType = DataType.Number,
                        Operator = Operator.Equal, Values = { 5 }
                    }
                }
            }
        });

        Assert.Equal(1, Assert.Single(result.Data).Id);
    }

    [Fact]
    public void A_scoped_query_leaves_the_change_tracker_clean()
    {
        // The injected predicate must not resurrect tracking. AsNoTracking is applied at the one
        // point every guarded query passes through, and injection happens well before that.
        FilterResult<Invoice> result = Books().ToList(new Filter());

        Assert.All(result.Data, row => Assert.Equal(EntityState.Detached, _db.Entry(row).State));
    }
}
