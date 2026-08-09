using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Source;
using DynamicWhere.Tests.Domain;

namespace DynamicWhere.Tests;

/// <summary>
/// Covers values that carry characters with meaning to the Dynamic LINQ tokenizer.
/// </summary>
/// <remarks>
/// Values reach the query as string literals inside a generated Dynamic LINQ expression, so a raw
/// backslash or double quote in the value would otherwise end the literal early: a trailing
/// backslash escapes the closing quote and the parser runs on into the rest of the expression
/// (the production report was <c>')' or ',' expected</c> on an Arabic search term ending in
/// <c>\</c>), and a crafted value could close the literal and append predicate logic of its own.
/// Both are covered here — the parse must succeed, and the value must be matched literally.
/// </remarks>
public class EscapingTests : SalesTestBase
{
    public EscapingTests(SalesFixture fixture) : base(fixture) { }

    /// <summary>The exact term from the production report: an Arabic word ending in a backslash.</summary>
    private const string Arabic = "الثانية\\";

    private static Condition Cond(string field, DataType type, Operator op, params object[] values) =>
        new() { Sort = 1, Field = field, DataType = type, Operator = op, Values = values.ToList() };

    /// <summary>
    /// Products whose names carry every character that is significant to the tokenizer, queried in
    /// memory so the assertions reflect the library's own semantics rather than a provider collation.
    /// </summary>
    private static IQueryable<Product> Hostile => new List<Product>
    {
        new() { Name = Arabic },
        new() { Name = "الثانية" },
        new() { Name = "back\\slash" },
        new() { Name = "qu\"ote" },
        new() { Name = "both\\\"mixed" },
        new() { Name = "tab\tsep" },
        new() { Name = "plain" }
    }.AsQueryable();

    private static string[] Match(Condition condition) =>
        Hostile.Where(condition).Select(p => p.Name).ToArray();

    #region Backslash

    // The production repro: before the fix this threw ParseException from System.Linq.Dynamic.Core.
    [Fact]
    public void ContainsValueEndingInBackslash() =>
        Assert.Equal([Arabic], Match(Cond("Name", DataType.Text, Operator.Contains, Arabic)));

    [Fact]
    public void IContainsValueEndingInBackslash() =>
        Assert.Equal([Arabic], Match(Cond("Name", DataType.Text, Operator.IContains, Arabic)));

    [Fact]
    public void EqualValueEndingInBackslash() =>
        Assert.Equal([Arabic], Match(Cond("Name", DataType.Text, Operator.Equal, Arabic)));

    [Fact]
    public void EndsWithBackslash() =>
        Assert.Equal([Arabic], Match(Cond("Name", DataType.Text, Operator.EndsWith, "\\")));

    [Fact]
    public void ContainsBackslash() =>
        Assert.Equal([Arabic, "back\\slash", "both\\\"mixed"], Match(Cond("Name", DataType.Text, Operator.Contains, "\\")));

    #endregion

    #region Double quote

    [Fact]
    public void ContainsDoubleQuote() =>
        Assert.Equal(["qu\"ote", "both\\\"mixed"], Match(Cond("Name", DataType.Text, Operator.Contains, "\"")));

    [Fact]
    public void EqualValueWithDoubleQuote() =>
        Assert.Equal(["qu\"ote"], Match(Cond("Name", DataType.Text, Operator.Equal, "qu\"ote")));

    [Fact]
    public void EqualValueWithBackslashAndQuote() =>
        Assert.Equal(["both\\\"mixed"], Match(Cond("Name", DataType.Text, Operator.Equal, "both\\\"mixed")));

    #endregion

    #region Multi-value operators

    [Fact]
    public void InWithHostileValues() =>
        Assert.Equal([Arabic, "qu\"ote"], Match(Cond("Name", DataType.Text, Operator.In, Arabic, "qu\"ote")));

    [Fact]
    public void NotInWithHostileValues() =>
        Assert.DoesNotContain(Arabic, Match(Cond("Name", DataType.Text, Operator.NotIn, Arabic, "qu\"ote")));

