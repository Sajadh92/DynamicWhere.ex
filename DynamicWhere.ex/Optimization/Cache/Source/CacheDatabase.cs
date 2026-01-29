using DynamicWhere.ex.Optimization.Cache.Enums;
using DynamicWhere.ex.Optimization.Cache.Input;
using DynamicWhere.ex.Optimization.Cache.Output;
using System.Collections.Concurrent;
using System.Reflection;

namespace DynamicWhere.ex.Optimization.Cache.Source;

/// <summary>
/// Manages all in-memory cache databases using ConcurrentDictionary collections.
/// This class is responsible for storing and providing access to all cache data structures
/// and their associated tracking mechanisms.
/// </summary>
internal static class CacheDatabase
{
    #region Cache Dictionaries

    /// <summary>
    /// Cache for type properties to avoid repeated reflection calls.
    /// Key: Type, Value: Dictionary of property names (case-insensitive) to PropertyInfo.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> _typePropertiesCache = new();

    /// <summary>
    /// Cache for property paths validation results.
    /// Key: (Type, PropertyPath), Value: Validated property path.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type, string), string> _propertyPathCache = new();

    /// <summary>
    /// Cache for collection element types.
    /// Key: Type, Value: Element type (or null if not a collection).
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Type?> _collectionElementTypeCache = new();

    #endregion Cache Dictionaries

    #region LRU Tracking Dictionaries

    /// <summary>
    /// Tracks access times for type properties cache entries (LRU strategy).
    /// Key: Type, Value: Last access timestamp in ticks.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, long> _typePropertiesAccessTime = new();

    /// <summary>
    /// Tracks access times for property path cache entries (LRU strategy).
    /// Key: (Type, PropertyPath), Value: Last access timestamp in ticks.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type, string), long> _propertyPathAccessTime = new();

    /// <summary>
    /// Tracks access times for collection element type cache entries (LRU strategy).
    /// Key: Type, Value: Last access timestamp in ticks.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, long> _collectionElementTypeAccessTime = new();

    #endregion LRU Tracking Dictionaries

    #region LFU Tracking Dictionaries

    /// <summary>
    /// Tracks access frequencies for type properties cache entries (LFU strategy).
    /// Key: Type, Value: Access count.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, long> _typePropertiesAccessCount = new();

    /// <summary>
    /// Tracks access frequencies for property path cache entries (LFU strategy).
    /// Key: (Type, PropertyPath), Value: Access count.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type, string), long> _propertyPathAccessCount = new();

    /// <summary>
    /// Tracks access frequencies for collection element type cache entries (LFU strategy).
    /// Key: Type, Value: Access count.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, long> _collectionElementTypeAccessCount = new();

    #endregion LFU Tracking Dictionaries

    #region Cache Access Properties

    /// <summary>
    /// Gets the type properties cache for read-only access.
    /// </summary>
    public static ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> TypePropertiesCache => _typePropertiesCache;

    /// <summary>
    /// Gets the property path cache for read-only access.
    /// </summary>
    public static ConcurrentDictionary<(Type, string), string> PropertyPathCache => _propertyPathCache;

    /// <summary>
    /// Gets the collection element type cache for read-only access.
    /// </summary>
    public static ConcurrentDictionary<Type, Type?> CollectionElementTypeCache => _collectionElementTypeCache;

    #endregion Cache Access Properties

    #region Tracking Access Properties

    /// <summary>
    /// Gets the type properties LRU access time tracking dictionary.
    /// </summary>
    public static ConcurrentDictionary<Type, long> TypePropertiesAccessTime => _typePropertiesAccessTime;

    /// <summary>
    /// Gets the property path LRU access time tracking dictionary.
    /// </summary>
    public static ConcurrentDictionary<(Type, string), long> PropertyPathAccessTime => _propertyPathAccessTime;

    /// <summary>
    /// Gets the collection element type LRU access time tracking dictionary.
    /// </summary>
    public static ConcurrentDictionary<Type, long> CollectionElementTypeAccessTime => _collectionElementTypeAccessTime;

    /// <summary>
    /// Gets the type properties LFU access count tracking dictionary.
    /// </summary>
    public static ConcurrentDictionary<Type, long> TypePropertiesAccessCount => _typePropertiesAccessCount;

    /// <summary>
    /// Gets the property path LFU access count tracking dictionary.
    /// </summary>
    public static ConcurrentDictionary<(Type, string), long> PropertyPathAccessCount => _propertyPathAccessCount;

    /// <summary>
    /// Gets the collection element type LFU access count tracking dictionary.
    /// </summary>
    public static ConcurrentDictionary<Type, long> CollectionElementTypeAccessCount => _collectionElementTypeAccessCount;

    #endregion Tracking Access Properties

    #region Database Operations

    /// <summary>
    /// Gets or adds a type properties dictionary to the cache.
    /// </summary>
    /// <param name="type">The type to get properties for.</param>
    /// <param name="valueFactory">Function to create the value if not present.</param>
    /// <returns>The dictionary of properties for the specified type.</returns>
    public static Dictionary<string, PropertyInfo> GetOrAddTypeProperties(Type type, Func<Type, Dictionary<string, PropertyInfo>> valueFactory)
    {
        return _typePropertiesCache.GetOrAdd(type, valueFactory);
    }

    /// <summary>
    /// Gets or adds a validated property path to the cache.
    /// </summary>
    /// <param name="cacheKey">The cache key (Type, PropertyPath) tuple.</param>
    /// <param name="valueFactory">Function to create the value if not present.</param>
    /// <returns>The validated property path string.</returns>
    public static string GetOrAddPropertyPath((Type, string) cacheKey, Func<(Type, string), string> valueFactory)
    {
        return _propertyPathCache.GetOrAdd(cacheKey, valueFactory);
    }

