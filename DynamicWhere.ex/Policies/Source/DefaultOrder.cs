using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Source;

namespace DynamicWhere.ex.Policies.Source;

/// <summary>
/// The order a type declares for a guarded query whose caller sends none:
/// <see cref="DwEntityAttribute.DefaultOrder"/>.
/// </summary>
/// <remarks>
/// Nothing is ever ordered by a default the type's own code did not declare. An entry naming a field
/// the type does not have is skipped, as is an entry that is not a field and a direction, and one the
/// core refuses to order by, such as a collection of entities. So a default can make a query stable
/// but can never make the library refuse one; <c>PolicyModelValidator</c> reports all three at
/// startup.
/// <para>
/// Only the sanitizer applies one, and only with the fields this caller may order by: ordering by any
/// other would rank rows by a value the caller is not allowed to see. The core never reads the
/// declaration, so an unguarded query is ordered only as its caller asks.
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

    /// <summary>The core's own conversion of an order clause, which refuses what it cannot sort by.</summary>
    private static readonly MethodInfo OrderAsString = typeof(Converter)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.Name == nameof(Converter.AsString)
                          && method.GetParameters()[0].ParameterType == typeof(OrderBy));

    /// <summary>The usable entries a type declares, or none.</summary>
    internal static IReadOnlyList<Entry> For(Type type) => Declared.GetOrAdd(type, Read);

    /// <summary>The entries as the order clauses a caller would have sent.</summary>
    internal static List<OrderBy> ToOrders(IEnumerable<Entry> entries) =>
        entries.Select((entry, index) => new OrderBy { Sort = index, Field = entry.Field, Direction = entry.Direction })
            .ToList();

    /// <summary>
    /// True when a query already sorts its rows somewhere along the chain that produced it.
    /// </summary>
    /// <remarks>
    /// Walks the source argument of each call, which is where a composed query keeps what came before
    /// it: <c>Order(...).Where(...).Page(...)</c> is still ordered when it reaches the page, and so is
    /// <c>db.Products.OrderBy(p => p.Name)</c> guarded afterwards. A default applied to such a query
    /// would replace the order the caller chose.
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
    /// Everything wrong with a type's declared default: entries that cannot be read and fields no query
    /// can order by, which are errors, and fields the type does not have, which are skipped and so only
    /// warnings.
    /// </summary>
    internal static (List<string> Malformed, List<string> Unknown, List<string> Unorderable) Problems(Type type)
    {
        List<string> malformed = new();
        List<string> unknown = new();
        List<string> unorderable = new();

        foreach (string part in Parts(type))
        {
            if (!TryParse(part, out string field, out _))
            {
                malformed.Add(part.Trim());
            }
            else if (Canonical(type, field) is not { } canonical)
            {
                unknown.Add(field);
            }
            else if (!Orderable(type, canonical))
            {
                unorderable.Add(field);
            }
        }

        return (malformed, unknown, unorderable);
    }

    private static IReadOnlyList<Entry> Read(Type type)
    {
        List<Entry> entries = new();

        foreach (string part in Parts(type))
        {
            if (!TryParse(part, out string field, out Direction direction)
                || Canonical(type, field) is not { } canonical
                || !Orderable(type, canonical)
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

    /// <summary>
    /// True when the core would build an order for the field, as it would for a caller who sent it.
    /// </summary>
    /// <remarks>
    /// Asked of the core's own conversion rather than a copy of its rules, so the two cannot drift. It
    /// refuses a path that ends on a collection of entities, which holds no single value to compare,
    /// and sorts a path through a collection to a value by that value's smallest or largest.
    /// </remarks>
    private static bool Orderable(Type type, string field)
    {
        try
        {
            OrderAsString.MakeGenericMethod(type).Invoke(null, new object[] { new OrderBy { Field = field } });

            return true;
        }
        catch (TargetInvocationException invocation) when (invocation.InnerException is LogicException)
        {
            return false;
        }
    }
}
