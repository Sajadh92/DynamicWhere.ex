using System.Globalization;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Masking;

/// <summary>
/// Runs one field's transform chain over one value.
/// </summary>
/// <remarks>
/// The order is fixed — mutate, generalize, format, mask, truncate — so custom logic sees the real
/// value, precision drops before rendering, characters are hidden after rendering, and a length cap
/// has the last word. A replacement short-circuits all of it.
/// <para>
/// The chain ends with an assignability check rather than a coercion. A mask emits text, and text
/// cannot be assigned to a <see cref="decimal"/>; the startup scan is meant to have refused that
/// pairing long before a query runs, and if it did not, this fails the query rather than quietly
/// handing back a value the chain never transformed.
/// </para>
/// </remarks>
internal static class TransformPipeline
{
    /// <summary>
    /// Applies a chain to one value.
    /// </summary>
    /// <param name="chain">The stages to run.</param>
    /// <param name="value">The value as materialized.</param>
    /// <param name="memberType">The type the result must be assignable to.</param>
    /// <param name="context">The entity, the field, and who is asking.</param>
    /// <param name="options">Carries the hash salt and the service provider.</param>
    /// <returns>The value to emit.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the chain produces a value the member cannot hold. A configuration error, not a
    /// policy decision, so it is not a <c>PolicyException</c> — but it fails the query rather than
    /// passing the untransformed value through.
    /// </exception>
    internal static object? Apply(
        ValueTransform chain,
        object? value,
        Type memberType,
        DwTransformContext context,
        DwPolicyOptions options)
    {
        object? result = Run(chain, value, memberType, context, options);

        return Fit(result, memberType, context.FieldPath, chain);
    }

    /// <summary>Runs the stages in order, or the replacement alone.</summary>
    private static object? Run(
        ValueTransform chain,
        object? value,
        Type memberType,
        DwTransformContext context,
        DwPolicyOptions options)
    {
        // A replacement discards whatever the value was, so nothing before it is worth computing and
        // nothing after it would have anything left to act on.
        if (chain.Default is { } replacement)
        {
            return replacement.HasValue
                ? Coerce(replacement.Value, memberType)
                : DefaultOf(memberType);
        }

        object? current = value;

        if (chain.Mutate is { } mutate)
        {
            // Not wrapped in a catch. A transformer that fails and is caught into "return what you
            // were given" hands the caller the real value, which is the one outcome a transformer
            // must never produce.
            current = TransformerCache.Resolve(mutate.Transformer, options).Transform(current, context);
        }

        if (chain.Generalize is { } generalize)
        {
            current = Generalizer.Apply(generalize, current);
        }

        if (chain.Format is { } format)
        {
            current = current is IFormattable formattable
                ? formattable.ToString(format.Format, CultureInfo.InvariantCulture)
                : current?.ToString();
        }

        if (chain.Mask is { } mask)
        {
            current = MaskEngine.Apply(mask, AsText(current), options.HashSalt);
        }

        if (chain.Truncate is { } truncate)
        {
            current = Shorten(AsText(current), truncate);
        }

        return current;
    }

    /// <summary>
    /// Renders a value to text for the stages that only work on text.
    /// </summary>
    /// <remarks>
    /// Invariant culture, so a masked or truncated value does not change shape when the server
    /// moves. A value that reaches a mask without a format stage is rendered here rather than
    /// refused, because the refusal that matters comes at the end of the chain, where the result has
    /// to fit the member.
    /// </remarks>
    private static string? AsText(object? value) =>
        value switch
        {
            null => null,
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };

    /// <summary>Shortens text, appending an ellipsis only when something was actually removed.</summary>
    private static string? Shorten(string? value, TruncateStage stage)
    {
        if (value is null || value.Length <= stage.Length)
        {
            return value;
        }

        string kept = value[..stage.Length];

        return stage.Ellipsis is null ? kept : string.Concat(kept, stage.Ellipsis);
    }

    /// <summary>
    /// Refuses a result the member cannot hold.
    /// </summary>
    /// <remarks>
    /// The single place the output-assignability rule is enforced at run time. Everything the
    /// documentation says about which transform suits which type is derived from this one check, so
    /// there is no second table to drift out of step with it.
    /// </remarks>
    private static object? Fit(object? result, Type memberType, string fieldPath, ValueTransform chain)
    {
        Type target = Nullable.GetUnderlyingType(memberType) ?? memberType;

        if (result is null)
        {
            // Null fits a reference type and a nullable value type, and nothing else. Emitting
            // default(int) instead would turn a removed value into a real-looking zero.
            if (memberType.IsValueType && Nullable.GetUnderlyingType(memberType) is null)
            {
                throw Mismatch(fieldPath, chain, "null", memberType);
            }

            return null;
        }

        return target.IsInstanceOfType(result)
            ? result
            : throw Mismatch(fieldPath, chain, result.GetType().Name, memberType);
    }

    /// <summary>Builds the misconfiguration error, naming the chain that produced it.</summary>
    private static InvalidOperationException Mismatch(
        string fieldPath, ValueTransform chain, string produced, Type memberType)
    {
        string stages = string.Join(" then ", chain.Stages.Select(s => s.Kind.ToString()));

        return new InvalidOperationException(
            $"The transform on '{fieldPath}' ({stages}) produced {produced}, which cannot be " +
            $"assigned to {memberType.Name}. Masking, formatting and truncation emit text; a " +
            "numeric or temporal member needs [DwGeneralize] or [DwDefault] instead. Call " +
            "DwPolicy.ValidateModel() at startup to catch this before a query does.");
    }

    /// <summary>The type's own default, boxed.</summary>
    private static object? DefaultOf(Type memberType) =>
        memberType.IsValueType && Nullable.GetUnderlyingType(memberType) is null
            ? Activator.CreateInstance(memberType)
            : null;

    /// <summary>
    /// Converts a constant written in an attribute into the member's type.
    /// </summary>
    /// <remarks>
    /// C# forbids <see cref="decimal"/> and <see cref="DateTime"/> as attribute arguments, so a
    /// default is written as text and converted here. The design document says the library's own
    /// <c>Normalizer</c> already does this; it does not — it converts a value to a string, the
    /// opposite direction — and nothing else in the library turns a string into a CLR value, because
    /// the query path hands its literals to the dynamic-LINQ parser instead. So it is written here,
    /// with invariant culture, which is what the rest of the library uses.
    /// </remarks>
    internal static object? Coerce(string? value, Type memberType)
    {
        Type target = Nullable.GetUnderlyingType(memberType) ?? memberType;

        if (value is null)
        {
            return DefaultOf(memberType);
        }

        if (target == typeof(string))
        {
            return value;
        }

        if (value.Length == 0)
        {
            return DefaultOf(memberType);
        }

        if (target.IsEnum)
        {
            return Enum.Parse(target, value, ignoreCase: true);
        }

        if (target == typeof(Guid))
        {
            return Guid.Parse(value);
        }

        if (target == typeof(DateOnly))
        {
            return DateOnly.Parse(value, CultureInfo.InvariantCulture);
        }

        if (target == typeof(TimeOnly))
        {
            return TimeOnly.Parse(value, CultureInfo.InvariantCulture);
        }

        if (target == typeof(DateTimeOffset))
        {
            return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
        }

        if (target == typeof(TimeSpan))
        {
            return TimeSpan.Parse(value, CultureInfo.InvariantCulture);
        }

        return Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }
}
