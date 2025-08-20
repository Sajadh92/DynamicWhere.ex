namespace DynamicWhere.ex;

/// <summary>
/// Builds EF Core–translatable predicate snippets from (<see cref="DataType"/>, <see cref="Operator"/>, field, values).
/// <para>
/// <b>Text comparisons</b>:
/// <list type="bullet">
///   <item><description>Escapes LIKE wildcards (<c>\</c>, <c>%</c>, <c>_</c>) so literals don’t act as wildcards.</description></item>
///   <item><description>Always emits explicit <c>ESCAPE '\'</c> by using the 3-arg <c>EF.Functions.Like</c> or <c>EF.Functions.ILike</c> (provider permitting).</description></item>
///   <item><description><c>I*</c> operators (e.g., <see cref="Operator.IContains"/>) are case-insensitive:
///     PostgreSQL uses <c>ILIKE</c>, others use <c>LOWER(field) LIKE LOWER(pattern)</c>.</description></item>
/// </list>
/// </para>
/// <para>
/// Returns a C# dynamic LINQ expression string (not SQL) intended for libraries like System.Linq.Dynamic.Core.
/// </para>
/// </summary>
public static class Builder
{
    /// <summary>
    /// Literal used as the explicit ESCAPE character argument in EF Core's 3-parameter LIKE/ILIKE.
    /// Emits: <c>EF.Functions.Like(field, pattern, "\\")</c>.
    /// </summary>
    private const string EscapeLiteral = "\"\\\\\"";

    /// <summary>
    /// Escapes SQL LIKE wildcards and the escape character itself.
    /// Converts literal <c>\</c>, <c>%</c>, and <c>_</c> to <c>\\</c>, <c>\%</c>, and <c>\_</c> respectively.
    /// </summary>
    /// <param name="input">Raw user input to be used in a LIKE pattern.</param>
    /// <returns>Escaped string safe for LIKE patterns.</returns>
    private static string EscapeLike(string input) => (input ?? string.Empty)
        .Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    /// <summary>
    /// Builds an exact-match LIKE pattern (no leading/trailing wildcards), with wildcards escaped.
    /// The resulting pattern is double-quoted for dynamic LINQ consumption.
    /// </summary>
    /// <param name="value">The value to match exactly.</param>
    /// <returns>Exact-match LIKE pattern string.</returns>
    private static string PatExact(string value) => $"\"{EscapeLike(value)}\"";

    /// <summary>
    /// Builds a contains-match LIKE pattern (<c>%value%</c>), with wildcards escaped.
    /// The resulting pattern is double-quoted for dynamic LINQ consumption.
    /// </summary>
    /// <param name="value">The value to search for anywhere inside the field.</param>
    /// <returns>Contains-match LIKE pattern string.</returns>
    private static string PatContains(string value) => $"\"%{EscapeLike(value)}%\"";

    /// <summary>
    /// Builds a starts-with LIKE pattern (<c>value%</c>), with wildcards escaped.
    /// The resulting pattern is double-quoted for dynamic LINQ consumption.
    /// </summary>
    /// <param name="value">The value that the field should start with.</param>
    /// <returns>Starts-with LIKE pattern string.</returns>
    private static string PatStarts(string value) => $"\"{EscapeLike(value)}%\"";

    /// <summary>
    /// Builds an ends-with LIKE pattern (<c>%value</c>), with wildcards escaped.
    /// The resulting pattern is double-quoted for dynamic LINQ consumption.
    /// </summary>
    /// <param name="value">The value that the field should end with.</param>
    /// <returns>Ends-with LIKE pattern string.</returns>
    private static string PatEnds(string value) => $"\"%{EscapeLike(value)}\"";

    /// <summary>
    /// Emits case-sensitive LIKE with explicit ESCAPE: <c>EF.Functions.Like(field, pattern, "\\")</c>.
    /// </summary>
    /// <param name="field">The field access expression (e.g., <c>"x.Email"</c>).</param>
    /// <param name="pattern">The LIKE pattern to match against (already escaped).</param>
    /// <returns>LIKE expression string suitable for dynamic LINQ.</returns>
    private static string LikeExpr(string field, string pattern)
        => $"EF.Functions.Like({field}, {pattern}, {EscapeLiteral})";

