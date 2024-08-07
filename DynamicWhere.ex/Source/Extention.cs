﻿using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

namespace DynamicWhere.ex;

/// <summary>
/// Provides extension methods for working with <see cref="IQueryable{T}"/> and applying custom query logic.
/// </summary>
public static class Extension
{
    /// <summary>
    /// Projects and filters the specified fields in an <see cref="IQueryable{T}"/> query.
    /// </summary>
    /// <typeparam name="T">The entity type of the <see cref="IQueryable{T}"/>.</typeparam>
    /// <param name="query">The source <see cref="IQueryable{T}"/> query to be projected.</param>
    /// <param name="fields">A list of field names to include in the projection.</param>
    /// <returns>
    /// An <see cref="IQueryable"/> query containing only the specified fields or the original <paramref name="query"/> if no valid fields are specified.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if either the input <paramref name="query"/> or <paramref name="fields"/> is null.</exception>
    /// <exception cref="LogicException">Thrown if no valid fields are specified in the <paramref name="fields"/> list.</exception>
    public static IQueryable<T> Select<T>(this IQueryable<T> query, List<string> fields)
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

        // Validate each field if it is exists in the query.
        for (int i = 0; i < fields.Count; i++)
        {
            fields[i] = fields[i].Validate<T>();
        }

        // Concatenate the individual field strings into a single comma-separated string.
        string select = string.Join(",", fields);

        // If the resulting select string is empty or whitespace, return the original query.
        if (string.IsNullOrWhiteSpace(select))
        {
            return query;
        }

        // Apply the select to the query and return new query.
        return query.Select<T>(select);
    }

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

        // Apply the select criteria to the query.
        if (filter.Selects != null)
        {
            query = query.Select(filter.Selects);
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

        // Return the filtered query.
        return query;
    }

    /// <summary>
    /// retrieves a list of entities from the <see cref="IQueryable{T}"/> which contains static data with optional filtering based on a <see cref="Filter"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> to retrieve entities from.</param>
    /// <param name="filter">The <see cref="Filter"/> containing filter conditions and optional pagination settings.</param>
    /// <returns>A <see cref="FilterResult{T}"/> containing entities that match the filter conditions in the <see cref="Filter"/> with pagination information.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either the input <paramref name="query"/> or <paramref name="filter"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when the <paramref name="filter"/> contains invalid data.</exception>
    public static FilterResult<T> ToList<T>(this IQueryable<T> query, Filter filter)
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

        // Apply the select criteria to the query.
        if (filter.Selects != null)
        {
            query = query.Select(filter.Selects);
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
            Data = newQuery.ToList()
        };
    }

    /// <summary>
    /// retrieves a list of entities from the <see cref="IEnumerable{T}"/> which contains static data with optional filtering based on a <see cref="Filter"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> to retrieve entities from.</param>
    /// <param name="filter">The <see cref="Filter"/> containing filter conditions and optional pagination settings.</param>
    /// <returns>A <see cref="FilterResult{T}"/> containing entities that match the filter conditions in the <see cref="Filter"/> with pagination information.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either the input <paramref name="query"/> or <paramref name="filter"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when the <paramref name="filter"/> contains invalid data.</exception>
    public static FilterResult<T> ToList<T>(this IEnumerable<T> query, Filter filter)
    {
        return query.AsQueryable().ToList(filter);
    }

    /// <summary>
    /// Asynchronously retrieves a list of entities from the <see cref="IQueryable{T}"/> with optional filtering based on a <see cref="Filter"/>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The <see cref="IQueryable{T}"/> to retrieve entities from.</param>
    /// <param name="filter">The <see cref="Filter"/> containing filter conditions and optional pagination settings.</param>
    /// <returns>A <see cref="FilterResult{T}"/> containing entities that match the filter conditions in the <see cref="Filter"/> with pagination information.</returns>
    /// <exception cref="ArgumentNullException">Thrown if either the input <paramref name="query"/> or <paramref name="filter"/> is null.</exception>
    /// <exception cref="LogicException">Thrown when the <paramref name="filter"/> contains invalid data.</exception>
    public static async Task<FilterResult<T>> ToListAsync<T>(this IQueryable<T> query, Filter filter)
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

        // Apply the select criteria to the query.
        if (filter.Selects != null)
        {
            query = query.Select(filter.Selects);
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
            Data = await newQuery.ToListAsync()
        };
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

        // If there are no filter conditions, return all results.
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
            FilterResult<T> fresult = await query.ToListAsync(filter);

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

            if (segment.Selects != null)
            {
                // Apply select criteria to the query.
                queryable = queryable.Select(segment.Selects);
            }

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

        // Create a new SegmentResult to store the result.
        SegmentResult<T> sresult = new()
        {
            // Get the total count of entities in the query.
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