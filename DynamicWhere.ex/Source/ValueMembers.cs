using System.Linq.Expressions;
using System.Reflection;
using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.ex.Source;

/// <summary>
/// What a typed projection binds to a member whose type is a struct, or a collection of values, when the
/// selection names a path beneath it.
/// </summary>
/// <remarks>
/// The typed projection builds a class navigation member by member and used to skip every
/// value-typed member a path went beneath. The member was never bound, so it came back as its
/// default with nothing refused and nothing said: <c>Name.Ar</c> over a <c>LocalizedText</c> struct
/// returned an empty name, guarded or not.
/// <list type="bullet">
///   <item><description>
///     An application's own struct is now built member by member, as a class navigation is, so the
///     row carries what was named and nothing beside it. A <see cref="Nullable{T}"/> one is read
///     through <c>Value</c>, <c>Alias.Value.Ar</c>, and built where it has a value; naming
///     <c>Alias.Value</c> whole, or only <c>Alias.HasValue</c>, leaves it unbound as before.
///   </description></item>
///   <item><description>
///     A framework type stays unbound. <c>Born.Year</c> names a value no typed row can hold apart
///     from the date, and binding the date whole would hand back more than the path names: a policy
///     allowing <c>Born.Year</c> alone would return the day of birth. The dynamic terminal carries
///     such a path as it names it.
///   </description></item>
/// </list>
/// The member is read with a plain member access rather than through <c>EF.Property</c>: a struct is
/// not a navigation, and a member access is what EF Core follows through a projection's initializer
/// and evaluates on the client where it cannot.
/// </remarks>
internal static class ValueMembers
{
    /// <summary>
    /// The binding for a struct-typed member the selection goes beneath, or null to leave the member
    /// unbound.
    /// </summary>
    /// <param name="member">The member, already known to be writable.</param>
    /// <param name="instance">The expression the member is read from.</param>
    /// <param name="node">What the selection names beneath the member.</param>
    /// <param name="build">Builds one level of the projection, as the typed projection does.</param>
    internal static MemberAssignment? Bind(
        PropertyInfo member,
        Expression instance,
        ProjectionNode node,
        Func<Type, Expression, ProjectionNode, MemberInitExpression> build)
    {
        Type type = member.PropertyType;
        Type? wrapped = Nullable.GetUnderlyingType(type);
        Type underlying = wrapped ?? type;

        // An application's own struct and nothing else: not a framework type, not a string, and not a
        // struct that is itself a collection.
        if (!underlying.IsValueType || AttributePolicyProvider.NavigationTypeOf(underlying) != underlying)
        {
            return null;
        }

        Expression access = Expression.Property(instance, member);

        if (wrapped is null)
        {
            MemberInitExpression whole = build(underlying, access, Within(underlying, node));

            // Nothing named could be set: a read-only member, or only a class navigation. A struct built
            // empty is a constant EF Core refuses to hold in a projection, so it stays unbound as before.
            return whole.Bindings.Count == 0 ? null : Expression.Bind(member, whole);
        }

        // A nullable struct is named through Value: Alias.Value.Ar. Naming Value itself asks for the
        // whole struct, and HasValue alone for none of it; both stay unbound as before. The policy names
        // the struct's members, not Value's, and a struct built empty is a constant EF Core refuses to
        // hold in a projection.
        if (node.Scalars.Contains(nameof(Nullable<int>.Value))
            || !node.Children.TryGetValue(nameof(Nullable<int>.Value), out ProjectionNode? inner))
        {
            return null;
        }

        MemberInitExpression built = build(
            underlying, Expression.Property(access, nameof(Nullable<int>.Value)), Within(underlying, inner));

        if (built.Bindings.Count == 0)
        {
            return null;
        }

        return Expression.Bind(
            member,
            Expression.Condition(
                Expression.Property(access, nameof(Nullable<int>.HasValue)),
                Expression.Convert(built, type),
                Expression.Constant(null, type)));
    }

