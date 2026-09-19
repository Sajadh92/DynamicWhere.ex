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
    internal static IEntityType? EntityType(Expression expression, Type type)
    {
        RootFinder finder = new();

        finder.Visit(expression);

        return finder.EntityType?.Model.FindEntityType(type);
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
