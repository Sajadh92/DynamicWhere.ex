using System.Globalization;
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Source;

namespace DynamicWhere.Tests;

/// <summary>
/// A number value is read the way the predicate builder writes it: as a literal of the expression
/// parser, in the invariant culture, compared with the member the condition names.
/// </summary>
/// <remarks>
/// Validation used to ask the host's culture through <c>TryParse</c>, so a value could pass it and
/// then fail in the parser with the parser's own exception, which a host maps to a server error.
/// Every test here runs the whole pipeline, so it holds the validator and the builder to one answer.
/// </remarks>
public class NumberValueTests
{
    /// <summary>A shade, for the members a number compares with through its underlying type.</summary>
    public enum Shade
    {
        /// <summary>One.</summary>
        Dark = 1,

        /// <summary>Two.</summary>
        Light = 2
    }

    /// <summary>One member of every type a number condition can name, and of several it cannot.</summary>
    public class Row
    {
        public byte B { get; set; }
        public sbyte Sb { get; set; }
        public short S { get; set; }
        public ushort Us { get; set; }
        public int I { get; set; }
        public uint Ui { get; set; }
        public long L { get; set; }
        public ulong Ul { get; set; }
        public float F { get; set; }
        public double D { get; set; }
        public decimal M { get; set; }
        public byte? Bn { get; set; }
        public sbyte? Sbn { get; set; }
        public short? Sn { get; set; }
        public ushort? Usn { get; set; }
        public int? In { get; set; }
        public uint? Uin { get; set; }
        public long? Ln { get; set; }
        public ulong? Uln { get; set; }
        public float? Fn { get; set; }
        public double? Dn { get; set; }
        public decimal? Mn { get; set; }
        public Shade E { get; set; }
        public Shade? En { get; set; }
        public string Text { get; set; } = "x";
        public bool Flag { get; set; }
        public Guid G { get; set; }
        public DateTime When { get; set; }
        public char C { get; set; } = 'c';
        public List<int> Scores { get; set; } = new() { 1 };
        public List<Child> Kids { get; set; } = new() { new Child() };
    }

    /// <summary>An element, so a path through a collection is covered.</summary>
    public class Child
    {
        public int Qty { get; set; }
        public decimal? Cost { get; set; }
    }

    /// <summary>A row with members named as the two values a number parser also reads as names.</summary>
    public class Named
    {
        public int Id { get; set; }
        public double Ratio { get; set; }
        public double Infinity { get; set; }
        public double NaN { get; set; }
    }

    private static readonly Operator[] Comparisons =
    {
        Operator.Equal, Operator.NotEqual, Operator.GreaterThan, Operator.GreaterThanOrEqual, Operator.LessThan,
        Operator.LessThanOrEqual, Operator.In, Operator.NotIn, Operator.Between, Operator.NotBetween
    };

    private static Filter Where(string field, Operator op, params object[] values)
    {
        Condition condition = new() { Sort = 1, Field = field, DataType = DataType.Number, Operator = op };

        condition.Values.AddRange(values);

        if (op is Operator.Between or Operator.NotBetween && values.Length == 1)
        {
            condition.Values.Add(values[0]);
        }

        return new Filter { ConditionGroup = new ConditionGroup { Connector = Connector.And, Conditions = { condition } } };
    }

