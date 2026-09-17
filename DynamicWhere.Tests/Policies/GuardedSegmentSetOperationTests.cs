using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Union, Intersect and Except on a guarded segment, against a real database.
/// </summary>
/// <remarks>
/// A guarded query runs over <c>AsNoTracking()</c>, so each condition set materializes instances of
/// its own, and a projection the policy synthesizes builds new ones regardless. Combining the sets
/// by reference found no row in common: Intersect returned nothing, Except removed nothing, and
/// Union counted a row once for every set that matched it.
/// </remarks>
[Collection(PolicyCollection.Name)]
public class GuardedSegmentSetOperationTests : IDisposable
{
    private readonly PolicyContext _db;

    public GuardedSegmentSetOperationTests(PolicyFixture fixture) => _db = fixture.CreateContext();

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private static DwPolicyContext Caller() =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    private static PolicyResolver Resolver() =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    private static DwPolicyOptions Options(DwTier tier) => new() { Tier = tier, Caps = { MinGroupSize = 1 } };

    private static Condition On(string field, DataType type, Operator op, params object[] values)
    {
        Condition condition = new() { Field = field, DataType = type, Operator = op };

        condition.Values.AddRange(values);

        return condition;
    }

    private static Segment Combine(Condition first, Intersection operation, Condition second) => new()
    {
        ConditionSets =
        {
            new ConditionSet { Sort = 1, ConditionGroup = new ConditionGroup { Conditions = { first } } },
            new ConditionSet
            {
                Sort = 2,
                Intersection = operation,
                ConditionGroup = new ConditionGroup { Conditions = { second } }
            }
        }
    };

    /// <summary>
    /// Offices carry no policy attribute, so nothing is projected: the rows are whole entities and
    /// only <c>AsNoTracking</c> keeps the two sets from sharing them.
    /// </summary>
    /// <remarks>Office 1 is Baghdad with 40 seats, office 2 is Amman with 25.</remarks>
    [Theory]
    [InlineData(DwTier.Convenience, Intersection.Union, new[] { 1, 2 })]
    [InlineData(DwTier.Convenience, Intersection.Intersect, new[] { 1 })]
    [InlineData(DwTier.Convenience, Intersection.Except, new[] { 2 })]
    [InlineData(DwTier.Strict, Intersection.Union, new[] { 1, 2 })]
    [InlineData(DwTier.Strict, Intersection.Intersect, new[] { 1 })]
    [InlineData(DwTier.Strict, Intersection.Except, new[] { 2 })]
    public async Task Whole_rows_combine_as_the_rows_they_are(DwTier tier, Intersection operation, int[] expected)
    {
        Segment segment = Combine(
            On("Capacity", DataType.Number, Operator.GreaterThanOrEqual, 25),
            operation,
            On("City", DataType.Text, Operator.Equal, "Baghdad"));

        SegmentResult<Office> result = await _db.Offices
            .ApplyPolicy(Caller(), Options(tier), Resolver())
            .ToListAsync(segment);

        Assert.Equal(expected, result.Data!.Select(office => office.Id).OrderBy(id => id));
        Assert.Equal(expected.Length, result.TotalCount);
    }

    /// <summary>
    /// Staff carry fields denied for projection, so a segment naming no projection is given one,
    /// and every set builds new objects whatever the tracking.
    /// </summary>
    /// <remarks>Engineering is Ada (1) and Bo (2); Bo (2) and Cy (3) are the other set.</remarks>
    [Theory]
    [InlineData(Intersection.Union, new[] { 1, 2, 3 })]
    [InlineData(Intersection.Intersect, new[] { 2 })]
    [InlineData(Intersection.Except, new[] { 1 })]
    public async Task Projected_rows_combine_as_the_rows_they_came_from(Intersection operation, int[] expected)
    {
        Segment segment = Combine(
            On("Department", DataType.Text, Operator.Equal, "Engineering"),
            operation,
            On("Name", DataType.Text, Operator.In, "Bo", "Cy"));

        SegmentResult<Staff> result = await _db.Staff
            .ApplyPolicy(Caller(), Options(DwTier.Convenience), Resolver())
            .ToListAsync(segment);

        Assert.Equal(expected, result.Data!.Select(staff => staff.Id).OrderBy(id => id));
        Assert.Equal(expected.Length, result.TotalCount);
        Assert.All(result.Data!, staff => Assert.Equal(string.Empty, staff.NationalId));
    }
}
