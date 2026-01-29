namespace DynamicWhere.ex.Optimization.Cache.Output;

/// <summary>
/// Represents the count of entries in all cache databases.
/// This class replaces tuple return types for better traceability and maintainability.
/// </summary>
public class CacheCounts
{
    /// <summary>
    /// Gets or sets the number of type properties currently cached.
    /// Represents the count of unique types that have their property information cached.
    /// </summary>
    public int TypePropertiesCount { get; set; }

    /// <summary>
    /// Gets or sets the number of property paths currently cached.
    /// Represents the count of validated property path strings stored in cache.
    /// </summary>
    public int PropertyPathCount { get; set; }

    /// <summary>
    /// Gets or sets the number of collection element types currently cached.
    /// Represents the count of collection types that have their element type information cached.
    /// </summary>
    public int CollectionTypeCount { get; set; }

    /// <summary>
    /// Gets the total number of cached entries across all cache types.
    /// </summary>
    public int TotalCachedEntries => TypePropertiesCount + PropertyPathCount + CollectionTypeCount;

    /// <summary>
    /// Creates a new CacheCounts instance from individual count values.
    /// </summary>
    /// <param name="typePropertiesCount">Number of cached type properties.</param>
    /// <param name="propertyPathCount">Number of cached property paths.</param>
    /// <param name="collectionTypeCount">Number of cached collection types.</param>
    /// <returns>A new CacheCounts instance with the specified values.</returns>
    public static CacheCounts FromValues(int typePropertiesCount, int propertyPathCount, int collectionTypeCount)
    {
        return new CacheCounts
        {
            TypePropertiesCount = typePropertiesCount,
            PropertyPathCount = propertyPathCount,
            CollectionTypeCount = collectionTypeCount
        };
    }

    /// <summary>
    /// Gets a summary of cache counts.
    /// </summary>
    /// <returns>A formatted string containing cache count information.</returns>
    public string GetSummary()
    {
        return $@"Cache Counts Summary:
====================
Type Properties: {TypePropertiesCount:N0}
Property Paths: {PropertyPathCount:N0}
Collection Types: {CollectionTypeCount:N0}
Total Entries: {TotalCachedEntries:N0}";
    }
}