    /// <summary>
    /// Emits a case-insensitive LIKE expression depending on <paramref name="provider"/>.
    /// Uses <c>EF.Functions.ILike</c> for PostgreSQL; otherwise uses <c>LOWER(field)</c> and <c>LOWER(pattern)</c> with <c>EF.Functions.Like</c>.
    /// </summary>
    /// <param name="provider">Database provider hint used to optimize case-insensitive comparison generation.</param>
    /// <param name="field">The field access expression (e.g., <c>"x.Email"</c>).</param>
    /// <param name="pattern">The LIKE pattern to match against (already escaped).</param>
    /// <returns>Case-insensitive LIKE expression string suitable for dynamic LINQ.</returns>
    private static string ILikeExpr(Provider? provider, string field, string pattern)
        => provider is not null && provider == Provider.Npgsql
           ? $"EF.Functions.ILike({field}, {pattern}, {EscapeLiteral})"
           : $"EF.Functions.Like(LOWER({field}), LOWER({pattern}), {EscapeLiteral})";

    /// <summary>
    /// Builds a condition expression for dynamic LINQ based on data type, operator, and values.
    /// The output is a C# expression string (including null checks) that EF Core can translate.
    /// </summary>
    /// <param name="dataType">Logical data type of the field.</param>
    /// <param name="_operator">Operator to apply.</param>
    /// <param name="field">Field access expression (e.g., <c>"x.Email"</c>).</param>
    /// <param name="values">Operator values (validated upstream; count depends on <paramref name="_operator"/>).</param>
    /// <param name="provider">Database provider hint to influence case-insensitive text handling; default is <see cref="Provider.Others"/>.</param>
    /// <returns>
    /// Dynamic LINQ predicate snippet. Returns an empty string only when the combination is unsupported (should not occur if validated).
    /// </returns>
    public static string BuildCondition(DataType dataType, Operator _operator, string field, List<string> values, Provider? provider = Provider.Others)
    {
        // Normalize value tokens by trimming whitespace; validation already happened upstream.
        values = values.Select(v => v.Trim()).ToList();

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
                        return $"{field} != null && {LikeExpr(field, PatExact(values[0]))}";

                    case Operator.IEqual:
                        return $"{field} != null && {ILikeExpr(provider, field, PatExact(values[0]))}";

                    case Operator.NotEqual:
                        return $"{field} != null && !{LikeExpr(field, PatExact(values[0]))}";

                    case Operator.INotEqual:
                        return $"{field} != null && !{ILikeExpr(provider, field, PatExact(values[0]))}";

                    case Operator.Contains:
                        return $"{field} != null && {LikeExpr(field, PatContains(values[0]))}";

                    case Operator.IContains:
                        return $"{field} != null && {ILikeExpr(provider, field, PatContains(values[0]))}";

                    case Operator.NotContains:
                        return $"{field} != null && !{LikeExpr(field, PatContains(values[0]))}";

                    case Operator.INotContains:
                        return $"{field} != null && !{ILikeExpr(provider, field, PatContains(values[0]))}";

                    case Operator.StartsWith:
                        return $"{field} != null && {LikeExpr(field, PatStarts(values[0]))}";

                    case Operator.IStartsWith:
                        return $"{field} != null && {ILikeExpr(provider, field, PatStarts(values[0]))}";

                    case Operator.EndsWith:
                        return $"{field} != null && {LikeExpr(field, PatEnds(values[0]))}";

                    case Operator.IEndsWith:
                        return $"{field} != null && {ILikeExpr(provider, field, PatEnds(values[0]))}";

                    case Operator.NotStartsWith:
                        return $"{field} != null && !{LikeExpr(field, PatStarts(values[0]))}";

                    case Operator.INotStartsWith:
                        return $"{field} != null && !{ILikeExpr(provider, field, PatStarts(values[0]))}";

                    case Operator.NotEndsWith:
                        return $"{field} != null && !{LikeExpr(field, PatEnds(values[0]))}";

                    case Operator.INotEndsWith:
                        return $"{field} != null && !{ILikeExpr(provider, field, PatEnds(values[0]))}";

                    case Operator.In:
                    {
                        var ors = values.Select(v => LikeExpr(field, PatExact(v)));
                        return $"{field} != null && ({string.Join(" || ", ors)})";
                    }

                    case Operator.IIn:
                    {
                        var ors = values.Select(v => ILikeExpr(provider, field, PatExact(v)));
                        return $"{field} != null && ({string.Join(" || ", ors)})";
                    }

                    case Operator.NotIn:
                    {
                        var ands = values.Select(v => "!" + LikeExpr(field, PatExact(v)));
                        return $"{field} != null && ({string.Join(" && ", ands)})";
                    }

                    case Operator.INotIn:
                    {
                        var ands = values.Select(v => "!" + ILikeExpr(provider, field, PatExact(v)));
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
            {
                switch (_operator)
                {
                    case Operator.Equal:
                        return $"{field} != null && {field} == DateTime.Parse(\"{values[0]}\")";

                    case Operator.NotEqual:
                        return $"{field} != null && {field} != DateTime.Parse(\"{values[0]}\")";

                    case Operator.GreaterThan:
                        return $"{field} != null && {field} > DateTime.Parse(\"{values[0]}\")";

                    case Operator.GreaterThanOrEqual:
                        return $"{field} != null && {field} >= DateTime.Parse(\"{values[0]}\")";

                    case Operator.LessThan:
                        return $"{field} != null && {field} < DateTime.Parse(\"{values[0]}\")";

                    case Operator.LessThanOrEqual:
                        return $"{field} != null && {field} <= DateTime.Parse(\"{values[0]}\")";

                    case Operator.Between:
                        return $"{field} != null && {field} >= DateTime.Parse(\"{values[0]}\") && {field} <= DateTime.Parse(\"{values[1]}\")";

                    case Operator.NotBetween:
                        return $"{field} != null && ({field} < DateTime.Parse(\"{values[0]}\") || {field} > DateTime.Parse(\"{values[1]}\") )";

                    case Operator.IsNull: return $"{field} == null";
                    case Operator.IsNotNull: return $"{field} != null";
                }
            }
            break;

            // ----------------------------------------------------------
            // DATE (compare by .Date)
            // ----------------------------------------------------------
            case DataType.Date:
            {
                switch (_operator)
                {
                    case Operator.Equal:
                        return $"{field} != null && {field}.Date == DateTime.Parse(\"{values[0]}\").Date";

                    case Operator.NotEqual:
                        return $"{field} != null && {field}.Date != DateTime.Parse(\"{values[0]}\").Date";

                    case Operator.GreaterThan:
                        return $"{field} != null && {field}.Date > DateTime.Parse(\"{values[0]}\").Date";

                    case Operator.GreaterThanOrEqual:
                        return $"{field} != null && {field}.Date >= DateTime.Parse(\"{values[0]}\").Date";

                    case Operator.LessThan:
                        return $"{field} != null && {field}.Date < DateTime.Parse(\"{values[0]}\").Date";

                    case Operator.LessThanOrEqual:
                        return $"{field} != null && {field}.Date <= DateTime.Parse(\"{values[0]}\").Date";

                    case Operator.Between:
                        return $"{field} != null && {field}.Date >= DateTime.Parse(\"{values[0]}\").Date && {field}.Date <= DateTime.Parse(\"{values[1]}\").Date";

                    case Operator.NotBetween:
                        return $"{field} != null && ({field}.Date < DateTime.Parse(\"{values[0]}\").Date || {field}.Date > DateTime.Parse(\"{values[1]}\").Date)";

                    case Operator.IsNull: return $"{field} == null";
                    case Operator.IsNotNull: return $"{field} != null";
                }
            }
            break;
        }

        // Fallback – should not happen if inputs were validated upstream
        return string.Empty;
    }
}
