using System.Globalization;
using System.Linq.Dynamic.Core;
using System.Linq.Dynamic.Core.Exceptions;
using System.Linq.Expressions;
using System.Reflection;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Optimization.Cache.Source;

namespace DynamicWhere.ex.Source;

/// <summary>
/// Reads a number condition value the way the predicate builder writes it: as a literal of the
/// expression parser, compared with the member the condition names.
/// </summary>
/// <remarks>
/// The builder embeds a number unquoted, exactly as sent, so whatever validation accepts the parser
/// has to read. Validation used to ask the host's culture instead, through <c>TryParse</c>, and the
/// two disagreed in both directions that matter. A thousands separator, a trailing sign, a leading
/// plus, <c>.5</c>, <c>NaN</c> and <c>Infinity</c> all passed and then failed in the parser, with its
/// own exception rather than <see cref="LogicException"/>, so a malformed request surfaced as a
/// server error. On a host whose culture writes <c>1,5</c> the same value passed on one server and
/// not on another. And a name such as <c>Infinity</c> was written into the expression as an
/// identifier, where a number was expected.
/// <para>
/// A value is read in two steps. The first is the parser's own grammar for a number, in the
/// invariant culture and within the range the parser holds an integer in. The second asks the parser
/// whether that literal compares with the member at all: <c>1.5</c> does not with an <c>int?</c>,
/// <c>1e5</c> does not with a <c>decimal</c>, and no number does with a <c>string</c>. The parser
/// is asked rather than imitated, because its promotion rules are its own; the common pairs are
/// settled without asking, and every such shortcut only ever skips the question, never answers no.
/// </para>
/// <para>
/// Nothing that ran before is refused now. Every value this refuses is one the parser refused.
/// </para>
/// </remarks>
internal static class NumberValue
{
    /// <summary>The digits an <see cref="int"/> always holds, which every numeric member compares with.</summary>
    private const int AlwaysAnInt = 9;

    /// <summary>The digits a <see cref="decimal"/> always holds.</summary>
    private const int AlwaysADecimal = 28;

    /// <summary>
    /// Refuses the values of a <c>WHERE</c> condition the predicate builder could not write a working
    /// comparison for.
    /// </summary>
    /// <param name="values">The normalized values.</param>
    /// <param name="rootType">The type being queried.</param>
    /// <param name="field">The validated path the condition names.</param>
    /// <param name="operator">The condition's operator.</param>
    /// <exception cref="LogicException">
    /// Thrown with <see cref="ErrorCode.InvalidFormat"/> when a value is not a number the parser
    /// reads, or is one it cannot compare with the member.
    /// </exception>
    internal static void Read(IReadOnlyList<string> values, Type rootType, string field, Operator @operator)
    {
        Type? memberType = null;
        bool asked = false;

        foreach (string value in values)
        {
            if (!TryShape(value, out Shape shape))
            {
                throw new LogicException(ErrorCode.InvalidFormat);
            }

            if (!Compares(@operator, out string symbol))
            {
                continue;
            }

            if (!asked)
            {
                memberType = Declared(rootType, field);
                asked = true;
            }

            if (memberType is null || Settled(shape, memberType))
            {
                continue;
            }

            if (!Parses(value, memberType, symbol))
            {
                throw new LogicException(ErrorCode.InvalidFormat);
            }
        }
    }

    /// <summary>
    /// Refuses the values of a <c>HAVING</c> condition that are not numbers the parser reads.
    /// </summary>
    /// <param name="values">The normalized values.</param>
    /// <remarks>
    /// The grammar alone. A <c>HAVING</c> condition names an aggregate alias, and what type the
    /// grouped projection gives that column is the provider's business, so there is no member to ask
    /// the parser about.
    /// </remarks>
    /// <exception cref="LogicException">
    /// Thrown with <see cref="ErrorCode.InvalidFormat"/> when a value is not a number the parser reads.
    /// </exception>
    internal static void Read(IReadOnlyList<string> values)
    {
        foreach (string value in values)
        {
            if (!TryShape(value, out _))
            {
                throw new LogicException(ErrorCode.InvalidFormat);
            }
        }
    }

