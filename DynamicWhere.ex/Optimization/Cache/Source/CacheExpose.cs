using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Optimization.Cache.Config;
using DynamicWhere.ex.Optimization.Cache.DTOs;
using DynamicWhere.ex.Optimization.Cache.Enums;
using DynamicWhere.ex.Optimization.Cache.Input;
using DynamicWhere.ex.Optimization.Cache.Output;
using System.Reflection;

namespace DynamicWhere.ex.Optimization.Cache.Source;

/// <summary>
/// Public interface for cache functionality exposed outside the class library.
/// This class provides controlled access to internal cache operations while maintaining
/// security and encapsulation of internal cache implementation details.
/// </summary>
public static class CacheExpose
{
    #region Configuration Functions

    /// <summary>
    /// Configures the reflection cache with custom options.
    /// </summary>
    /// <param name="options">The cache configuration options to apply.</param>
    /// <remarks>
    /// This method is thread-safe and can be called at any time to update cache behavior.
    /// Existing cache entries are not affected, but new operations will use the updated configuration.
    /// </remarks>
    /// <example>
    /// <code>
    /// // Configure for high-memory environment
    /// CacheExpose.Configure(CacheOptions.ForHighMemoryEnvironment());
    /// 
    /// // Custom configuration
    /// CacheExpose.Configure(new CacheOptions
    /// {
    ///     MaxCacheSize = 2000,
    ///     LeastUsedThreshold = 30,
    ///     MostUsedThreshold = 70,
    ///     EvictionStrategy = CacheEvictionStrategy.LFU
    /// });
    /// </code>
    /// </example>
    public static void Configure(CacheOptions options)
    {
        CacheReflection.Configure(options);
    }

    /// <summary>
    /// Configures the reflection cache using a configuration builder pattern.
    /// </summary>
    /// <param name="configureOptions">Action to configure the cache options.</param>
    /// <example>
    /// <code>
    /// CacheExpose.Configure(options =>
    /// {
    ///     options.MaxCacheSize = 2000;
    ///     options.LeastUsedThreshold = 20;
    ///     options.EvictionStrategy = CacheEvictionStrategy.LRU;
    /// });
    /// </code>
    /// </example>
    public static void Configure(Action<CacheOptions> configureOptions)
    {
        if (configureOptions == null)
        {
            throw new ArgumentNullException(nameof(configureOptions));
        }

        var options = new CacheOptions();
        configureOptions(options);
        CacheReflection.Configure(options);
    }

    /// <summary>
    /// Gets the current reflection cache configuration.
    /// </summary>
    /// <returns>A copy of the current cache configuration options.</returns>
    public static CacheOptions GetCacheConfigOptions()
    {
        return CacheReflection.GetCacheConfigOptions();
    }

    #endregion Configuration Functions

    #region Reflection Functions

    /// <summary>
    /// Gets all properties for a type with case-insensitive lookup.
    /// Results are cached for performance.
    /// </summary>
    /// <param name="type">The type to get properties for.</param>
    /// <returns>A dictionary mapping property names (case-insensitive) to PropertyInfo objects.</returns>
    public static Dictionary<string, PropertyInfo> GetTypeProperties(Type type)
    {
        return CacheReflection.GetTypeProperties(type);
    }

    /// <summary>
    /// Finds a property by name (case-insensitive) for the specified type.
    /// </summary>
    /// <param name="type">The type to search in.</param>
    /// <param name="propertyName">The property name to find.</param>
    /// <returns>The PropertyInfo if found, null otherwise.</returns>
    public static PropertyInfo? FindProperty(Type type, string propertyName)
    {
        return CacheReflection.FindProperty(type, propertyName);
    }

    /// <summary>
    /// Checks if a type is a collection type that supports enumeration.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is a collection, false otherwise.</returns>
    public static bool IsCollectionType(Type type)
    {
        return CacheReflection.IsCollectionType(type);
    }

    /// <summary>
    /// Gets the element type for collection types, with caching.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>The element type if it's a collection, null otherwise.</returns>
    public static Type? GetCollectionElementType(Type type)
    {
        return CacheReflection.GetCollectionElementType(type);
    }

