using System.Collections.Concurrent;
using System.Reflection;

namespace DynamicWhere.ex.Optimization.Cache.Output;

/// <summary>
/// Represents all cache databases and tracking dictionaries for memory calculation purposes.
/// This class replaces complex tuple return types for better traceability and maintainability.
/// </summary>
public class CacheDatabases
{
    #region Cache Dictionaries

    /// <summary>
    /// Gets or sets the type properties cache.
    /// Key: Type, Value: Dictionary of property names (case-insensitive) to PropertyInfo.
    /// </summary>
    public ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> TypePropertiesCache { get; set; } = null!;

    /// <summary>
    /// Gets or sets the property path cache.
    /// Key: (Type, PropertyPath), Value: Validated property path.
    /// </summary>
    public ConcurrentDictionary<(Type, string), string> PropertyPathCache { get; set; } = null!;

    /// <summary>
    /// Gets or sets the collection element type cache.
    /// Key: Type, Value: Element type (or null if not a collection).
    /// </summary>
    public ConcurrentDictionary<Type, Type?> CollectionElementTypeCache { get; set; } = null!;

    #endregion Cache Dictionaries

    #region LRU Tracking Dictionaries

    /// <summary>
    /// Gets or sets the type properties LRU access time tracking dictionary.
    /// Key: Type, Value: Last access timestamp in ticks.
    /// </summary>
    public ConcurrentDictionary<Type, long> TypePropertiesAccessTime { get; set; } = null!;

    /// <summary>
    /// Gets or sets the property path LRU access time tracking dictionary.
    /// Key: (Type, PropertyPath), Value: Last access timestamp in ticks.
    /// </summary>
    public ConcurrentDictionary<(Type, string), long> PropertyPathAccessTime { get; set; } = null!;

    /// <summary>
    /// Gets or sets the collection element type LRU access time tracking dictionary.
    /// Key: Type, Value: Last access timestamp in ticks.
    /// </summary>
    public ConcurrentDictionary<Type, long> CollectionElementTypeAccessTime { get; set; } = null!;

    #endregion LRU Tracking Dictionaries

    #region LFU Tracking Dictionaries

    /// <summary>
    /// Gets or sets the type properties LFU access count tracking dictionary.
    /// Key: Type, Value: Access count.
    /// </summary>
    public ConcurrentDictionary<Type, long> TypePropertiesAccessCount { get; set; } = null!;

    /// <summary>
    /// Gets or sets the property path LFU access count tracking dictionary.
    /// Key: (Type, PropertyPath), Value: Access count.
    /// </summary>
    public ConcurrentDictionary<(Type, string), long> PropertyPathAccessCount { get; set; } = null!;

    /// <summary>
    /// Gets or sets the collection element type LFU access count tracking dictionary.
    /// Key: Type, Value: Access count.
    /// </summary>
    public ConcurrentDictionary<Type, long> CollectionElementTypeAccessCount { get; set; } = null!;

    #endregion LFU Tracking Dictionaries

    #region Helper Methods

    /// <summary>
    /// Creates a new CacheDatabases instance from individual database references.
    /// </summary>
    /// <param name="typePropertiesCache">The type properties cache.</param>
    /// <param name="propertyPathCache">The property path cache.</param>
    /// <param name="collectionElementTypeCache">The collection element type cache.</param>
    /// <param name="typePropertiesAccessTime">Type properties LRU tracking.</param>
    /// <param name="propertyPathAccessTime">Property path LRU tracking.</param>
    /// <param name="collectionElementTypeAccessTime">Collection types LRU tracking.</param>
    /// <param name="typePropertiesAccessCount">Type properties LFU tracking.</param>
    /// <param name="propertyPathAccessCount">Property path LFU tracking.</param>
    /// <param name="collectionElementTypeAccessCount">Collection types LFU tracking.</param>
    /// <returns>A new CacheDatabases instance containing all the database references.</returns>
    public static CacheDatabases FromDictionaries(
        ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> typePropertiesCache,
        ConcurrentDictionary<(Type, string), string> propertyPathCache,
        ConcurrentDictionary<Type, Type?> collectionElementTypeCache,
        ConcurrentDictionary<Type, long> typePropertiesAccessTime,
        ConcurrentDictionary<(Type, string), long> propertyPathAccessTime,
        ConcurrentDictionary<Type, long> collectionElementTypeAccessTime,
        ConcurrentDictionary<Type, long> typePropertiesAccessCount,
        ConcurrentDictionary<(Type, string), long> propertyPathAccessCount,
        ConcurrentDictionary<Type, long> collectionElementTypeAccessCount)
    {
        return new CacheDatabases
        {
            TypePropertiesCache = typePropertiesCache,
            PropertyPathCache = propertyPathCache,
            CollectionElementTypeCache = collectionElementTypeCache,
            TypePropertiesAccessTime = typePropertiesAccessTime,
            PropertyPathAccessTime = propertyPathAccessTime,
            CollectionElementTypeAccessTime = collectionElementTypeAccessTime,
            TypePropertiesAccessCount = typePropertiesAccessCount,
            PropertyPathAccessCount = propertyPathAccessCount,
            CollectionElementTypeAccessCount = collectionElementTypeAccessCount
        };
    }

    /// <summary>
    /// Gets the cache counts from all databases.
    /// </summary>
    /// <returns>A CacheCounts instance with current entry counts.</returns>
    public CacheCounts GetCacheCounts()
    {
        return CacheCounts.FromValues(
            TypePropertiesCache.Count,
            PropertyPathCache.Count,
            CollectionElementTypeCache.Count);
    }

    /// <summary>
    /// Gets the tracking counts from all tracking databases.
    /// </summary>
    /// <returns>A TrackingCounts instance with current tracking record counts.</returns>
    public TrackingCounts GetTrackingCounts()
    {
        return TrackingCounts.FromValues(
            TypePropertiesAccessTime.Count,
            PropertyPathAccessTime.Count,
            CollectionElementTypeAccessTime.Count,
            TypePropertiesAccessCount.Count,
            PropertyPathAccessCount.Count,
            CollectionElementTypeAccessCount.Count);
    }

    /// <summary>
    /// Checks if all databases are properly initialized.
    /// </summary>
    /// <returns>True if all database references are not null, false otherwise.</returns>
    public bool AreAllDatabasesInitialized()
    {
        return TypePropertiesCache != null &&
               PropertyPathCache != null &&
               CollectionElementTypeCache != null &&
               TypePropertiesAccessTime != null &&
               PropertyPathAccessTime != null &&
               CollectionElementTypeAccessTime != null &&
               TypePropertiesAccessCount != null &&
               PropertyPathAccessCount != null &&
               CollectionElementTypeAccessCount != null;
    }

    #endregion Helper Methods
}