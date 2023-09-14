using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

namespace DynamicWhere.ex;

/// <summary>
/// Provides extension methods for working with IQueryable and applying custom query logic based on a Segment.
/// </summary>
public static class Extension
{
    /// <summary>
    /// Asynchronously retrieves a list of entities from the IQueryable with optional filtering based on a Segment.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The IQueryable to retrieve entities from.</param>
    /// <param name="segment">The Segment containing filter conditions.</param>
    /// <returns>A List of entities that match the filter conditions in the Segment.</returns>
    /// <exception cref="ArgumentNullException">Thrown when query or segment is null.</exception>
    /// <exception cref="LogicException">Thrown when the Segment contains invalid data.</exception>
    public static async Task<List<T>> ToListAsync<T>(this IQueryable<T> query, Segment segment)
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

        if (sets.Count == 0)
        {
            // No filter conditions, return all results.
            return await query.ToListAsync();
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
        List<T> result = dataSets.OrderBy(x => x.sort).First().list;

        foreach ((int sort, Intersection? intersection, List<T> list) in dataSets.OrderBy(x => x.sort).Skip(1))
        {
            switch (intersection)
            {
                case Intersection.Union:
                {
                    result = result.Union(list).ToList();
                }
                break;

                case Intersection.Intersect:
                {
                    result = result.Intersect(list).ToList();
                }
                break;

                case Intersection.Except:
                {
                    result = result.Except(list).ToList();
                }
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Applies filtering conditions to the IQueryable based on a ConditionGroup.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The IQueryable to filter.</param>
    /// <param name="group">The ConditionGroup containing filtering conditions.</param>
    /// <returns>The filtered IQueryable based on the ConditionGroup.</returns>
    /// <exception cref="ArgumentNullException">Thrown when query or group is null.</exception>
    /// <exception cref="LogicException">Thrown when the ConditionGroup contains invalid data</exception>
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

        if (string.IsNullOrWhiteSpace(where))
        {
            return query;
        }

        return query.Where(where);
    }

    /// <summary>
    /// Applies filtering conditions to the IQueryable based on a Condition.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The IQueryable to filter.</param>
    /// <param name="condition">The Condition containing filtering conditions.</param>
    /// <returns>The filtered IQueryable based on the Condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown when query or condition is null.</exception>
    /// <exception cref="LogicException">Thrown when the Condition contains invalid data</exception>
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

        if (string.IsNullOrWhiteSpace(where))
        {
            return query;
        }

        return query.Where(where);
    }

    /// <summary>
    /// Paginates the elements of an IQueryable sequence based on the specified PageBy object.
    /// </summary>
    /// <typeparam name="T">The type of elements in the sequence.</typeparam>
    /// <param name="query">The IQueryable sequence to paginate.</param>
    /// <param name="page">The PageBy object containing pagination information.</param>
    /// <returns>An IQueryable sequence representing a single page of data.</returns>
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

        // Skip the required number of items to reach the desired page,
        // and then take the specified number of items for the page.
        return query.Skip((page.PageNumber - 1) * page.PageSize).Take(page.PageSize);
    }
}