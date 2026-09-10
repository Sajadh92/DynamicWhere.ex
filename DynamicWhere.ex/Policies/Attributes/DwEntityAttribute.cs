namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Declares type-level policy behaviour.
/// </summary>
/// <remarks>
/// <see cref="RequirePolicy"/> closes the hole in an opt-in model: without it, every field policy
/// on a type is bypassed simply by not applying a policy context to the query. Enforcement of this
/// flag lands with the guarded query handle.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class DwEntityAttribute : Attribute
{
    /// <summary>
    /// When true, querying this type without a policy context throws instead of returning rows.
    /// </summary>
    public bool RequirePolicy { get; set; }
}
