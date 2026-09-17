using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Policies.Validation;
using DynamicWhere.ex.Source;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.Tests.Policies;

/// <summary>A role that belongs to one institution, or to none at all.</summary>
public class ScopedRole
{
    public int Id { get; set; }

    [DwForceWhere(Operator.Equal, ContextValue = "TenantId", AllowNull = true)]
    public int? InstitutionId { get; set; }

    [DwForceWhere(Operator.Equal, Value = "false")]
    public bool IsRetired { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>A widened scope on a member the caller must also filter on.</summary>
public class RequiredScopedRole
{
    public int Id { get; set; }

    [DwRequireWhere]
    [DwForceWhere(Operator.Equal, ContextValue = "TenantId", AllowNull = true)]
    public int? InstitutionId { get; set; }
}

/// <summary>A role whose only forced predicate lets null through.</summary>
public class OpenScopedRole
{
    public int Id { get; set; }

    [DwForceWhere(Operator.Equal, ContextValue = "TenantId", AllowNull = true)]
    public int? InstitutionId { get; set; }
}

/// <summary>AllowNull on an operator that already decides about null.</summary>
public class AllowNullOnANullCheck
{
    public int Id { get; set; }

    [DwForceWhere(Operator.IsNull, AllowNull = true)]
    public DateTime? RetiredAt { get; set; }
}

/// <summary>AllowNull on a member that can never be null.</summary>
public class AllowNullOnAValue
{
    public int Id { get; set; }

    [DwForceWhere(Operator.Equal, ContextValue = "TenantId", AllowNull = true)]
    public int InstitutionId { get; set; }
}

public sealed class ForcedNullContext : DbContext
{
    private readonly SqliteConnection _connection;

    public ForcedNullContext(SqliteConnection connection) => _connection = connection;

    public DbSet<ScopedRole> Roles => Set<ScopedRole>();

    public DbSet<RequiredScopedRole> RequiredRoles => Set<RequiredScopedRole>();

    public DbSet<OpenScopedRole> OpenRoles => Set<OpenScopedRole>();

    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlite(_connection);
}

/// <summary>
/// <c>[DwForceWhere(AllowNull = true)]</c>: a scope that also admits the rows belonging to no one.
/// </summary>
/// <remarks>
/// Institution 5 owns roles 1 and 5, institution 9 owns role 2, and roles 3 and 4 belong to no
/// institution. Roles 4 and 5 are retired, which the second forced predicate removes for everyone.
/// </remarks>
public sealed class ForcedAllowNullTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ForcedNullContext _db;

    public ForcedAllowNullTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _db = new ForcedNullContext(_connection);
        _db.Database.EnsureCreated();

        _db.Roles.AddRange(
            new ScopedRole { Id = 1, InstitutionId = 5, Name = "Admin-5" },
            new ScopedRole { Id = 2, InstitutionId = 9, Name = "Admin-9" },
            new ScopedRole { Id = 3, InstitutionId = null, Name = "System" },
            new ScopedRole { Id = 4, InstitutionId = null, IsRetired = true, Name = "Legacy system" },
            new ScopedRole { Id = 5, InstitutionId = 5, IsRetired = true, Name = "Old-5" });

        _db.RequiredRoles.AddRange(
            new RequiredScopedRole { Id = 1, InstitutionId = 5 },
            new RequiredScopedRole { Id = 2, InstitutionId = null },
            new RequiredScopedRole { Id = 3, InstitutionId = 9 });

        _db.OpenRoles.AddRange(
            new OpenScopedRole { Id = 1, InstitutionId = 5 },
            new OpenScopedRole { Id = 2, InstitutionId = null },
            new OpenScopedRole { Id = 3, InstitutionId = 9 });

        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static DwPolicyContext Tenant(int id) =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1").WithValue("TenantId", id);

    private static DwPolicyOptions Options(DwTier tier = DwTier.Convenience, bool dryRun = false) =>
        new() { Tier = tier, DryRun = dryRun, Caps = { MinGroupSize = 1 } };

    private static PolicyResolver Resolver(params IDwPolicyProvider[] more) =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() }.Concat(more).ToArray());

    private static Condition On(string field, DataType type, Operator op, params object[] values)
    {
        Condition condition = new() { Field = field, DataType = type, Operator = op };

        condition.Values.AddRange(values);

        return condition;
    }

    private static int[] Ids<T>(FilterResult<T> result, Func<T, int> id) =>
        result.Data.Select(id).OrderBy(x => x).ToArray();

    [Theory]
    [InlineData(DwTier.Convenience, 5, new[] { 1, 3 })]
    [InlineData(DwTier.Convenience, 9, new[] { 2, 3 })]
    [InlineData(DwTier.Strict, 5, new[] { 1, 3 })]
    [InlineData(DwTier.Strict, 9, new[] { 2, 3 })]
    public void A_row_belonging_to_no_institution_passes_beside_the_callers_own(DwTier tier, int tenant, int[] expected)
    {
        FilterResult<ScopedRole> result = _db.Roles
            .ApplyPolicy(Tenant(tenant), Options(tier), Resolver())
            .ToList(new Filter());

        Assert.Equal(expected, Ids(result, role => role.Id));
        Assert.Equal(expected.Length, result.TotalCount);
    }

    [Fact]
    public void A_scope_made_only_of_a_widened_predicate_still_applies_with_no_caller_filter()
    {
        // The injected root then holds no condition of its own, only the widened group.
        FilterResult<OpenScopedRole> result = _db.OpenRoles
            .ApplyPolicy(Tenant(5), Options(DwTier.Strict), Resolver())
            .ToList(new Filter());

        Assert.Equal(new[] { 1, 2 }, Ids(result, role => role.Id));
    }

    [Fact]
    public void An_or_filter_cannot_reach_another_institution()
    {
        // Wrapped in its own group and joined by And, the widening cannot merge with the caller's Or
        // into (Name = Admin-9 OR Name = System OR InstitutionId IS NULL).
        Filter filter = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.Or,
                Conditions =
                {
                    new Condition { Sort = 0, Field = "Name", DataType = DataType.Text, Operator = Operator.Equal, Values = { "Admin-9" } },
                    new Condition { Sort = 1, Field = "Name", DataType = DataType.Text, Operator = Operator.Equal, Values = { "System" } }
                }
            }
        };

        FilterResult<ScopedRole> result = _db.Roles.ApplyPolicy(Tenant(5), Options(), Resolver()).ToList(filter);

        Assert.Equal(new[] { 3 }, Ids(result, role => role.Id));
    }

    [Fact]
    public void A_caller_with_no_tenant_is_still_refused()
    {
        // Letting null rows through says which rows pass. It does not let a caller with no scope of
        // their own through as though they had one.
        DwPolicyContext unscoped = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

        PolicyException refused = Assert.Throws<PolicyException>(
            () => _db.Roles.ApplyPolicy(unscoped, Options(), Resolver()).ToList(new Filter()));

        Assert.Equal(PolicyErrorCode.MissingContextValue, refused.ErrorCode);
    }

    [Fact]
    public void The_trace_says_the_scope_lets_null_through()
    {
        FilterResult<ScopedRole> result = _db.Roles
            .ApplyPolicy(Tenant(5), Options(), Resolver())
            .ToList(new Filter());

        Assert.Contains(
            result.Policy!.Decisions,
            decision => decision.FieldPath == "InstitutionId"
                        && decision.Action == PolicyAction.Injected
                        && decision.Reason == "forced predicate (Equal, or null)");
        Assert.Contains(
            result.Policy.Decisions,
            decision => decision.FieldPath == "IsRetired" && decision.Reason == "forced predicate (Equal)");
    }

    [Fact]
    public void A_dry_run_injects_nothing()
    {
        FilterResult<ScopedRole> result = _db.Roles
            .ApplyPolicy(Tenant(5), Options(dryRun: true), Resolver())
            .ToList(new Filter());

        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Ids(result, role => role.Id));
    }

    [Theory]
    [InlineData(5, new[] { 3 })]
    [InlineData(9, new[] { 2, 3 })]
    public async Task Every_set_of_a_segment_is_scoped_and_widened(int tenant, int[] expected)
    {
        Segment segment = new()
        {
            ConditionSets =
            {
                new ConditionSet
                {
                    Sort = 1,
                    ConditionGroup = new ConditionGroup { Conditions = { On("Name", DataType.Text, Operator.Equal, "System") } }
                },
                new ConditionSet
                {
                    Sort = 2,
                    Intersection = Intersection.Union,
                    ConditionGroup = new ConditionGroup { Conditions = { On("Name", DataType.Text, Operator.Equal, "Admin-9") } }
                }
            }
        };

        SegmentResult<ScopedRole> result = await _db.Roles
            .ApplyPolicy(Tenant(tenant), Options(), Resolver())
            .ToListAsync(segment);

        Assert.Equal(expected, result.Data!.Select(role => role.Id).OrderBy(id => id));
    }

    [Fact]
    public void A_summary_counts_only_the_rows_the_scope_admits()
    {
        Summary summary = new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Name" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Field = "Id", Alias = "Roles", Aggregator = Aggregator.Count }
                }
            }
        };

        SummaryResult result = _db.Roles.ApplyPolicy(Tenant(5), Options(), Resolver()).ToList(summary);

        Assert.Equal(
            new[] { "Admin-5", "System" },
            result.Data.Select(row => (string)row.Name).OrderBy(name => name).ToArray());
    }

    [Fact]
    public void A_widened_scope_does_not_satisfy_a_requirement_on_the_same_member()
    {
        // (InstitutionId = 5 OR InstitutionId IS NULL) is a disjunction, and a requirement is met only
        // by a condition that binds. The caller still has to narrow it themselves.
        PolicyException refused = Assert.Throws<PolicyException>(
            () => _db.RequiredRoles.ApplyPolicy(Tenant(5), Options(), Resolver()).ToList(new Filter()));

        Assert.Equal(PolicyErrorCode.RequiredFilterMissing, refused.ErrorCode);

        Filter narrowed = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions = { On("InstitutionId", DataType.Number, Operator.Equal, 5) }
            }
        };

        FilterResult<RequiredScopedRole> result = _db.RequiredRoles
            .ApplyPolicy(Tenant(5), Options(), Resolver())
            .ToList(narrowed);

        Assert.Equal(new[] { 1 }, Ids(result, role => role.Id));
    }

    [Fact]
    public void AllowNull_on_a_null_check_is_refused_at_resolution_and_at_startup()
    {
        List<AllowNullOnANullCheck> rows = new() { new AllowNullOnANullCheck { Id = 1 } };

        Assert.Throws<ArgumentException>(
            () => rows.AsQueryable().ApplyPolicy(Tenant(5), Options(), Resolver()).ToList(new Filter()));

        PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(AllowNullOnANullCheck) });

        Assert.Contains(report.Errors, error => error.StartsWith("AllowNullOnANullCheck.RetiredAt:", StringComparison.Ordinal));
    }

    [Fact]
    public void AllowNull_on_a_member_that_can_never_be_null_is_refused_at_resolution_and_at_startup()
    {
        List<AllowNullOnAValue> rows = new() { new AllowNullOnAValue { Id = 1, InstitutionId = 5 } };

        Assert.Throws<ArgumentException>(
            () => rows.AsQueryable().ApplyPolicy(Tenant(5), Options(), Resolver()).ToList(new Filter()));

        PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(AllowNullOnAValue) });

        Assert.Contains(report.Errors, error => error.StartsWith("AllowNullOnAValue.InstitutionId:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_runtime_rule_widening_a_member_that_can_never_be_null_injects_the_comparison_alone()
    {
        // A stored rule is written without the type to hand, so it is not refused. On a member that is
        // never null the widening could match nothing, and the comparison alone is the same predicate.
        FakePolicyProvider rules = new FakePolicyProvider().AddForced(
            ForcedPredicate.FromContext("Id", Operator.Equal, DataType.Number, "RoleId", allowNull: true),
            PolicyLevel.DynamicGlobal);

        DwPolicyContext caller = Tenant(5).WithValue("RoleId", 3);

        FilterResult<ScopedRole> result = _db.Roles
            .ApplyPolicy(caller, Options(), Resolver(rules))
            .ToList(new Filter());

        Assert.Equal(new[] { 3 }, Ids(result, role => role.Id));
        Assert.Contains(
            result.Policy!.Decisions,
            decision => decision.FieldPath == "Id" && decision.Reason == "forced predicate (Equal)");
    }

    [Fact]
    public void A_malformed_forced_predicate_is_reported_at_startup_as_well()
    {
        // Resolution always refused naming both a constant and a context key; the startup scan did
        // not look at forced predicates at all, so the first query was where it surfaced.
        PolicyModelReport report = PolicyModelValidator.Inspect(new[] { typeof(BothValueAndContext) });

        Assert.Contains(report.Errors, error => error.StartsWith("BothValueAndContext.TenantId:", StringComparison.Ordinal));
        Assert.Empty(PolicyModelValidator.Inspect(new[] { typeof(ScopedRole), typeof(RequiredScopedRole) }).Errors);
    }

    /// <summary>A forced predicate naming both a constant and a context key.</summary>
    public class BothValueAndContext
    {
        public int Id { get; set; }

        [DwForceWhere(Operator.Equal, Value = "5", ContextValue = "TenantId")]
        public int TenantId { get; set; }
    }
}