    /// <summary>
    /// Validates and normalizes a property path for the specified type, with caching.
    /// </summary>
    /// <param name="rootType">The root type to validate against.</param>
    /// <param name="propertyPath">The property path to validate.</param>
    /// <returns>The validated and normalized property path.</returns>
    /// <exception cref="LogicException">Thrown when the path is invalid.</exception>
    public static string ValidatePropertyPath(Type rootType, string propertyPath)
    {
        return CacheReflection.ValidatePropertyPath(rootType, propertyPath);
    }

    /// <summary>
    /// Pre-warms the reflection cache for a specific type to improve first-use performance.
    /// </summary>
    /// <typeparam name="T">The type to pre-warm the cache for.</typeparam>
    /// <param name="commonPropertyPaths">Optional list of property paths that are frequently used with this type.</param>
    /// <remarks>
    /// This method can be called during application startup to pre-populate caches for frequently used types,
    /// thereby reducing the latency of the first dynamic query operations.
    /// </remarks>
    public static void WarmupCache<T>(params string[] commonPropertyPaths)
    {
        // Pre-load type properties into cache
        CacheReflection.GetTypeProperties(typeof(T));

        // Pre-validate common property paths
        if (commonPropertyPaths != null)
        {
            foreach (var path in commonPropertyPaths)
            {
                try
                {
                    CacheReflection.ValidatePropertyPath(typeof(T), path);
                }
                catch
                {
                    // Ignore validation errors during warmup
                }
            }
        }
    }

    /// <summary>
    /// Pre-warms the reflection cache for a specific type to improve first-use performance.
    /// </summary>
    /// <param name="type">The type to pre-warm the cache for.</param>
    /// <param name="commonPropertyPaths">Optional list of property paths that are frequently used with this type.</param>
    /// <remarks>
    /// This method can be called during application startup to pre-populate caches for frequently used types,
    /// thereby reducing the latency of the first dynamic query operations.
    /// </remarks>
    public static void WarmupCache(Type type, params string[] commonPropertyPaths)
    {
        // Pre-load type properties into cache
        CacheReflection.GetTypeProperties(type);

        // Pre-validate common property paths
        if (commonPropertyPaths != null)
        {
            foreach (var path in commonPropertyPaths)
            {
                try
                {
                    CacheReflection.ValidatePropertyPath(type, path);
                }
                catch
                {
                    // Ignore validation errors during warmup
                }
            }
        }
    }

    #endregion Reflection Functions

    #region Report Functions

    /// <summary>
    /// Gets current reflection cache statistics for monitoring and debugging purposes.
    /// </summary>
    /// <returns>A comprehensive DTO containing cache sizes, LRU tracking information, and LFU tracking information for different cache types.</returns>
    public static CacheStatistics GetCacheStatistics()
    {
        return CacheReporting.GetCacheStatistics();
    }

    /// <summary>
    /// Gets the cache configuration settings including eviction strategy.
    /// </summary>
    /// <returns>A comprehensive DTO containing MaxCacheSize, LeastUsedThreshold, MostUsedThreshold, EvictionStrategy, and tracking settings.</returns>
    public static CacheConfiguration GetCacheConfiguration()
    {
        var config = CacheReflection.GetCacheConfigOptions();
        return CacheReporting.GetCacheConfiguration(config);
    }

    /// <summary>
    /// Gets detailed memory usage information.
    /// </summary>
    /// <returns>A CacheMemoryUsage containing detailed memory breakdown.</returns>
    public static CacheMemoryUsage GetMemoryUsage()
    {
        return CacheReporting.GetMemoryUsage();
    }

    /// <summary>
    /// Generates a comprehensive performance report including cache efficiency metrics.
    /// </summary>
    /// <returns>A formatted string containing detailed performance analysis.</returns>
    public static string GeneratePerformanceReport()
    {
        var config = CacheReflection.GetCacheConfigOptions();
        return CacheReporting.GeneratePerformanceReport(config);
    }

    /// <summary>
    /// Generates a compact status report suitable for logging or monitoring dashboards.
    /// </summary>
    /// <returns>A compact string containing key performance indicators.</returns>
    public static string GenerateCompactStatusReport()
    {
        var config = CacheReflection.GetCacheConfigOptions();
        return CacheReporting.GenerateCompactStatusReport(config);
    }

    /// <summary>
    /// Generates a detailed cache analysis report.
    /// </summary>
    /// <returns>A formatted string containing cache analysis.</returns>
    public static string GenerateCacheAnalysisReport()
    {
        var config = CacheReflection.GetCacheConfigOptions();
        return CacheReporting.GenerateCacheAnalysisReport(config);
    }