    /// <summary>
    /// The type the member at the end of a path is declared with, or null when the path names none.
    /// </summary>
    /// <remarks>
    /// Declared, not unwrapped. The builder steps through a collection on the way to a member, so a
    /// collection along the path stands for its elements, and at the end of the path it stands for
    /// itself: <c>Scores == 5</c> compares a list with a number, which the parser refuses, where
    /// <c>Lines.Quantity == 5</c> compares a number with a number. The field type the cache hands out
    /// is the element's in both places, which is the right answer for a date and the wrong one here.
    /// </remarks>
    private static Type? Declared(Type rootType, string field)
    {
        string[] names = field.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Type type = rootType;

        for (int i = 0; i < names.Length; i++)
        {
            PropertyInfo? property = CacheReflection.FindProperty(type, names[i]);

            if (property is null)
            {
                return null;
            }

            type = property.PropertyType;

            if (i < names.Length - 1)
            {
                type = CacheReflection.GetCollectionElementType(type) ?? type;
            }
        }

        return names.Length == 0 ? null : type;
    }

    /// <summary>What the grammar found in a value.</summary>
    /// <param name="Negative">True when it starts with a minus sign.</param>
    /// <param name="Integer">True when it has neither a fraction nor an exponent.</param>
    /// <param name="Exponent">True when it has an exponent.</param>
    /// <param name="Digits">Its digits, less the leading zeros of the integer part.</param>
    private readonly record struct Shape(bool Negative, bool Integer, bool Exponent, int Digits);

    /// <summary>
    /// Reads a value against the parser's grammar for a number.
    /// </summary>
    /// <remarks>
    /// An optional minus, digits, an optional fraction of a point and digits, an optional exponent of
    /// <c>e</c>, a sign and digits, with the white space <c>TryParse</c> always allowed around it.
    /// A point needs a digit on both sides, and a plus sign is no part of a number: the parser has no
    /// unary plus. An integer is held in a <see cref="ulong"/>, or in a <see cref="long"/> when it is
    /// negative, and one that fits neither is refused by the parser as an invalid literal. A real
    /// number has no such bound, since one too large to hold reads as infinity.
    /// </remarks>
    private static bool TryShape(string value, out Shape shape)
    {
        shape = default;

        int end = value.Length;
        int at = 0;

        while (at < end && IsSpace(value[at]))
        {
            at++;
        }

        while (end > at && IsSpace(value[end - 1]))
        {
            end--;
        }

        int start = at;
        bool negative = at < end && value[at] == '-';

        if (negative)
        {
            at++;
        }

        int whole = Digits(value, ref at, end);

        if (whole == 0)
        {
            return false;
        }

        int leading = 0;

        while (leading < whole - 1 && value[at - whole + leading] == '0')
        {
            leading++;
        }

        int digits = whole - leading;
        bool integer = true;
        bool exponent = false;

        if (at < end && value[at] == '.')
        {
            at++;

            int fraction = Digits(value, ref at, end);

            if (fraction == 0)
            {
                return false;
            }

            digits += fraction;
            integer = false;
        }

        if (at < end && (value[at] == 'e' || value[at] == 'E'))
        {
            at++;

            if (at < end && (value[at] == '+' || value[at] == '-'))
            {
                at++;
            }

            if (Digits(value, ref at, end) == 0)
            {
                return false;
            }

            integer = false;
            exponent = true;
        }

        if (at != end)
        {
            return false;
        }

        if (integer && !InRange(value.Substring(start, end - start), negative))
        {
            return false;
        }

        shape = new Shape(negative, integer, exponent, digits);

        return true;
    }

    /// <summary>The white space a number may stand in: what <c>TryParse</c> allowed, and no more.</summary>
    private static bool IsSpace(char c) => c == ' ' || (c >= '\t' && c <= '\r');

