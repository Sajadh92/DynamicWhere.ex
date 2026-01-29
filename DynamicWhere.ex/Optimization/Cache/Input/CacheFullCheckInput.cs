using DynamicWhere.ex.Optimization.Cache.Config;
using DynamicWhere.ex.Optimization.Cache.Enums;

namespace DynamicWhere.ex.Optimization.Cache.Input;

/// <summary>
/// Input parameters for checking if cache is full.
/// </summary>
public class CacheFullCheckInput
{
    /// <summary>
    /// Gets or sets the type of cache to check.
    /// </summary>
    public CacheMemoryType CacheType { get; set; }

    /// <summary>
    /// Gets or sets the maximum allowed size for the cache.
    /// </summary>
    public int MaxSize { get; set; }

    /// <summary>
    /// Creates a new CacheFullCheckInput instance.
    /// </summary>
    /// <param name="cacheType">The type of cache to check.</param>
    /// <param name="maxSize">The maximum allowed size.</param>
    /// <returns>A new CacheFullCheckInput instance with the specified parameters.</returns>
    public static CacheFullCheckInput Create(CacheMemoryType cacheType, int maxSize)
    {
        return new CacheFullCheckInput
        {
            CacheType = cacheType,
            MaxSize = maxSize
        };
    }

    /// <summary>
    /// Creates a new CacheFullCheckInput instance from configuration.
    /// </summary>
    /// <param name="cacheType">The type of cache to check.</param>
    /// <param name="config">Cache configuration containing max size.</param>
    /// <returns>A new CacheFullCheckInput instance with the specified parameters.</returns>
    public static CacheFullCheckInput FromConfig(CacheMemoryType cacheType, CacheOptions config)
    {
        return new CacheFullCheckInput
        {
            CacheType = cacheType,
            MaxSize = config.MaxCacheSize
        };
    }

    /// <summary>
    /// Validates that all required parameters are properly set.
    /// </summary>
    /// <returns>True if all required parameters are set, false otherwise.</returns>
    public bool IsValid()
    {
        return MaxSize > 0;
    }
}