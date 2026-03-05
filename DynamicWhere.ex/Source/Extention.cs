using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Classes.Result;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Source;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

namespace DynamicWhere.ex.Source;

/// <summary>
/// Extension methods for building dynamic LINQ queries over <see cref="IQueryable{T}"/> and <see cref="IEnumerable{T}"/>.
/// These helpers rely on <c>System.Linq.Dynamic.Core</c> to translate string-based expressions.
/// Enhanced with reflection caching for improved performance.
/// </summary>
public static class Extension
{
    /// <summary>
    /// Projects each element of the source query to a new instance of type T containing only the specified fields.
    /// </summary>
    /// <remarks>This method creates a projection that includes only the specified fields for each element in
    /// the query. The resulting IQueryable<T> can be further composed in LINQ queries. All field names must match
    /// properties on type T. If the select string is empty or whitespace, the original query is returned.</remarks>
    /// <typeparam name="T">The type of the elements in the source query. Must be a reference type with a parameterless constructor.</typeparam>
    /// <param name="query">The source query to project fields from. Cannot be null.</param>
    /// <param name="fields">A list of field names to include in the projection. Each field must correspond to a property on type T. Cannot
    /// be null or empty.</param>
    /// <returns>An IQueryable<T> where each element contains only the specified fields from the original query.</returns>
    /// <exception cref="ArgumentNullException">Thrown if query or fields is null.</exception>
    /// <exception cref="LogicException">Thrown if fields is empty, or if type T does not have a parameterless constructor.</exception>
    public static IQueryable<T> Select<T>(this IQueryable<T> query, List<string> fields) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (fields == null)
        {
            throw new ArgumentNullException(nameof(fields));
        }

        if (fields.Count == 0)
        {
            throw new LogicException(ErrorCode.MustHaveFields);
        }

        // Validate each field if it exists in the query.
        for (int i = 0; i < fields.Count; i++)
        {
            fields[i] = fields[i].Validate<T>();
        }

        // Apply the select to the query and return a new query.
        // Build a strongly-typed projection to T so EF Core can translate it.
        // This keeps the public API returning IQueryable<T> while selecting only requested scalar members.
        // Requirement: T must have a parameterless constructor.
        if (typeof(T).GetConstructor(Type.EmptyTypes) is null)
        {
            throw new LogicException("Select projection requires a parameterless constructor on type '" + typeof(T).Name + "'.");
        }

        // Build the strongly-typed projection expression.
        var selector = Converter.BuildTypedSelectExpression<T>(fields);

