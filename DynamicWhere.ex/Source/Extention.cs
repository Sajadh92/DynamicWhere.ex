using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

namespace DynamicWhere.ex;

/// <summary>
/// Provides extension methods for working with <see cref="IQueryable{T}"/> and applying custom query logic.
/// </summary>
public static class Extension
{
    /// <summary>
    /// Applies filtering conditions to the <see cref="IQueryable{T}"/> based on a <see cref="Condition"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> to filter.</param>
    /// <param name="condition">The <see cref="Condition"/> containing filtering conditions.</param>
    /// <returns>The filtered <see cref="IQueryable{T}"/> based on the <see cref="Condition"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either the input <paramref name="query"/> or <paramref name="condition"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when the <paramref name="condition"/> contains invalid data</exception>
    public static IQueryable<T> Where<T>(this IQueryable<T> query, Condition condition)
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
    /// <exception cref="ArgumentNullException">Thrown if either the input <paramref name="query"/> or <paramref name="group"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when the <paramref name="group"/> contains invalid data</exception>
    public static IQueryable<T> Where<T>(this IQueryable<T> query, ConditionGroup group)
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
    /// Orders the elements of an <see cref="IQueryable{T}"/> sequence based on the specified <see cref="OrderBy"/> instance.
    /// </summary>
    /// <typeparam name="T">The type of elements in the sequence.</typeparam>
    /// <param name="query">The source <see cref="IQueryable{T}"/> sequence to order.</param>
    /// <param name="order">The <see cref="OrderBy"/> instance that defines the sorting criteria.</param>
    /// <returns>An <see cref="IQueryable{T}"/> that contains elements from the input sequence ordered as specified by the <see cref="OrderBy"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either the input <paramref name="query"/> or <paramref name="order"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when the <paramref name="order"/> contains invalid data</exception>"
    public static IQueryable<T> Order<T>(this IQueryable<T> query, OrderBy order)
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

        // apply the ordering to the query and return the result.
        return query.OrderBy(orderBy);
    }

    /// <summary>
    /// Orders the elements of an <see cref="IQueryable{T}"/> sequence based on a list of <see cref="OrderBy"/> instances.
    /// </summary>
    /// <typeparam name="T">The type of elements in the sequence.</typeparam>
    /// <param name="query">The source <see cref="IQueryable{T}"/> sequence to order.</param>
    /// <param name="orders">A list of <see cref="OrderBy"/> instances that define the sorting criteria.</param>
    /// <returns>
    /// An <see cref="IQueryable{T}"/> that contains elements from the input sequence ordered as specified by the list of <see cref="OrderBy"/> instances.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if either the input <paramref name="query"/> or <paramref name="orders"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when the <paramref name="orders"/> contains invalid data</exception>""
    public static IQueryable<T> Order<T>(this IQueryable<T> query, List<OrderBy> orders)
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
    /// <returns>An <see cref="IQueryable{T}"/> sequence representing a single page of data.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either the input <paramref name="query"/> or <paramref name="page"/> is null.</exception>
    public static IQueryable<T> Page<T>(this IQueryable<T> query, PageBy page)
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
    /// <exception cref="LogicException">Thrown when the <paramref name="filter"/> contains invalid data</exception>
    public static IQueryable<T> Filter<T>(this IQueryable<T> query, Filter filter)
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

        return query;
    }

    /// <summary>
    /// Asynchronously retrieves a list of entities from the <see cref="IQueryable{T}"/> with optional filtering based on a <see cref="Segment"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> to retrieve entities from.</param>
    /// <param name="segment">The <see cref="Segment"/> containing filter conditions and optional pagination settings.</param>
    /// <returns>A <see cref="SegmentResult{T}"/> containing entities that match the filter conditions in the <see cref="Segment"/> with pagination information.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either the input <paramref name="query"/> or <paramref name="segment"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when the <paramref name="segment"/> contains invalid data.</exception>
    public static async Task<SegmentResult<T>> ToListAsync<T>(this IQueryable<T> query, Segment segment)
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

        SegmentResult<T> result = new();

        // If there are no filter conditions, return all results.
        if (sets.Count == 0)
        {
            // Get the total count of entities in the query.
            result.TotalCount = await query.CountAsync();

            // Apply ordering if it is set.
            if (segment.Orders != null)
            {
                // Apply ordering to the query.
                query = query.Order(segment.Orders);
            }

            // Apply pagination if it is set.
            if (segment.Page != null)
            {
                // Apply pagination to the query.
                query = query.Page(segment.Page);

                // Set PageNumber, PageSize and PageCount.
                result.PageNumber = segment.Page.PageNumber;
                result.PageSize = segment.Page.PageSize;

                result.PageCount = (int)Math.Ceiling((double)result.TotalCount /
                                   (result.PageSize == 0 ? 1 : result.PageSize));
            }

            // Get the data from database.
            result.Data = await query.ToListAsync();

            // Return the result.
            return result;
        }

        // Store filtered data sets.
        List<(int sort, Intersection? intersection, List<T> list)> dataSets = new();

        foreach (ConditionSet? set in sets.OrderBy(x => x.Sort))
        {
            // Apply filter conditions from ConditionGroup.
            IQueryable<T> queryable = query.Where(set.ConditionGroup);

            // Materialize the filtered data and store it.
            List<T> list = await queryable.ToListAsync();

            dataSets.Add(new(set.Sort, set.Intersection, list));
        }

        // Combine and apply Intersection operations to the data sets.
        List<T> data = dataSets.OrderBy(x => x.sort).First().list;

        foreach ((int sort, Intersection? intersection, List<T> list) in dataSets.OrderBy(x => x.sort).Skip(1))
        {
            switch (intersection)
            {
                case Intersection.Union:
                {
                    // apply union operation to the result and the current list.
                    data = data.Union(list).ToList();
                }
                break;

                case Intersection.Intersect:
                {
                    // apply intersect operation to the result and the current list.
                    data = data.Intersect(list).ToList();
                }
                break;

                case Intersection.Except:
                {
                    // apply except operation to the result and the current list.
                    data = data.Except(list).ToList();
                }
                break;
            }
        }

        // Get the total count of entities in the query.
        result.TotalCount = data.Count;

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
            result.Data = data.AsQueryable().Page(segment.Page).ToList();

            // Set PageNumber, PageSize and PageCount.
            result.PageNumber = segment.Page.PageNumber;
            result.PageSize = segment.Page.PageSize;

            result.PageCount = (int)Math.Ceiling((double)result.TotalCount /
                               (result.PageSize == 0 ? 1 : result.PageSize));
        }
        else
        {
            result.Data = data;
        }

        // Return the result.
        return result;
    }
}