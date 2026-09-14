using System.Collections.Concurrent;
using DynamicWhere.ex.Policies.Config;

namespace DynamicWhere.ex.Policies.Masking;

/// <summary>
/// Holds one instance of each <c>[DwMutate]</c> transformer for the life of the process.
/// </summary>
/// <remarks>
/// Constructing a transformer per row would dominate the cost of masking a large result, so an
/// instance is built once and reused. That is the reason an implementation must be stateless and
/// safe to call from many threads at once, which the interface documents.
/// <para>
/// Resolved from the configured service provider when there is one, so a transformer can take
/// dependencies, and constructed directly otherwise, so the core package stays usable without a
/// container.
/// </para>
/// </remarks>
internal static class TransformerCache
{
    private static readonly ConcurrentDictionary<Type, IValueTransformer> Instances = new();

    /// <summary>
    /// Returns the transformer of the given type.
    /// </summary>
    /// <param name="transformer">The type to resolve.</param>
    /// <param name="options">Supplies the service provider, when one is configured.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the type does not implement <see cref="IValueTransformer"/>, or cannot be built.
    /// A configuration error the startup scan is meant to have caught, and a failure rather than a
    /// pass-through, because a transformer that cannot run must not leave the value untransformed.
    /// </exception>
    internal static IValueTransformer Resolve(Type transformer, DwPolicyOptions options)
    {
        // Not GetOrAdd with a closure over options: the factory would capture whichever options
        // object happened to lose the race, and a transformer resolved from the wrong container is
        // a wrong answer rather than a stale one.
        if (Instances.TryGetValue(transformer, out IValueTransformer? cached))
        {
            return cached;
        }

        IValueTransformer created = Build(transformer, options);

        return Instances.GetOrAdd(transformer, created);
    }

    /// <summary>Builds one transformer, from the container when there is one.</summary>
    private static IValueTransformer Build(Type transformer, DwPolicyOptions options)
    {
        if (!typeof(IValueTransformer).IsAssignableFrom(transformer))
        {
            throw new InvalidOperationException(
                $"[DwMutate] names '{transformer.FullName}', which does not implement " +
                "IValueTransformer, so there is nothing to run and the value would be emitted as " +
                "stored.");
        }

        object? instance = options.Services?.GetService(transformer)
            ?? Activator.CreateInstance(transformer);

        return instance as IValueTransformer
            ?? throw new InvalidOperationException(
                $"[DwMutate] names '{transformer.FullName}', which could not be constructed. " +
                "Register it with the configured service provider, or give it a parameterless " +
                "constructor.");
    }

    /// <summary>Empties the cache. For tests that swap a transformer between runs.</summary>
    internal static void Clear() => Instances.Clear();
}
