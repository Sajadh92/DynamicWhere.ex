using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;
using DynamicWhere.ex.Source;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// A date the query engine refuses, on a field the caller reached through its alias.
/// </summary>
/// <remarks>
/// Policy refusals already name a field the way the caller wrote it. A date is read later, by the
/// query engine, after the sanitizer has rewritten the alias to the member it stands for — so
/// <c>LogicException.Subject</c> handed back the internal member name, which is one of the two
/// things an alias exists to keep from a caller.
/// </remarks>
public class AliasedDateSubjectTests
{
    private static readonly Ledger[] Rows = { new() { Id = 1, InternalIssuedAtUtc = new DateTime(2026, 9, 1) } };

    private static PolicyQueryable<Ledger> Guarded()
    {
        DwPolicyOptions options = new();

        options.Caps.MinGroupSize = 1;
        options.Freeze();

        return Rows.AsQueryable().ApplyPolicy(
            new DwPolicyContext(),
            options,
            new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));
    }

    private static ConditionGroup Ambiguous(string field) => new()
    {
        Conditions =
        {
            new Condition { Field = field, DataType = DataType.Date, Operator = Operator.Equal, Values = { "01/09/2026" } }
        }
    };

    [Fact]
    public void A_filter_names_the_alias_the_caller_wrote()
    {
        LogicException refused = Assert.Throws<LogicException>(
            () => Guarded().ToList(new Filter { ConditionGroup = Ambiguous("issued") }));

        Assert.Equal("AmbiguousDateFormat", refused.Message);
        Assert.Equal("issued", refused.Subject);
    }

    [Fact]
    public void A_composable_where_names_the_alias_the_caller_wrote()
    {
        LogicException refused = Assert.Throws<LogicException>(() => Guarded().Where(Ambiguous("issued")));

        Assert.Equal("issued", refused.Subject);
    }

    [Fact]
    public void A_summary_names_the_alias_the_caller_wrote()
    {
        Summary summary = new()
        {
            ConditionGroup = Ambiguous("issued"),
            GroupBy = new GroupBy
            {
                Fields = new List<string> { "Id" },
                AggregateBy = new List<AggregateBy> { new() { Field = "Id", Aggregator = Aggregator.Count, Alias = "n" } }
            }
        };

        LogicException refused = Assert.Throws<LogicException>(() => Guarded().ToList(summary));

        Assert.Equal("issued", refused.Subject);
    }

    [Fact]
    public void Outside_a_policy_the_member_is_named_as_written()
    {
        // No alias was involved, so there is nothing to hide and nothing to translate.
        LogicException refused = Assert.Throws<LogicException>(
            () => Rows.AsQueryable().Where(Ambiguous("InternalIssuedAtUtc")).ToList());

        Assert.Equal("InternalIssuedAtUtc", refused.Subject);
    }
}

/// <summary>A row whose date is published under a name other than its member's.</summary>
internal class Ledger
{
    public int Id { get; set; }

    [DwAlias("issued")]
    public DateTime InternalIssuedAtUtc { get; set; }
}
