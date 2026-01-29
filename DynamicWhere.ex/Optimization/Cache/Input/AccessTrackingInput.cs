using DynamicWhere.ex.Optimization.Cache.Config;
using System.Collections.Concurrent;

namespace DynamicWhere.ex.Optimization.Cache.Input;

/// <summary>
/// Input parameters for updating access tracking for cache entries.
/// </summary>
/// <typeparam name="TKey">The type of the cache key.</typeparam>
public class AccessTrackingInput<TKey> where TKey : notnull
{
    /// <summary>
    /// Gets or sets the cache key being accessed.
    /// </summary>
    public TKey Key { get; set; } = default!;

    /// <summary>
    /// Gets or sets the current cache configuration.
    /// </summary>
    public CacheOptions Config { get; set; } = null!;

    /// <summary>
    /// Gets or sets the access time tracking dictionary for LRU strategy.
    /// </summary>
    public ConcurrentDictionary<TKey, long> AccessTimes { get; set; } = null!;

    /// <summary>
    /// Gets or sets the access count tracking dictionary for LFU strategy.
    /// </summary>
    public ConcurrentDictionary<TKey, long> AccessCounts { get; set; } = null!;

    /// <summary>
    /// Creates a new AccessTrackingInput instance.
    /// </summary>
    /// <param name="key">The cache key being accessed.</param>
    /// <param name="config">Current cache configuration.</param>
    /// <param name="accessTimes">Access time tracking dictionary for LRU.</param>
    /// <param name="accessCounts">Access count tracking dictionary for LFU.</param>
    /// <returns>A new AccessTrackingInput instance with the specified parameters.</returns>
    public static AccessTrackingInput<TKey> Create(
        TKey key,
        CacheOptions config,
        ConcurrentDictionary<TKey, long> accessTimes,
        ConcurrentDictionary<TKey, long> accessCounts)
    {
        return new AccessTrackingInput<TKey>
        {
            Key = key,
            Config = config,
            AccessTimes = accessTimes,
            AccessCounts = accessCounts
        };
    }

    /// <summary>
    /// Validates that all required parameters are properly set.
    /// </summary>
    /// <returns>True if all required parameters are set, false otherwise.</returns>
    public bool IsValid()
    {
        return Key != null &&
               Config != null &&
               AccessTimes != null &&
               AccessCounts != null;
    }
}