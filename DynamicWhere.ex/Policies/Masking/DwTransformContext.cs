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
/// <para>
/// A struct, because one of these is built for every transformed value of every row and a class
/// would put an allocation on the hottest path this library has. It is passed to an interface
/// method, which does not box it, and it holds three references and nothing else — so copying one
/// costs less than the indirection reading a class through a reference would.
/// </para>
/// </remarks>
public readonly struct DwTransformContext : IEquatable<DwTransformContext>
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

    /// <summary>Compares two contexts by what they point at.</summary>
    /// <param name="other">The context to compare with.</param>
    /// <remarks>
    /// Present because a struct that does not define equality gets the reflective default, which is
    /// slow and surprising. Nothing in this library compares two of these; the members exist so
    /// that anything which does gets a sensible answer rather than a reflective one.
    /// </remarks>
    public bool Equals(DwTransformContext other) =>
        ReferenceEquals(Entity, other.Entity)
        && string.Equals(FieldPath, other.FieldPath, StringComparison.Ordinal)
        && ReferenceEquals(Policy, other.Policy);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is DwTransformContext other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(
        System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Entity),
        FieldPath,
        Policy is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Policy));

    /// <summary>Compares two contexts by what they point at.</summary>
    /// <param name="left">The first context.</param>
    /// <param name="right">The second context.</param>
    public static bool operator ==(DwTransformContext left, DwTransformContext right) =>
        left.Equals(right);

    /// <summary>Compares two contexts by what they point at.</summary>
    /// <param name="left">The first context.</param>
    /// <param name="right">The second context.</param>
    public static bool operator !=(DwTransformContext left, DwTransformContext right) =>
        !left.Equals(right);
}
