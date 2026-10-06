namespace DynamicWhere.ex.Enums;

/// <summary>
/// How the case-insensitive text operators match, set once for the process with <c>DwText.Configure</c>.
/// </summary>
public enum TextMatching
{
    /// <summary>
    /// Both sides lowered: <c>field.ToLower().Contains("value")</c>, which a relational provider writes as
    /// <c>lower(column) LIKE …</c>. The default, and what every provider and LINQ to objects can run.
    /// </summary>
    Lower = 0,

    /// <summary>
    /// PostgreSQL's <c>ILIKE</c>, through Npgsql's <c>EF.Functions.ILike</c>, for the six pattern operators:
    /// <see cref="Operator.IContains"/>, <see cref="Operator.INotContains"/>, <see cref="Operator.IStartsWith"/>,
    /// <see cref="Operator.INotStartsWith"/>, <see cref="Operator.IEndsWith"/> and
    /// <see cref="Operator.INotEndsWith"/>.
    /// </summary>
    /// <remarks>
    /// <c>column ILIKE '%value%'</c> can use a <c>pg_trgm</c> GIN or GiST index on the column, where
    /// <c>lower(column) LIKE '%value%'</c> cannot. It applies only to a query EF Core's own provider
    /// translates, so LINQ to objects and a provider wrapping EF Core keep <see cref="Lower"/>, and it needs
    /// <c>Npgsql.EntityFrameworkCore.PostgreSQL</c>: every EF Core query in the process is rewritten, so a
    /// process that also queries another database through EF Core must not choose it.
    /// <see cref="Operator.IEqual"/>, <see cref="Operator.INotEqual"/>, <see cref="Operator.IIn"/> and
    /// <see cref="Operator.INotIn"/> stay <c>lower(column) = …</c>, which an index on <c>lower(column)</c>
    /// serves, and <c>HAVING</c> stays lowered, since an aggregate uses no index.
    /// </remarks>
    ILike = 1
}