    private static void UnderCulture(string name, Action act)
    {
        CultureInfo saved = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            act();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    /// <summary>The values the host's <c>TryParse</c> accepted and the parser then refused.</summary>
    public static TheoryData<string> Unreadable()
    {
        TheoryData<string> data = new()
        {
            "+5", "5-", "5+", "1,000", "1,5", ".5", "5.", "-.5", "1.e5", "1e", "e5", "1.5e", "NaN", "nan", "Infinity",
            "-Infinity", "infinity", "99999999999999999999", "18446744073709551616", "-9223372036854775809",
            "79228162514264337593543950336", "(5)", "1_000", "1 000", "1'000", "--5", "1.5.5", "", " ", "abc"
        };

        // Built from code points, so no such character stands in this file: a no-break space as a
        // thousands separator, the minus sign several cultures write, an infinity sign, and a digit
        // that is not an ASCII one.
        data.Add("1" + (char)0x00A0 + "000");
        data.Add((char)0x2212 + "5");
        data.Add(((char)0x221E).ToString());
        data.Add(((char)0x0665).ToString());

        return data;
    }

    [Theory]
    [MemberData(nameof(Unreadable))]
    public void A_value_the_parser_cannot_read_is_a_format_error(string value)
    {
        foreach (string field in new[] { "I", "D", "M", "Ln" })
        {
            LogicException refusal = Assert.Throws<LogicException>(
                () => new List<Row> { new() }.AsQueryable().ToList(Where(field, Operator.Equal, value)));

            Assert.Equal(ErrorCode.InvalidFormat, refusal.Message);
        }
    }

    [Theory]
    [InlineData("5")]
    [InlineData("-5")]
    [InlineData(" 5 ")]
    [InlineData("\t5")]
    [InlineData("5\n")]
    [InlineData("00005")]
    [InlineData("-0")]
    [InlineData("1.5")]
    [InlineData("1e5")]
    [InlineData("1E+20")]
    [InlineData("1e400")]
    [InlineData("18446744073709551615")]
    [InlineData("-9223372036854775808")]
    public void A_value_the_parser_reads_runs(string value) =>
        Assert.NotNull(new List<Row> { new() }.AsQueryable().ToList(Where("D", Operator.Equal, value)).Data);

    /// <summary>What <c>TryParse</c> never accepted stays refused, though the parser has a reading for it.</summary>
    [Theory]
    [InlineData("5L")]
    [InlineData("5m")]
    [InlineData("5.0m")]
    [InlineData("5f")]
    [InlineData("5d")]
    [InlineData("0x1F")]
    [InlineData("- 5")]
    public void Nothing_is_accepted_that_was_not(string value)
    {
        LogicException refusal = Assert.Throws<LogicException>(
            () => new List<Row> { new() }.AsQueryable().ToList(Where("D", Operator.Equal, value)));

        Assert.Equal(ErrorCode.InvalidFormat, refusal.Message);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("sv-SE")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    [InlineData("en-US")]
    public void The_culture_of_the_host_decides_nothing(string culture) => UnderCulture(culture, () =>
    {
        List<Row> rows = new() { new Row { D = 1.5, M = 1.5m }, new Row { D = 15, M = 15m } };

        // A point is the decimal separator on every host, and the row that holds 1.5 is the one found.
        Assert.Equal(1.5, Assert.Single(rows.AsQueryable().ToList(Where("D", Operator.Equal, "1.5")).Data!).D);
        Assert.Equal(1.5m, Assert.Single(rows.AsQueryable().ToList(Where("M", Operator.Equal, "1.5")).Data!).M);

        // A comma is not one on any host.
        Assert.Equal(
            ErrorCode.InvalidFormat,
            Assert.Throws<LogicException>(() => rows.AsQueryable().ToList(Where("D", Operator.Equal, "1,5"))).Message);

        // A number placed in Values from code is written in the invariant culture.
        Assert.Equal(1.5, Assert.Single(rows.AsQueryable().ToList(Where("D", Operator.Equal, 1.5)).Data!).D);
        Assert.Equal(1.5m, Assert.Single(rows.AsQueryable().ToList(Where("M", Operator.Equal, 1.5m)).Data!).M);
    });

    /// <summary>
    /// A value is never written into the expression as a name. <c>Infinity</c> and <c>NaN</c> passed
    /// the old check as numbers, and on a type with a member of that name the condition compared two
    /// columns.
    /// </summary>
    [Theory]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    [InlineData("infinity")]
    [InlineData("nan")]
    public void A_value_is_never_read_as_a_member(string value)
    {
        List<Named> rows = new() { new Named { Id = 1, Ratio = 7, Infinity = 7, NaN = 7 } };

        LogicException refusal = Assert.Throws<LogicException>(
            () => rows.AsQueryable().ToList(Where("Ratio", Operator.Equal, value)));

        Assert.Equal(ErrorCode.InvalidFormat, refusal.Message);
    }

    [Fact]
    public void One_unreadable_value_among_several_refuses_the_condition()
    {
        List<Row> rows = new() { new Row() };

        Assert.Throws<LogicException>(() => rows.AsQueryable().ToList(Where("I", Operator.In, 1, "2,0", 3)));
        Assert.Throws<LogicException>(() => rows.AsQueryable().ToList(Where("I", Operator.Between, 1, "NaN")));
        Assert.NotNull(rows.AsQueryable().ToList(Where("I", Operator.In, 1, "2", 3.0)).Data);
    }

    /// <summary>
    /// Over every kind of member, literal and comparison: validation accepts exactly what the parser
    /// compares, and what it refuses it refuses as a format error, never with the parser's exception.
    /// </summary>
    /// <remarks>
    /// The expectation is worked out here, by asking the parser about a parameter of the member's
    /// type, found by reflection. It shares nothing with the library's reader but the parser itself,
    /// so the shortcuts that reader takes, the type it is handed for a path through a collection and
    /// the exceptions it maps are all held to the parser's answer.
    /// </remarks>
    [Fact]
    public void Validation_accepts_exactly_what_the_parser_compares()
    {
        string[] literals =
        {
            "5", "-5", "300", "-300", "70000", "999999999", "1000000000", "3000000000", "5000000000", "-5000000000",
            "9223372036854775808", "18446744073709551615", "1.5", "-1.5", "1e5", "1E-5", "1.5e3", "0.1", "00005", " 5 ",
            "0", "-0", "1E+20", "1e400", "0.0000000000000000000000000001", "79228162514264337593543950335.5",
            "1234567890123456789012345678", "12345678901234567890123456789", "1.0", "5.0"
        };

        string[] fields = typeof(Row).GetProperties().Select(p => p.Name).Where(n => n != "Kids")
            .Concat(new[] { "Kids.Qty", "Kids.Cost" }).ToArray();

        List<Row> rows = new() { new Row() };
        List<string> wrong = new();

        foreach (string field in fields)
        {
            Type memberType = field.StartsWith("Kids.", StringComparison.Ordinal)
                ? typeof(Child).GetProperty(field.Substring(5))!.PropertyType
                : typeof(Row).GetProperty(field)!.PropertyType;

            foreach (string literal in literals)
            {
                foreach (Operator op in Comparisons)
                {
                    bool expected = ParserCompares(memberType, op, literal);
                    string actual;

                    try
                    {
                        _ = rows.AsQueryable().ToList(Where(field, op, literal)).Data!.Count;
                        actual = "ran";
                    }
                    catch (LogicException refusal) when (refusal.Message == ErrorCode.InvalidFormat)
                    {
                        actual = "refused";
                    }
                    catch (Exception other)
                    {
                        actual = other.GetType().Name;
                    }

                    if (actual != (expected ? "ran" : "refused"))
                    {
                        wrong.Add($"{field} {op} [{literal}]: expected {(expected ? "ran" : "refused")}, got {actual}");
                    }
                }
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong.Take(40)));
    }

    private static bool ParserCompares(Type memberType, Operator op, string literal)
    {
        string symbol = op switch
        {
            Operator.Equal or Operator.In => "==",
            Operator.NotEqual or Operator.NotIn => "!=",
            Operator.GreaterThan => ">",
            Operator.GreaterThanOrEqual or Operator.Between => ">=",
            Operator.LessThan or Operator.NotBetween => "<",
            _ => "<="
        };

        try
        {
            ParameterExpression x = Expression.Parameter(memberType, "x");

            DynamicExpressionParser.ParseLambda(DynamicLinq.Config, new[] { x }, typeof(bool), $"x {symbol} {literal}");

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ------------------------------------------------------------------ having

    private static Summary Having(object value) => new()
    {
        GroupBy = new GroupBy
        {
            Fields = { "Text" },
            AggregateBy = { new AggregateBy { Field = "I", Aggregator = Aggregator.Sumation, Alias = "total" } }
        },
        Having = new ConditionGroup
        {
            Connector = Connector.And,
            Conditions =
            {
                new Condition { Sort = 1, Field = "total", DataType = DataType.Number, Operator = Operator.GreaterThanOrEqual, Values = { value } }
            }
        }
    };

    [Theory]
    [InlineData("1,000")]
    [InlineData("NaN")]
    [InlineData("+5")]
    [InlineData("5-")]
    public void A_having_value_the_parser_cannot_read_is_a_format_error(string value)
    {
        LogicException refusal = Assert.Throws<LogicException>(
            () => new List<Row> { new() { I = 3 } }.AsQueryable().ToList(Having(value)));

        Assert.Equal(ErrorCode.InvalidFormat, refusal.Message);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    public void A_having_value_is_read_the_same_on_every_host(string culture) => UnderCulture(culture, () =>
    {
        List<Row> rows = new() { new Row { I = 3 }, new Row { I = 4 } };

        Assert.Single(rows.AsQueryable().ToList(Having("6.5")).Data!);
        Assert.Empty(rows.AsQueryable().ToList(Having("7.5")).Data!);
        Assert.Throws<LogicException>(() => rows.AsQueryable().ToList(Having("6,5")));
    });
}