    /// <summary>
    /// Gets or adds a collection element type to the cache.
    /// </summary>
    /// <param name="type">The type to check for collection element type.</param>
    /// <param name="valueFactory">Function to create the value if not present.</param>
    /// <returns>The element type if it's a collection, null otherwise.</returns>
    public static Type? GetOrAddCollectionElementType(Type type, Func<Type, Type?> valueFactory)
    {
        return _collectionElementTypeCache.GetOrAdd(type, valueFactory);
    }

    #endregion Database Operations

    #region Access Tracking Operations

    /// <summary>
    /// Updates access tracking for cache entries based on the configured eviction strategy.
    /// </summary>
    /// <param name="input">Input parameters containing key, config, and tracking dictionaries.</param>
    public static void UpdateAccessTracking<TKey>(AccessTrackingInput<TKey> input)
        where TKey : notnull
    {
        if (!input.IsValid())
            return;

        switch (input.Config.EvictionStrategy)
        {
            case CacheEvictionStrategy.LRU:
                input.AccessTimes.AddOrUpdate(input.Key, DateTime.UtcNow.Ticks, (k, oldValue) => DateTime.UtcNow.Ticks);
                break;

            case CacheEvictionStrategy.LFU:
                input.AccessCounts.AddOrUpdate(input.Key, 1, (k, oldValue) => oldValue + 1);
                break;

            case CacheEvictionStrategy.FIFO:
                // No tracking needed for FIFO
                break;
        }
    }

    #endregion Access Tracking Operations

    #region Database Statistics

    /// <summary>
    /// Gets the current count of entries in all cache databases.
    /// </summary>
    /// <returns>A CacheCounts object containing the count of entries in each cache type.</returns>
    public static CacheCounts GetCacheCounts()
    {
        return CacheCounts.FromValues(_typePropertiesCache.Count, _propertyPathCache.Count, _collectionElementTypeCache.Count);
    }

    /// <summary>
    /// Gets the current count of entries in all tracking databases.
    /// </summary>
    /// <returns>A TrackingCounts object containing the count of tracking entries for each cache type.</returns>
    public static TrackingCounts GetTrackingCounts()
    {
        return TrackingCounts.FromValues(
            _typePropertiesAccessTime.Count,
            _propertyPathAccessTime.Count,
            _collectionElementTypeAccessTime.Count,
            _typePropertiesAccessCount.Count,
            _propertyPathAccessCount.Count,
            _collectionElementTypeAccessCount.Count);
    }

    /// <summary>
    /// Checks if the specified cache has reached its maximum size.
    /// </summary>
    /// <param name="input">Input parameters containing cache type and maximum size.</param>
    /// <returns>True if the cache has reached its maximum size, false otherwise.</returns>
    public static bool IsCacheFull(CacheFullCheckInput input)
    {
        if (!input.IsValid())
            return false;

        return input.CacheType switch
        {
            CacheMemoryType.TypeProperties => _typePropertiesCache.Count > input.MaxSize,
            CacheMemoryType.PropertyPath => _propertyPathCache.Count > input.MaxSize,
            CacheMemoryType.CollectionElementType => _collectionElementTypeCache.Count > input.MaxSize,
            _ => false
        };
    }

    #endregion Database Statistics

    #region Database Management

    /// <summary>
    /// Clears all cache databases and tracking dictionaries.
    /// </summary>
    public static void ClearAllDatabases()
    {
        // Clear main cache dictionaries
        _typePropertiesCache.Clear();
        _propertyPathCache.Clear();
        _collectionElementTypeCache.Clear();

        // Clear LRU tracking dictionaries
        _typePropertiesAccessTime.Clear();
        _propertyPathAccessTime.Clear();
        _collectionElementTypeAccessTime.Clear();

        // Clear LFU tracking dictionaries
        _typePropertiesAccessCount.Clear();
        _propertyPathAccessCount.Clear();
        _collectionElementTypeAccessCount.Clear();
    }

    /// <summary>
    /// Clears a specific cache database and its associated tracking dictionaries.
    /// </summary>
    /// <param name="cacheType">The type of cache to clear.</param>
    public static void ClearCache(CacheMemoryType cacheType)
    {
        switch (cacheType)
        {
            case CacheMemoryType.TypeProperties:
                _typePropertiesCache.Clear();
                _typePropertiesAccessTime.Clear();
                _typePropertiesAccessCount.Clear();
                break;

            case CacheMemoryType.PropertyPath:
                _propertyPathCache.Clear();
                _propertyPathAccessTime.Clear();
                _propertyPathAccessCount.Clear();
                break;

            case CacheMemoryType.CollectionElementType:
                _collectionElementTypeCache.Clear();
                _collectionElementTypeAccessTime.Clear();
                _collectionElementTypeAccessCount.Clear();
                break;
        }
    }

    /// <summary>
    /// Gets all cache databases for memory calculation purposes.
    /// </summary>
    /// <returns>A CacheDatabases object containing all cache and tracking dictionaries.</returns>
    public static CacheDatabases GetAllDatabases()
    {
        return CacheDatabases.FromDictionaries(
            _typePropertiesCache,
            _propertyPathCache,
            _collectionElementTypeCache,
            _typePropertiesAccessTime,
            _propertyPathAccessTime,
            _collectionElementTypeAccessTime,
            _typePropertiesAccessCount,
            _propertyPathAccessCount,
            _collectionElementTypeAccessCount);
    }

    #endregion Database Management
}
