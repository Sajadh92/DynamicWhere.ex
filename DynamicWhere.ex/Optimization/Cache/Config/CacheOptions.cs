using DynamicWhere.ex.Optimization.Cache.Enums;

namespace DynamicWhere.ex.Optimization.Cache.Config;

/// <summary>
/// Configuration options for the reflection cache system.
/// Allows developers to customize cache behavior based on their application's needs.
/// </summary>
public class CacheOptions
{
    /// <summary>
    /// Maximum number of cached entries per cache type to prevent memory leaks.
    /// Default: 1000 entries per cache.
    /// </summary>
    /// <remarks>
    /// Higher values provide better cache hit ratios but consume more memory.
    /// Lower values reduce memory usage but may cause more frequent cache evictions.
    /// </remarks>
    public int MaxCacheSize { get; set; } = 1000;

    /// <summary>
    /// Percentage of least used cache entries to remove when cache is full.
    /// Default: 25% (remove 25% of least used entries).
    /// </summary>
    /// <remarks>
    /// Valid range: 1-50%. Higher values remove more entries during eviction,
    /// reducing eviction frequency but potentially removing useful data.
    /// Lower values are more conservative but may trigger evictions more often.
    /// </remarks>
    public int LeastUsedThreshold { get; set; } = 25;

    /// <summary>
    /// Percentage of most used cache entries to keep when cache is full.
    /// Default: 75% (keep 75% of most used entries).
    /// </summary>
    /// <remarks>
    /// This value should be (100 - LeastUsedThreshold) for consistency.
    /// It represents the portion of the cache that will be preserved during eviction.
    /// </remarks>
    public int MostUsedThreshold { get; set; } = 75;

    /// <summary>
    /// Cache eviction strategy to use when cache reaches maximum size.
    /// Default: LRU (Least Recently Used).
    /// </summary>
    /// <remarks>
    /// - FIFO: Simple first-in-first-out, minimal memory overhead
    /// - LRU: Least Recently Used, optimizes for temporal locality
    /// - LFU: Least Frequently Used, optimizes for access frequency patterns
    /// </remarks>
    public CacheEvictionStrategy EvictionStrategy { get; set; } = CacheEvictionStrategy.LRU;

    /// <summary>
    /// Whether to enable access time tracking for LRU cache eviction.
    /// Default: true when EvictionStrategy is LRU, false otherwise.
    /// </summary>
    /// <remarks>
    /// This property is automatically managed based on EvictionStrategy.
    /// Manual changes may be overridden during validation.
    /// </remarks>
    public bool EnableLruTracking { get; set; } = true;

    /// <summary>
    /// Whether to enable access frequency tracking for LFU cache eviction.
    /// Default: false unless EvictionStrategy is LFU.
    /// </summary>
    /// <remarks>
    /// This property is automatically managed based on EvictionStrategy.
    /// Manual changes may be overridden during validation.
    /// </remarks>
    public bool EnableLfuTracking { get; set; } = false;

    /// <summary>
    /// Whether to automatically validate configuration values on set.
    /// Default: true (validates configuration consistency).
    /// </summary>
    /// <remarks>
    /// When enabled, automatically adjusts tracking options and thresholds
    /// to ensure consistency with the selected eviction strategy.
    /// </remarks>
    public bool AutoValidateConfiguration { get; set; } = true;

