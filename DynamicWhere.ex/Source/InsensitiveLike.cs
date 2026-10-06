using System.Linq.Expressions;
using System.Reflection;
using DynamicWhere.ex.Enums;
using Microsoft.EntityFrameworkCore;

namespace DynamicWhere.ex.Source;

/// <summary>
/// Rewrites the case-insensitive pattern operators of a parsed filter into Npgsql's <c>EF.Functions.ILike</c>,
/// when the process chose <see cref="TextMatching.ILike"/>.
/// </summary>
/// <remarks>
/// The predicate text is built the way it always was, <c>x.Name.ToLower().Contains("abc")</c>, and parsed the
/// way it always was. What changes is the parsed lambda: each <c>ToLower()</c> followed by <c>Contains</c>,
/// <c>StartsWith</c> or <c>EndsWith</c> with a constant becomes <c>EF.Functions.ILike(x.Name, "%abc%", "\")</c>,
/// with <c>%</c>, <c>_</c> and the escape character in the value escaped, so the value still matches as text and
/// never as a pattern. Those three calls on a lowered member are what the six pattern operators build and
/// nothing else does, so the rewrite needs nothing from the builder; a shape it does not recognise is left as
/// it is, which only ever costs the index, never the result. The negations keep their <c>!</c> around the
/// call, and the null guard in front of it.
/// <para>
/// Only a query EF Core's own provider translates is rewritten. <c>ILike</c> has no meaning in memory, and a
/// provider wrapping EF Core rewrites expressions for its own purposes, so both keep the lowered form.
/// </para>
/// </remarks>
internal static class InsensitiveLike
{
    /// <summary>The escape character every pattern is written with.</summary>
    private const string EscapeCharacter = "\\";

    private static readonly MethodInfo ToLower = typeof(string).GetMethod(nameof(string.ToLower), Type.EmptyTypes)!;
    private static readonly MethodInfo Contains = typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;
    private static readonly MethodInfo StartsWith = typeof(string).GetMethod(nameof(string.StartsWith), new[] { typeof(string) })!;
    private static readonly MethodInfo EndsWith = typeof(string).GetMethod(nameof(string.EndsWith), new[] { typeof(string) })!;

    /// <summary>
    /// <c>NpgsqlDbFunctionsExtensions.ILike(DbFunctions, string, string, string)</c>, or null when Npgsql's EF
    /// Core provider cannot be loaded.
    /// </summary>
    /// <remarks>
    /// Found by name, so the package references no provider. The overload with an escape character has existed
    /// beside the plain one since Npgsql 2.
    /// </remarks>
    internal static MethodInfo? Resolve()
    {
        Type? extensions = Type.GetType(
            "Microsoft.EntityFrameworkCore.NpgsqlDbFunctionsExtensions, Npgsql.EntityFrameworkCore.PostgreSQL",
            throwOnError: false);

        return extensions?.GetMethod(
            "ILike",
            BindingFlags.Public | BindingFlags.Static,
            null,
            new[] { typeof(DbFunctions), typeof(string), typeof(string), typeof(string) },
            null);
    }

    /// <summary>
    /// The filtered query with its pattern operators matched by <c>ILIKE</c>, or the same query when the process
    /// matches by lowering, the query is not EF Core's, or there is nothing to rewrite.
    /// </summary>
    /// <param name="filtered">The query <c>Where</c> just returned, whose outermost call is that <c>Where</c>.</param>
    internal static IQueryable<T> Apply<T>(IQueryable<T> filtered)
    {
        MethodInfo? iLike = DwText.Current.ILike;

        if (iLike is null || !EfCoreOwns(filtered.Provider))
        {
            return filtered;
        }

        // Only the predicate the library just parsed: whatever the caller composed beneath it is theirs.
        if (filtered.Expression is not MethodCallExpression call
            || call.Method.DeclaringType != typeof(Queryable)
            || call.Method.Name != nameof(Queryable.Where)
            || call.Arguments.Count != 2
            || call.Arguments[1] is not UnaryExpression { NodeType: ExpressionType.Quote, Operand: LambdaExpression predicate })
        {
            return filtered;
        }

        Expression rewritten = new Rewriter(iLike).Visit(predicate);

        return ReferenceEquals(rewritten, predicate)
            ? filtered
            : filtered.Provider.CreateQuery<T>(call.Update(call.Object, new[] { call.Arguments[0], Expression.Quote(rewritten) }));
    }

    /// <summary>
    /// A value written into a pattern so it matches only itself: the escape character, <c>%</c> and <c>_</c>
    /// each escaped.
    /// </summary>
    internal static string Escape(string value) =>
        value.Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter)
            .Replace("%", EscapeCharacter + "%")
            .Replace("_", EscapeCharacter + "_");

    /// <summary>True when EF Core's own provider is the one that will translate the query.</summary>
    /// <remarks>
    /// Matched by name and by the assembly it was declared in, as the policy layer matches it: no internal type
    /// is referenced, EF Core 6 to 10 answer the same, and a wrapper deriving from it is not taken for it.
    /// </remarks>
    private static bool EfCoreOwns(IQueryProvider provider)
    {
        Type type = provider.GetType();

        return type.FullName == "Microsoft.EntityFrameworkCore.Query.Internal.EntityQueryProvider"
               && type.Assembly.GetName().Name == "Microsoft.EntityFrameworkCore";
    }

    /// <summary>Turns <c>member.ToLower().Contains("value")</c> and its two siblings into <c>ILike</c>.</summary>
    private sealed class Rewriter : ExpressionVisitor
    {
        private readonly MethodInfo _iLike;

        /// <summary><c>EF.Functions</c>, the receiver every provider function is called on.</summary>
        private readonly Expression _functions =
            Expression.Property(null, typeof(EF).GetProperty(nameof(EF.Functions), BindingFlags.Public | BindingFlags.Static)!);

        internal Rewriter(MethodInfo iLike) => _iLike = iLike;

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Object is MethodCallExpression { Object: { } member } lowered
                && lowered.Method == ToLower
                && node.Arguments.Count == 1
                && node.Arguments[0] is ConstantExpression { Value: string value })
            {
                string? pattern =
                    node.Method == Contains ? "%" + Escape(value) + "%"
                    : node.Method == StartsWith ? Escape(value) + "%"
                    : node.Method == EndsWith ? "%" + Escape(value)
                    : null;

                if (pattern is not null)
                {
                    return Expression.Call(
                        _iLike, _functions, Visit(member), Expression.Constant(pattern), Expression.Constant(EscapeCharacter));
                }
            }

            return base.VisitMethodCall(node);
        }
    }
}
