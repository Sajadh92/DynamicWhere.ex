using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace DynamicWhere.ex.Policies.Masking;

/// <summary>
/// Compiled getters and setters, one pair per property, built once and reused.
/// </summary>
/// <remarks>
/// Transformation runs per value per row, so reflection's own <c>GetValue</c> and <c>SetValue</c>
/// would dominate the cost of masking a large result. A compiled delegate is roughly an order of
/// magnitude cheaper and is what the performance budget assumes.
/// <para>
/// Keyed on <see cref="PropertyInfo"/> rather than on a type and a name, so a dynamic projection —
/// whose type is generated per query shape and never seen again — does not collide with anything,
/// and so the same property reached through two paths shares one delegate.
/// </para>
/// </remarks>
internal static class MutatorCache
{
    private static readonly ConcurrentDictionary<PropertyInfo, Func<object, object?>> Getters = new();
    private static readonly ConcurrentDictionary<PropertyInfo, Action<object, object?>?> Setters = new();

    /// <summary>Reads a property's value from an instance.</summary>
    internal static object? Read(PropertyInfo property, object instance) =>
        Getters.GetOrAdd(property, BuildGetter)(instance);

    /// <summary>
    /// Writes a property's value onto an instance.
    /// </summary>
    /// <returns>False when the property cannot be written to.</returns>
    /// <remarks>
    /// A read-only property is reported rather than thrown on, because the caller knows whether a
    /// value that could not be written is a masked value escaping — which it is — or a navigation
    /// being walked through, which it is not.
    /// </remarks>
    internal static bool Write(PropertyInfo property, object instance, object? value)
    {
        Action<object, object?>? setter = Setters.GetOrAdd(property, BuildSetter);

        if (setter is null)
        {
            return false;
        }

        setter(instance, value);

        return true;
    }

    /// <summary>Compiles <c>instance =&gt; (object)((T)instance).Property</c>.</summary>
    private static Func<object, object?> BuildGetter(PropertyInfo property)
    {
        ParameterExpression instance = Expression.Parameter(typeof(object), "instance");

        UnaryExpression typed = Expression.Convert(instance, property.DeclaringType!);
        MemberExpression read = Expression.Property(typed, property);
        UnaryExpression boxed = Expression.Convert(read, typeof(object));

        return Expression.Lambda<Func<object, object?>>(boxed, instance).Compile();
    }

    /// <summary>
    /// Compiles <c>(instance, value) =&gt; ((T)instance).Property = (TValue)value</c>, or null when
    /// the property cannot be written.
    /// </summary>
    private static Action<object, object?>? BuildSetter(PropertyInfo property)
    {
        if (!property.CanWrite || property.SetMethod is null)
        {
            return null;
        }

        ParameterExpression instance = Expression.Parameter(typeof(object), "instance");
        ParameterExpression value = Expression.Parameter(typeof(object), "value");

        UnaryExpression typedInstance = Expression.Convert(instance, property.DeclaringType!);
        UnaryExpression typedValue = Expression.Convert(value, property.PropertyType);

        BinaryExpression assign = Expression.Assign(
            Expression.Property(typedInstance, property), typedValue);

        return Expression.Lambda<Action<object, object?>>(assign, instance, value).Compile();
    }
}
