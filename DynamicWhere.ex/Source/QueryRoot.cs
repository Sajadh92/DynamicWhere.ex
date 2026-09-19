using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DynamicWhere.ex.Source;

/// <summary>
/// The EF Core model behind a query, read from the query's root.
/// </summary>
/// <remarks>
/// The root expression's type differs between EF Core versions — <c>QueryRootExpression</c> in 6,
/// <c>EntityQueryRootExpression</c> from 7 — and both carry the entity type in a property named
/// <c>EntityType</c>, so the property is found by name. Any root reaches the model, and the model is
/// asked for the type the caller names, which also answers for a derived type queried through
/// <c>OfType</c>.
/// </remarks>
internal static class QueryRoot
{
    /// <summary>The <c>EntityType</c> property of each query-root expression type, or null for a type with none.</summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> RootEntityTypeProperties = new();

    /// <summary>
    /// The entity type the model maps for <paramref name="type"/>, or null when the query is not an
    /// EF Core query or the model does not map that type.
    /// </summary>
    internal static IEntityType? EntityType(Expression expression, Type type) => Model(expression)?.FindEntityType(type);

    /// <summary>The EF Core model behind a query, or null when the query is not an EF Core query.</summary>
    internal static IModel? Model(Expression expression)
    {
        RootFinder finder = new();

        finder.Visit(expression);

        return finder.EntityType?.Model;
    }

    /// <summary>
    /// True when a call along a query's chain hands back the rows it was given, fewer of them or in
    /// another order, rather than rows it builds or reaches through them.
    /// </summary>
    /// <remarks>
    /// A method this does not know counts as building its rows, which only ever reads a query more
    /// warily than it needs.
    /// </remarks>
    internal static bool KeepsRows(MethodCallExpression call)
    {
        Type? declaring = call.Method.DeclaringType;

        if (declaring == typeof(Queryable) || declaring == typeof(Enumerable))
        {
            return call.Method.Name switch
            {
                nameof(Queryable.Where) or nameof(Queryable.OrderBy) or nameof(Queryable.OrderByDescending)
                    or nameof(Queryable.ThenBy) or nameof(Queryable.ThenByDescending) or "Order" or "OrderDescending"
                    or nameof(Queryable.Skip) or nameof(Queryable.Take) or nameof(Queryable.SkipWhile)
                    or nameof(Queryable.TakeWhile) or "SkipLast" or "TakeLast" or nameof(Queryable.Distinct)
                    or "DistinctBy" or nameof(Queryable.Reverse) or nameof(Queryable.AsQueryable)
                    or nameof(Queryable.OfType) or nameof(Queryable.Cast) => true,

                // Each of these returns the rows of both sources, and the other one is read the same way.
                nameof(Queryable.Concat) or nameof(Queryable.Union) or "UnionBy" or nameof(Queryable.Intersect)
                    or "IntersectBy" or nameof(Queryable.Except) or "ExceptBy" => true,

                // With no argument of its own, only a missing row is added.
                nameof(Queryable.DefaultIfEmpty) => call.Arguments.Count == 1,

                _ => false
            };
        }

        // EF Core's own operators that load, track or tag the rows without changing which they are.
        return declaring?.Namespace == "Microsoft.EntityFrameworkCore"
               && call.Method.Name is "Include" or "ThenInclude" or "AsNoTracking"
                   or "AsNoTrackingWithIdentityResolution" or "AsTracking" or "IgnoreQueryFilters"
                   or "IgnoreAutoIncludes" or "TagWith" or "TagWithCallSite" or "AsSplitQuery" or "AsSingleQuery";
    }

    /// <summary>
    /// True when some call along the chain, or along the other source of a set operation, builds its
    /// rows or reaches them through the rows it was given: a <c>Select</c>, a <c>SelectMany</c>, a
    /// <c>Join</c>, a <c>GroupBy</c>, or anything <see cref="KeepsRows"/> does not know.
    /// </summary>
    internal static bool Reshapes(Expression expression)
    {
        for (Expression? node = expression; node is MethodCallExpression call;
             node = call.Arguments.Count > 0 ? call.Arguments[0] : null)
        {
            if (!KeepsRows(call))
            {
                return true;
            }

            if (call.Arguments.Count > 1
                && call.Method.Name is nameof(Queryable.Concat) or nameof(Queryable.Union) or "UnionBy"
                    or nameof(Queryable.Intersect) or "IntersectBy" or nameof(Queryable.Except) or "ExceptBy"
                && Reshapes(call.Arguments[1]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Finds the first query root that names an entity type.</summary>
    private sealed class RootFinder : ExpressionVisitor
    {
        public IEntityType? EntityType { get; private set; }

        public override Expression? Visit(Expression? node) => EntityType is null ? base.Visit(node) : node;

        protected override Expression VisitExtension(Expression node)
        {
            // Enumerated rather than looked up by name, which throws when a derived root hides the
            // property with a new one.
            PropertyInfo? property = RootEntityTypeProperties.GetOrAdd(
                node.GetType(),
                type => type
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(candidate => candidate.Name == "EntityType"
                                                 && candidate.GetIndexParameters().Length == 0));

            if (property?.GetValue(node) is IEntityType entityType)
            {
                EntityType = entityType;

                return node;
            }

            return base.VisitExtension(node);
        }
    }
}
