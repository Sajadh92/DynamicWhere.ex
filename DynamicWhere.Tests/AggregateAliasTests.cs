using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Source;
using DynamicWhere.Tests.Domain;

namespace DynamicWhere.Tests;

/// <summary>
/// Covers <see cref="AggregateBy.Alias"/> values that carry characters with meaning to the
/// Dynamic LINQ parser.
/// </summary>
/// <remarks>
/// The alias is emitted verbatim into the generated <c>Select</c> projection as <c>… as {alias}</c>.
/// An alias holding a comma therefore appended projection terms of its own — the group could be made
/// to return columns the caller never asked for. Aliases carrying any other separator (a space, a
/// dash) never parsed in the first place, so requiring a plain identifier rejects nothing that worked.
/// </remarks>
public class AggregateAliasTests : SalesTestBase
{
    public AggregateAliasTests(SalesFixture fixture) : base(fixture) { }

    private static GroupBy Grouped(string alias) => new()
    {
        Fields = ["Status"],
        AggregateBy = [new AggregateBy { Aggregator = Aggregator.Count, Alias = alias }]
    };

    private string[] Columns(string alias) =>
        Orders.Group(Grouped(alias)).ElementType.GetProperties().Select(p => p.Name).ToArray();

    [Fact]
    public void PlainAliasProjectsOneColumn() => Assert.Contains("Total", Columns("Total"));

    [Fact]
    public void UnderscoreAliasIsAccepted() => Assert.Contains("Total_Sales", Columns("Total_Sales"));

    [Fact]
    public void DigitsAfterFirstCharacterAreAccepted() => Assert.Contains("Total2", Columns("Total2"));

    // Dynamic LINQ matches identifiers by Unicode category, so a non-Latin alias stays valid.
    [Fact]
    public void NonLatinAliasIsAccepted() => Assert.Contains("المجموع", Columns("المجموع"));

    [Theory]
    [InlineData("Total, 1 as Leaked")]           // appended a projection term
    [InlineData("Total, Key as Leaked")]         // appended the grouping key
    [InlineData("Total Sales")]                  // never parsed
    [InlineData("Total-Sales")]                  // never parsed
    [InlineData("Total.Sales")]                  // rejected before, still rejected
    [InlineData("1Total")]                       // identifiers cannot start with a digit
    [InlineData("")]
    [InlineData("   ")]
    public void MalformedAliasIsRejected(string alias) =>
        Assert.Throws<LogicException>(() => Orders.Group(Grouped(alias)));
}
