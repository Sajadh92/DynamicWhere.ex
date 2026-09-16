namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Declares type-level policy behaviour.
/// </summary>
/// <remarks>
/// <see cref="RequirePolicy"/> closes the hole in an opt-in model: without it, every field policy
/// on a type is bypassed simply by not applying a policy context to the query. It is enforced by
/// every one of this library's extension methods, on the <c>IQueryable</c> and <c>IEnumerable</c>
/// paths alike: reaching such a type through any of them without <c>ApplyPolicy</c> throws
/// <c>PolicyException</c> with <c>PolicyRequired</c>.
/// <para>
/// What it does not cover is plain LINQ. The guard lives in this library's methods, so a
/// <c>DbSet</c> queried directly is not intercepted — the flag protects the paths that read a
/// filter, which is the shape an API exposes.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class DwEntityAttribute : Attribute
{
    /// <summary>
    /// When true, querying this type without a policy context throws instead of returning rows.
    /// </summary>
    public bool RequirePolicy { get; set; }
}