    /// <summary>
    /// Validates the configuration values and ensures they are within acceptable ranges.
    /// Also synchronizes tracking options with the selected eviction strategy.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when configuration values are invalid.</exception>
    public void Validate()
    {
        if (MaxCacheSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxCacheSize),
                "MaxCacheSize must be greater than 0.");
        }

        if (LeastUsedThreshold < 1 || LeastUsedThreshold > 50)
        {
            throw new ArgumentOutOfRangeException(nameof(LeastUsedThreshold),
                "LeastUsedThreshold must be between 1 and 50 percent.");
        }

        if (MostUsedThreshold < 50 || MostUsedThreshold > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(MostUsedThreshold),
                "MostUsedThreshold must be between 50 and 99 percent.");
        }

        if (LeastUsedThreshold + MostUsedThreshold != 100)
        {
            if (AutoValidateConfiguration)
            {
                // Auto-correct MostUsedThreshold to maintain consistency
                MostUsedThreshold = 100 - LeastUsedThreshold;
            }
            else
            {
                throw new ArgumentException(
                    $"LeastUsedThreshold ({LeastUsedThreshold}) + MostUsedThreshold ({MostUsedThreshold}) must equal 100.");
            }
        }

        // Ensure only appropriate tracking is enabled based on eviction strategy
        if (AutoValidateConfiguration)
        {
            switch (EvictionStrategy)
            {
                case CacheEvictionStrategy.FIFO:
                    EnableLruTracking = false;
                    EnableLfuTracking = false;
                    break;

                case CacheEvictionStrategy.LRU:
                    EnableLruTracking = true;
                    EnableLfuTracking = false;
                    break;

                case CacheEvictionStrategy.LFU:
                    EnableLruTracking = false;
                    EnableLfuTracking = true;
                    break;
            }
        }
        else
        {
            // Manual validation - ensure consistency
            var expectedLru = EvictionStrategy == CacheEvictionStrategy.LRU;
            var expectedLfu = EvictionStrategy == CacheEvictionStrategy.LFU;

            if (EnableLruTracking && !expectedLru)
            {
                throw new ArgumentException($"EnableLruTracking cannot be true when EvictionStrategy is {EvictionStrategy}");
            }

            if (EnableLfuTracking && !expectedLfu)
            {
                throw new ArgumentException($"EnableLfuTracking cannot be true when EvictionStrategy is {EvictionStrategy}");
            }

            if (EnableLruTracking && EnableLfuTracking)
            {
                throw new ArgumentException("EnableLruTracking and EnableLfuTracking cannot both be true");
            }
        }
    }

    /// <summary>
    /// Creates a copy of the current configuration.
    /// </summary>
    /// <returns>A new CacheOptions instance with the same values.</returns>
    public CacheOptions Clone()
    {
        return new CacheOptions
        {
            MaxCacheSize = MaxCacheSize,
            LeastUsedThreshold = LeastUsedThreshold,
            MostUsedThreshold = MostUsedThreshold,
            EvictionStrategy = EvictionStrategy,
            EnableLruTracking = EnableLruTracking,
            EnableLfuTracking = EnableLfuTracking,
            AutoValidateConfiguration = AutoValidateConfiguration
        };
    }

    /// <summary>
    /// Creates a configuration optimized for high-memory scenarios using LRU strategy.
    /// </summary>
    /// <returns>Configuration with larger cache sizes and conservative eviction.</returns>
    public static CacheOptions ForHighMemoryEnvironment()
    {
        return new CacheOptions
        {
            MaxCacheSize = 5000,
            LeastUsedThreshold = 10, // Remove only 10% when full
            MostUsedThreshold = 90,  // Keep 90% of entries
            EvictionStrategy = CacheEvictionStrategy.LRU,
            AutoValidateConfiguration = true
        };
    }

    /// <summary>
    /// Creates a configuration optimized for low-memory scenarios using aggressive LFU strategy.
    /// </summary>
    /// <returns>Configuration with smaller cache sizes and aggressive eviction.</returns>
    public static CacheOptions ForLowMemoryEnvironment()
    {
        return new CacheOptions
        {
            MaxCacheSize = 250,
            LeastUsedThreshold = 40, // Remove 40% when full
            MostUsedThreshold = 60,  // Keep 60% of entries
            EvictionStrategy = CacheEvictionStrategy.LFU, // Use frequency-based eviction for better efficiency
            AutoValidateConfiguration = true
        };
    }

    /// <summary>
    /// Creates a configuration optimized for development/testing scenarios using simple FIFO.
    /// </summary>
    /// <returns>Configuration with small cache sizes for predictable behavior.</returns>
    public static CacheOptions ForDevelopment()
    {
        return new CacheOptions
        {
            MaxCacheSize = 100,
            LeastUsedThreshold = 50, // Remove 50% when full for frequent turnover
            MostUsedThreshold = 50,  // Keep 50% of entries
            EvictionStrategy = CacheEvictionStrategy.FIFO, // Predictable FIFO for testing
            AutoValidateConfiguration = true
        };
    }

    /// <summary>
    /// Creates a configuration optimized for high-frequency access patterns using LFU strategy.
    /// </summary>
    /// <returns>Configuration optimized for workloads with repeated access to specific items.</returns>
    public static CacheOptions ForHighFrequencyAccess()
    {
        return new CacheOptions
        {
            MaxCacheSize = 2000,
            LeastUsedThreshold = 20, // Conservative eviction to preserve frequently used items
            MostUsedThreshold = 80,  // Keep most frequently used items
            EvictionStrategy = CacheEvictionStrategy.LFU, // Optimize for access frequency
            AutoValidateConfiguration = true
        };
    }

    /// <summary>
    /// Creates a configuration optimized for temporal access patterns using LRU strategy.
    /// </summary>
    /// <returns>Configuration optimized for workloads with recent access patterns.</returns>
    public static CacheOptions ForTemporalAccess()
    {
        return new CacheOptions
        {
            MaxCacheSize = 1500,
            LeastUsedThreshold = 25, // Standard eviction threshold
            MostUsedThreshold = 75,  // Keep recently used items
            EvictionStrategy = CacheEvictionStrategy.LRU, // Optimize for recent access
            AutoValidateConfiguration = true
        };
    }
}