using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;

namespace DynamicWhere.ex.Source;

/// <summary>
/// Builds EF Core–translatable predicate snippets from (<see cref="DataType"/>, <see cref="Operator"/>, field, values).
/// <para>
/// <b>Text comparisons</b>:
/// <list type="bullet">
///   <item><description>Uses C# string operations (<c>==</c>, <c>Contains</c>, <c>StartsWith</c>, <c>EndsWith</c>).</description></item>
///   <item><description><c>I*</c> operators (e.g., <see cref="Operator.IContains"/>) are case-insensitive by normalizing both sides with <c>ToLower()</c>.</description></item>
/// </list>
/// </para>
/// <para>
/// Returns a C# dynamic LINQ expression string (not SQL) intended for libraries like System.Linq.Dynamic.Core.
/// </para>
/// </summary>
internal static class Builder
{
    /// <summary>
    /// Builds a dynamic LINQ predicate snippet for the provided <see cref="DataType"/> and <see cref="Operator"/>.
    /// The returned value is a C# expression string (not SQL) suitable for libraries like System.Linq.Dynamic.Core.
    /// For text comparisons, the expression uses <c>==</c>/<c>!=</c> and string methods (<c>Contains</c>, <c>StartsWith</c>, <c>EndsWith</c>).
    /// Case-insensitive (<c>I*</c>) operators normalize both sides via <c>ToLower()</c>.
    /// </summary>
    /// <param name="dataType">Logical data type of the field.</param>
    /// <param name="_operator">Operator to apply.</param>
    /// <param name="field">Field access expression (e.g., <c>"x.Email"</c>).</param>
    /// <param name="values">Operator values (validated upstream; count depends on <paramref name="_operator"/>).</param>
    /// <param name="memberType">
    /// The CLR type of the member the field resolves to, or null where the caller has none to give —
    /// a <c>HAVING</c> clause names an aggregate alias rather than a member. Only the date types read
    /// it, and only to decide what a null guard, a literal and <c>.Date</c> have to look like.
    /// </param>
    /// <returns>Dynamic LINQ predicate snippet.</returns>
    /// <exception cref="LogicException">Thrown if the combination of <paramref name="dataType"/> and <paramref name="_operator"/> is unsupported.</exception>
    public static string BuildCondition(
        DataType dataType, Operator _operator, string field, List<string> values, Type? memberType = null)
    {
        // Normalize value tokens by trimming whitespace; validation already happened upstream.
        // Escape them for embedding in a dynamic LINQ string literal — see Escape.
        values = values.Select(v => Escape(v.Trim())).ToList();

        switch (dataType)
        {
            // ----------------------------------------------------------
            // TEXT
            // ----------------------------------------------------------
            case DataType.Text:
            {
                switch (_operator)
                {
                    case Operator.Equal:
                        return $"{field} != null && {field} == \"{values[0]}\"";

                    case Operator.IEqual:
                        return $"{field} != null && {field}.ToLower() == \"{values[0].ToLower()}\"";

                    case Operator.NotEqual:
                        return $"{field} != null && {field} != \"{values[0]}\"";

                    case Operator.INotEqual:
                        return $"{field} != null && {field}.ToLower() != \"{values[0].ToLower()}\"";

                    case Operator.Contains:
                        return $"{field} != null && {field}.Contains(\"{values[0]}\")";

                    case Operator.IContains:
                        return $"{field} != null && {field}.ToLower().Contains(\"{values[0].ToLower()}\")";

                    case Operator.NotContains:
                        return $"{field} != null && !{field}.Contains(\"{values[0]}\")";

                    case Operator.INotContains:
                        return $"{field} != null && !{field}.ToLower().Contains(\"{values[0].ToLower()}\")";

                    case Operator.StartsWith:
                        return $"{field} != null && {field}.StartsWith(\"{values[0]}\")";

                    case Operator.IStartsWith:
                        return $"{field} != null && {field}.ToLower().StartsWith(\"{values[0].ToLower()}\")";

                    case Operator.EndsWith:
                        return $"{field} != null && {field}.EndsWith(\"{values[0]}\")";

                    case Operator.IEndsWith:
                        return $"{field} != null && {field}.ToLower().EndsWith(\"{values[0].ToLower()}\")";

                    case Operator.NotStartsWith:
                        return $"{field} != null && !{field}.StartsWith(\"{values[0]}\")";

                    case Operator.INotStartsWith:
                        return $"{field} != null && !{field}.ToLower().StartsWith(\"{values[0].ToLower()}\")";

                    case Operator.NotEndsWith:
                        return $"{field} != null && !{field}.EndsWith(\"{values[0]}\")";

                    case Operator.INotEndsWith:
                        return $"{field} != null && !{field}.ToLower().EndsWith(\"{values[0].ToLower()}\")";

                    case Operator.In:
                    {
                        var ors = values.Select(v => $"{field} == \"{v}\"");
                        return $"{field} != null && ({string.Join(" || ", ors)})";
                    }

                    case Operator.IIn:
                    {
                        var ors = values.Select(v => $"{field}.ToLower() == \"{v.ToLower()}\"");
                        return $"{field} != null && ({string.Join(" || ", ors)})";
                    }

                    case Operator.NotIn:
                    {
                        var ands = values.Select(v => $"{field} != \"{v}\"");
                        return $"{field} != null && ({string.Join(" && ", ands)})";
                    }

                    case Operator.INotIn:
                    {
                        var ands = values.Select(v => $"{field}.ToLower() != \"{v.ToLower()}\"");
                        return $"{field} != null && ({string.Join(" && ", ands)})";
                    }

                    case Operator.IsNull: return $"{field} == null";
                    case Operator.IsNotNull: return $"{field} != null";
                }
                break;
            }

            // ----------------------------------------------------------
            // GUID (exact string semantics)
            // ----------------------------------------------------------
            case DataType.Guid:
            {
                switch (_operator)
                {
                    case Operator.Equal:
                        return $"{field} != null && {field} == \"{values[0]}\"";

                    case Operator.NotEqual:
                        return $"{field} != null && {field} != \"{values[0]}\"";

                    case Operator.In:
                    {
                        var ors = values.Select(v => $"{field} == \"{v}\"");
                        return $"{field} != null && ({string.Join(" || ", ors)})";
                    }

                    case Operator.NotIn:
                    {
                        var ands = values.Select(v => $"{field} != \"{v}\"");
                        return $"{field} != null && ({string.Join(" && ", ands)})";
                    }

                    case Operator.IsNull: return $"{field} == null";
                    case Operator.IsNotNull: return $"{field} != null";
                }
                break;
            }

            // ----------------------------------------------------------
            // NUMBER
            // ----------------------------------------------------------
            case DataType.Number:
            {
                switch (_operator)
                {
                    case Operator.Equal: return $"{field} != null && {field} == {values[0]}";
                    case Operator.NotEqual: return $"{field} != null && {field} != {values[0]}";
                    case Operator.GreaterThan: return $"{field} != null && {field} > {values[0]}";
                    case Operator.GreaterThanOrEqual: return $"{field} != null && {field} >= {values[0]}";
                    case Operator.LessThan: return $"{field} != null && {field} < {values[0]}";
                    case Operator.LessThanOrEqual: return $"{field} != null && {field} <= {values[0]}";

                    case Operator.In:
                    {
                        var ors = values.Select(v => $"{field} == {v}");
                        return $"{field} != null && ({string.Join(" || ", ors)})";
                    }

                    case Operator.NotIn:
                    {
                        var ands = values.Select(v => $"{field} != {v}");
                        return $"{field} != null && ({string.Join(" && ", ands)})";
                    }

                    case Operator.Between:
                        return $"{field} != null && {field} >= {values[0]} && {field} <= {values[1]}";

                    case Operator.NotBetween:
                        return $"{field} != null && ({field} < {values[0]} || {field} > {values[1]})";

                    case Operator.IsNull: return $"{field} == null";
                    case Operator.IsNotNull: return $"{field} != null";
                }
            }
            break;

            // ----------------------------------------------------------
            // BOOLEAN
            // ----------------------------------------------------------
            case DataType.Boolean:
            {
                switch (_operator)
                {
                    case Operator.Equal: return $"{field} != null && {field} == {values[0]}";
                    case Operator.NotEqual: return $"{field} != null && {field} != {values[0]}";
                    case Operator.IsNull: return $"{field} == null";
                    case Operator.IsNotNull: return $"{field} != null";
                }
            }
            break;

            // ----------------------------------------------------------
            // DATETIME (expect ISO 8601 strings)
            // ----------------------------------------------------------
            case DataType.DateTime:
                return BuildDate(dataType, _operator, field, values, memberType);

            // ----------------------------------------------------------
            // DATE (compare by .Date)
            // ----------------------------------------------------------
            case DataType.Date:
                return BuildDate(dataType, _operator, field, values, memberType);

            // ----------------------------------------------------------
            // Enum
            // ----------------------------------------------------------
            case DataType.Enum:
            {
                switch (_operator)
                {
                    case Operator.Equal:
                        return $"{field} != null && {field} == \"{values[0]}\"";

                    case Operator.NotEqual:
                        return $"{field} != null && {field} != \"{values[0]}\"";

                    case Operator.Contains:
                        return $"{field} != null && {field}.Contains(\"{values[0]}\")";

                    case Operator.NotContains:
                        return $"{field} != null && !{field}.Contains(\"{values[0]}\")";

                    case Operator.StartsWith:
                        return $"{field} != null && {field}.StartsWith(\"{values[0]}\")";

                    case Operator.EndsWith:
                        return $"{field} != null && {field}.EndsWith(\"{values[0]}\")";

                    case Operator.NotStartsWith:
                        return $"{field} != null && !{field}.StartsWith(\"{values[0]}\")";

                    case Operator.NotEndsWith:
                        return $"{field} != null && !{field}.EndsWith(\"{values[0]}\")";

                    case Operator.In:
                    {
                        var ors = values.Select(v => $"{field} == \"{v}\"");
                        return $"{field} != null && ({string.Join(" || ", ors)})";
                    }

                    case Operator.NotIn:
                    {
                        var ands = values.Select(v => $"{field} != \"{v}\"");
                        return $"{field} != null && ({string.Join(" && ", ands)})";
                    }

                    case Operator.IsNull: return $"{field} == null";
                    case Operator.IsNotNull: return $"{field} != null";
                }
                break;
            }
        }

        throw new LogicException($"Unsupported combination of DataType '{dataType}' and Operator '{_operator}'.");
    }

