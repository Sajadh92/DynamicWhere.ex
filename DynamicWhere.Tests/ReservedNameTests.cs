using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Policies.Validation;
using DynamicWhere.ex.Source;

namespace DynamicWhere.Tests;

/// <summary>A row carrying a member for every name the expression parser keeps, and some it does not.</summary>
public class ReservedRow
{
    public int Id { get; set; }

    public string New { get; set; } = "x";
    public string Iif { get; set; } = "x";
    public string Np { get; set; } = "x";
    public string IsNull { get; set; } = "x";
    public string Is { get; set; } = "x";
    public string As { get; set; } = "x";
    public string Cast { get; set; } = "x";
    public string True { get; set; } = "x";
    public string False { get; set; } = "x";
    public string Null { get; set; } = "x";

    public string It { get; set; } = "x";
    public string Root { get; set; } = "x";
    public string Parent { get; set; } = "x";
    public string String { get; set; } = "x";
    public string Math { get; set; } = "x";
    public string Guid { get; set; } = "x";
    public string Uri { get; set; } = "x";
    public string DateTime { get; set; } = "x";

    public ReservedNested Nested { get; set; } = new();
}

public class ReservedNested
{
    public int Id { get; set; }

    public string New { get; set; } = "x";
    public string Null { get; set; } = "x";
}

/// <summary>A default order naming a member the parser keeps, which the startup scan reports.</summary>
[DwEntity(DefaultOrder = "Null desc, Id")]
public class ReservedOrderRow
{
    public int Id { get; set; }

    public string Null { get; set; } = "x";
}

/// <summary>
/// A member named after one of the parser's own words is refused by name, in every clause.
/// </summary>
/// <remarks>
/// The parser reads its functions and literals before it looks for a member, so such a field never
/// reaches the query: seven of the names raised its <c>ParseException</c>, <c>True</c> and <c>False</c>
/// an <c>InvalidOperationException</c> from the comparison built for them, and <c>Null</c> was read as
/// the null literal, which returned no rows and no error at all. The library now answers for itself.
/// </remarks>
public sealed class ReservedNameTests
{
    public static readonly TheoryData<string> Reserved = new()
    {
        "New", "Iif", "Np", "IsNull", "Is", "As", "Cast", "True", "False", "Null",
    };

    private static readonly IQueryable<ReservedRow> Rows = new[] { new ReservedRow { Id = 1 } }.AsQueryable();

    private static Filter Where(string field) => new()
    {
        ConditionGroup = new ConditionGroup
        {
            Conditions = new List<Condition>
            {
                new()
                {
                    Sort = 1, Field = field, DataType = DataType.Text, Operator = Operator.Equal,
                    Values = new List<object> { "x" },
                },
            },
        },
    };

    [Theory]
    [MemberData(nameof(Reserved))]
    public void A_condition_on_a_name_the_parser_keeps_is_refused(string field)
    {
        LogicException refused = Assert.Throws<LogicException>(() => Rows.ToList(Where(field)));

        Assert.Equal($"FieldPath[{field}]StartsWithReservedName", refused.Message);
        Assert.Equal(field, refused.Subject);
    }

    [Theory]
    [MemberData(nameof(Reserved))]
    public void Every_clause_refuses_it_alike(string field)
    {
        Filter ordered = new() { Orders = new List<OrderBy> { new() { Sort = 1, Field = field } } };
        Filter projected = new() { Selects = new List<string> { "Id", field } };
        Summary grouped = new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { field },
                AggregateBy = new List<AggregateBy> { new() { Aggregator = Aggregator.Count, Alias = "Rows" } },
            },
        };
        Summary aggregated = new()
        {
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Id" },
                AggregateBy = new List<AggregateBy>
                {
                    new() { Aggregator = Aggregator.Minimum, Field = field, Alias = "Least" },
                },
            },
        };

        Assert.Throws<LogicException>(() => Rows.ToList(ordered));
        Assert.Throws<LogicException>(() => Rows.ToList(projected));
        Assert.Throws<LogicException>(() => Rows.ToList(grouped));
        Assert.Throws<LogicException>(() => Rows.ToList(aggregated));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("NULL")]
    [InlineData("nEw")]
    [InlineData(" Null ")]
    public void The_letter_case_and_the_spacing_do_not_matter(string field) =>
        Assert.Throws<LogicException>(() => Rows.ToList(Where(field)));

    [Theory]
    [InlineData("Nested.New")]
    [InlineData("Nested.Null")]
    public void Only_the_first_segment_is_the_parser_s(string field)
    {
        // After a dot the parser looks for a member, so a navigation may carry any name.
        Assert.Single(Rows.ToList(Where(field)).Data);
    }

    [Theory]
    [InlineData("It")]
    [InlineData("Root")]
    [InlineData("Parent")]
    [InlineData("String")]
    [InlineData("Math")]
    [InlineData("Guid")]
    [InlineData("Uri")]
    [InlineData("DateTime")]
    public void A_name_the_parser_does_not_keep_is_a_member_like_any_other(string field)
    {
        // The context keywords are off since 3.1.0, and a predefined type name is read as a member.
        Assert.Single(Rows.ToList(Where(field)).Data);
    }

    [Fact]
    public void A_guarded_query_refuses_it_by_its_tier()
    {
        DwPolicyContext caller = new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1");
        PolicyResolver resolver = new(new IDwPolicyProvider[] { new AttributePolicyProvider() });

        LogicException convenience = Assert.Throws<LogicException>(
            () => Rows.ApplyPolicy(caller, new DwPolicyOptions { Tier = DwTier.Convenience }, resolver)
                .ToList(Where("Null")));

        // The strict tier answers every name it cannot use alike, as it does one that matches nothing:
        // the refusal says which clause, and nothing else.
        PolicyException strict = Assert.Throws<PolicyException>(
            () => Rows.ApplyPolicy(caller, new DwPolicyOptions { Tier = DwTier.Strict }, resolver)
                .ToList(Where("Null")));

        Assert.Equal("FieldPath[Null]StartsWithReservedName", convenience.Message);
        Assert.Equal(PolicyErrorCode.FieldDeniedForWhere, strict.ErrorCode);
        Assert.Equal("*", strict.FieldPath);
    }

    [Fact]
    public void A_default_order_naming_one_fails_the_startup_scan() =>
        Assert.Contains(
            "ReservedOrderRow: DefaultOrder names 'Null', which starts with a name the expression parser "
            + "keeps for itself, so no query can use it. Rename the member.",
            PolicyModelValidator.Inspect(new[] { typeof(ReservedOrderRow) }).Errors);
}
