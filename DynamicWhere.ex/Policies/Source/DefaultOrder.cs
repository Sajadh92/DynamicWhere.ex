using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Source;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

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
    /// True when a query's rows are built by a projection: the outermost <c>Select</c> of the chain
    /// constructs each row, in an initializer or with a constructor, so every member holds what the
    /// projection gave it.
    /// </summary>
    /// <remarks>
    /// A <c>Select</c> that hands back an entity, <c>Select(o =&gt; o.Customer)</c>, builds nothing: its
    /// rows are entities as EF Core loads them, whose navigations hold a value only when something
    /// includes them. This answers what the rows are, not whether a default can be applied to them,
    /// which <see cref="HidesDefault"/> answers.
    /// </remarks>
    internal static bool BuildsRows(Expression expression) =>
        RowSelect(expression) is { } select
        && StripQuotes(select.Arguments[1]) is LambdaExpression selector
        && StripConversions(selector.Body) is MemberInitExpression or NewExpression;

    /// <summary>
    /// The <c>Select</c> that makes a query's rows: the outermost one, when every call after it hands back
    /// the rows it was given. Null when nothing projects the rows, or when a call such as a
    /// <c>SelectMany</c> or a <c>Join</c> reaches the rows through it, so that its members are not theirs.
    /// </summary>
    internal static MethodCallExpression? RowSelect(Expression expression)
    {
        for (Expression? node = expression; node is MethodCallExpression call;
             node = call.Arguments.Count > 0 ? call.Arguments[0] : null)
        {
            if ((call.Method.DeclaringType == typeof(Queryable) || call.Method.DeclaringType == typeof(Enumerable))
                && call.Method.Name == nameof(Queryable.Select))
            {
                return call;
            }

            if (!QueryRoot.KeepsRows(call))
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// True when a projection along the chain could have left out a field the type's default names.
    /// </summary>
    /// <remarks>
    /// A default names fields of the type, and a projection keeps only the members it assigns, so a
    /// default ordering one it could reach for a member the projection left out and fail to translate:
    /// <c>Select(["Id", "Title"]).Page(...)</c> on a type ordered by <c>Priority</c> worked before the
    /// default existed.
    /// <para>
    /// Only the outermost projection is read, because it makes the rows the default orders. It hides
    /// nothing when it builds the type itself in an initializer — <c>new Row { Code = t.Code }</c> —
    /// and assigns every field the default names, at every level of a nested path, a column of the
    /// entity it reads on EF Core. EF Core can then translate the order. Anything else, a constructor
    /// with arguments, a member it does not assign, a value it computes or a nested path through
    /// something other than an initializer, leaves the query in whatever order it had.
    /// </para>
    /// </remarks>
    internal static bool HidesDefault(Expression expression, Type type)
    {
        if (OutermostSelect(expression) is not { } select)
        {
            return false;
        }

        if (StripQuotes(select.Arguments[1]) is not LambdaExpression selector
            || StripConversions(selector.Body) is not MemberInitExpression initializer
            || !type.IsAssignableFrom(initializer.Type))
        {
            return true;
        }

        // On EF Core a default field must be a column the database can order by. In memory anything can
        // be ordered.
        ColumnReader? columns = QueryRoot.Model(expression) is { } model
            ? new ColumnReader(selector.Parameters[0], model.FindEntityType(selector.Parameters[0].Type))
            : null;

        foreach (Entry entry in For(type))
        {
            if (!Assigns(initializer.Bindings, entry.Field.Split('.'), 0, columns))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The <c>Select</c> nearest the end of the chain, or null when nothing projects it.</summary>
    internal static MethodCallExpression? OutermostSelect(Expression expression)
    {
        for (Expression? node = expression; node is MethodCallExpression call;
             node = call.Arguments.Count > 0 ? call.Arguments[0] : null)
        {
            if ((call.Method.DeclaringType == typeof(Queryable) || call.Method.DeclaringType == typeof(Enumerable))
                && call.Method.Name == nameof(Queryable.Select))
            {
                return call;
            }
        }

        return null;
    }

    /// <summary>True when the bindings assign the path, following nested initializers down it.</summary>
    private static bool Assigns(IEnumerable<MemberBinding> bindings, string[] path, int depth, ColumnReader? columns)
    {
        MemberBinding? binding = bindings.FirstOrDefault(
            candidate => string.Equals(candidate.Member.Name, path[depth], StringComparison.Ordinal));

        if (binding is null)
        {
            return false;
        }

        if (depth == path.Length - 1)
        {
            return binding is MemberAssignment { Expression: var assigned }
                && (columns is null || columns.Column(assigned));
        }

        return binding switch
        {
            MemberAssignment { Expression: var assigned }
                when StripConversions(assigned) is MemberInitExpression nested =>
                Assigns(nested.Bindings, path, depth + 1, columns),
            MemberMemberBinding nested => Assigns(nested.Bindings, path, depth + 1, columns),
            _ => false
        };
    }

    /// <summary>
    /// Reads whether EF Core can order by what a projection assigns: a column of the entity the
    /// projection reads, directly, through reference navigations, or through <c>EF.Property</c>.
    /// </summary>
    /// <remarks>
    /// An assigned field is not enough on its own. <c>Label = Decorate(r.Code)</c> assigns the field the
    /// default names, and EF Core evaluates the application's own method on the client, where it can
    /// project the value but cannot order by it; so it does <c>Regex.Replace</c>, a member the model does
    /// not map, and any number of framework methods a given provider cannot translate. Anything but a
    /// column is therefore left out of the default, as the projection left the query unordered before:
    /// a default must never be the reason a query that ran unguarded fails.
    /// </remarks>
    private sealed class ColumnReader
    {
        private readonly ParameterExpression _row;
        private readonly IEntityType? _source;

        internal ColumnReader(ParameterExpression row, IEntityType? source)
        {
            _row = row;
            _source = source;
        }

        /// <summary>True when the expression reads one column the model maps.</summary>
        internal bool Column(Expression expression)
        {
            expression = StripConversions(expression);

            return Read(expression) is ({ } owner, { } name) && owner.FindProperty(name) is not null;
        }

        /// <summary>The entity a navigation expression reaches, through reference navigations only.</summary>
        private IEntityType? EntityOf(Expression expression)
        {
            expression = StripConversions(expression);

            if (expression == _row)
            {
                return _source;
            }

            // A reference, read the way EF Core 6 reads IsCollection: the property that moved in EF Core 8
            // is not bound, so the check holds on either.
            return Read(expression) is ({ } owner, { } name)
                   && owner.FindNavigation(name) is { } navigation
                   && (navigation.IsOnDependent || navigation.ForeignKey.IsUnique)
                ? navigation.TargetEntityType
                : null;
        }

        /// <summary>The entity a member read is made on, and the member's name.</summary>
        private (IEntityType? Owner, string? Name) Read(Expression expression) => expression switch
        {
            MemberExpression { Member: PropertyInfo property, Expression: { } owner } =>
                (EntityOf(owner), property.Name),
            MethodCallExpression { Method.Name: nameof(EF.Property) } call
                when call.Method.DeclaringType == typeof(EF)
                     && call.Arguments[1] is ConstantExpression { Value: string name } =>
                (EntityOf(call.Arguments[0]), name),
            _ => (null, null)
        };
    }

    internal static Expression StripQuotes(Expression expression) =>
        expression is UnaryExpression { NodeType: ExpressionType.Quote } quote ? quote.Operand : expression;

    internal static Expression StripConversions(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.TypeAs } conversion)
        {
            expression = conversion.Operand;
        }

        return expression;
    }

    /// <summary>
    /// True when a guarded query over this source takes the type's default: nothing has ordered it and
    /// no projection hides a field the default names.
    /// </summary>
    internal static bool Applies(Expression expression, Type type) =>
        !IsOrdered(expression) && !HidesDefault(expression, type);

    /// <summary>
    /// Everything wrong with a type's declared default: entries that cannot be read, fields no query can
    /// order by and fields whose name the expression parser keeps for itself, which are errors, and
    /// fields the type does not have, which are skipped and so only warnings.
    /// </summary>
    internal static (List<string> Malformed, List<string> Unknown, List<string> Unorderable, List<string> Reserved)
        Problems(Type type)
    {
        List<string> malformed = new();
        List<string> unknown = new();
        List<string> unorderable = new();
        List<string> reserved = new();

        foreach (string part in Parts(type))
        {
            if (!TryParse(part, out string field, out _))
            {
                malformed.Add(part.Trim());
            }
            else if (ReservedNames.Starts(field))
            {
                // Before the path is validated, which refuses such a field without saying why: the type
                // may well have the member, and the reason no query can reach it is the parser's.
                reserved.Add(field);
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

        return (malformed, unknown, unorderable, reserved);
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
