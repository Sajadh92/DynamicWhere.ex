using System.Collections;
using System.Linq.Dynamic.Core;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace DynamicWhere.ex.Source;

/// <summary>
/// Asynchronous reads of a query whose element type is only known at run time: a dynamic projection or
/// a grouping.
/// </summary>
/// <remarks>
/// EF Core's own asynchronous operators are generic in the element type, and a dynamic query's element
/// type is only known at run time, so they are reached by reflection. Through them a cancellation reaches
/// the database. They replace System.Linq.Dynamic.Core's <c>ToDynamicListAsync</c> on an EF Core query, and
/// <c>Count()</c>, which counted a grouping synchronously.
/// <para>
/// Any other provider, rows in memory among them, keeps <c>ToDynamicListAsync</c>, which reads on the
/// calling thread.
/// </para>
/// </remarks>
internal static class AsyncReads
{
    /// <summary><c>EntityFrameworkQueryableExtensions.ToListAsync&lt;TSource&gt;(source, cancellationToken)</c>.</summary>
    private static readonly MethodInfo ToListAsyncMethod = typeof(EntityFrameworkQueryableExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(EntityFrameworkQueryableExtensions.ToListAsync)
                          && method.GetParameters().Length == 2);

    /// <summary><c>EntityFrameworkQueryableExtensions.CountAsync&lt;TSource&gt;(source, cancellationToken)</c>.</summary>
    private static readonly MethodInfo CountAsyncMethod = typeof(EntityFrameworkQueryableExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(EntityFrameworkQueryableExtensions.CountAsync)
                          && method.GetParameters().Length == 2
                          && method.GetParameters()[1].ParameterType == typeof(CancellationToken));

    /// <summary>Reads every row of a query into a list of dynamic objects.</summary>
    internal static async Task<List<dynamic>> ToDynamicListAsync(IQueryable query, CancellationToken cancellationToken)
    {
        if (query.Provider is not IAsyncQueryProvider)
        {
            return await query.ToDynamicListAsync(cancellationToken);
        }

        Task read = (Task)Call(ToListAsyncMethod, query, cancellationToken);

        await read;

        IList rows = (IList)read.GetType().GetProperty(nameof(Task<object>.Result))!.GetValue(read)!;
        List<dynamic> list = new(rows.Count);

        foreach (object? row in rows)
        {
            list.Add(row!);
        }

        return list;
    }

    /// <summary>Counts the rows of a query.</summary>
    internal static async Task<int> CountAsync(IQueryable query, CancellationToken cancellationToken)
    {
        if (query.Provider is not IAsyncQueryProvider)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return query.Count();
        }

        return await (Task<int>)Call(CountAsyncMethod, query, cancellationToken);
    }

    /// <summary>
    /// Calls one of EF Core's operators for the query's element type, letting whatever it throws leave as
    /// itself: a query EF Core cannot translate fails with its own exception, not one wrapped by reflection.
    /// </summary>
    private static object Call(MethodInfo operatorMethod, IQueryable query, CancellationToken cancellationToken) =>
        operatorMethod
            .MakeGenericMethod(query.ElementType)
            .Invoke(null, BindingFlags.DoNotWrapExceptions, null, new object[] { query, cancellationToken }, null)!;
}
