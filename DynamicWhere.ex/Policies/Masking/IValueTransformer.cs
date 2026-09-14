namespace DynamicWhere.ex.Policies.Masking;

/// <summary>
/// Transforms one value on its way out of the library. The extension point behind
/// <c>[DwMutate]</c>.
/// </summary>
/// <remarks>
/// One instance is reused for the life of the process, so an implementation must be stateless and
/// safe to call from many threads at once.
/// <para>
/// An implementation that throws fails the query. That is deliberate: a transformer swallowing its
/// own failure and returning what it was given hands the caller the real value, which is the one
/// outcome a transformer must never produce.
/// </para>
/// </remarks>
public interface IValueTransformer
{
    /// <summary>
    /// Returns the value to emit in place of <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The real value read from the materialized object.</param>
    /// <param name="context">The entity, the field, and who is asking.</param>
    /// <returns>The value to emit. Must be assignable to the member being transformed.</returns>
    object? Transform(object? value, DwTransformContext context);
}
