using DynamicWhere.ex.Policies.Context;

namespace DynamicWhere.ex.Policies.Masking;

/// <summary>
/// What a transformer is told about the value it is given.
/// </summary>
/// <remarks>
/// Carries the whole entity rather than the value alone, so a transformer can decide from its
/// siblings — a salary band that depends on a department, a redaction that depends on a country.
/// It carries the policy context too, so a transformer can be role-aware without reaching for
/// ambient state that would not survive a background job.
/// </remarks>
public sealed class DwTransformContext
{
    /// <summary>Initializes the context.</summary>
    /// <param name="entity">The object the value was read from.</param>
    /// <param name="fieldPath">The canonical path of the member being transformed.</param>
    /// <param name="policy">Who is asking.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
    public DwTransformContext(object entity, string fieldPath, DwPolicyContext policy)
    {
        Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        FieldPath = fieldPath ?? throw new ArgumentNullException(nameof(fieldPath));
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>The object the value was read from.</summary>
    public object Entity { get; }

    /// <summary>
    /// The canonical path of the member being transformed, never the alias the caller wrote.
    /// </summary>
    /// <remarks>
    /// Alias resolution finishes before the pipeline runs, so everything downstream of the
    /// sanitizer works in canonical paths. A transformer keyed on what a caller happened to write
    /// would miss every caller who wrote the other spelling.
    /// </remarks>
    public string FieldPath { get; }

    /// <summary>Who is asking.</summary>
    public DwPolicyContext Policy { get; }
}