    /// <summary>
    /// Checks cache health and generates alerts if thresholds are exceeded.
    /// </summary>
    /// <param name="input">Input parameters containing configuration and thresholds.</param>
    /// <returns>A list of alert messages, empty if no issues detected.</returns>
    public static List<string> GenerateHealthAlerts(HealthAlertsInput input)
    {
        return CacheReporting.GenerateHealthAlerts(input);
    }

    /// <summary>
    /// Generates a continuous monitoring report for automated systems.
    /// </summary>
    /// <returns>A structured monitoring report in key-value format.</returns>
    public static Dictionary<string, object> GenerateMonitoringReport()
    {
        var config = CacheReflection.GetCacheConfigOptions();
        return CacheReporting.GenerateMonitoringReport(config);
    }

    /// <summary>
    /// Gets a quick health summary suitable for status displays.
    /// </summary>
    /// <returns>A short health status string.</returns>
    public static string GetQuickHealthSummary()
    {
        return CacheReporting.GetQuickHealthSummary();
    }

    #endregion Report Functions

    #region Access and Database Management Functions

    /// <summary>
    /// Clears all reflection caches. Useful for memory management or testing scenarios.
    /// </summary>
    /// <remarks>
    /// This method clears all cached reflection data including type properties, property paths, and collection types.
    /// Use this method judiciously as it will temporarily reduce performance until caches are repopulated.
    /// </remarks>
    public static void ClearAllCaches()
    {
        CacheDatabase.ClearAllDatabases();
    }

    /// <summary>
    /// Clears a specific reflection cache type.
    /// </summary>
    /// <param name="cacheType">The type of cache to clear.</param>
    public static void ClearCache(CacheMemoryType cacheType)
    {
        CacheDatabase.ClearCache(cacheType);
    }

    /// <summary>
    /// Gets basic cache statistics including entry counts.
    /// </summary>
    /// <returns>A CacheCounts object containing the count of entries in each cache type.</returns>
    public static CacheCounts GetCacheCounts()
    {
        return CacheDatabase.GetCacheCounts();
    }

    /// <summary>
    /// Gets tracking statistics including LRU and LFU record counts.
    /// </summary>
    /// <returns>A TrackingCounts object containing the count of tracking entries for each cache type.</returns>
    public static TrackingCounts GetTrackingCounts()
    {
        return CacheDatabase.GetTrackingCounts();
    }

    /// <summary>
    /// Checks if the specified cache has reached its maximum size.
    /// </summary>
    /// <param name="input">Input parameters containing cache type and maximum size.</param>
    /// <returns>True if the cache has reached its maximum size, false otherwise.</returns>
    public static bool IsCacheFull(CacheFullCheckInput input)
    {
        return CacheDatabase.IsCacheFull(input);
    }

    /// <summary>
    /// Checks if the specified cache has reached its maximum size based on current configuration.
    /// </summary>
    /// <param name="cacheType">The type of cache to check.</param>
    /// <returns>True if the cache has reached its maximum size, false otherwise.</returns>
    public static bool IsCacheFull(CacheMemoryType cacheType)
    {
        var config = CacheReflection.GetCacheConfigOptions();
        var input = CacheFullCheckInput.FromConfig(cacheType, config);
        return CacheDatabase.IsCacheFull(input);
    }

    /// <summary>
    /// Determines if eviction is needed for a specific cache type.
    /// </summary>
    /// <param name="cacheType">The type of cache to check.</param>
    /// <returns>True if eviction is needed, false otherwise.</returns>
    public static bool IsEvictionNeeded(CacheMemoryType cacheType)
    {
        var config = CacheReflection.GetCacheConfigOptions();
        return CacheEviction.IsEvictionNeeded(cacheType, config);
    }

    /// <summary>
    /// Calculates the number of entries that would be removed during eviction.
    /// </summary>
    /// <param name="currentCacheSize">Current number of entries in the cache.</param>
    /// <returns>The number of entries that would be evicted.</returns>
    public static int CalculateEvictionCount(int currentCacheSize)
    {
        var config = CacheReflection.GetCacheConfigOptions();
        return CacheEviction.CalculateEvictionCount(currentCacheSize, config);
    }

