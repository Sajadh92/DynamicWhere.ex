namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Hands the decorated member's value to a transformer the application supplies.
/// </summary>
/// <remarks>
/// The escape hatch for everything the built-in strategies cannot express: a salary band, a
/// role-aware redaction, a lookup against another system. It runs first in the chain, so it sees the
/// real value rather than one another stage has already reshaped.
/// <para>
/// The type is resolved from the service provider when policy options carry one, and through
/// <c>Activator.CreateInstance</c> otherwise. One instance is reused for the life of the
/// process, so an implementation must be stateless and safe to call from many threads at once.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DwMutate(typeof(SalaryBandTransformer))]
/// public decimal Salary { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class DwMutateAttribute : DwPolicyAttribute
{
    /// <summary>Initializes the attribute.</summary>
    /// <param name="transformer">
    /// A type implementing <c>IValueTransformer</c>. Checked by the startup scan, because a type
    /// that does not implement it fails on a query rather than on deployment.
    /// </param>
    public DwMutateAttribute(Type transformer) => Transformer = transformer;

    /// <summary>The transformer to run.</summary>
    public Type Transformer { get; }
}