    /// <summary>
    /// The binding for a collection of values the selection goes beneath: the collection whole where
    /// its elements are scalars, each element built member by member where they are an application's
    /// own structs, and null to leave it unbound.
    /// </summary>
    /// <param name="member">The collection member, already known to be writable.</param>
    /// <param name="instance">The expression the member is read from.</param>
    /// <param name="node">What the selection names beneath each element.</param>
    /// <param name="elementType">The collection's element type, a value type or a string.</param>
    /// <param name="build">Builds one level of the projection, as the typed projection does.</param>
    /// <remarks>
    /// The typed projection bound every collection of values whole. That is right for strings, numbers
    /// and dates, which have no member a policy names. A struct has: <c>Pairs.Shown</c> over a
    /// <c>List&lt;Pair&gt;</c> handed back every <c>Pair</c> whole, a <c>[DwDenied]</c> member included, in
    /// both tiers, while the policy had approved <c>Pairs.Shown</c> alone. Each element is now built as a
    /// class element is, <c>Pairs.Select(v =&gt; new Pair { Shown = v.Shown }).ToList()</c>, into a list or an
    /// array. A collection of nullable structs, one of another shape, and an element nothing named can be
    /// set in are left unbound: nothing is carried rather than everything.
    /// </remarks>
    internal static MemberAssignment? BindElements(
        PropertyInfo member,
        Expression instance,
        ProjectionNode node,
        Type elementType,
        Func<Type, Expression, ProjectionNode, MemberInitExpression> build)
    {
        Expression access = Expression.Property(instance, member);
        Type underlying = Nullable.GetUnderlyingType(elementType) ?? elementType;

        if (!underlying.IsValueType || AttributePolicyProvider.NavigationTypeOf(underlying) != underlying)
        {
            return Expression.Bind(member, access);
        }

        if (underlying != elementType)
        {
            return null;
        }

        ParameterExpression element = Expression.Parameter(elementType, "v");
        MemberInitExpression each = build(elementType, element, Within(elementType, node));

        if (each.Bindings.Count == 0)
        {
            return null;
        }

        // Read as the class elements are: AsQueryable, then Select, then back to the member's shape.
        Expression selected = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Select),
            new[] { elementType, elementType },
            Expression.Call(typeof(Queryable), nameof(Queryable.AsQueryable), new[] { elementType }, access),
            Expression.Lambda(each, element));

        Type holder = member.PropertyType;
        Expression? filled = null;

        if (holder.IsAssignableFrom(typeof(List<>).MakeGenericType(elementType)))
        {
            filled = Expression.Call(typeof(Enumerable), nameof(Enumerable.ToList), new[] { elementType }, selected);
        }
        else if (holder == elementType.MakeArrayType())
        {
            filled = Expression.Call(typeof(Enumerable), nameof(Enumerable.ToArray), new[] { elementType }, selected);
        }

        if (filled is null)
        {
            return null;
        }

        // A null collection stays null, as it did when it was bound whole: AsQueryable refuses one.
        return Expression.Bind(
            member,
            Expression.Condition(
                Expression.Equal(access, Expression.Constant(null, holder)),
                Expression.Constant(null, holder),
                Expression.Convert(filled, holder)));
    }

    /// <summary>
    /// What the selection names beneath a struct, less the class navigations in it.
    /// </summary>
    /// <remarks>
    /// The typed projection reads a class navigation through <c>EF.Property</c>, whose instance is an
    /// object, and a struct cannot be handed to it without a conversion the builder does not make. Such
    /// a navigation stays unbound, as the whole struct did before; the struct's other members are built.
    /// </remarks>
    private static ProjectionNode Within(Type type, ProjectionNode node)
    {
        ProjectionNode within = new(node.Type);

        within.Scalars.UnionWith(node.Scalars);

        foreach (KeyValuePair<string, ProjectionNode> child in node.Children)
        {
            if (CacheReflection.FindProperty(type, child.Key) is not { } property)
            {
                continue;
            }

            Type held = property.PropertyType;

            if (held.IsValueType || held == typeof(string) || CacheReflection.IsCollectionType(held))
            {
                within.Children[child.Key] = child.Value;
            }
        }

        return within;
    }
}