    /// <summary>
    /// Gets the eviction strategy description for the current configuration.
    /// </summary>
    /// <returns>A description of how the current eviction strategy works.</returns>
    public static string GetEvictionStrategyDescription()
    {
        var config = CacheReflection.GetCacheConfigOptions();
        return CacheEviction.GetEvictionStrategyDescription(config.EvictionStrategy);
    }

    /// <summary>
    /// Forces eviction on all cache types regardless of their current size.
    /// This method is useful for memory pressure scenarios or testing.
    /// </summary>
    public static void ForceEvictionOnAllCaches()
    {
        var config = CacheReflection.GetCacheConfigOptions();
        CacheEviction.ForceEvictionOnAllCaches(config);
    }

    #endregion Access and Database Management Functions

    #region Utility Functions

    /// <summary>
    /// Formats bytes into a human-readable string with appropriate units.
    /// </summary>
    /// <param name="bytes">The number of bytes.</param>
    /// <returns>A formatted string with appropriate units (B, KB, MB).</returns>
    public static string FormatBytes(long bytes)
    {
        return CacheReporting.FormatBytes(bytes);
    }

    /// <summary>
    /// Gets memory size constants used in calculations for reference.
    /// These are the actual .NET object sizes on 64-bit architecture.
    /// </summary>
    /// <returns>A dictionary of memory size constants.</returns>
    public static Dictionary<string, long> GetMemorySizeConstants()
    {
        return CacheCalculator.GetMemorySizeConstants();
    }

    /// <summary>
    /// Calculates the ACTUAL size of a string in memory.
    /// Accounts for .NET's UTF-16 encoding and string object overhead.
    /// </summary>
    /// <param name="str">The string to measure.</param>
    /// <returns>ACTUAL memory usage in bytes.</returns>
    public static long CalculateStringSize(string str)
    {
        return CacheCalculator.CalculateStringSize(str);
    }

    #endregion Utility Functions

    #region Advanced Monitoring Functions

    /// <summary>
    /// Creates a comprehensive cache monitoring session for continuous tracking.
    /// </summary>
    /// <returns>A monitoring session that can be used for ongoing cache health tracking.</returns>
    public static CacheMonitoringSession CreateMonitoringSession()
    {
        return new CacheMonitoringSession();
    }

    /// <summary>
    /// Evaluates cache performance and provides optimization recommendations.
    /// </summary>
    /// <returns>A detailed performance evaluation with recommendations.</returns>
    public static CachePerformanceEvaluation EvaluatePerformance()
    {
        var statistics = CacheReporting.GetCacheStatistics();
        var memoryUsage = CacheReporting.GetMemoryUsage();
        var config = CacheReflection.GetCacheConfigOptions();

        return new CachePerformanceEvaluation
        {
            Statistics = statistics,
            MemoryUsage = memoryUsage,
            Configuration = config,
            PerformanceScore = CalculatePerformanceScore(statistics, memoryUsage, config),
            Recommendations = memoryUsage.GetOptimizationRecommendations(),
            HealthAlerts = CacheReporting.GenerateHealthAlerts(HealthAlertsInput.WithDefaults(config)),
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Calculates a performance score for the current cache configuration and usage.
    /// </summary>
    /// <param name="statistics">Current cache statistics.</param>
    /// <param name="memoryUsage">Current memory usage.</param>
    /// <param name="config">Current cache configuration.</param>
    /// <returns>A performance score from 0-100 (higher is better).</returns>
    private static double CalculatePerformanceScore(CacheStatistics statistics, CacheMemoryUsage memoryUsage, CacheOptions config)
    {
        var utilizationScore = Math.Min(100, statistics.CalculateUtilizationPercentage(config.MaxCacheSize));
        var memoryEfficiencyScore = Math.Min(100, statistics.CalculateMemoryEfficiency() / 10.0); // Normalize to 0-100
        var trackingOverheadScore = Math.Max(0, 100 - memoryUsage.CalculateTrackingOverheadPercentage());
        var healthScore = memoryUsage.EvaluateMemoryHealthStatus().Contains("HEALTHY") ? 100 :
                         memoryUsage.EvaluateMemoryHealthStatus().Contains("WARNING") ? 70 : 30;

        return (utilizationScore + memoryEfficiencyScore + trackingOverheadScore + healthScore) / 4.0;
    }

    #endregion Advanced Monitoring Functions
}