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
/// Covers the numeric limits applied to every guarded query.
/// </summary>
/// <remarks>
/// These exist independently of access control. Before this release the library accepted a filter
/// of any size and a navigation path of any depth, so one request could generate an unbounded join
/// — a denial of service that needed no policy violation at all. Caps refuse in both tiers, because
/// silently truncating a filter would widen its result the same way dropping a condition does.
/// </remarks>
public class CapTests
{
    private static DwPolicyContext Caller() =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    private static (Filter Result, PolicyTrace Trace) Guard<T>(
        Filter filter, DwTier tier, Action<DwCaps>? configure = null)
        where T : class
    {
        DwPolicyOptions options = new() { Tier = tier };

        configure?.Invoke(options.Caps);

        PolicyTrace trace = new(tier, dryRun: false);

        Filter result = FilterSanitizer.Sanitize<T>(
            filter,
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }),
            Caller(),
            options,
            trace);

        return (result, trace);
    }

    private static Condition On(string field) => new()
    {
        Field = field,
        DataType = DataType.Text,
        Operator = Operator.Equal,
        Values = { "x" }
    };

    [Fact]
    public void Conditions_are_counted_across_the_whole_nested_tree()
    {
        // Counting only the root group would let any budget be evaded by nesting, which is the
        // cheapest possible way to rebuild the unbounded join the cap exists to prevent.
        Filter filter = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions = { On("Name") },
                SubConditionGroups =
                {
                    new ConditionGroup
                    {
                        Conditions = { On("Name") },
                        SubConditionGroups =
                        {
                            new ConditionGroup { Conditions = { On("Name"), On("Name") } }
                        }
                    }
                }
            }
        };

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<SecuredEmployee>(filter, DwTier.Convenience, caps => caps.MaxConditions = 3));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
        Assert.Contains("MaxConditions", exception.SourceOrigin!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_filter_inside_the_condition_budget_passes()
    {
        Filter filter = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions = { On("Name") },
                SubConditionGroups = { new ConditionGroup { Conditions = { On("Name") } } }
            }
        };

        (Filter result, _) = Guard<SecuredEmployee>(filter, DwTier.Strict, caps => caps.MaxConditions = 2);

        Assert.NotNull(result.ConditionGroup);
    }

    [Fact]
    public void Too_many_order_fields_are_refused()
    {
        Filter filter = new()
        {
            Orders = new List<OrderBy>
            {
                new() { Field = "Name" },
                new() { Field = "Id" },
                new() { Field = "Salary" }
            }
        };

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<SecuredEmployee>(filter, DwTier.Convenience, caps => caps.MaxOrderFields = 2));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
        Assert.Contains("MaxOrderFields", exception.SourceOrigin!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_oversized_page_is_refused_rather_than_quietly_clamped()
    {
        // Clamping would hand back a page smaller than the caller asked for while reporting
        // success, and a caller paging through results would silently skip rows.
        Filter filter = new() { Page = new PageBy { PageNumber = 1, PageSize = 5000 } };

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<SecuredEmployee>(filter, DwTier.Convenience));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
        Assert.Contains("MaxPageSize", exception.SourceOrigin!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_inside_the_limit_passes()
    {
        Filter filter = new() { Page = new PageBy { PageNumber = 1, PageSize = 50 } };

        (Filter result, _) = Guard<SecuredEmployee>(filter, DwTier.Strict);

        Assert.Equal(50, result.Page!.PageSize);
    }

    [Fact]
    public void A_navigation_path_deeper_than_the_cap_is_refused()
    {
        // PlainNode points at itself, so a caller can write an arbitrarily long path and make the
        // provider emit one join per segment.
        Filter filter = new() { Selects = new List<string> { "Next.Next.Next.Next.Label" } };

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<PlainNode>(filter, DwTier.Convenience));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
        Assert.Equal("Next.Next.Next.Next.Label", exception.FieldPath);
        Assert.Contains("MaxNavigationDepth", exception.SourceOrigin!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_navigation_path_at_the_cap_passes()
    {
        Filter filter = new() { Selects = new List<string> { "Next.Next.Next.Label" } };

        (Filter result, _) = Guard<PlainNode>(filter, DwTier.Strict);

        Assert.Equal("Next.Next.Next.Label", result.Selects![0]);
    }

    [Fact]
    public void Depth_is_checked_on_conditions_and_orders_too()
    {
        Filter conditions = new()
        {
            ConditionGroup = new ConditionGroup { Conditions = { On("Next.Next.Next.Next.Label") } }
        };

        Assert.Throws<PolicyException>(() => Guard<PlainNode>(conditions, DwTier.Strict));

        Filter orders = new()
        {
            Orders = new List<OrderBy> { new() { Field = "Next.Next.Next.Next.Label" } }
        };

        Assert.Throws<PolicyException>(() => Guard<PlainNode>(orders, DwTier.Strict));
    }

    [Fact]
    public void Caps_refuse_in_the_convenience_tier_as_well_as_the_strict_one()
    {
        Filter filter = new() { Page = new PageBy { PageSize = 5000 } };

        foreach (DwTier tier in new[] { DwTier.Convenience, DwTier.Strict })
        {
            Assert.Throws<PolicyException>(() => Guard<SecuredEmployee>(filter, tier));
        }
    }

    [Fact]
    public void A_cap_is_checked_before_the_field_policy_is_consulted()
    {
        // Resolving policy for every field of an unbounded filter is the work the cap exists to
        // avoid, so the cheap structural check has to come first.
        Filter filter = new()
        {
            Page = new PageBy { PageSize = 5000 },
            ConditionGroup = new ConditionGroup { Conditions = { On("Contact.Email") } }
        };

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<SecuredEmployee>(filter, DwTier.Strict));

        Assert.Equal(PolicyErrorCode.CapExceeded, exception.ErrorCode);
    }
}
