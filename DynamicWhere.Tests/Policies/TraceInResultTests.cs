using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using Microsoft.Extensions.Configuration;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// <c>DwPolicyOptions.IncludeTraceInResult</c>: whether the record of what a policy did travels back to
/// the caller on the result.
/// </summary>
/// <remarks>
/// Under the strict tier it names the columns a caller may not read, the attribute that sealed each
/// one, and every predicate injected on their behalf, so by default it stays in-process there.
/// </remarks>
[Collection(PolicyCollection.Name)]
public class TraceInResultTests : IDisposable
{
    private readonly PolicyContext _db;

    public TraceInResultTests(PolicyFixture fixture) => _db = fixture.CreateContext();

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Staff, whose <c>NationalId</c> and <c>Salary</c> a policy withholds, which the trace records.</summary>
    private PolicyQueryable<Staff> Guarded(DwTier tier, bool? include) =>
        _db.Staff.ApplyPolicy(
            new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
            new DwPolicyOptions { Tier = tier, IncludeTraceInResult = include, Caps = { MinGroupSize = 1 } },
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

    [Theory]
    [InlineData(DwTier.Strict, null, false)]
    [InlineData(DwTier.Convenience, null, true)]
    [InlineData(DwTier.Strict, true, true)]
    [InlineData(DwTier.Convenience, false, false)]
    [InlineData(DwTier.Strict, false, false)]
    [InlineData(DwTier.Convenience, true, true)]
    public async Task Every_guarded_result_follows_the_setting_or_the_tier(DwTier tier, bool? include, bool carried)
    {
        Filter filter = new() { Selects = new List<string> { "Id", "Name" } };
        Summary summary = new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Department" },
                AggregateBy = new List<AggregateBy> { new() { Field = "Id", Alias = "People", Aggregator = Aggregator.Count } }
            }
        };
        Segment segment = new()
        {
            ConditionSets =
            {
                new ConditionSet
                {
                    Sort = 1,
                    ConditionGroup = new ConditionGroup
                    {
                        Conditions = { new Condition { Field = "Id", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = { 0 } } }
                    }
                }
            },
            Selects = new List<string> { "Id", "Name" }
        };

        PolicyQueryable<Staff> guarded = Guarded(tier, include);

        Assert.Equal(carried, guarded.ToList(filter).Policy is not null);
        Assert.Equal(carried, (await guarded.ToListAsync(filter)).Policy is not null);
        Assert.Equal(carried, guarded.ToListDynamic(filter).Policy is not null);
        Assert.Equal(carried, (await guarded.ToListAsyncDynamic(filter)).Policy is not null);
        Assert.Equal(carried, guarded.ToList(summary).Policy is not null);
        Assert.Equal(carried, (await guarded.ToListAsync(summary)).Policy is not null);
        Assert.Equal(carried, (await guarded.ToListAsync(segment)).Policy is not null);
    }

    [Fact]
    public void The_trace_is_still_recorded_when_the_result_does_not_carry_it()
    {
        PolicyQueryable<Staff> guarded = Guarded(DwTier.Strict, include: null);

        FilterResult<Staff> result = guarded.ToList(new Filter());

        Assert.Null(result.Policy);
        Assert.NotNull(guarded.LastTrace);
        Assert.Contains(guarded.LastTrace!.Decisions, decision => decision.FieldPath == "NationalId");
    }

    [Fact]
    public void The_setting_freezes_with_the_rest_of_the_posture()
    {
        DwPolicyOptions options = new();

        options.Freeze();

        Assert.Throws<InvalidOperationException>(() => options.IncludeTraceInResult = true);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void The_setting_binds_from_configuration(string value, bool expected)
    {
        IConfiguration section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DynamicWhere:Policies:Tier"] = "Strict",
                ["DynamicWhere:Policies:IncludeTraceInResult"] = value
            })
            .Build()
            .GetSection("DynamicWhere:Policies");

        DwPolicyOptions options = new DwPolicyOptions().Bind(section);

        Assert.Equal(expected, options.IncludeTraceInResult);
    }
}