    /// <summary>
    /// Builds a predicate for <see cref="DataType.DateTime"/> or <see cref="DataType.Date"/>.
    /// </summary>
    /// <remarks>
    /// One method for both date types because the three things that decide the shape are the same in
    /// each: whether the member can be null, which of the two date types it is, and whether the
    /// comparison is by instant or by calendar day.
    /// <para>
    /// The null guard is emitted only for a member that can actually be null. On a non-nullable
    /// member it was never merely redundant: System.Linq.Dynamic.Core compares a struct against the
    /// null constant by looking for an implicit conversion and, finding none, falls through to
    /// <c>Expression.NotEqual</c>, which throws for <c>DateTimeOffset</c>. <c>DateTime</c> and
    /// <c>int</c> escape that only because the parser carries an exclusion list that
    /// <c>DateTimeOffset</c> is not on. Where the guard is dropped, <c>IsNull</c> and
    /// <c>IsNotNull</c> answer with the constant the guard already implied.
    /// </para>
    /// <para>
    /// The literal is built as the member's own type: <c>DateTimeOffset >= DateTime</c> has no
    /// signature, so a literal of the wrong type fails even where the guard is right. A nullable
    /// member is unwrapped with <c>.Value</c> under the guard that protects it, because
    /// <c>DateTime?</c> has no <c>.Date</c>.
    /// </para>
    /// <para>
    /// Values are parsed here, with the invariant culture, and re-emitted in round-trip form. The
    /// shipped predicate used to carry the caller's text into a <c>DateTime.Parse</c> the runtime
    /// evaluated in the host's culture, so the same filter meant different days on two servers.
    /// A <c>DateTimeOffset</c> is normalised to UTC and a value with no zone is read as UTC, which
    /// is what keeps a day comparison naming the day the caller wrote.
    /// </para>
    /// </remarks>
    private static string BuildDate(
        DataType dataType, Operator _operator, string field, List<string> values, Type? memberType)
    {
        Type? nullableOf = memberType is null ? null : Nullable.GetUnderlyingType(memberType);

        // An unknown member type keeps the guard: a HAVING alias can be anything, and the shape that
        // has shipped for it is the one its callers already depend on.
        bool nullable = memberType is null || nullableOf is not null || !memberType.IsValueType;
        DateValue.Kind kind = DateValue.KindOf(memberType);

        // A DateOnly is already a day, and has no .Date to ask for.
        bool byDay = dataType == DataType.Date && kind != DateValue.Kind.DateOnly;

        string member = nullableOf is not null ? $"{field}.Value" : field;
        string access = byDay ? $"{member}.Date" : member;
        string guard = nullable ? $"{field} != null && " : string.Empty;

        string Literal(string value)
        {
            // The same reader validation uses, so a value it accepted is one this can build.
            string parsed = DateValue.Read(value, memberType, field);

            string call = kind switch
            {
                // A constructor, not DateOnly.Parse: the runtime evaluates the literal in the host's
                // culture, and DateOnly.Parse reads "2026-09-01" as the year 1483 on a Thai server and
                // refuses it on a Saudi one. DateTime.Parse and DateTimeOffset.Parse recognise round-trip
                // text under every calendar, so those two stay as they are.
                DateValue.Kind.DateOnly => $"DateOnly({DateOnlyArguments(parsed)})",
                DateValue.Kind.Offset => $"DateTimeOffset.Parse(\"{parsed}\")",
                _ => $"DateTime.Parse(\"{parsed}\")"
            };

            return byDay ? $"{call}.Date" : call;
        }

        switch (_operator)
        {
            case Operator.Equal:
                return $"{guard}{access} == {Literal(values[0])}";

            case Operator.NotEqual:
                return $"{guard}{access} != {Literal(values[0])}";

            case Operator.GreaterThan:
                return $"{guard}{access} > {Literal(values[0])}";

            case Operator.GreaterThanOrEqual:
                return $"{guard}{access} >= {Literal(values[0])}";

            case Operator.LessThan:
                return $"{guard}{access} < {Literal(values[0])}";

            case Operator.LessThanOrEqual:
                return $"{guard}{access} <= {Literal(values[0])}";

            case Operator.Between:
                return $"{guard}{access} >= {Literal(values[0])} && {access} <= {Literal(values[1])}";

            case Operator.NotBetween:
                return $"{guard}({access} < {Literal(values[0])} || {access} > {Literal(values[1])})";

            // A member that cannot hold null is never null and always not-null. Answering with the
            // constant is what the dropped guard means, and it is also the only answer the parser
            // can build: the comparison itself is what throws.
            case Operator.IsNull:
                return nullable ? $"{field} == null" : "false";

            case Operator.IsNotNull:
                return nullable ? $"{field} != null" : "true";
        }

        throw new LogicException(
            $"Unsupported combination of DataType '{dataType}' and Operator '{_operator}'.");
    }

