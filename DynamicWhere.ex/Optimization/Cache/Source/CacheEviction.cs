using DynamicWhere.ex;
using DynamicWhere.ex.Optimization.Cache.Config;
using DynamicWhere.ex.Optimization.Cache.Enums;
using DynamicWhere.ex.Optimization.Cache.Input;
using System.Collections.Concurrent;

namespace DynamicWhere.ex.Optimization.Cache.Source;

/// <summary>
/// Handles all cache eviction strategies and related operations.
/// This class is responsible for implementing different eviction algorithms
/// (FIFO, LRU, LFU) and managing cache size limits.
/// </summary>
internal static class CacheEviction
{
    #region Eviction Strategy Entry Points

    /// <summary>
    /// Evicts cache entries based on the configured eviction strategy for type properties cache.
    /// </summary>
    /// <param name="config">Cache configuration options.</param>
    public static void EvictTypePropertiesEntries(CacheOptions config)
    {
        var cacheFullInput = CacheFullCheckInput.Create(CacheMemoryType.TypeProperties, config.MaxCacheSize);
        if (CacheDatabase.IsCacheFull(cacheFullInput))
        {
            EvictEntries(CacheDatabase.TypePropertiesCache,
                        CacheDatabase.TypePropertiesAccessTime,
                        CacheDatabase.TypePropertiesAccessCount,
                        config);
        }
    }

    /// <summary>
    /// Evicts cache entries based on the configured eviction strategy for property path cache.
    /// </summary>
    /// <param name="config">Cache configuration options.</param>
    public static void EvictPropertyPathEntries(CacheOptions config)
    {
        var cacheFullInput = CacheFullCheckInput.Create(CacheMemoryType.PropertyPath, config.MaxCacheSize);
        if (CacheDatabase.IsCacheFull(cacheFullInput))
        {
            EvictEntries(CacheDatabase.PropertyPathCache,
                        CacheDatabase.PropertyPathAccessTime,
                        CacheDatabase.PropertyPathAccessCount,
                        config);
        }
    }

    /// <summary>
    /// Evicts cache entries based on the configured eviction strategy for collection element type cache.
    /// </summary>
    /// <param name="config">Cache configuration options.</param>
    public static void EvictCollectionElementTypeEntries(CacheOptions config)
    {
        var cacheFullInput = CacheFullCheckInput.Create(CacheMemoryType.CollectionElementType, config.MaxCacheSize);
        if (CacheDatabase.IsCacheFull(cacheFullInput))
        {
            EvictEntries(CacheDatabase.CollectionElementTypeCache,
                        CacheDatabase.CollectionElementTypeAccessTime,
                        CacheDatabase.CollectionElementTypeAccessCount,
                        config);
        }
    }

    #endregion Eviction Strategy Entry Points

    #region Core Eviction Logic

    /// <summary>
    /// Evicts cache entries based on the configured eviction strategy.
    /// </summary>
    /// <typeparam name="TKey">The key type of the cache.</typeparam>
    /// <typeparam name="TValue">The value type of the cache.</typeparam>
    /// <param name="cache">The cache to clean.</param>
    /// <param name="accessTimes">Access time tracking dictionary for LRU.</param>
    /// <param name="accessCounts">Access count tracking dictionary for LFU.</param>
    /// <param name="config">Cache configuration options.</param>
    private static void EvictEntries<TKey, TValue>(
        ConcurrentDictionary<TKey, TValue> cache,
        ConcurrentDictionary<TKey, long> accessTimes,
        ConcurrentDictionary<TKey, long> accessCounts,
        CacheOptions config)
        where TKey : notnull
    {
        try
        {
            switch (config.EvictionStrategy)
            {
                case CacheEvictionStrategy.FIFO:
                    EvictFifoEntries(cache, config);
                    break;

                case CacheEvictionStrategy.LRU:
                    EvictLeastRecentlyUsedEntries(cache, accessTimes, config);
                    break;

                case CacheEvictionStrategy.LFU:
                    EvictLeastFrequentlyUsedEntries(cache, accessCounts, config);
                    break;

                default:
                    // Fallback to development configuration (FIFO)
                    var fallbackConfig = CacheOptions.ForDevelopment();
                    EvictFifoEntries(cache, fallbackConfig);
                    break;
            }
        }
        catch
        {
            // Ultimate fallback - simple FIFO with development settings
            EvictFifoEntries(cache, CacheOptions.ForDevelopment());
        }
    }

