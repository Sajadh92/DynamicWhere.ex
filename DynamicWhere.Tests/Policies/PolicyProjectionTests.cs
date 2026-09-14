using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// What a projection carries, as against what its text says.
/// </summary>
/// <remarks>
/// Deny-select is enforced by deciding about a list of names, and three things put a field in the
/// result without its name being in that list: sending no projection at all, naming the field's
/// parent, and naming a sibling of a nested node's key. Each was a way to receive a denied column
/// by asking for less rather than more, which is the shape a gate that reads the request cannot
/// see.
/// </remarks>
public class PolicyProjectionTests
{
    private static DwPolicyContext Caller() =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    private static PolicyResolver Resolver() =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    private static (Filter Result, PolicyTrace Trace) Guard<T>(
        Filter filter, DwTier tier = DwTier.Convenience)
        where T : class
    {
        PolicyTrace trace = new(tier, dryRun: false);

        Filter result = FilterSanitizer.Sanitize<T>(
            filter, Resolver(), Caller(), new DwPolicyOptions { Tier = tier }, trace);

        return (result, trace);
    }

    private static Segment Guard<T>(Segment segment, DwTier tier = DwTier.Convenience)
        where T : class =>
        FilterSanitizer.Sanitize<T>(
            segment,
            Resolver(),
            Caller(),
            new DwPolicyOptions { Tier = tier },
            new PolicyTrace(tier, dryRun: false));

    // ---- a segment that names no projection -----------------------------------------------------

    /// <summary>
    /// The hole the filter path closed and the segment path did not. A segment with no projection
    /// composes whole rows, so every deny-select decision was enforced against precisely the
    /// callers who volunteered a projection.
    /// </summary>
    [Fact]
    public void A_segment_with_no_projection_gets_the_allowed_one()
    {
        Segment sanitized = Guard<SecuredEmployee>(new Segment
        {
            ConditionSets =
            {
                new ConditionSet
                {
                    Sort = 1,
                    ConditionGroup = new ConditionGroup
                    {
                        Sort = 1,
                        Conditions =
                        {
                            new Condition
                            {
                                Sort = 1, Field = "Name", DataType = DataType.Text,
                                Operator = Operator.Equal, Values = { "Ada" }
                            }
                        }
                    }
                }
            }
        });

        Assert.NotNull(sanitized.Selects);
        Assert.DoesNotContain("NationalId", sanitized.Selects!);
        Assert.Contains("Name", sanitized.Selects!);
    }

    /// <summary>
    /// The shortest form of the same request. A segment carrying no condition sets is handed to the
    /// unguarded filter overload, which projects nothing either.
    /// </summary>
    [Fact]
    public void An_empty_segment_gets_the_allowed_projection_too()
    {
        Segment sanitized = Guard<SecuredEmployee>(new Segment());

        Assert.NotNull(sanitized.Selects);
        Assert.DoesNotContain("NationalId", sanitized.Selects!);
    }

    /// <summary>
    /// A projection is synthesized only where something is denied, so a type the policy has no
    /// opinion about still generates the SQL the unguarded path would.
    /// </summary>
    [Fact]
    public void A_segment_over_an_undecided_type_keeps_its_null_projection()
    {
        Assert.Null(Guard<PlainProduct>(new Segment()).Selects);
    }

    /// <summary>
    /// A segment that did name a projection is gated as it always was.
    /// </summary>
    /// <remarks>
    /// Salary rather than NationalId, because a field denied for every feature is refused earlier
    /// by participation: set membership discloses through which rows survive, so a segment naming
    /// one cannot be narrowed into safety. Salary is denied for select alone, which is the case
    /// this gate decides.
    /// </remarks>
    [Fact]
    public void A_segment_that_named_a_denied_field_still_loses_it()
    {
        Segment sanitized = Guard<SecuredEmployee>(new Segment
        {
            Selects = new List<string> { "Name", "Salary" }
        });

        Assert.Equal(new[] { "Name" }, sanitized.Selects);
    }

    [Fact]
    public void A_segment_whose_every_field_is_denied_is_refused()
    {
        PolicyException refused = Assert.Throws<PolicyException>(
            () => Guard<AllDeniedRow>(new Segment()));

        Assert.Equal(PolicyErrorCode.AllSelectsDenied, refused.ErrorCode);
    }

    // ---- naming the parent of a denied field ----------------------------------------------------

    /// <summary>
    /// Naming a navigation projects the whole object, so the caller who names the parent receives
    /// every field beneath it — including the ones naming them directly would refuse.
    /// </summary>
    [Fact]
    public void Naming_a_parent_whose_every_child_is_denied_is_refused()
    {
        // Next carries one field and it is denied, so nothing survives the expansion and the
        // clause has nothing left to project.
        PolicyException refused = Assert.Throws<PolicyException>(
            () => Guard<SecuredNode>(new Filter { Selects = new List<string> { "Next" } }));

        Assert.Equal(PolicyErrorCode.AllSelectsDenied, refused.ErrorCode);
    }

    /// <summary>
    /// The same one level down, through a collection navigation.
    /// </summary>
    [Fact]
    public void Naming_a_collection_parent_does_not_carry_its_denied_children()
    {
        (Filter result, _) = Guard<SecuredInvoiceDto>(new Filter
        {
            Selects = new List<string> { "Lines" }
        });

        Assert.DoesNotContain("Lines", result.Selects!);
        Assert.DoesNotContain("Lines.Cost", result.Selects!);
        Assert.Contains("Lines.Sku", result.Selects!);
    }

