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
/// Covers the gate as the pure function it is: a <see cref="Filter"/> in, a sanitized
/// <see cref="Filter"/> out, with no database and no EF involvement.
/// </summary>
public class FilterSanitizerTests
{
    private static PolicyResolver EmptyResolver() =>
        new(Array.Empty<IDwPolicyProvider>());

    private static DwPolicyContext Caller() =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    private static DwPolicyOptions Options() => new();

    private static PolicyTrace Trace() => new(DwTier.Convenience, dryRun: false);

    private static Filter Sanitize<T>(Filter filter) where T : class =>
        FilterSanitizer.Sanitize<T>(filter, EmptyResolver(), Caller(), Options(), Trace());

    [Fact]
    public void The_callers_filter_is_never_touched()
    {
        // Validator rewrites Field in place. Without the clone, a guarded query would leave the
        // caller holding a filter the policy layer had edited.
        Filter original = new()
        {
            Selects = new List<string> { "name" },
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition
                    {
                        Field = "name",
                        DataType = DataType.Text,
                        Operator = Operator.Equal,
                        Values = { "alpha" }
                    }
                }
            }
        };

        Filter sanitized = Sanitize<SecuredEmployee>(original);

        Assert.NotSame(original, sanitized);
        Assert.Equal("name", original.Selects![0]);
        Assert.Equal("name", original.ConditionGroup!.Conditions[0].Field);
    }

    [Fact]
    public void A_select_comes_back_in_the_canonical_form_the_pipeline_uses()
    {
        Filter filter = new() { Selects = new List<string> { "  name  ", "contact.email" } };

        Filter sanitized = Sanitize<SecuredEmployee>(filter);

        Assert.Equal(new[] { "Name", "Contact.Email" }, sanitized.Selects!);
    }

    [Fact]
    public void An_order_comes_back_canonical()
    {
        Filter filter = new()
        {
            Orders = new List<OrderBy> { new() { Field = "contact. phone", Direction = Direction.Ascending } }
        };

        Filter sanitized = Sanitize<SecuredEmployee>(filter);

        Assert.Equal("Contact.Phone", sanitized.Orders![0].Field);
    }

    [Fact]
    public void A_condition_nested_in_a_subgroup_is_canonicalized_too()
    {
        // The recursion is where a hand-written walk stops early, and a path left uncanonicalized
        // is a path a deny fragment fails to match.
        Filter filter = new()
        {
            ConditionGroup = new ConditionGroup
            {
                SubConditionGroups =
                {
                    new ConditionGroup
                    {
                        SubConditionGroups =
                        {
                            new ConditionGroup
                            {
                                Conditions =
                                {
                                    new Condition
                                    {
                                        Field = "  contact.email  ",
                                        DataType = DataType.Text,
                                        Operator = Operator.Equal,
                                        Values = { "a@b.c" }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        Filter sanitized = Sanitize<SecuredEmployee>(filter);

        Assert.Equal(
            "Contact.Email",
            sanitized.ConditionGroup!.SubConditionGroups[0].SubConditionGroups[0].Conditions[0].Field);
    }

    [Fact]
    public void An_unknown_field_fails_as_a_validation_error_not_a_policy_error()
    {
        // A field that does not exist has no policy, so it must fail before any policy decision is
        // reached -- and it must fail identically guarded or unguarded, or the error itself tells a
        // prober which fields exist.
        Filter filter = new() { Selects = new List<string> { "NoSuchField" } };

        LogicException exception = Assert.Throws<LogicException>(() => Sanitize<SecuredEmployee>(filter));

        Assert.IsNotType<PolicyException>(exception);
    }

    [Fact]
    public void A_blank_condition_field_fails_the_way_the_pipeline_fails_it()
    {
        Filter filter = new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions =
                {
                    new Condition { Field = "   ", DataType = DataType.Text, Operator = Operator.Equal }
                }
            }
        };

        LogicException exception = Assert.Throws<LogicException>(() => Sanitize<SecuredEmployee>(filter));

        Assert.IsNotType<PolicyException>(exception);
    }

    [Fact]
    public void A_filter_with_nothing_in_it_survives_untouched()
    {
        Filter sanitized = Sanitize<SecuredEmployee>(new Filter());

        Assert.Null(sanitized.ConditionGroup);
        Assert.Null(sanitized.Selects);
        Assert.Null(sanitized.Orders);
        Assert.Null(sanitized.Page);
    }

    [Fact]
    public void Page_survives_the_sanitizer_unchanged()
    {
        Filter filter = new() { Page = new PageBy { PageNumber = 2, PageSize = 30 } };

        Filter sanitized = Sanitize<SecuredEmployee>(filter);

        Assert.Equal(2, sanitized.Page!.PageNumber);
        Assert.Equal(30, sanitized.Page.PageSize);
    }
}
