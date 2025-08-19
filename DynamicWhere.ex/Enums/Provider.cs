namespace DynamicWhere.ex;

/// <summary>
/// Database provider hint used to optimize case-insensitive text comparison generation.
/// </summary>
public enum Provider
{
    /// <summary>PostgreSQL (Npgsql) — supports <c>EF.Functions.ILike</c>.</summary>
    Npgsql,
    /// <summary>Microsoft SQL Server.</summary>
    SqlServer,
    /// <summary>Any other provider (fallback behavior uses LOWER/LIKE).</summary>
    Others
}