    /// <summary>
    /// Naming the child directly was always refused. The point of the two tests above is that
    /// naming its parent now answers the same way.
    /// </summary>
    [Fact]
    public void Naming_the_child_directly_is_refused_as_it_always_was()
    {
        (Filter result, _) = Guard<SecuredInvoiceDto>(new Filter
        {
            Selects = new List<string> { "Id", "Lines.Cost" }
        });

        Assert.DoesNotContain("Lines.Cost", result.Selects!);
    }

    /// <summary>
    /// A navigation with nothing denied beneath it is left exactly as the caller wrote it. The
    /// expansion happens only where there is something to narrow.
    /// </summary>
    [Fact]
    public void A_navigation_with_nothing_denied_beneath_it_is_left_alone()
    {
        (Filter result, _) = Guard<SecuredEmployee>(new Filter
        {
            Selects = new List<string> { "Contact" }
        });

        Assert.Equal(new[] { "Contact" }, result.Selects);
    }

    /// <summary>
    /// A navigation that is itself denied is refused on its own account, before anything asks what
    /// it carries.
    /// </summary>
    [Fact]
    public void A_navigation_that_is_itself_denied_is_still_refused()
    {
        (Filter result, _) = Guard<SealedNavigationRow>(new Filter
        {
            Selects = new List<string> { "Id", "Sealed" }
        });

        Assert.Equal(new[] { "Id" }, result.Selects);
    }

    [Fact]
    public void The_strict_tier_refuses_a_parent_that_carries_a_denied_child()
    {
        PolicyException refused = Assert.Throws<PolicyException>(
            () => Guard<SecuredInvoiceDto>(
                new Filter { Selects = new List<string> { "Lines" } }, DwTier.Strict));

        Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, refused.ErrorCode);
    }

    // ---- the key a nested node carries unasked --------------------------------------------------

    /// <summary>
    /// The projection builder adds the key of every nested node whether the caller named it or not,
    /// so a caller who asks for one sibling receives the key as well. Naming it in the projection is
    /// what lets the outbound transform see it as carried; without that a masked key leaves
    /// unmasked, because the walker matches carried paths against what the caller wrote.
    /// </summary>
    [Fact]
    public void A_nested_key_the_builder_adds_is_named_in_the_projection()
    {
        (Filter result, _) = Guard<KeyedParent>(new Filter
        {
            Selects = new List<string> { "Child.Label" }
        });

        Assert.Contains("Child.Id", result.Selects!);
    }

    /// <summary>
    /// A key that is denied outright refuses the clause rather than dropping it, because the
    /// projection is built with the key regardless and declining to build it is the only way to
    /// honour the denial.
    /// </summary>
    [Fact]
    public void A_denied_nested_key_refuses_the_projection_in_both_tiers()
    {
        foreach (DwTier tier in new[] { DwTier.Convenience, DwTier.Strict })
        {
            PolicyException refused = Assert.Throws<PolicyException>(
                () => Guard<SealedKeyParent>(
                    new Filter { Selects = new List<string> { "Child.Label" } }, tier));

            Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, refused.ErrorCode);
        }
    }

    /// <summary>
    /// A top-level field carries no nested key, so the ordinary request is untouched.
    /// </summary>
    [Fact]
    public void A_flat_projection_gains_nothing()
    {
        (Filter result, _) = Guard<SecuredEmployee>(new Filter
        {
            Selects = new List<string> { "Id", "Name" }
        });

        Assert.Equal(new[] { "Id", "Name" }, result.Selects);
    }
}

/// <summary>
/// The same three holes end to end, against a real database, so a decision that survives the gate
/// is shown to survive translation as well.
/// </summary>
[Collection(PolicyCollection.Name)]
public class GuardedProjectionTests : IDisposable
{
    private readonly PolicyContext _db;

    public GuardedProjectionTests(PolicyFixture fixture) => _db = fixture.CreateContext();

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    private PolicyQueryable<Staff> Guarded() =>
        _db.Staff.ApplyPolicy(
            new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
            new DwPolicyOptions { Tier = DwTier.Convenience },
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

    /// <summary>
    /// The exploit in its shortest form: a segment naming no projection returned every column of
    /// every row, denied ones included.
    /// </summary>
    [Fact]
    public async Task A_segment_with_no_projection_does_not_return_the_denied_column()
    {
        SegmentResult<Staff> result = await Guarded().ToListAsync(new Segment
        {
            ConditionSets =
            {
                new ConditionSet
                {
                    Sort = 1,
                    ConditionGroup = new ConditionGroup
                    {
                        Sort = 1,
                        Conditions =
                        {
                            new Condition
                            {
                                Sort = 1, Field = "Department", DataType = DataType.Text,
                                Operator = Operator.Equal, Values = { "Engineering" }
                            }
                        }
                    }
                }
            }
        });

        Assert.NotEmpty(result.Data);
        Assert.All(result.Data, row => Assert.Equal(string.Empty, row.NationalId));
        Assert.All(result.Data, row => Assert.NotEqual(string.Empty, row.Name));
    }

    /// <summary>
    /// And the same for a segment carrying nothing at all, which takes the other branch into the
    /// unguarded filter overload.
    /// </summary>
    [Fact]
    public async Task An_empty_segment_does_not_return_the_denied_column_either()
    {
        SegmentResult<Staff> result = await Guarded().ToListAsync(new Segment());

        Assert.NotEmpty(result.Data);
        Assert.All(result.Data, row => Assert.Equal(string.Empty, row.NationalId));
    }
}
