using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Policies.Resolution;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>
    /// True when a call along the chain that <see cref="KeepsRows"/> does not know can hand its rows an
    /// object the query's own includes do not account for: one a lambda constructs, one an application's
    /// method returns, and one a lambda captured, another query's rows among them.
    /// </summary>
    /// <remarks>
    /// A projection, such as the one behind <c>Select(x =&gt; x)</c>, an anonymous row or a conditional,
    /// assigns what it builds, and loads whatever navigation it assigns. A method can return anything. A
    /// query captured in a variable becomes part of the query EF Core runs, with its own includes and
    /// projections, and an object captured from memory holds whatever it holds. A value, such as a
    /// predicate's result, a key or a date, carries none of them, so what only feeds one is not read.
    /// </remarks>
    internal static bool Builds(Expression expression) => Builds(expression, depth: 0);

    private static bool Builds(Expression expression, int depth)
    {
        // A captured query that captures itself would never end; one this deep is counted as building.
        if (depth > MaxCapturedDepth)
        {
            return true;
        }

        ForeignFinder finder = new(depth);

        for (Expression? node = expression; node is MethodCallExpression call;
             node = call.Arguments.Count > 0 ? call.Arguments[0] : null)
        {
            if (call.Arguments.Count > 1
                && call.Method.Name is nameof(Queryable.Concat) or nameof(Queryable.Union) or "UnionBy"
                    or nameof(Queryable.Intersect) or "IntersectBy" or nameof(Queryable.Except) or "ExceptBy"
                && Builds(call.Arguments[1], depth))
            {
                return true;
            }

            if (KeepsRows(call))
            {
                continue;
            }

            foreach (Expression argument in call.Arguments.Skip(1))
            {
                finder.Visit(argument);

                if (finder.Found)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>How many captured queries deep <see cref="Builds(Expression)"/> reads before it stops.</summary>
    private const int MaxCapturedDepth = 8;

    /// <summary>
    /// Finds, anywhere in an expression, what can hand a row an object its query does not load: an object
    /// constructed, an application's method's result, and an object or a query captured from outside.
    /// </summary>
    private sealed class ForeignFinder : ExpressionVisitor
    {
        private readonly int _depth;

        internal ForeignFinder(int depth) => _depth = depth;

        internal bool Found { get; private set; }

        public override Expression? Visit(Expression? node) =>
            Found || node is null || IsValue(node.Type) ? node : base.Visit(node);

        protected override Expression VisitNew(NewExpression node)
        {
            // An anonymous object carries what it is given, the range variables of a query-syntax join or
            // let, or the parts of a composite key, and builds nothing of its own: what it carries is read.
            if (IsAnonymous(node.Type))
            {
                return base.VisitNew(node);
            }

            Found = true;

            return node;
        }

        protected override Expression VisitMemberInit(MemberInitExpression node)
        {
            Found = true;

            return node;
        }

        protected override Expression VisitListInit(ListInitExpression node)
        {
            Found = true;

            return node;
        }

        protected override Expression VisitNewArray(NewArrayExpression node)
        {
            Found = true;

            return node;
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            // A call that reads nothing of the lambda's and hands back a query or an expression, a
            // repository's query, a specification or FromSql, is evaluated before the query runs, as EF
            // Core evaluates it, and what it returns is read.
            if (Evaluable(node))
            {
                Found = !TryEvaluate(node, out object? value) || Holds(value, _depth);

                return node;
            }

            if (Reads(node.Method))
            {
                return base.VisitMethodCall(node);
            }

            // Any other method's result is its own to say.
            Found = true;

            return node;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (!IsCaptured(node))
            {
                return base.VisitMember(node);
            }

            Found = !TryEvaluate(node, out object? value) || Holds(value, _depth);

            return node;
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            Found = Holds(node.Value, _depth);

            return node;
        }
    }

    /// <summary>
    /// True for a method that hands back what it was given, or reads it: LINQ's operators, EF Core's
    /// <c>EF.Property</c> and query operators, a context's <c>Set</c>, which names a query root, and a
    /// context's own method returning a query, which EF Core translates only as a function the model maps.
    /// </summary>
    private static bool Reads(MethodInfo method)
    {
        Type? declaring = method.DeclaringType;

        return declaring == typeof(Queryable)
               || declaring == typeof(Enumerable)
               || declaring == typeof(EF)
               || declaring == typeof(EntityFrameworkQueryableExtensions)
               || (declaring == typeof(DbContext) && method.Name == nameof(DbContext.Set))
               || (declaring is not null && typeof(DbContext).IsAssignableFrom(declaring)
                                        && typeof(IQueryable).IsAssignableFrom(method.ReturnType));
    }

    /// <summary>True for a type the compiler generates for an anonymous object.</summary>
    private static bool IsAnonymous(Type type) =>
        type.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false)
        && type.Name.Contains("AnonymousType", StringComparison.Ordinal);

    /// <summary>
    /// True for a call EF Core evaluates before it translates the query: one that reads no parameter of the
    /// lambdas around it and hands back a query or an expression, which the query then holds.
    /// </summary>
    private static bool Evaluable(MethodCallExpression call)
    {
        if (!typeof(IQueryable).IsAssignableFrom(call.Type) && !typeof(Expression).IsAssignableFrom(call.Type))
        {
            return false;
        }

        ParameterFinder parameters = new();

        parameters.Visit(call);

        return !parameters.Found;
    }

    /// <summary>Finds a parameter of a lambda outside the expression it is given.</summary>
    private sealed class ParameterFinder : ExpressionVisitor
    {
        private readonly HashSet<ParameterExpression> _declared = new();

        internal bool Found { get; private set; }

        public override Expression? Visit(Expression? node) => Found ? node : base.Visit(node);

        protected override Expression VisitLambda<TDelegate>(Expression<TDelegate> node)
        {
            _declared.UnionWith(node.Parameters);

            return base.VisitLambda(node);
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            Found |= !_declared.Contains(node);

            return node;
        }
    }

    /// <summary>Runs a call EF Core would evaluate itself, and reads what it returns.</summary>
    private static bool TryEvaluate(MethodCallExpression call, out object? value)
    {
        try
        {
            value = Expression.Lambda<Func<object?>>(Expression.Convert(call, typeof(object)))
                .Compile(preferInterpretation: true)();

            return true;
        }
        catch (Exception)
        {
            // Whatever it throws, EF Core would throw when it ran the query; read here, it only means the
            // call counts as handing the rows anything.
            value = null;

            return false;
        }
    }

    /// <summary>True when a value of the type holds only values: a string, a number, a date, or a collection of them.</summary>
    private static bool IsValue(Type type) => CacheReflection.IsSimpleType(AttributePolicyProvider.Peeled(type));

    /// <summary>True for a member read off something the lambda captured, or off nothing, rather than off its parameters.</summary>
    private static bool IsCaptured(MemberExpression node)
    {
        Expression? target = node.Expression;

        while (target is MemberExpression member)
        {
            target = member.Expression;
        }

        return target is null or ConstantExpression;
    }

    /// <summary>Reads a captured member's value as EF Core does before it runs the query.</summary>
    private static bool TryEvaluate(MemberExpression node, out object? value)
    {
        value = null;

        object? target = null;

        if (node.Expression is ConstantExpression constant)
        {
            target = constant.Value;
        }
        else if (node.Expression is MemberExpression member && !TryEvaluate(member, out target))
        {
            return false;
        }

        try
        {
            value = node.Member switch
            {
                FieldInfo field => field.GetValue(target),
                PropertyInfo property => property.GetValue(target),
                _ => null
            };

            return node.Member is FieldInfo or PropertyInfo;
        }
        catch (Exception exception) when (exception is TargetException or TargetInvocationException
                                              or ArgumentException or InvalidOperationException
                                              or NotSupportedException or MemberAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when a captured value can hand a row an object its query does not load: an object in memory,
    /// or a query with its own includes or projections, or one over rows in memory.
    /// </summary>
    private static bool Holds(object? value, int depth)
    {
        if (value is null || value is DbContext || IsValue(value.GetType()))
        {
            return false;
        }

        if (depth > MaxCapturedDepth)
        {
            return true;
        }

        // An expression, a specification say, becomes part of the query, and is read as though written there.
        if (value is Expression expression)
        {
            ForeignFinder finder = new(depth + 1);

            finder.Visit(expression);

            return finder.Found;
        }

        if (value is not IQueryable query)
        {
            return true;
        }

        if (query.Provider is EnumerableQuery)
        {
            return !IsValue(query.ElementType);
        }

        IncludeFinder includes = new();

        includes.Visit(query.Expression);

        return includes.Found || Builds(query.Expression, depth + 1);
    }

    /// <summary>Finds an <c>Include</c> or a <c>ThenInclude</c> anywhere in a query.</summary>
    private sealed class IncludeFinder : ExpressionVisitor
    {
        internal bool Found { get; private set; }

        public override Expression? Visit(Expression? node) => Found ? node : base.Visit(node);

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(EntityFrameworkQueryableExtensions)
                && node.Method.Name is nameof(EntityFrameworkQueryableExtensions.Include)
                    or nameof(EntityFrameworkQueryableExtensions.ThenInclude))
            {
                Found = true;

                return node;
            }

            return base.VisitMethodCall(node);
        }
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