    /// <summary>Turns <c>yyyy-MM-dd</c> into the three arguments of the <c>DateOnly</c> constructor.</summary>
    private static string DateOnlyArguments(string day)
    {
        string[] parts = day.Split('-');

        return $"{int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture)}, " +
               $"{int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)}, " +
               $"{int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Escapes a value for embedding between the double quotes of a dynamic LINQ string literal.
    /// </summary>
    /// <remarks>
    /// The generated predicate is parsed as source text, so a value carrying a backslash or a double
    /// quote would otherwise end its literal early. A trailing backslash escapes the closing quote and
    /// the parser runs on into the rest of the expression — the reported symptom was
    /// <c>')' or ',' expected</c> on a search term ending in <c>\</c> — and a crafted value could close
    /// the literal and append predicate logic of its own, so this also closes an injection path.
    /// <para>
    /// Backslashes are doubled first: escaping quotes first would introduce backslashes that the
    /// backslash pass would then double a second time, turning <c>"</c> into <c>\\"</c> and ending the
    /// literal anyway. Values for the numeric and boolean data types are format-validated upstream and
    /// contain neither character, so escaping leaves them untouched.
    /// </para>
    /// </remarks>
    /// <param name="value">The raw value token.</param>
    /// <returns>The value with backslashes and double quotes escaped.</returns>
    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
