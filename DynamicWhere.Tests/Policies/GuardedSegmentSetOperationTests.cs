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

    private static DwPolicyContext Tenant(int id) =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1").WithValue("TenantId", id);

    /// <summary>
    /// Two sets over invoices, chosen so that dropping the scope from either set changes the answer.
    /// </summary>
    /// <remarks>
    /// Unscoped, the Union is <c>[1, 3, 4]</c>, the Intersect <c>[1, 4]</c> and the Except
    /// <c>[1, 2, 4]</c>. A Union reaches past the scope when a set after the first is left unscoped,
    /// and an Except does when the first is.
    /// </remarks>
    private static Segment Invoices(Intersection operation) => operation switch
    {
        Intersection.Union => Combine(
            On("Number", DataType.Text, Operator.Equal, "INV-1"),
            operation,
            On("Id", DataType.Number, Operator.GreaterThanOrEqual, 3)),
        Intersection.Intersect => Combine(
            On("Id", DataType.Number, Operator.GreaterThanOrEqual, 1),
            operation,
            On("Number", DataType.Text, Operator.Equal, "INV-1")),
        _ => Combine(
            On("Id", DataType.Number, Operator.GreaterThanOrEqual, 1),
            operation,
            On("Number", DataType.Text, Operator.Equal, "INV-3"))
    };

    /// <summary>
    /// Invoices carry two forced predicates, a tenant and a soft delete, and every set of a segment
    /// is scoped by both before the sets are combined in the database.
    /// </summary>
    /// <remarks>Tenant 5 owns invoices 1 and 2, and 2 is void; tenant 9 owns 3 and 4.</remarks>
    [Theory]
    [InlineData(DwTier.Convenience, 5, Intersection.Union, new[] { 1 })]
    [InlineData(DwTier.Convenience, 9, Intersection.Union, new[] { 3, 4 })]
    [InlineData(DwTier.Convenience, 5, Intersection.Intersect, new[] { 1 })]
    [InlineData(DwTier.Convenience, 9, Intersection.Intersect, new[] { 4 })]
    [InlineData(DwTier.Convenience, 5, Intersection.Except, new[] { 1 })]
    [InlineData(DwTier.Convenience, 9, Intersection.Except, new[] { 4 })]
    [InlineData(DwTier.Strict, 5, Intersection.Union, new[] { 1 })]
    [InlineData(DwTier.Strict, 9, Intersection.Union, new[] { 3, 4 })]
    [InlineData(DwTier.Strict, 5, Intersection.Intersect, new[] { 1 })]
    [InlineData(DwTier.Strict, 9, Intersection.Intersect, new[] { 4 })]
    [InlineData(DwTier.Strict, 5, Intersection.Except, new[] { 1 })]
    [InlineData(DwTier.Strict, 9, Intersection.Except, new[] { 4 })]
    public async Task Every_set_is_scoped_before_the_sets_combine(
        DwTier tier, int tenant, Intersection operation, int[] expected)
    {
        SegmentResult<Invoice> result = await _db.Invoices
            .ApplyPolicy(Tenant(tenant), Options(tier), Resolver())
            .ToListAsync(Invoices(operation));

        Assert.Equal(expected, result.Data!.Select(invoice => invoice.Id).OrderBy(id => id));
        Assert.Equal(expected.Length, result.TotalCount);
        Assert.All(result.Data!, invoice => Assert.Equal(tenant, invoice.TenantId));
        Assert.All(result.Data!, invoice => Assert.False(invoice.IsVoid));
    }

    /// <summary>
    /// A set that names another tenant outright still reads only the caller's own rows.
    /// </summary>
    /// <remarks>
    /// Unscoped, <c>Number = INV-3</c> is <c>[3]</c> and <c>TenantId = 5</c> is <c>[1, 2]</c>, so the
    /// Union would hand tenant 9 both of tenant 5's invoices, the void one included.
    /// </remarks>
    [Theory]
    [InlineData(DwTier.Convenience, 9, new[] { 3 })]
    [InlineData(DwTier.Convenience, 5, new[] { 1 })]
    [InlineData(DwTier.Strict, 9, new[] { 3 })]
    [InlineData(DwTier.Strict, 5, new[] { 1 })]
    public async Task A_set_naming_another_tenant_cannot_widen_the_scope(DwTier tier, int tenant, int[] expected)
    {
        Segment segment = Combine(
            On("Number", DataType.Text, Operator.Equal, "INV-3"),
            Intersection.Union,
            On("TenantId", DataType.Number, Operator.Equal, 5));

        SegmentResult<Invoice> result = await _db.Invoices
            .ApplyPolicy(Tenant(tenant), Options(tier), Resolver())
            .ToListAsync(segment);

        Assert.Equal(expected, result.Data!.Select(invoice => invoice.Id).OrderBy(id => id));
        Assert.Equal(expected.Length, result.TotalCount);
    }
}
