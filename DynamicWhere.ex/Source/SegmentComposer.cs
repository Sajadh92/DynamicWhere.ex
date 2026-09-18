using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DynamicWhere.ex.Source;

/// <summary>
/// Turns a segment's condition sets into one query for the database to answer.
/// </summary>
/// <remarks>
/// A segment used to load each set into a list and combine the lists with LINQ's <c>Union</c>,
/// <c>Intersect</c> and <c>Except</c>. Those compare objects by reference, so they were right only when
/// every set handed back the same instances: a tracking query from one context, with no projection.
/// Under <c>AsNoTracking</c>, which every guarded query runs over, or with any projection, each set
/// built objects of its own. Intersect then found nothing, Except removed nothing, and Union counted a
/// row once for every set that matched it. Every row of every set was also loaded before the page
/// was cut.
/// <para>
/// Every set filters the same source, so whether a row belongs to a set is a property of the row.
/// Union and Intersect become OR and AND of the sets' own conditions on a single query, and the
/// ordering, paging, projection and count that follow run in the database as they do for a filter.
/// </para>
/// <para>
/// Except is written as <c>NOT EXISTS</c> over the excluded set, matched on the primary key, rather
/// than as NOT of that set's condition. NOT of a condition is right only while the condition is never
/// NULL. The conditions this library builds guard every nullable member and every optional navigation,
/// so today none is, but an exclusion should not rest on how every predicate happens to be built, and
/// <c>EXISTS</c> is only ever true or false. A key the data does not keep unique can only make it
/// exclude too much, never return a row no set admitted.
/// </para>
/// <para>
/// A type with no primary key has no row identity to match on. Its sets are combined with the
/// database's UNION, INTERSECT and EXCEPT, which compare whole rows: every column has to be a type the
/// database can compare, and the provider has to support all three.
/// </para>
/// </remarks>
internal static class SegmentComposer
{
    /// <summary>The <c>EntityType</c> property of each query-root expression type, or null for a type with none.</summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> RootEntityTypeProperties = new();

    /// <summary>
    /// Combines validated condition sets, already in <c>Sort</c> order, into one query.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The source every set filters.</param>
    /// <param name="sets">The sets, as <c>ValidateAndGetSets</c> returns them: at least one, the first with no intersection.</param>
    /// <returns>A query holding exactly the rows the combination admits.</returns>
    internal static IQueryable<T> Compose<T>(IQueryable<T> query, IReadOnlyList<ConditionSet> sets) where T : class
    {
        if (sets.Count == 1)
        {
            return query.Where(sets[0].ConditionGroup);
        }

        IReadOnlyList<IProperty>? key = PrimaryKey(query);

        return key is null ? CombineRows(query, sets) : CombineConditions(query, sets, key);
    }

    /// <summary>
    /// Combines the sets as one predicate over <paramref name="query"/>'s rows.
    /// </summary>
    private static IQueryable<T> CombineConditions<T>(
        IQueryable<T> query, IReadOnlyList<ConditionSet> sets, IReadOnlyList<IProperty> key)
        where T : class
    {
        ParameterExpression row = Expression.Parameter(typeof(T), "row");
        Expression? body = null;

        foreach (ConditionSet set in sets)
        {
            IQueryable<T> members = query.Where(set.ConditionGroup);

            if (body is null)
            {
                body = ConditionOn(query, members, key, row);

                continue;
            }

            // An intersection outside the enum is skipped, as the in-memory combination skipped it.
            body = set.Intersection switch
            {
                Intersection.Union => Expression.OrElse(body, ConditionOn(query, members, key, row)),
                Intersection.Intersect => Expression.AndAlso(body, ConditionOn(query, members, key, row)),
                Intersection.Except => Expression.AndAlso(body, Expression.Not(HoldsRow(members, key, row))),
                _ => body
            };
        }

        return query.Where(Expression.Lambda<Func<T, bool>>(body!, row));
    }

    /// <summary>
    /// Combines the sets with the database's own set operators, comparing whole rows.
    /// </summary>
    private static IQueryable<T> CombineRows<T>(IQueryable<T> query, IReadOnlyList<ConditionSet> sets)
        where T : class
    {
        IQueryable<T> combined = query.Where(sets[0].ConditionGroup);

        foreach (ConditionSet set in sets.Skip(1))
        {
            combined = set.Intersection switch
            {
                Intersection.Union => combined.Union(query.Where(set.ConditionGroup)),
                Intersection.Intersect => combined.Intersect(query.Where(set.ConditionGroup)),
                Intersection.Except => combined.Except(query.Where(set.ConditionGroup)),
                _ => combined
            };
        }

        return combined;
    }

