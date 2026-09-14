using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
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
/// Covers <c>[DwEntity(RequirePolicy = true)]</c>.
/// </summary>
/// <remarks>
/// This is what makes the opt-in handle a boundary rather than a suggestion. Without it, every
/// field policy on a type is bypassed by not calling <c>ApplyPolicy</c>, and nothing in a code
/// review distinguishes the query that forgot from the query that never needed one.
/// </remarks>
public class RequirePolicyTests
{
    private static IQueryable<SecuredEmployee> Secured() => new List<SecuredEmployee>
    {
        new() { Id = 1, Name = "Ada", NationalId = "AAA" }
    }.AsQueryable();

    private static IQueryable<PlainProduct> Plain() => new List<PlainProduct>
    {
        new() { Id = 1, Name = "Widget" }
    }.AsQueryable();

    private static DwPolicyContext Caller() =>
        new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");

    [Fact]
    public void An_unguarded_query_on_a_type_that_requires_policy_is_refused()
    {
        PolicyException exception = Assert.Throws<PolicyException>(
            () => Secured().ToList(new Filter()));

        Assert.Equal(PolicyErrorCode.PolicyRequired, exception.ErrorCode);
        Assert.Equal(nameof(SecuredEmployee), exception.FieldPath);
    }

    [Fact]
    public void The_same_query_through_the_handle_succeeds()
    {
        var result = Secured().ApplyPolicy(Caller()).ToList(new Filter());

        Assert.Single(result.Data);
    }

    [Fact]
    public void The_requirement_covers_the_composable_methods_too()
    {
        // Guarding only the terminal calls would leave the boundary open: every one of these
        // returns an IQueryable the caller can enumerate with plain LINQ.
        Assert.Throws<PolicyException>(() => Secured().Select(new List<string> { "Name" }));
        Assert.Throws<PolicyException>(() => Secured().Order(new OrderBy { Field = "Name" }));
        Assert.Throws<PolicyException>(() => Secured().Page(new PageBy { PageNumber = 1, PageSize = 1 }));
        Assert.Throws<PolicyException>(() => Secured().Filter(new Filter()));
        Assert.Throws<PolicyException>(() => Secured().Where(new ConditionGroup()));
    }

    [Fact]
    public void A_type_that_does_not_require_policy_is_unaffected()
    {
        var result = Plain().ToList(new Filter());

        Assert.Single(result.Data);
    }

    [Fact]
    public void The_scope_does_not_survive_the_call_that_opened_it()
    {
        // A scope that leaked would leave every later unguarded query on this call context silently
        // unguarded, which is the failure mode worth testing rather than assuming.
        Secured().ApplyPolicy(Caller()).ToList(new Filter());

        Assert.Throws<PolicyException>(() => Secured().ToList(new Filter()));
    }

    [Fact]
    public void The_scope_is_restored_even_when_the_guarded_call_throws()
    {
        Filter refused = new()
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

        Assert.Throws<PolicyException>(() => Secured().ApplyPolicy(Caller()).ToList(refused));

        PolicyException after = Assert.Throws<PolicyException>(() => Secured().ToList(new Filter()));

        Assert.Equal(PolicyErrorCode.PolicyRequired, after.ErrorCode);
    }

    [Fact]
    public async Task The_scope_survives_an_await_and_does_not_outlive_it()
    {
        // An await can resume on a different thread, so a thread-static flag would drop the scope
        // mid-call and refuse the guarded query it was opened for.
        await Task.Yield();

        var result = Secured().ApplyPolicy(Caller()).ToList(new Filter());

        Assert.Single(result.Data);

        await Task.Yield();

        Assert.Throws<PolicyException>(() => Secured().ToList(new Filter()));
    }

    [Fact]
    public void The_refusal_names_the_attribute_that_caused_it()
    {
        PolicyException exception = Assert.Throws<PolicyException>(
            () => Secured().ToList(new Filter()));

        Assert.Contains("RequirePolicy", exception.SourceOrigin!, StringComparison.Ordinal);
    }
}