    #endregion Core Eviction Logic

    #region FIFO Eviction Strategy

    /// <summary>
    /// Simple FIFO eviction strategy - removes oldest entries based on insertion order.
    /// This strategy has minimal memory overhead as it doesn't require tracking access patterns.
    /// </summary>
    /// <typeparam name="TKey">The key type of the cache.</typeparam>
    /// <typeparam name="TValue">The value type of the cache.</typeparam>
    /// <param name="cache">The cache to clean.</param>
    /// <param name="config">Cache configuration options.</param>
    private static void EvictFifoEntries<TKey, TValue>(
        ConcurrentDictionary<TKey, TValue> cache,
        CacheOptions config)
        where TKey : notnull
    {
        int targetRemoveCount = Math.Max(1, cache.Count * config.LeastUsedThreshold / 100);

        var keysToRemove = cache.Keys.Take(targetRemoveCount).ToList();
        foreach (var key in keysToRemove)
        {
            cache.TryRemove(key, out _);
        }
    }

    #endregion FIFO Eviction Strategy

    #region LRU Eviction Strategy

    /// <summary>
    /// Evicts the least recently used entries from a cache based on access timestamps.
    /// This strategy optimizes for temporal locality - recently accessed items are more likely to be accessed again.
    /// </summary>
    /// <typeparam name="TKey">The key type of the cache.</typeparam>
    /// <typeparam name="TValue">The value type of the cache.</typeparam>
    /// <param name="cache">The cache to clean.</param>
    /// <param name="accessTimes">The access time tracking dictionary.</param>
    /// <param name="config">Cache configuration options.</param>
    private static void EvictLeastRecentlyUsedEntries<TKey, TValue>(
        ConcurrentDictionary<TKey, TValue> cache,
        ConcurrentDictionary<TKey, long> accessTimes,
        CacheOptions config)
        where TKey : notnull
    {
        int targetRemoveCount = Math.Max(1, cache.Count * config.LeastUsedThreshold / 100);

        // Get all entries with their access times and sort by access time (oldest first)
        var sortedEntries = accessTimes
            .Where(kvp => cache.ContainsKey(kvp.Key))
            .OrderBy(kvp => kvp.Value)
            .Take(targetRemoveCount)
            .Select(kvp => kvp.Key)
            .ToList();

        // Remove the least recently used entries
        foreach (var key in sortedEntries)
        {
            cache.TryRemove(key, out _);
            accessTimes.TryRemove(key, out _);
        }
    }

    #endregion LRU Eviction Strategy

    #region LFU Eviction Strategy

    /// <summary>
    /// Evicts the least frequently used entries from a cache based on access counts.
    /// This strategy optimizes for access frequency patterns - frequently accessed items are more likely to be accessed again.
    /// </summary>
    /// <typeparam name="TKey">The key type of the cache.</typeparam>
    /// <typeparam name="TValue">The value type of the cache.</typeparam>
    /// <param name="cache">The cache to clean.</param>
    /// <param name="accessCounts">The access count tracking dictionary.</param>
    /// <param name="config">Cache configuration options.</param>
    private static void EvictLeastFrequentlyUsedEntries<TKey, TValue>(
        ConcurrentDictionary<TKey, TValue> cache,
        ConcurrentDictionary<TKey, long> accessCounts,
        CacheOptions config)
        where TKey : notnull
    {
        int targetRemoveCount = Math.Max(1, cache.Count * config.LeastUsedThreshold / 100);

        // Get all entries with their access counts and sort by count (lowest first)
        var sortedEntries = accessCounts
            .Where(kvp => cache.ContainsKey(kvp.Key))
            .OrderBy(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key.GetHashCode()) // Tie-breaker for deterministic behavior
            .Take(targetRemoveCount)
            .Select(kvp => kvp.Key)
            .ToList();

        // Remove the least frequently used entries
        foreach (var key in sortedEntries)
        {
            cache.TryRemove(key, out _);
            accessCounts.TryRemove(key, out _);
        }
    }

    #endregion LFU Eviction Strategy

    #region Eviction Analysis and Utilities