    /// <summary>
    /// The condition a set applied to <paramref name="query"/>, rewritten to test <paramref name="row"/>.
    /// </summary>
    /// <remarks>
    /// Read back from the query <c>Where(ConditionGroup)</c> built rather than built a second time, so
    /// the predicate is exactly the one a filter with that group runs. A group with no conditions
    /// leaves the query as it was, and admits every row.
    /// </remarks>
    private static Expression ConditionOn<T>(
        IQueryable<T> query, IQueryable<T> members, IReadOnlyList<IProperty> key, ParameterExpression row)
    {
        if (ReferenceEquals(members.Expression, query.Expression))
        {
            return Expression.Constant(true);
        }

        if (members.Expression is MethodCallExpression { Method.Name: nameof(Queryable.Where) } call
            && call.Method.DeclaringType == typeof(Queryable)
            && ReferenceEquals(call.Arguments[0], query.Expression)
            && call.Arguments[1] is UnaryExpression { Operand: LambdaExpression predicate })
        {
            return new ParameterSwap(predicate.Parameters[0], row).Visit(predicate.Body);
        }

        // Not a shape Where(ConditionGroup) produces today. Membership by key is still exact.
        return HoldsRow(members, key, row);
    }

    /// <summary>
    /// <c>EXISTS</c> over <paramref name="members"/>, matching <paramref name="row"/> on every key property.
    /// </summary>
    private static Expression HoldsRow<T>(IQueryable<T> members, IReadOnlyList<IProperty> key, ParameterExpression row)
    {
        ParameterExpression member = Expression.Parameter(typeof(T), "member");

        Expression sameRow = key
            .Select(property => SameValue(Read(member, property), Read(row, property)))
            .Aggregate(Expression.AndAlso);

        return Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Any),
            new[] { typeof(T) },
            members.Expression,
            Expression.Quote(Expression.Lambda<Func<T, bool>>(sameRow, member)));
    }

    /// <summary>A key property read through <c>EF.Property</c>, which reaches shadow keys as well.</summary>
    private static Expression Read(ParameterExpression instance, IProperty property) =>
        Expression.Call(
            typeof(EF), nameof(EF.Property), new[] { property.ClrType }, instance, Expression.Constant(property.Name));

    /// <summary>
    /// Equality of two key values, including a key type that defines no <c>==</c>.
    /// </summary>
    /// <remarks>
    /// A struct key behind a value converter often has no equality operator, and
    /// <see cref="Expression.Equal(Expression, Expression)"/> refuses to build one. EF Core translates
    /// <see cref="object.Equals(object, object)"/> for such a type as the same column comparison.
    /// </remarks>
    private static Expression SameValue(Expression left, Expression right)
    {
        Type type = left.Type;

        bool hasEquality = !type.IsValueType
            || type.IsPrimitive
            || type.IsEnum
            || Nullable.GetUnderlyingType(type) is not null
            || type.GetMethod("op_Equality", BindingFlags.Public | BindingFlags.Static, null, new[] { type, type }, null) is not null;

        if (hasEquality)
        {
            return Expression.Equal(left, right);
        }

        return Expression.Call(
            typeof(object),
            nameof(Equals),
            null,
            Expression.Convert(left, typeof(object)),
            Expression.Convert(right, typeof(object)));
    }

    /// <summary>
    /// The primary key EF Core maps for <typeparamref name="T"/>, or null when the query is not an EF Core
    /// query or the type has none.
    /// </summary>
    /// <remarks>
    /// Read from the model behind the query's root. The root expression's type differs between EF Core
    /// versions — <c>QueryRootExpression</c> in 6, <c>EntityQueryRootExpression</c> from 7 — and both
    /// carry the entity type in a property named <c>EntityType</c>, so the property is found by name.
    /// Any root reaches the model, and the model is asked for <typeparamref name="T"/> itself, which
    /// also answers for a derived type queried through <c>OfType</c>.
    /// </remarks>
    private static IReadOnlyList<IProperty>? PrimaryKey<T>(IQueryable<T> query)
    {
        RootFinder finder = new();

        finder.Visit(query.Expression);

        return finder.EntityType?.Model.FindEntityType(typeof(T))?.FindPrimaryKey()?.Properties;
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

    /// <summary>Replaces one lambda parameter with another expression.</summary>
    private sealed class ParameterSwap : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly Expression _to;

        public ParameterSwap(ParameterExpression from, Expression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node) =>
            node == _from ? _to : base.VisitParameter(node);
    }
}
