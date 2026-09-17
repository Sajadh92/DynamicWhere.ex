using System.Collections.Concurrent;
using System.Linq.Expressions;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Source;

namespace DynamicWhere.ex.Source;

/// <summary>
/// The order a type declares for a query whose caller sends none:
/// <see cref="DwEntityAttribute.DefaultOrder"/>.
/// </summary>
/// <remarks>
/// Nothing is ever ordered by a default the type's own code did not declare. An entry naming a field
/// the type does not have is skipped, as is an entry that is not a field and a direction, so a
/// default can make a query stable but can never make one fail; <c>PolicyModelValidator</c> reports
/// both at startup.
/// <para>
/// Under a policy the sanitizer decides the default instead, because a default field the caller may
/// not order by must be left out, and ordering by it anyway would rank rows by a value the caller is
/// not allowed to see. So the core never applies one while a policy scope is active.
/// </para>
/// </remarks>
internal static class DefaultOrder
{
    /// <summary>One field of a declared default order, in canonical form.</summary>
    internal sealed class Entry
    {
        internal Entry(string field, Direction direction)
        {
            Field = field;
            Direction = direction;
        }

        /// <summary>The field, as the pipeline spells it.</summary>
        internal string Field { get; }

        /// <summary>The direction.</summary>
        internal Direction Direction { get; }
    }

    private static readonly ConcurrentDictionary<Type, IReadOnlyList<Entry>> Declared = new();

    /// <summary>The usable entries a type declares, or none.</summary>
    internal static IReadOnlyList<Entry> For(Type type) => Declared.GetOrAdd(type, Read);

    /// <summary>
    /// Orders a query by its type's declared default, unless a policy is deciding the order, the query
    /// is already ordered, or the type declares none.
    /// </summary>
    /// <remarks>
    /// A query ordered before it reached the library — <c>db.Products.OrderBy(p => p.Name).ToList(filter)</c>
    /// — keeps that order, as it always did.
    /// </remarks>
    internal static IQueryable<T> Apply<T>(IQueryable<T> query) where T : class
    {
        if (PolicyScope.Current is not null || IsOrdered(query.Expression))
        {
            return query;
        }

        IReadOnlyList<Entry> entries = For(typeof(T));

        return entries.Count == 0 ? query : query.Order(ToOrders(entries));
    }

    /// <summary>The entries as the order clauses a caller would have sent.</summary>
    internal static List<OrderBy> ToOrders(IEnumerable<Entry> entries) =>
        entries.Select((entry, index) => new OrderBy { Sort = index, Field = entry.Field, Direction = entry.Direction })
            .ToList();

    /// <summary>
    /// True when a query already sorts its rows somewhere along the chain that produced it.
    /// </summary>
    /// <remarks>
    /// Walks the source argument of each call, which is where a composed query keeps what came before
    /// it: <c>Order(...).Where(...).Page(...)</c> is still ordered when it reaches the page. A default
    /// applied to such a query would replace the order the caller chose.
    /// </remarks>
    internal static bool IsOrdered(Expression expression)
    {
        for (Expression? node = expression; node is MethodCallExpression call;
             node = call.Arguments.Count > 0 ? call.Arguments[0] : null)
        {
            if ((call.Method.DeclaringType == typeof(Queryable) || call.Method.DeclaringType == typeof(Enumerable))
                && call.Method.Name is nameof(Queryable.OrderBy) or nameof(Queryable.OrderByDescending)
                    or nameof(Queryable.ThenBy) or nameof(Queryable.ThenByDescending))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Everything wrong with a type's declared default: entries that cannot be read, which are errors,
    /// and fields the type does not have, which are skipped and so only warnings.
    /// </summary>
    internal static (List<string> Malformed, List<string> Unknown) Problems(Type type)
    {
        List<string> malformed = new();
        List<string> unknown = new();

        foreach (string part in Parts(type))
        {
            if (!TryParse(part, out string field, out _))
            {
                malformed.Add(part.Trim());
            }
            else if (Canonical(type, field) is null)
            {
                unknown.Add(field);
            }
        }

        return (malformed, unknown);
    }

    private static IReadOnlyList<Entry> Read(Type type)
    {
        List<Entry> entries = new();

        foreach (string part in Parts(type))
        {
            if (!TryParse(part, out string field, out Direction direction)
                || Canonical(type, field) is not { } canonical
                || entries.Exists(entry => string.Equals(entry.Field, canonical, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            entries.Add(new Entry(canonical, direction));
        }

        return entries;
    }

    private static IEnumerable<string> Parts(Type type)
    {
        string? declared = (Attribute.GetCustomAttribute(type, typeof(DwEntityAttribute)) as DwEntityAttribute)
            ?.DefaultOrder;

        // A blank entry, such as the one a trailing comma leaves, says nothing and is not a mistake.
        return string.IsNullOrWhiteSpace(declared)
            ? Array.Empty<string>()
            : declared.Split(',').Where(part => !string.IsNullOrWhiteSpace(part));
    }

    /// <summary>Reads <c>Field</c>, <c>Field asc</c> or <c>Field desc</c>, in any letter case.</summary>
    internal static bool TryParse(string part, out string field, out Direction direction)
    {
        string[] words = part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        field = words.Length > 0 ? words[0] : string.Empty;
        direction = Direction.Ascending;

        if (words.Length == 1)
        {
            return true;
        }

        if (words.Length != 2)
        {
            return false;
        }

        if (string.Equals(words[1], "asc", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(words[1], "desc", StringComparison.OrdinalIgnoreCase))
        {
            direction = Direction.Descending;

            return true;
        }

        return false;
    }

    private static string? Canonical(Type type, string field)
    {
        try
        {
            return CacheReflection.ValidatePropertyPath(type, field);
        }
        catch (LogicException)
        {
            return null;
        }
    }
}
