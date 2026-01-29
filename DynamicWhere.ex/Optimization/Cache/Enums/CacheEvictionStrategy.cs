namespace DynamicWhere.ex.Optimization.Cache.Enums;

/// <summary>
/// Cache eviction strategy types for reflection cache.
/// </summary>
public enum CacheEvictionStrategy
{
    /// <summary>
    /// First In, First Out - Simple eviction based on insertion order.
    /// Predictable behavior with minimal memory overhead.
    /// </summary>
    FIFO,

    /// <summary>
    /// Least Recently Used - Evicts items that haven't been accessed recently.
    /// Optimizes for temporal locality of access patterns.
    /// </summary>
    LRU,

    /// <summary>
    /// Least Frequently Used - Evicts items with the lowest access frequency.
    /// Optimizes for items that are accessed frequently over time.
    /// </summary>
    LFU
}