    /// <summary>Steps over a run of ASCII digits and says how long it was.</summary>
    private static int Digits(string value, ref int at, int end)
    {
        int from = at;

        while (at < end && value[at] >= '0' && value[at] <= '9')
        {
            at++;
        }

        return at - from;
    }

    /// <summary>True when the parser can hold an integer literal.</summary>
    private static bool InRange(string literal, bool negative) => negative
        ? long.TryParse(literal, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
        : ulong.TryParse(literal, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    /// <summary>
    /// The comparison the builder writes for an operator, where it writes one with the value in it.
    /// </summary>
    /// <remarks>
    /// Two are enough. The parser promotes the operands of <c>==</c> and <c>!=</c> alike, and those of
    /// the four orderings alike, and the two families differ: a nullable enumeration equals a number
    /// and is not ordered against one.
    /// </remarks>
    private static bool Compares(Operator @operator, out string symbol)
    {
        switch (@operator)
        {
            case Operator.Equal:
            case Operator.NotEqual:
            case Operator.In:
            case Operator.NotIn:
                symbol = "==";
                return true;

            case Operator.GreaterThan:
            case Operator.GreaterThanOrEqual:
            case Operator.LessThan:
            case Operator.LessThanOrEqual:
            case Operator.Between:
            case Operator.NotBetween:
                symbol = ">";
                return true;

            default:
                symbol = string.Empty;
                return false;
        }
    }

    /// <summary>
    /// True for the pairs of literal and member the parser always compares, which are nearly every
    /// request there is.
    /// </summary>
    /// <remarks>
    /// A shortcut can only say yes, and a wrong yes is the parser's own refusal a moment later, as it
    /// was before this class. So each one is kept narrow enough to be plainly true: a real member
    /// takes any number; a decimal takes one written without an exponent that it can hold; an
    /// integral member takes an integer no longer than an <see cref="int"/> always holds, and an
    /// unsigned one takes it without a sign. An enumeration, and every member that is not a number,
    /// is left to the parser.
    /// </remarks>
    private static bool Settled(Shape shape, Type memberType)
    {
        Type type = Nullable.GetUnderlyingType(memberType) ?? memberType;

        if (type.IsEnum)
        {
            return false;
        }

        switch (Type.GetTypeCode(type))
        {
            case TypeCode.Single:
            case TypeCode.Double:
                return true;

            case TypeCode.Decimal:
                return !shape.Exponent && shape.Digits <= AlwaysADecimal;

            case TypeCode.SByte:
            case TypeCode.Int16:
            case TypeCode.Int32:
            case TypeCode.Int64:
                return shape.Integer && shape.Digits <= AlwaysAnInt;

            case TypeCode.Byte:
            case TypeCode.UInt16:
            case TypeCode.UInt32:
            case TypeCode.UInt64:
                return shape.Integer && shape.Digits <= AlwaysAnInt && !shape.Negative;

            default:
                return false;
        }
    }

    /// <summary>
    /// Asks the parser whether the literal compares with a member of the given type.
    /// </summary>
    /// <remarks>
    /// The same parser, with the same configuration, reading the same comparison the builder is
    /// about to write, against a parameter of the member's type in place of the member. What it
    /// throws for a pair it cannot compare is not one exception: a literal it cannot promote is a
    /// <see cref="ParseException"/>, an operator the type does not define is an
    /// <see cref="InvalidOperationException"/>, and an ordering against a <see cref="string"/> is an
    /// <see cref="ArgumentException"/>.
    /// </remarks>
    private static bool Parses(string value, Type memberType, string symbol)
    {
        try
        {
            ParameterExpression member = Expression.Parameter(memberType, "member");

            DynamicExpressionParser.ParseLambda(
                DynamicLinq.Config, new[] { member }, typeof(bool), $"member {symbol} {value}");

            return true;
        }
        catch (ParseException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