    /// <summary>
    /// Calculates the number of entries that would be removed based on the current configuration.
    /// </summary>
    /// <param name="currentCacheSize">Current number of entries in the cache.</param>
    /// <param name="config">Cache configuration options.</param>
    /// <returns>The number of entries that would be evicted.</returns>
    public static int CalculateEvictionCount(int currentCacheSize, CacheOptions config)
    {
        return Math.Max(1, currentCacheSize * config.LeastUsedThreshold / 100);
    }

    /// <summary>
    /// Determines if eviction is needed for a specific cache type.
    /// </summary>
    /// <param name="cacheType">The type of cache to check.</param>
    /// <param name="config">Cache configuration options.</param>
    /// <returns>True if eviction is needed, false otherwise.</returns>
    public static bool IsEvictionNeeded(CacheMemoryType cacheType, CacheOptions config)
    {
        var cacheFullInput = CacheFullCheckInput.Create(cacheType, config.MaxCacheSize);
        return CacheDatabase.IsCacheFull(cacheFullInput);
    }

    /// <summary>
    /// Gets the eviction strategy description for the current configuration.
    /// </summary>
    /// <param name="strategy">The eviction strategy.</param>
    /// <returns>A description of how the eviction strategy works.</returns>
    public static string GetEvictionStrategyDescription(CacheEvictionStrategy strategy)
    {
        return strategy switch
        {
            CacheEvictionStrategy.FIFO => "First In, First Out - Removes oldest entries based on insertion order. Minimal memory overhead.",
            CacheEvictionStrategy.LRU => "Least Recently Used - Removes entries that haven't been accessed recently. Optimizes for temporal locality.",
            CacheEvictionStrategy.LFU => "Least Frequently Used - Removes entries with the lowest access count. Optimizes for access frequency patterns.",
            _ => "Unknown eviction strategy"
        };
    }

    /// <summary>
    /// Performs a dry run of eviction to see which entries would be removed without actually removing them.
    /// </summary>
    /// <typeparam name="TKey">The key type of the cache.</typeparam>
    /// <typeparam name="TValue">The value type of the cache.</typeparam>
    /// <param name="cache">The cache to analyze.</param>
    /// <param name="accessTimes">Access time tracking dictionary for LRU.</param>
    /// <param name="accessCounts">Access count tracking dictionary for LFU.</param>
    /// <param name="config">Cache configuration options.</param>
    /// <returns>A list of keys that would be evicted.</returns>
    public static List<TKey> GetEvictionCandidates<TKey, TValue>(
        ConcurrentDictionary<TKey, TValue> cache,
        ConcurrentDictionary<TKey, long> accessTimes,
        ConcurrentDictionary<TKey, long> accessCounts,
        CacheOptions config)
        where TKey : notnull
    {
        int targetRemoveCount = Math.Max(1, cache.Count * config.LeastUsedThreshold / 100);

        return config.EvictionStrategy switch
        {
            CacheEvictionStrategy.FIFO => cache.Keys.Take(targetRemoveCount).ToList(),
            CacheEvictionStrategy.LRU => accessTimes
                .Where(kvp => cache.ContainsKey(kvp.Key))
                .OrderBy(kvp => kvp.Value)
                .Take(targetRemoveCount)
                .Select(kvp => kvp.Key)
                .ToList(),
            CacheEvictionStrategy.LFU => accessCounts
                .Where(kvp => cache.ContainsKey(kvp.Key))
                .OrderBy(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key.GetHashCode())
                .Take(targetRemoveCount)
                .Select(kvp => kvp.Key)
                .ToList(),
            _ => cache.Keys.Take(targetRemoveCount).ToList()
        };
    }

    /// <summary>
    /// Forces eviction on all cache types regardless of their current size.
    /// This method is useful for memory pressure scenarios or testing.
    /// </summary>
    /// <param name="config">Cache configuration options.</param>
    public static void ForceEvictionOnAllCaches(CacheOptions config)
    {
        EvictEntries(CacheDatabase.TypePropertiesCache,
                    CacheDatabase.TypePropertiesAccessTime,
                    CacheDatabase.TypePropertiesAccessCount,
                    config);

        EvictEntries(CacheDatabase.PropertyPathCache,
                    CacheDatabase.PropertyPathAccessTime,
                    CacheDatabase.PropertyPathAccessCount,
                    config);

        EvictEntries(CacheDatabase.CollectionElementTypeCache,
                    CacheDatabase.CollectionElementTypeAccessTime,
                    CacheDatabase.CollectionElementTypeAccessCount,
                    config);
    }

    #endregion Eviction Analysis and Utilities
}