        // Apply the projection to the query.
        return query.Select(selector);
    }

    /// <summary>
    /// Projects each element of the source query into a dynamic anonymous type containing only the specified fields,
    /// using <c>System.Linq.Dynamic.Core</c>'s string-based <c>Select</c>.
    /// </summary>
    /// <remarks>
    /// Fields are projected as follows:
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Direct scalar or whole-object paths</b> (non-dotted) — projected as-is
    ///     (e.g., <c>"Id"</c>, <c>"Category"</c> (whole object), <c>"Brands"</c> (whole collection)).
    ///   </description></item>
    ///   <item><description>
    ///     <b>Dotted paths through reference navigations</b> — projected as nested dynamic objects:
    ///     <c>"Category.Name"</c> → <c>Category: { Name: "…" }</c>,
    ///     <c>"Category.SubCategory.Name"</c> → <c>Category: { SubCategory: { Name: "…" } }</c>.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Dotted paths through collection navigations</b> — each collection segment is projected
    ///     using a typed <c>Select</c> lambda so individual element fields can be extracted:
    ///     <c>"Category.Vendors.Id"</c> → <c>Category: { Vendors: [{ Id: … }] }</c>.
    ///     Nested collections are supported at any depth.
    ///   </description></item>
    /// </list>
    /// <para>
    /// When both a whole-navigation field (e.g., <c>"Category"</c>) and sub-field paths sharing the same
    /// root segment (e.g., <c>"Category.Name"</c>) are requested, the sub-field projection takes precedence
    /// and the whole-navigation entry is silently dropped to avoid duplicate property names.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The type of the elements in the source query. Must be a reference type.</typeparam>
    /// <param name="query">The source query to project fields from. Cannot be null.</param>
    /// <param name="fields">A list of field names to include in the projection. Each field must correspond to a property on type T. Cannot be null or empty.</param>
    /// <returns>An <see cref="IQueryable"/> where each element is a dynamic object containing only the specified fields.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="query"/> or <paramref name="fields"/> is null.</exception>
    /// <exception cref="LogicException">Thrown if <paramref name="fields"/> is empty or contains an invalid field name.</exception>
    public static IQueryable SelectDynamic<T>(this IQueryable<T> query, List<string> fields) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (fields == null)
        {
            throw new ArgumentNullException(nameof(fields));
        }

        if (fields.Count == 0)
        {
            throw new LogicException(ErrorCode.MustHaveFields);
        }

        // Validate each field against type T.
        for (int i = 0; i < fields.Count; i++)
        {
            fields[i] = fields[i].Validate<T>();
        }

        // Build a type-aware "new(...)" selector string that reflects the navigation hierarchy.
        // Non-dotted fields are projected as-is; dotted paths through collections use Select lambdas.
        string selector = Converter.BuildDynamicSelectString(fields, typeof(T));

        // Apply the string-based Dynamic LINQ select and return the non-generic IQueryable.
        return query.Select(selector);
    }

    /// <summary>
    /// Applies filtering conditions to the <see cref="IQueryable{T}"/> based on a <see cref="Condition"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> to filter.</param>
    /// <param name="condition">The <see cref="Condition"/> containing filtering conditions.</param>
    /// <returns>The filtered <see cref="IQueryable{T}"/> based on the <see cref="Condition"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="query"/> or <paramref name="condition"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="condition"/> contains invalid data.</exception>
    public static IQueryable<T> Where<T>(this IQueryable<T> query, Condition condition) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (condition == null)
        {
            throw new ArgumentNullException(nameof(condition));
        }

        // Convert Condition to a string representation and apply filtering.
        string where = condition.AsString<T>();

        // If the resulting where string is null or empty, return the original query.
        if (string.IsNullOrWhiteSpace(where))
        {
            return query;
        }

        // Apply the filter to the query and return the result.
        return query.Where(where);
    }

    /// <summary>
    /// Applies filtering conditions to the <see cref="IQueryable{T}"/> based on a <see cref="ConditionGroup"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> to filter.</param>
    /// <param name="group">The <see cref="ConditionGroup"/> containing filtering conditions.</param>
    /// <returns>The filtered <see cref="IQueryable{T}"/> based on the <see cref="ConditionGroup"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="query"/> or <paramref name="group"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="group"/> contains invalid data.</exception>
    public static IQueryable<T> Where<T>(this IQueryable<T> query, ConditionGroup group) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (group == null)
        {
            throw new ArgumentNullException(nameof(group));
        }

        // Convert ConditionGroup to a string representation and apply filtering.
        string where = group.AsString<T>();

        // If the resulting where string is null or empty, return the original query.
        if (string.IsNullOrWhiteSpace(where))
        {
            return query;
        }

        // Apply the filter to the query and return the result.
        return query.Where(where);
    }

    /// <summary>
    /// Groups the elements of an <see cref="IQueryable{T}"/> sequence based on the specified <see cref="GroupBy"/> instance.
    /// Returns a dynamic query result containing the grouped fields and any specified aggregations.
    /// </summary>
    /// <typeparam name="T">The type of elements in the source sequence.</typeparam>
    /// <param name="query">The source <see cref="IQueryable{T}"/> sequence to group.</param>
    /// <param name="groupBy">The <see cref="GroupBy"/> instance that defines the grouping fields and aggregations.</param>
    /// <returns>
    /// An <see cref="IQueryable"/> containing dynamic objects with the grouped fields and aggregation results.
    /// Each result object contains properties for each grouping field and each aggregation alias.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="query"/> or <paramref name="groupBy"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="groupBy"/> contains invalid data.</exception>
    /// <remarks>
    /// This method uses System.Linq.Dynamic.Core to build the GroupBy and Select expressions.
    /// The result is a dynamic IQueryable that can be further processed or materialized.
    /// </remarks>
    public static IQueryable Group<T>(this IQueryable<T> query, GroupBy groupBy) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (groupBy == null)
        {
            throw new ArgumentNullException(nameof(groupBy));
        }

        // Convert GroupBy to dynamic LINQ strings.
        var (groupByString, selectString) = groupBy.AsString<T>();

        // Apply GroupBy and Select using dynamic LINQ.
        return query.GroupBy(groupByString).Select(selectString);
    }

    /// <summary>
    /// Orders the elements of an <see cref="IQueryable{T}"/> sequence based on the specified <see cref="OrderBy"/> instance.
    /// </summary>
    /// <typeparam name="T">The type of elements in the sequence.</typeparam>
    /// <param name="query">The source <see cref="IQueryable{T}"/> sequence to order.</param>
    /// <param name="order">The <see cref="OrderBy"/> instance that defines the sorting criteria.</param>
    /// <returns>An <see cref="IQueryable{T}"/> that contains elements from the input sequence ordered as specified by <paramref name="order"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="query"/> or <paramref name="order"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="order"/> contains invalid data.</exception>
    public static IQueryable<T> Order<T>(this IQueryable<T> query, OrderBy order) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (order == null)
        {
            throw new ArgumentNullException(nameof(order));
        }

        // Convert OrderBy to a string representation and apply ordering.
        string orderBy = order.AsString<T>();

        // If the resulting order string is empty or whitespace, return the original query.
        if (string.IsNullOrWhiteSpace(orderBy))
        {
            return query;
        }

        // Apply the ordering to the query and return the result.
        return query.OrderBy(orderBy);
    }

    /// <summary>
    /// Orders the elements of an <see cref="IQueryable{T}"/> sequence based on a list of <see cref="OrderBy"/> instances.
    /// </summary>
    /// <typeparam name="T">The type of elements in the sequence.</typeparam>
    /// <param name="query">The source <see cref="IQueryable{T}"/> sequence to order.</param>
    /// <param name="orders">A list of <see cref="OrderBy"/> instances that define the sorting criteria. Items are applied by their <see cref="OrderBy.Sort"/> value.</param>
    /// <returns>
    /// An <see cref="IQueryable{T}"/> that contains elements from the input sequence ordered as specified by the list of <see cref="OrderBy"/> instances.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="query"/> or <paramref name="orders"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="orders"/> contains invalid data.</exception>
    public static IQueryable<T> Order<T>(this IQueryable<T> query, List<OrderBy> orders) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (orders == null)
        {
            throw new ArgumentNullException(nameof(orders));
        }

        // Concatenate the individual order strings into a single comma-separated string.
        string orderBy = string.Join(",", orders
                               .OrderBy(x => x.Sort)
                               .Select(x => x.AsString<T>())
                               .Where(x => !string.IsNullOrWhiteSpace(x)));

        // If the resulting order string is empty or whitespace, return the original query.
        if (string.IsNullOrWhiteSpace(orderBy))
        {
            return query;
        }

        // Apply the ordering to the query and return the result.
        return query.OrderBy(orderBy);
    }

    /// <summary>
    /// Paginates the elements of an <see cref="IQueryable{T}"/> sequence based on the specified <see cref="PageBy"/> object.
    /// </summary>
    /// <typeparam name="T">The type of elements in the sequence.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> sequence to paginate.</param>
    /// <param name="page">The <see cref="PageBy"/> object containing pagination information.</param>
    /// <returns>An <see cref="IQueryable{T}"/> representing a single page of data.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="query"/> or <paramref name="page"/> is null.</exception>
    public static IQueryable<T> Page<T>(this IQueryable<T> query, PageBy page) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (page == null)
        {
            throw new ArgumentNullException(nameof(page));
        }

        page.Validate<T>();

        // Skip the required number of items to reach the desired page,
        // and then take the specified number of items for the page.
        return query.Skip((page.PageNumber - 1) * page.PageSize).Take(page.PageSize);
    }

    /// <summary>
    /// Applies a <see cref="Filter"/> to an <see cref="IQueryable{T}"/> data source based on the provided filter criteria.
    /// </summary>
    /// <typeparam name="T">The type of elements in the data source.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> data source to filter.</param>
    /// <param name="filter">The <see cref="Filter"/> criteria containing a condition group, order-by criteria, and pagination settings.</param>
    /// <returns>An <see cref="IQueryable{T}"/> data source with the filter applied.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> or <paramref name="filter"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="filter"/> contains invalid data.</exception>
    public static IQueryable<T> Filter<T>(this IQueryable<T> query, Filter filter) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (filter == null)
        {
            throw new ArgumentNullException(nameof(filter));
        }

        // Apply the filter criteria to the query.
        if (filter.ConditionGroup != null)
        {
            query = query.Where(filter.ConditionGroup);
        }

        // Apply the order-by criteria to the query.
        if (filter.Orders != null)
        {
            query = query.Order(filter.Orders);
        }

        // Apply the pagination criteria to the query.
        if (filter.Page != null)
        {
            query = query.Page(filter.Page);
        }

        // Apply the select criteria last so ordering and pagination use the original field names.
        if (filter.Selects != null)
        {
            query = query.Select(filter.Selects);
        }

        // Return the filtered query.
        return query;
    }

    /// <summary>
    /// Applies a <see cref="Filter"/> to an <see cref="IQueryable{T}"/> data source and returns a dynamic <see cref="IQueryable"/>
    /// using <see cref="SelectDynamic{T}"/> for the field projection.
    /// Ordering and pagination are applied on the typed query before the dynamic projection.
    /// </summary>
    /// <typeparam name="T">The type of elements in the data source.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> data source to filter.</param>
    /// <param name="filter">The <see cref="Filter"/> criteria containing a condition group, order-by criteria, and pagination settings.</param>
    /// <returns>An <see cref="IQueryable"/> containing dynamic objects with the filter applied.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> or <paramref name="filter"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="filter"/> contains invalid data.</exception>
    public static IQueryable FilterDynamic<T>(this IQueryable<T> query, Filter filter) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (filter == null)
        {
            throw new ArgumentNullException(nameof(filter));
        }

        // Apply the filter criteria to the query.
        if (filter.ConditionGroup != null)
        {
            query = query.Where(filter.ConditionGroup);
        }

        // Apply ordering on the typed query before projection.
        if (filter.Orders != null)
        {
            query = query.Order(filter.Orders);
        }

        // Apply pagination on the typed query before projection.
        if (filter.Page != null)
        {
            query = query.Page(filter.Page);
        }

        // Apply dynamic select and return IQueryable.
        if (filter.Selects != null)
        {
            return query.SelectDynamic(filter.Selects);
        }

        // Return the filtered query (IQueryable<T> is assignable to IQueryable).
        return query;
    }

    /// <summary>
    /// Retrieves a list of entities from the <see cref="IQueryable{T}"/> with optional filtering based on a <see cref="Filter"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> to retrieve entities from.</param>
    /// <param name="filter">The <see cref="Filter"/> containing filter conditions and optional pagination settings.</param>
    /// <returns>A <see cref="FilterResult{T}"/> containing entities that match the filter conditions in the <see cref="Filter"/> with pagination information.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="query"/> or <paramref name="filter"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="filter"/> contains invalid data.</exception>
    public static FilterResult<T> ToList<T>(this IQueryable<T> query, Filter filter, bool getQueryString = false) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (filter == null)
        {
            throw new ArgumentNullException(nameof(filter));
        }

        // Apply the filter criteria to the query.
        if (filter.ConditionGroup != null)
        {
            query = query.Where(filter.ConditionGroup);
        }

        // Create a new query to apply ordering and pagination.
        var newQuery = query;

        // Apply the order-by criteria to the new query.
        if (filter.Orders != null)
        {
            newQuery = newQuery.Order(filter.Orders);
        }

        // Initialize variables for pagination.
        int pageNumber = 0, pageSize = 0;

        // Apply the pagination criteria to the new query.
        if (filter.Page != null)
        {
            newQuery = newQuery.Page(filter.Page);

            pageNumber = filter.Page.PageNumber;
            pageSize = filter.Page.PageSize;
        }

        // Apply the select criteria last so ordering and pagination use the original field names.
        if (filter.Selects != null)
        {
            newQuery = newQuery.Select(filter.Selects);
        }

        // Calculate the total count of entities before pagination.
        int totalCount = query.Count();

        // Calculate the total page count based on the page size.
        int pageCount = (int)Math.Ceiling((double)totalCount /
                        (pageSize == 0 ? 1 : pageSize));

        // Create and return a FilterResult containing the result data and pagination information.
        return new FilterResult<T>
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            PageCount = pageCount,
            TotalCount = totalCount,

            // Execute the query to retrieve the data.
            Data = newQuery.ToList(),
            QueryString = getQueryString ? newQuery.ToQueryString() : null
        };
    }

    /// <summary>
    /// Retrieves a list of dynamic objects from the <see cref="IQueryable{T}"/> with optional filtering based on a <see cref="Filter"/>,
    /// using <see cref="SelectDynamic{T}"/> for the field projection.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> to retrieve entities from.</param>
    /// <param name="filter">The <see cref="Filter"/> containing filter conditions and optional pagination settings.</param>
    /// <param name="getQueryString">If true, includes the generated query string in the result.</param>
    /// <returns>A <see cref="FilterResult{T}"/> of <c>dynamic</c> containing dynamic objects that match the filter conditions with pagination information.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="query"/> or <paramref name="filter"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="filter"/> contains invalid data.</exception>
    public static FilterResult<dynamic> ToListDynamic<T>(this IQueryable<T> query, Filter filter, bool getQueryString = false) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (filter == null)
        {
            throw new ArgumentNullException(nameof(filter));
        }

        // Apply the filter criteria to the query.
        if (filter.ConditionGroup != null)
        {
            query = query.Where(filter.ConditionGroup);
        }

        // Calculate the total count of entities before pagination.
        int totalCount = query.Count();

        // Create a new query to apply ordering and pagination.
        IQueryable<T> newQuery = query;

        // Apply the order-by criteria to the new query.
        if (filter.Orders != null)
        {
            newQuery = newQuery.Order(filter.Orders);
        }

        // Initialize variables for pagination.
        int pageNumber = 0, pageSize = 0;

        // Apply the pagination criteria to the new query.
        if (filter.Page != null)
        {
            newQuery = newQuery.Page(filter.Page);

            pageNumber = filter.Page.PageNumber;
            pageSize = filter.Page.PageSize;
        }

        // Apply dynamic select projection.
        IQueryable result = filter.Selects != null
            ? newQuery.SelectDynamic(filter.Selects)
            : newQuery;

        // Calculate the total page count based on the page size.
        int pageCount = (int)Math.Ceiling((double)totalCount /
                        (pageSize == 0 ? 1 : pageSize));

        // Create and return a FilterResult containing the dynamic result data and pagination information.
        return new FilterResult<dynamic>
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            PageCount = pageCount,
            TotalCount = totalCount,

            // Execute the query to retrieve the dynamic data.
            Data = result.ToDynamicList(),
            QueryString = getQueryString ? result.ToQueryString() : null
        };
    }

    /// <summary>
    /// Retrieves a list of entities from an in-memory <see cref="IEnumerable{T}"/> with optional filtering based on a <see cref="Filter"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IEnumerable{T}"/> to retrieve entities from.</param>
    /// <param name="filter">The <see cref="Filter"/> containing filter conditions and optional pagination settings.</param>
    /// <returns>A <see cref="FilterResult{T}"/> containing entities that match the filter conditions in the <see cref="Filter"/> with pagination information.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="query"/> or <paramref name="filter"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="filter"/> contains invalid data.</exception>
    public static FilterResult<T> ToList<T>(this IEnumerable<T> query, Filter filter, bool getQueryString = false) where T : class
    {
        return query.AsQueryable().ToList(filter, getQueryString);
    }

    /// <summary>
    /// Retrieves a list of dynamic objects from an in-memory <see cref="IEnumerable{T}"/> with optional filtering based on a <see cref="Filter"/>,
    /// using <see cref="SelectDynamic{T}"/> for the field projection.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IEnumerable{T}"/> to retrieve entities from.</param>
    /// <param name="filter">The <see cref="Filter"/> containing filter conditions and optional pagination settings.</param>
    /// <param name="getQueryString">If true, includes the generated query string in the result.</param>
    /// <returns>A <see cref="FilterResult{T}"/> of <c>dynamic</c> containing dynamic objects that match the filter conditions with pagination information.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="query"/> or <paramref name="filter"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="filter"/> contains invalid data.</exception>
    public static FilterResult<dynamic> ToListDynamic<T>(this IEnumerable<T> query, Filter filter, bool getQueryString = false) where T : class
    {
        return query.AsQueryable().ToListDynamic(filter, getQueryString);
    }

    /// <summary>
    /// Asynchronously retrieves a list of entities from the <see cref="IQueryable{T}"/> with optional filtering based on a <see cref="Filter"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> to retrieve entities from.</param>
    /// <param name="filter">The <see cref="Filter"/> containing filter conditions and optional pagination settings.</param>
    /// <returns>A <see cref="FilterResult{T}"/> containing entities that match the filter conditions in the <see cref="Filter"/> with pagination information.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="query"/> or <paramref name="filter"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="filter"/> contains invalid data.</exception>
    public static async Task<FilterResult<T>> ToListAsync<T>(this IQueryable<T> query, Filter filter, bool getQueryString = false) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (filter == null)
        {
            throw new ArgumentNullException(nameof(filter));
        }

        // Apply the filter criteria to the query.
        if (filter.ConditionGroup != null)
        {
            query = query.Where(filter.ConditionGroup);
        }

        // Create a new query to apply ordering and pagination.
        var newQuery = query;

        // Apply the order-by criteria to the new query.
        if (filter.Orders != null)
        {
            newQuery = newQuery.Order(filter.Orders);
        }

        // Initialize variables for pagination.
        int pageNumber = 0, pageSize = 0;

        // Apply the pagination criteria to the new query.
        if (filter.Page != null)
        {
            newQuery = newQuery.Page(filter.Page);

            pageNumber = filter.Page.PageNumber;
            pageSize = filter.Page.PageSize;
        }

        // Apply the select criteria last so ordering and pagination use the original field names.
        if (filter.Selects != null)
        {
            newQuery = newQuery.Select(filter.Selects);
        }

        // Calculate the total count of entities before pagination.
        int totalCount = await query.CountAsync();

        // Calculate the total page count based on the page size.
        int pageCount = (int)Math.Ceiling((double)totalCount /
                        (pageSize == 0 ? 1 : pageSize));

        // Create and return a FilterResult containing the result data and pagination information.
        return new FilterResult<T>
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            PageCount = pageCount,
            TotalCount = totalCount,

            // Execute the query to retrieve the data.
            Data = await newQuery.ToListAsync(),
            QueryString = getQueryString ? newQuery.ToQueryString() : null
        };
    }

    /// <summary>
    /// Asynchronously retrieves a list of dynamic objects from the <see cref="IQueryable{T}"/> with optional filtering based on a <see cref="Filter"/>,
    /// using <see cref="SelectDynamic{T}"/> for the field projection.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> to retrieve entities from.</param>
    /// <param name="filter">The <see cref="Filter"/> containing filter conditions and optional pagination settings.</param>
    /// <param name="getQueryString">If true, includes the generated query string in the result.</param>
    /// <returns>A <see cref="FilterResult{T}"/> of <c>dynamic</c> containing dynamic objects that match the filter conditions with pagination information.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="query"/> or <paramref name="filter"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="filter"/> contains invalid data.</exception>
    public static async Task<FilterResult<dynamic>> ToListAsyncDynamic<T>(this IQueryable<T> query, Filter filter, bool getQueryString = false) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (filter == null)
        {
            throw new ArgumentNullException(nameof(filter));
        }

        // Apply the filter criteria to the query.
        if (filter.ConditionGroup != null)
        {
            query = query.Where(filter.ConditionGroup);
        }

        // Calculate the total count of entities before pagination.
        int totalCount = await query.CountAsync();

        // Create a new query to apply ordering and pagination.
        IQueryable<T> newQuery = query;

        // Apply the order-by criteria to the new query.
        if (filter.Orders != null)
        {
            newQuery = newQuery.Order(filter.Orders);
        }

        // Initialize variables for pagination.
        int pageNumber = 0, pageSize = 0;

        // Apply the pagination criteria to the new query.
        if (filter.Page != null)
        {
            newQuery = newQuery.Page(filter.Page);

            pageNumber = filter.Page.PageNumber;
            pageSize = filter.Page.PageSize;
        }

        // Apply dynamic select projection.
        IQueryable result = filter.Selects != null
            ? newQuery.SelectDynamic(filter.Selects)
            : newQuery;

        // Calculate the total page count based on the page size.
        int pageCount = (int)Math.Ceiling((double)totalCount /
                        (pageSize == 0 ? 1 : pageSize));

        // Create and return a FilterResult containing the dynamic result data and pagination information.
        return new FilterResult<dynamic>
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            PageCount = pageCount,
            TotalCount = totalCount,

            // Execute the query to retrieve the dynamic data asynchronously.
            Data = await result.ToDynamicListAsync(),
            QueryString = getQueryString ? result.ToQueryString() : null
        };
    }

    /// <summary>
    /// Applies a <see cref="Classes.Complex.Summary"/> to an <see cref="IQueryable{T}"/> data source
    /// </summary>
    /// <typeparam name="T">The type of elements in the data source.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> data source to summarize.</param>
    /// <param name="summary">The <see cref="Classes.Complex.Summary"/> criteria containing a condition group, group-by criteria, order-by criteria, and pagination settings.</param>
    /// <returns>An <see cref="IQueryable"/> containing dynamic grouped objects with the summary applied.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> or <paramref name="summary"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="summary"/> contains invalid data.</exception>
    public static IQueryable Summary<T>(this IQueryable<T> query, Summary summary) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (summary == null)
        {
            throw new ArgumentNullException(nameof(summary));
        }

        // Validate the Summary (including order fields against grouped fields).
        summary.Validate<T>();

        // Apply the filter criteria to the query.
        if (summary.ConditionGroup != null)
        {
            query = query.Where(summary.ConditionGroup);
        }

        // Apply GroupBy (required).
        IQueryable result = query.Group(summary.GroupBy!);

        // Apply Having filter on grouped results.
        if (summary.Having != null)
        {
            string havingFilter = summary.Having.AsHavingString();

            if (!string.IsNullOrWhiteSpace(havingFilter))
            {
                result = result.Where(havingFilter);
            }
        }

        // Apply ordering on grouped results.
        // Dots are stripped from field names (e.g. "CreatedAt.Year" → "CreatedAtYear") to match
        // the aliases emitted by the GroupBy Select projection.
        if (summary.Orders != null && summary.Orders.Count > 0)
        {
            string orderBy = string.Join(",", summary.Orders
                .OrderBy(x => x.Sort)
                .Select(x => $"{x.Field?.Replace(".", "")} {(x.Direction == Direction.Ascending ? "asc" : "desc")}"));

            if (!string.IsNullOrWhiteSpace(orderBy))
            {
                result = result.OrderBy(orderBy);
            }
        }

        // Apply pagination.
        if (summary.Page != null)
        {
            result = result.Skip((summary.Page.PageNumber - 1) * summary.Page.PageSize)
                           .Take(summary.Page.PageSize);
        }

        // Return the summarized query.
        return result;
    }

    /// <summary>
    /// Retrieves a list of dynamic grouped entities from the <see cref="IQueryable{T}"/> with optional filtering based on a <see cref="Classes.Complex.Summary"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> to retrieve grouped entities from.</param>
    /// <param name="summary">The <see cref="Classes.Complex.Summary"/> containing filter conditions, group-by criteria, and optional pagination settings.</param>
    /// <param name="getQueryString">If true, includes the generated query string in the result.</param>
    /// <returns>A <see cref="SummaryResult"/> containing grouped entities that match the summary criteria with pagination information.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="query"/> or <paramref name="summary"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="summary"/> contains invalid data.</exception>
    public static SummaryResult ToList<T>(this IQueryable<T> query, Summary summary, bool getQueryString = false) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (summary == null)
        {
            throw new ArgumentNullException(nameof(summary));
        }

        // Validate the Summary (including order fields against grouped fields).
        summary.Validate<T>();

        // Apply the filter criteria to the query.
        if (summary.ConditionGroup != null)
        {
            query = query.Where(summary.ConditionGroup);
        }

        // Apply GroupBy (required).
        IQueryable result = query.Group(summary.GroupBy!);

        // Apply Having filter on grouped results.
        if (summary.Having != null)
        {
            string havingFilter = summary.Having.AsHavingString();

            if (!string.IsNullOrWhiteSpace(havingFilter))
            {
                result = result.Where(havingFilter);
            }
        }

        // Calculate the total count of grouped entities before pagination.
        int totalCount = result.Count();

        // Create a new query to apply ordering and pagination.
        IQueryable newResult = result;

        // Apply ordering on grouped results.
        // Dots are stripped from field names (e.g. "CreatedAt.Year" → "CreatedAtYear") to match
        // the aliases emitted by the GroupBy Select projection.
        if (summary.Orders != null && summary.Orders.Count > 0)
        {
            string orderBy = string.Join(",", summary.Orders
                .OrderBy(x => x.Sort)
                .Select(x => $"{x.Field?.Replace(".", "")} {(x.Direction == Direction.Ascending ? "asc" : "desc")}"));

            if (!string.IsNullOrWhiteSpace(orderBy))
            {
                newResult = newResult.OrderBy(orderBy);
            }
        }

        // Initialize variables for pagination.
        int pageNumber = 0, pageSize = 0;

        // Apply pagination.
        if (summary.Page != null)
        {
            newResult = newResult.Skip((summary.Page.PageNumber - 1) * summary.Page.PageSize)
                                 .Take(summary.Page.PageSize);

            pageNumber = summary.Page.PageNumber;
            pageSize = summary.Page.PageSize;
        }

        // Calculate the total page count based on the page size.
        int pageCount = (int)Math.Ceiling((double)totalCount /
                        (pageSize == 0 ? 1 : pageSize));

        // Create and return a SummaryResult containing the result data and pagination information.
        return new SummaryResult
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            PageCount = pageCount,
            TotalCount = totalCount,

            // Execute the query to retrieve the data.
            Data = newResult.ToDynamicList(),
            QueryString = getQueryString ? newResult.ToQueryString() : null
        };
    }

    /// <summary>
    /// Retrieves a list of dynamic grouped entities from an in-memory <see cref="IEnumerable{T}"/>
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IEnumerable{T}"/> to retrieve grouped entities from.</param>
    /// <param name="summary">The <see cref="Classes.Complex.Summary"/> containing filter conditions, group-by criteria, and optional pagination settings.</param>
    /// <param name="getQueryString">If true, includes the generated query string in the result.</param>
    /// <returns>A <see cref="SummaryResult"/> containing grouped entities that match the summary criteria with pagination information.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="query"/> or <paramref name="summary"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="summary"/> contains invalid data.</exception>
    public static SummaryResult ToList<T>(this IEnumerable<T> query, Summary summary, bool getQueryString = false) where T : class
    {
        return query.AsQueryable().ToList(summary, getQueryString);
    }

    /// <summary>
    /// Asynchronously retrieves a list of dynamic grouped entities from the <see cref="IQueryable{T}"/> with optional filtering based on a <see cref="Classes.Complex.Summary"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> to retrieve grouped entities from.</param>
    /// <param name="summary">The <see cref="Classes.Complex.Summary"/> containing filter conditions, group-by criteria, and optional pagination settings.</param>
    /// <param name="getQueryString">If true, includes the generated query string in the result.</param>
    /// <returns>A <see cref="SummaryResult"/> containing grouped entities that match the summary criteria with pagination information.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="query"/> or <paramref name="summary"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="summary"/> contains invalid data.</exception>
    public static async Task<SummaryResult> ToListAsync<T>(this IQueryable<T> query, Summary summary, bool getQueryString = false) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (summary == null)
        {
            throw new ArgumentNullException(nameof(summary));
        }

        // Validate the Summary (including order fields against grouped fields).
        summary.Validate<T>();

        // Apply the filter criteria to the query.
        if (summary.ConditionGroup != null)
        {
            query = query.Where(summary.ConditionGroup);
        }

        // Apply GroupBy (required).
        IQueryable result = query.Group(summary.GroupBy!);

        // Apply Having filter on grouped results.
        if (summary.Having != null)
        {
            string havingFilter = summary.Having.AsHavingString();

            if (!string.IsNullOrWhiteSpace(havingFilter))
            {
                result = result.Where(havingFilter);
            }
        }

        // Calculate the total count of grouped entities before pagination.
        int totalCount = result.Count();

        // Create a new query to apply ordering and pagination.
        IQueryable newResult = result;

        // Apply ordering on grouped results.
        // Dots are stripped from field names (e.g. "CreatedAt.Year" → "CreatedAtYear") to match
        // the aliases emitted by the GroupBy Select projection.
        if (summary.Orders != null && summary.Orders.Count > 0)
        {
            string orderBy = string.Join(",", summary.Orders
                .OrderBy(x => x.Sort)
                .Select(x => $"{x.Field?.Replace(".", "")} {(x.Direction == Direction.Ascending ? "asc" : "desc")}"));

            if (!string.IsNullOrWhiteSpace(orderBy))
            {
                newResult = newResult.OrderBy(orderBy);
            }
        }

        // Initialize variables for pagination.
        int pageNumber = 0, pageSize = 0;

        // Apply pagination.
        if (summary.Page != null)
        {
            newResult = newResult.Skip((summary.Page.PageNumber - 1) * summary.Page.PageSize)
                                 .Take(summary.Page.PageSize);

            pageNumber = summary.Page.PageNumber;
            pageSize = summary.Page.PageSize;
        }

        // Calculate the total page count based on the page size.
        int pageCount = (int)Math.Ceiling((double)totalCount /
                        (pageSize == 0 ? 1 : pageSize));

        // Create and return a SummaryResult containing the result data and pagination information.
        return new SummaryResult
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            PageCount = pageCount,
            TotalCount = totalCount,

            // Execute the query to retrieve the data asynchronously.
            Data = await newResult.ToDynamicListAsync(),
            QueryString = getQueryString ? newResult.ToQueryString() : null
        };
    }

    /// <summary>
    /// Asynchronously retrieves a list of entities from the <see cref="IQueryable{T}"/> with optional filtering based on a <see cref="Segment"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> to retrieve entities from.</param>
    /// <param name="segment">The <see cref="Segment"/> containing filter conditions and optional pagination settings.</param>
    /// <returns>A <see cref="SegmentResult{T}"/> containing entities that match the filter conditions in the <see cref="Segment"/> with pagination information.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either <paramref name="query"/> or <paramref name="segment"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when <paramref name="segment"/> contains invalid data.</exception>
    public static async Task<SegmentResult<T>> ToListAsync<T>(this IQueryable<T> query, Segment segment) where T : class
    {
        // Validate input parameters.
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        if (segment == null)
        {
            throw new ArgumentNullException(nameof(segment));
        }

        // Validate and retrieve ConditionSets from the Segment.
        List<ConditionSet> sets = segment.ValidateAndGetSets();

        // If there are no filter conditions, return all results (respecting select/order/page).
        if (sets.Count == 0)
        {
            // Create a new filter with the same select, order, and pagination criteria.
            Filter filter = new()
            {
                ConditionGroup = null,
                Selects = segment.Selects,
                Orders = segment.Orders,
                Page = segment.Page
            };

            // Retrieve the results using the filter.
            FilterResult<T> fresult = await query.ToListAsync<T>(filter);

            // Return the results as a SegmentResult.
            return new()
            {
                PageNumber = fresult.PageNumber,
                PageSize = fresult.PageSize,
                PageCount = fresult.PageCount,
                TotalCount = fresult.TotalCount,
                Data = fresult.Data
            };
        }

        // Store filtered data sets.
        List<(int sort, Intersection? intersection, List<T> list)> dataSets = new();

        foreach (ConditionSet? set in sets.OrderBy(x => x.Sort))
        {
            // Apply filter conditions from ConditionGroup.
            IQueryable<T> queryable = query.Where(set.ConditionGroup);

            // Apply select criteria to the query if provided.
            if (segment.Selects != null)
            {
                queryable = queryable.Select(segment.Selects);
            }

            // Materialize the filtered data and store it.
            List<T> list = await queryable.ToListAsync();

            dataSets.Add(new(set.Sort, set.Intersection, list));
        }

        // Combine and apply intersection operations to the data sets.
        List<T> data = dataSets.OrderBy(x => x.sort).First().list;

        foreach ((int sort, Intersection? intersection, List<T> list) in dataSets.OrderBy(x => x.sort).Skip(1))
        {
            switch (intersection)
            {
                case Intersection.Union:
                {
                    // Apply union operation to the result and the current list.
                    data = data.Union(list).ToList();
                }
                break;

                case Intersection.Intersect:
                {
                    // Apply intersect operation to the result and the current list.
                    data = data.Intersect(list).ToList();
                }
                break;

                case Intersection.Except:
                {
                    // Apply except operation to the result and the current list.
                    data = data.Except(list).ToList();
                }
                break;
            }
        }

        // Create a new SegmentResult to store the result.
        SegmentResult<T> sresult = new()
        {
            // Get the total count of entities in the result.
            TotalCount = data.Count
        };

        // Apply ordering if it is set.
        if (segment.Orders != null)
        {
            // Apply ordering to the data.
            data = data.AsQueryable().Order(segment.Orders).ToList();
        }

        // Apply pagination if it is set.
        if (segment.Page != null)
        {
            // Apply pagination to the data.
            sresult.Data = data.AsQueryable().Page(segment.Page).ToList();

            // Set PageNumber, PageSize and PageCount.
            sresult.PageNumber = segment.Page.PageNumber;
            sresult.PageSize = segment.Page.PageSize;

            sresult.PageCount = (int)Math.Ceiling((double)sresult.TotalCount /
                               (sresult.PageSize == 0 ? 1 : sresult.PageSize));
        }
        else
        {
            sresult.Data = data;
        }

        // Return the result.
        return sresult;
    }
}