    #endregion

    #region Expression injection

    // A value that closes the literal and appends an always-true clause would return every row.
    // Escaped, it is matched literally and selects nothing.
    [Fact]
    public void InjectedPredicateIsMatchedLiterally() =>
        Assert.Empty(Match(Cond("Name", DataType.Text, Operator.Contains, "x\") || (1==1) || Name.Contains(\"y")));

    [Fact]
    public void InjectedPredicateViaEqualIsMatchedLiterally() =>
        Assert.Empty(Match(Cond("Name", DataType.Text, Operator.Equal, "x\" || \"a\" == \"a")));

    [Fact]
    public void InjectedPredicateViaInIsMatchedLiterally() =>
        Assert.Empty(Match(Cond("Name", DataType.Text, Operator.In, "x\") || (1==1", "z")));

    // Method-call injection: Dynamic LINQ can invoke members, so an escape here is a code path,
    // not only a wrong result set.
    [Fact]
    public void InjectedMethodCallIsMatchedLiterally() =>
        Assert.Empty(Match(Cond("Name", DataType.Text, Operator.Contains, "x\").Length.ToString().Contains(\"1")));

    #endregion

    #region Round trip

    // Every operator that embeds the value in a literal must match the row it came from.
    [Theory]
    [InlineData(Operator.Equal)]
    [InlineData(Operator.IEqual)]
    [InlineData(Operator.Contains)]
    [InlineData(Operator.IContains)]
    [InlineData(Operator.StartsWith)]
    [InlineData(Operator.IStartsWith)]
    [InlineData(Operator.EndsWith)]
    [InlineData(Operator.IEndsWith)]
    [InlineData(Operator.In)]
    [InlineData(Operator.IIn)]
    public void OperatorRoundTripsHostileValue(Operator op)
    {
        foreach (string name in Hostile.Select(p => p.Name))
        {
            Assert.Contains(name, Match(Cond("Name", DataType.Text, op, name)));
        }
    }

    // Negated operators must exclude the row the value came from.
    [Theory]
    [InlineData(Operator.NotEqual)]
    [InlineData(Operator.INotEqual)]
    [InlineData(Operator.NotContains)]
    [InlineData(Operator.INotContains)]
    [InlineData(Operator.NotIn)]
    [InlineData(Operator.INotIn)]
    public void NegatedOperatorExcludesHostileValue(Operator op)
    {
        foreach (string name in Hostile.Select(p => p.Name))
        {
            Assert.DoesNotContain(name, Match(Cond("Name", DataType.Text, op, name)));
        }
    }

    #endregion

    #region Provider translation

    // The escaped literal must still translate to SQL — the seeded set holds no such name,
    // so the assertion is that the provider runs the query and matches nothing.
    [Fact]
    public void EscapedValueTranslatesToSql() =>
        Assert.Empty(Products.Where(Cond("Name", DataType.Text, Operator.Contains, Arabic)).ToList());

    [Fact]
    public void EscapedQuoteTranslatesToSql() =>
        Assert.Empty(Products.Where(Cond("Name", DataType.Text, Operator.IContains, "qu\"ote")).ToList());

    #endregion

    #region Unaffected data types

    // Enum comparisons embed the value in a literal too, and must keep working unchanged.
    [Fact]
    public void EnumEqualStillResolves() =>
        Assert.All(Orders.Where(Cond("Status", DataType.Enum, Operator.Equal, "Delivered")).ToList(),
                   o => Assert.Equal(OrderStatus.Delivered, o.Status));

    // Guid values are format-validated upstream, so escaping is a no-op on them.
    [Fact]
    public void GuidEqualStillResolves()
    {
        Product product = Products.First();

        Assert.Equal([product.Id],
                     Products.Where(Cond("Id", DataType.Guid, Operator.Equal, product.Id.ToString()))
                             .Select(p => p.Id).ToList());
    }

    #endregion
}
