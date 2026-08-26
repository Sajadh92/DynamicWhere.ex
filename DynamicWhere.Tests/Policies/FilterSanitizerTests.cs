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

    private static PolicyResolver AttributeResolver() =>
        new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

    private static DwPolicyOptions Options(DwTier tier) => new() { Tier = tier };

    private static PolicyTrace Trace() => new(DwTier.Convenience, dryRun: false);

    /// <summary>Sanitizes against no policy at all, for the canonicalization tests.</summary>
    private static Filter Sanitize<T>(Filter filter) where T : class =>
        FilterSanitizer.Sanitize<T>(
            filter, EmptyResolver(), Caller(), Options(DwTier.Convenience), Trace());

    /// <summary>Sanitizes against the attributes on <typeparamref name="T"/> at a given tier.</summary>
    private static (Filter Result, PolicyTrace Trace) Guard<T>(Filter filter, DwTier tier)
        where T : class
    {
        PolicyTrace trace = new(tier, dryRun: false);

        Filter result = FilterSanitizer.Sanitize<T>(
            filter, AttributeResolver(), Caller(), Options(tier), trace);

        return (result, trace);
    }

    /// <summary>Sanitizes against fragments a test supplies directly.</summary>
    private static (Filter Result, PolicyTrace Trace) Guard<T>(
        Filter filter, DwTier tier, FakePolicyProvider provider)
        where T : class
    {
        PolicyTrace trace = new(tier, dryRun: false);

        Filter result = FilterSanitizer.Sanitize<T>(
            filter, new PolicyResolver(new IDwPolicyProvider[] { provider }),
            Caller(), Options(tier), trace);

        return (result, trace);
    }

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

    [Fact]
    public void Convenience_drops_a_field_the_policy_refuses_to_project()
    {
        // Salary carries [DwNoSelect]. Dropping is safe here in a way it never is for a filter:
        // a narrower projection cannot widen the result set.
        Filter filter = new() { Selects = new List<string> { "Name", "Salary" } };

        (Filter result, PolicyTrace trace) = Guard<SecuredEmployee>(filter, DwTier.Convenience);

        Assert.Equal(new[] { "Name" }, result.Selects!);
        Assert.Contains(
            trace.Decisions,
            d => d.FieldPath == "Salary"
                 && d.Feature == PolicyFeature.Select
                 && d.Action == PolicyAction.Dropped);
    }

    [Fact]
    public void Strict_throws_rather_than_quietly_returning_less_than_was_asked_for()
    {
        Filter filter = new() { Selects = new List<string> { "Name", "Salary" } };

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<SecuredEmployee>(filter, DwTier.Strict));

        Assert.Equal(PolicyErrorCode.FieldDeniedForSelect, exception.ErrorCode);
        Assert.Equal("Salary", exception.FieldPath);
        Assert.Equal(PolicyFeature.Select, exception.Feature);
        Assert.Equal(DwTier.Strict, exception.Tier);
    }

    [Fact]
    public void A_field_denied_for_every_feature_is_denied_for_select_too()
    {
        Filter filter = new() { Selects = new List<string> { "Name", "NationalId" } };

        (Filter result, _) = Guard<SecuredEmployee>(filter, DwTier.Convenience);

        Assert.Equal(new[] { "Name" }, result.Selects!);
    }

    [Fact]
    public void A_field_denied_only_for_where_still_projects()
    {
        // Contact.Email carries [DwNoWhere]. Gating has to be per feature, or a single denial
        // would spread across the whole query.
        Filter filter = new() { Selects = new List<string> { "Contact.Email" } };

        (Filter result, PolicyTrace trace) = Guard<SecuredEmployee>(filter, DwTier.Convenience);

        Assert.Equal(new[] { "Contact.Email" }, result.Selects!);
        Assert.Empty(trace.Decisions);
    }

    [Fact]
    public void Dropping_every_requested_field_throws_rather_than_widening_the_projection()
    {
        // An empty Selects list would reach the pipeline as a validation error, and a null one
        // would project the whole entity -- the exact inversion of what the policy asked for.
        Filter filter = new() { Selects = new List<string> { "Salary", "NationalId" } };

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<SecuredEmployee>(filter, DwTier.Convenience));

        Assert.Equal(PolicyErrorCode.AllSelectsDenied, exception.ErrorCode);
    }

    [Fact]
    public void An_allowed_projection_records_nothing_and_keeps_its_order()
    {
        Filter filter = new() { Selects = new List<string> { "Name", "Id" } };

        (Filter result, PolicyTrace trace) = Guard<SecuredEmployee>(filter, DwTier.Strict);

        Assert.Equal(new[] { "Name", "Id" }, result.Selects!);
        Assert.Empty(trace.Decisions);
    }

    [Fact]
    public void A_type_with_no_attributes_is_projected_exactly_as_asked()
    {
        Filter filter = new() { Selects = new List<string> { "Id", "Name" } };

        (Filter result, PolicyTrace trace) = Guard<PlainProduct>(filter, DwTier.Strict);

        Assert.Equal(new[] { "Id", "Name" }, result.Selects!);
        Assert.Empty(trace.Decisions);
    }

    // ---------------------------------------------------------------- synthesized projection

    [Fact]
    public void A_query_that_asks_for_no_projection_still_loses_the_denied_fields()
    {
        // Extension.ToList projects only when Selects is non-null, so leaving it null returns the
        // whole entity. Gating a list the caller never sent would enforce deny-select against
        // exactly the callers who volunteered one.
        Filter filter = new();

        (Filter result, _) = Guard<SecuredEmployee>(filter, DwTier.Convenience);

        Assert.NotNull(result.Selects);
        Assert.DoesNotContain("Salary", result.Selects!);
        Assert.DoesNotContain("NationalId", result.Selects!);
        Assert.Contains("Id", result.Selects!);
        Assert.Contains("Name", result.Selects!);
        Assert.Contains("InternalNotes", result.Selects!);
    }

    [Fact]
    public void A_type_that_denies_nothing_keeps_a_null_projection()
    {
        // The unguarded path generates no projection at all here, and the guarded one must
        // generate the same SQL. A policy layer that rewrites every query in the application is a
        // different product from one that is invisible until it has something to say.
        Filter filter = new();

        (Filter result, PolicyTrace trace) = Guard<PlainProduct>(filter, DwTier.Strict);

        Assert.Null(result.Selects);
        Assert.Empty(trace.Decisions);
    }

    [Fact]
    public void Each_field_the_synthesis_leaves_out_is_recorded()
    {
        Filter filter = new();

        (_, PolicyTrace trace) = Guard<SecuredEmployee>(filter, DwTier.Convenience);

        Assert.Contains(trace.Decisions, d => d.FieldPath == "Salary" && d.Action == PolicyAction.Dropped);
        Assert.Contains(trace.Decisions, d => d.FieldPath == "NationalId" && d.Action == PolicyAction.Dropped);
    }

    [Fact]
    public void Strict_synthesizes_rather_than_throwing_because_nothing_was_asked_for()
    {
        // Strict refuses what a caller asks for. Here the caller named no field at all, so there
        // is nothing to refuse -- and throwing would make every strict-tier query against a type
        // with any denied field fail outright.
        Filter filter = new();

        (Filter result, _) = Guard<SecuredEmployee>(filter, DwTier.Strict);

        Assert.NotNull(result.Selects);
        Assert.DoesNotContain("Salary", result.Selects!);
    }

    [Fact]
    public void Synthesis_covers_scalars_only_and_leaves_navigations_out()
    {
        // A navigation is not loaded by an unguarded ToList in the first place, and projecting one
        // whole would carry every field beneath it -- reopening the same hole one level down.
        Filter filter = new();

        (Filter result, _) = Guard<SecuredEmployee>(filter, DwTier.Convenience);

        Assert.DoesNotContain("Contact", result.Selects!);
    }

    [Fact]
    public void Denying_every_field_throws_rather_than_synthesizing_an_empty_projection()
    {
        FakePolicyProvider provider = new FakePolicyProvider()
            .Add("*", PolicyFeature.Select, PolicyEffect.Deny, PolicyLevel.SealedAttribute);

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<PlainProduct>(new Filter(), DwTier.Convenience, provider));

        Assert.Equal(PolicyErrorCode.AllSelectsDenied, exception.ErrorCode);
    }

    [Fact]
    public void An_explicit_projection_is_never_replaced_by_a_synthesized_one()
    {
        Filter filter = new() { Selects = new List<string> { "Name" } };

        (Filter result, _) = Guard<SecuredEmployee>(filter, DwTier.Convenience);

        Assert.Equal(new[] { "Name" }, result.Selects!);
    }

    // ---------------------------------------------------------------------------- order

    [Fact]
    public void Convenience_drops_an_order_field_the_policy_refuses()
    {
        // InternalNotes carries [DwDeny(Order | Group)].
        Filter filter = new()
        {
            Orders = new List<OrderBy>
            {
                new() { Sort = 1, Field = "Name" },
                new() { Sort = 2, Field = "InternalNotes" }
            }
        };

        (Filter result, PolicyTrace trace) = Guard<SecuredEmployee>(filter, DwTier.Convenience);

        Assert.Single(result.Orders!);
        Assert.Equal("Name", result.Orders![0].Field);
        Assert.Contains(
            trace.Decisions,
            d => d.FieldPath == "InternalNotes"
                 && d.Feature == PolicyFeature.Order
                 && d.Action == PolicyAction.Dropped);
    }

    [Fact]
    public void Strict_throws_on_an_order_field_the_policy_refuses()
    {
        Filter filter = new()
        {
            Orders = new List<OrderBy> { new() { Field = "InternalNotes" } }
        };

        PolicyException exception = Assert.Throws<PolicyException>(
            () => Guard<SecuredEmployee>(filter, DwTier.Strict));

        Assert.Equal(PolicyErrorCode.FieldDeniedForOrder, exception.ErrorCode);
        Assert.Equal("InternalNotes", exception.FieldPath);
    }

    [Fact]
    public void Dropping_every_order_leaves_the_query_unordered_rather_than_refused()
    {
        // Unlike a projection, an empty order list is meaningful: the pipeline returns the query
        // unchanged, so the result is simply unordered.
        Filter filter = new()
        {
            Orders = new List<OrderBy> { new() { Field = "InternalNotes" } }
        };

        (Filter result, _) = Guard<SecuredEmployee>(filter, DwTier.Convenience);

        Assert.Empty(result.Orders!);
    }

    [Fact]
    public void A_field_denied_for_order_can_still_be_projected()
    {
        Filter filter = new()
        {
            Selects = new List<string> { "InternalNotes" },
            Orders = new List<OrderBy> { new() { Field = "InternalNotes" } }
        };

        (Filter result, _) = Guard<SecuredEmployee>(filter, DwTier.Convenience);

        Assert.Equal(new[] { "InternalNotes" }, result.Selects!);
        Assert.Empty(result.Orders!);
    }
}
