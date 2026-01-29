namespace DynamicWhere.ex.Optimization.Cache.DTOs;

/// <summary>
/// Data Transfer Object representing ACTUAL memory usage breakdown for reflection cache components.
/// This lightweight DTO focuses specifically on memory consumption metrics and provides 
/// detailed analysis capabilities for cache memory optimization and monitoring.
/// </summary>
/// <remarks>
/// This class is designed to work in conjunction with the Calculator class to provide
/// precise memory measurements of cache operations. It offers both raw memory data
/// and analytical methods to help developers understand and optimize cache memory usage.
/// </remarks>
public class CacheMemoryUsage
{
    #region Memory Properties (Raw Bytes)

    /// <summary>
    /// Gets or sets the ACTUAL memory usage for type properties cache in bytes.
    /// This includes ConcurrentDictionary overhead, Type references, and all PropertyInfo dictionaries.
    /// </summary>
    /// <remarks>
    /// Memory breakdown includes:
    /// - ConcurrentDictionary base overhead (~256 bytes)
    /// - Type references (8 bytes each on 64-bit)
    /// - Dictionary overhead for each type (~72 bytes)
    /// - Property name strings with UTF-16 encoding (24 + length * 2 bytes)
    /// - PropertyInfo objects (~200 bytes each)
    /// - Entry overhead for concurrent dictionary operations (~48 bytes per entry)
    /// </remarks>
    public long TypePropertiesMemory { get; set; }

    /// <summary>
    /// Gets or sets the ACTUAL memory usage for property paths cache in bytes.
    /// This includes tuple keys with variable-length strings and validated string values.
    /// </summary>
    /// <remarks>
    /// Memory breakdown includes:
    /// - ConcurrentDictionary base overhead (~256 bytes)
    /// - ValueTuple key overhead (24 bytes per tuple)
    /// - Type references in tuple keys (8 bytes each)
    /// - Input property path strings (24 + length * 2 bytes each)
    /// - Output validated path strings (24 + length * 2 bytes each)
    /// - Entry overhead for concurrent operations (~48 bytes per entry)
    /// </remarks>
    public long PropertyPathMemory { get; set; }

    /// <summary>
    /// Gets or sets the ACTUAL memory usage for collection element types cache in bytes.
    /// This includes Type references and nullable Type values for collection type mappings.
    /// </summary>
    /// <remarks>
    /// Memory breakdown includes:
    /// - ConcurrentDictionary base overhead (~256 bytes)
    /// - Type key references (8 bytes each on 64-bit)
    /// - Nullable Type values (8 bytes for reference + 1 byte for HasValue flag)
    /// - Entry overhead for concurrent operations (~48 bytes per entry)
    /// </remarks>
    public long CollectionTypeMemory { get; set; }

    /// <summary>
    /// Gets or sets the ACTUAL memory usage for LRU (Least Recently Used) tracking records in bytes.
    /// This includes all timestamp tracking dictionaries across different cache types.
    /// </summary>
    /// <remarks>
    /// Memory breakdown includes:
    /// - Multiple ConcurrentDictionary overhead (~256 bytes each)
    /// - Type keys for type properties tracking (8 bytes each)
    /// - Tuple keys for property path tracking (variable size based on path length)
    /// - Long timestamp values (8 bytes each)
    /// - Entry overhead for all tracking dictionaries (~48 bytes per entry each)
    /// </remarks>
    public long LruTrackingMemory { get; set; }

    /// <summary>
    /// Gets or sets the ACTUAL memory usage for LFU (Least Frequently Used) tracking records in bytes.
    /// This includes all frequency counting dictionaries across different cache types.
    /// </summary>
    /// <remarks>
    /// Memory breakdown includes:
    /// - Multiple ConcurrentDictionary overhead (~256 bytes each)
    /// - Type keys for type properties tracking (8 bytes each)
    /// - Tuple keys for property path tracking (variable size based on path length)
    /// - Long frequency counter values (8 bytes each)
    /// - Entry overhead for all tracking dictionaries (~48 bytes per entry each)
    /// </remarks>
    public long LfuTrackingMemory { get; set; }

    #endregion Memory Properties (Raw Bytes)

    #region Calculated Properties

    /// <summary>
    /// Gets the total ACTUAL memory usage across all cache components in bytes.
    /// This represents the complete memory footprint of the reflection cache system.
    /// </summary>
    public long TotalMemory => TypePropertiesMemory + PropertyPathMemory + CollectionTypeMemory + LruTrackingMemory + LfuTrackingMemory;

    /// <summary>
    /// Gets the total cache memory usage (excluding tracking overhead) in bytes.
    /// This represents the memory used purely for caching reflection data.
    /// </summary>
    public long CacheOnlyMemory => TypePropertiesMemory + PropertyPathMemory + CollectionTypeMemory;

    /// <summary>
    /// Gets the total tracking memory usage (LRU + LFU) in bytes.
    /// This represents the memory overhead required for cache eviction strategies.
    /// </summary>
    public long TrackingOnlyMemory => LruTrackingMemory + LfuTrackingMemory;

    /// <summary>
    /// Gets the total memory usage in megabytes, rounded to 3 decimal places.
    /// Provides a more readable representation of memory consumption.
    /// </summary>
    public double TotalMemoryMB => Math.Round(TotalMemory / (1024.0 * 1024), 3);

    /// <summary>
    /// Gets the cache-only memory usage in megabytes, rounded to 3 decimal places.
    /// Shows memory used for actual cache data without tracking overhead.
    /// </summary>
    public double CacheOnlyMemoryMB => Math.Round(CacheOnlyMemory / (1024.0 * 1024), 3);

    /// <summary>
    /// Gets the tracking-only memory usage in megabytes, rounded to 3 decimal places.
    /// Shows the memory overhead required for cache management.
    /// </summary>
    public double TrackingOnlyMemoryMB => Math.Round(TrackingOnlyMemory / (1024.0 * 1024), 3);

    #endregion Calculated Properties

    #region Analysis Methods

    /// <summary>
    /// Calculates the percentage of total memory used by each cache component.
    /// Provides insights into memory distribution across different cache types.
    /// </summary>
    /// <returns>A dictionary containing memory distribution percentages for each component.</returns>
    public Dictionary<string, double> GetMemoryDistribution()
    {
        if (TotalMemory == 0) return new Dictionary<string, double>();

        return new Dictionary<string, double>
        {
            ["TypeProperties"] = Math.Round((double)TypePropertiesMemory / TotalMemory * 100, 2),
            ["PropertyPaths"] = Math.Round((double)PropertyPathMemory / TotalMemory * 100, 2),
            ["CollectionTypes"] = Math.Round((double)CollectionTypeMemory / TotalMemory * 100, 2),
            ["LruTracking"] = Math.Round((double)LruTrackingMemory / TotalMemory * 100, 2),
            ["LfuTracking"] = Math.Round((double)LfuTrackingMemory / TotalMemory * 100, 2)
        };
    }

    /// <summary>
    /// Calculates the tracking overhead percentage relative to total memory usage.
    /// Higher values indicate more memory is being used for cache management vs actual data.
    /// </summary>
    /// <returns>The percentage of total memory used for tracking mechanisms.</returns>
    public double CalculateTrackingOverheadPercentage()
    {
        return TotalMemory > 0 ? Math.Round((double)TrackingOnlyMemory / TotalMemory * 100, 2) : 0.0;
    }

    /// <summary>
    /// Calculates the cache efficiency ratio (cache data vs tracking overhead).
    /// Higher values indicate more efficient memory utilization with less tracking overhead.
    /// </summary>
    /// <returns>The ratio of cache memory to tracking memory (cache/tracking).</returns>
    public double CalculateCacheEfficiencyRatio()
    {
        return TrackingOnlyMemory > 0 ? Math.Round((double)CacheOnlyMemory / TrackingOnlyMemory, 2) : double.PositiveInfinity;
    }

    /// <summary>
    /// Determines the largest memory consumer among cache components.
    /// Useful for identifying optimization targets.
    /// </summary>
    /// <returns>A tuple containing the component name and its memory usage in bytes.</returns>
    public (string ComponentName, long MemoryBytes) GetLargestMemoryConsumer()
    {
        var components = new Dictionary<string, long>
        {
            ["TypeProperties"] = TypePropertiesMemory,
            ["PropertyPaths"] = PropertyPathMemory,
            ["CollectionTypes"] = CollectionTypeMemory,
            ["LruTracking"] = LruTrackingMemory,
            ["LfuTracking"] = LfuTrackingMemory
        };

        var largest = components.OrderByDescending(kvp => kvp.Value).First();
        return (largest.Key, largest.Value);
    }

    /// <summary>
    /// Evaluates the memory usage health status based on common thresholds.
    /// Provides a quick assessment of whether memory usage is within acceptable ranges.
    /// </summary>
    /// <param name="warningThresholdMB">Warning threshold in megabytes (default: 50MB).</param>
    /// <param name="criticalThresholdMB">Critical threshold in megabytes (default: 100MB).</param>
    /// <returns>A status string indicating the current memory health level.</returns>
    public string EvaluateMemoryHealthStatus(double warningThresholdMB = 50.0, double criticalThresholdMB = 100.0)
    {
        return TotalMemoryMB switch
        {
            var total when total >= criticalThresholdMB => $"🚨 CRITICAL: {total:F2} MB (>{criticalThresholdMB} MB)",
            var total when total >= warningThresholdMB => $"⚠️ WARNING: {total:F2} MB (>{warningThresholdMB} MB)",
            var total => $"✅ HEALTHY: {total:F2} MB (<{warningThresholdMB} MB)"
        };
    }

    #endregion Analysis Methods

    #region Summary and Reporting

    /// <summary>
    /// Generates a comprehensive summary of cache memory usage with detailed breakdowns.
    /// Provides both technical metrics and human-readable analysis for monitoring and optimization.
    /// </summary>
    /// <returns>A formatted string containing detailed memory usage analysis.</returns>
    public string GetDetailedSummary()
    {
        var distribution = GetMemoryDistribution();
        var trackingOverhead = CalculateTrackingOverheadPercentage();
        var efficiencyRatio = CalculateCacheEfficiencyRatio();
        var (ComponentName, MemoryBytes) = GetLargestMemoryConsumer();
        var healthStatus = EvaluateMemoryHealthStatus();

        return $@"Cache Memory Usage Summary (ACTUAL MEASUREMENTS):
================================================
                            
Memory Breakdown by Component:
┌─ Type Properties Cache: {TypePropertiesMemory / 1024.0:N1} KB ({distribution.GetValueOrDefault("TypeProperties", 0):F1}%)
├─ Property Paths Cache:  {PropertyPathMemory / 1024.0:N1} KB ({distribution.GetValueOrDefault("PropertyPaths", 0):F1}%)
├─ Collection Types Cache:{CollectionTypeMemory / 1024.0:N1} KB ({distribution.GetValueOrDefault("CollectionTypes", 0):F1}%)
├─ LRU Tracking Memory:   {LruTrackingMemory / 1024.0:N1} KB ({distribution.GetValueOrDefault("LruTracking", 0):F1}%)
└─ LFU Tracking Memory:   {LfuTrackingMemory / 1024.0:N1} KB ({distribution.GetValueOrDefault("LfuTracking", 0):F1}%)
                            
Total Memory Analysis:
• Total Memory Usage:     {TotalMemoryMB:F3} MB ({TotalMemory:N0} bytes)
• Cache Data Only:        {CacheOnlyMemoryMB:F3} MB ({CacheOnlyMemory:N0} bytes)
• Tracking Overhead:      {TrackingOnlyMemoryMB:F3} MB ({TrackingOnlyMemory:N0} bytes)
                            
Performance Metrics:
• Tracking Overhead:      {trackingOverhead:F1}% of total memory
• Cache Efficiency Ratio: {(efficiencyRatio == double.PositiveInfinity ? "∞" : efficiencyRatio.ToString("F2"))} (cache/tracking)
• Largest Consumer:        {ComponentName} ({MemoryBytes / 1024.0:N1} KB)
• Health Status:           {healthStatus}
                            
Memory Distribution Chart: {GenerateMemoryChart(distribution)}";
    }

    /// <summary>
    /// Generates a compact summary suitable for logging or quick monitoring.
    /// Provides essential metrics in a concise format.
    /// </summary>
    /// <returns>A compact string with key memory usage metrics.</returns>
    public string GetCompactSummary()
    {
        var trackingOverhead = CalculateTrackingOverheadPercentage();
        var (ComponentName, MemoryBytes) = GetLargestMemoryConsumer();

        return $"Cache Memory: {TotalMemoryMB:F2}MB total | " +
               $"Data: {CacheOnlyMemoryMB:F2}MB | " +
               $"Tracking: {TrackingOnlyMemoryMB:F2}MB ({trackingOverhead:F1}%) | " +
               $"Top: {ComponentName} ({MemoryBytes / 1024.0:N1}KB)";
    }

    /// <summary>
    /// Generates optimization recommendations based on current memory usage patterns.
    /// Provides actionable insights for reducing memory consumption.
    /// </summary>
    /// <returns>A list of optimization recommendations.</returns>
    public List<string> GetOptimizationRecommendations()
    {
        var recommendations = new List<string>();
        var distribution = GetMemoryDistribution();
        var trackingOverhead = CalculateTrackingOverheadPercentage();
        var largestConsumer = GetLargestMemoryConsumer();

        // High tracking overhead recommendations
        if (trackingOverhead > 30)
        {
            recommendations.Add($"⚠️ High tracking overhead ({trackingOverhead:F1}%). Consider switching to FIFO eviction strategy to reduce memory usage.");
        }

        // Component-specific recommendations
        if (distribution.GetValueOrDefault("TypeProperties", 0) > 60)
        {
            recommendations.Add("📊 Type Properties cache dominates memory usage. Consider reducing MaxCacheSize or clearing unused type caches.");
        }

        if (distribution.GetValueOrDefault("PropertyPaths", 0) > 40)
        {
            recommendations.Add("🔗 Property Paths cache is using significant memory. Review property path validation patterns for optimization opportunities.");
        }

        // Memory size recommendations
        if (TotalMemoryMB > 100)
        {
            recommendations.Add($"🚨 Total memory usage is high ({TotalMemoryMB:F1}MB). Consider implementing more aggressive eviction policies.");
        }
        else if (TotalMemoryMB > 50)
        {
            recommendations.Add($"⚠️ Memory usage is elevated ({TotalMemoryMB:F1}MB). Monitor growth trends and consider optimization if it continues to increase.");
        }

        // Efficiency recommendations
        var efficiencyRatio = CalculateCacheEfficiencyRatio();
        if (efficiencyRatio < 2.0 && TrackingOnlyMemory > 0)
        {
            recommendations.Add($"📈 Cache efficiency ratio is low ({efficiencyRatio:F1}). Tracking overhead may be too high relative to cached data.");
        }

        // Default recommendation if no issues found
        if (recommendations.Count == 0)
        {
            recommendations.Add("✅ Memory usage appears optimal. Continue monitoring for any changes in usage patterns.");
        }

        return recommendations;
    }

    /// <summary>
    /// Generates a simple ASCII chart showing memory distribution.
    /// Provides visual representation of memory usage across components.
    /// </summary>
    /// <param name="distribution">Memory distribution percentages.</param>
    /// <returns>ASCII chart string.</returns>
    private static string GenerateMemoryChart(Dictionary<string, double> distribution)
    {
        const int maxBarLength = 40;
        var chart = new System.Text.StringBuilder();

        foreach (var kvp in distribution.OrderByDescending(x => x.Value))
        {
            var barLength = (int)(kvp.Value / 100.0 * maxBarLength);
            var bar = new string('█', barLength);
            var padding = new string(' ', maxBarLength - barLength);
            chart.AppendLine($"  {kvp.Key,-15} |{bar}{padding}| {kvp.Value:F1}%");
        }

        return chart.ToString();
    }

    #endregion Summary and Reporting

    #region Factory Methods

    /// <summary>
    /// Creates a new CacheUsageOutput instance from individual memory component values.
    /// This factory method ensures all memory values are properly initialized.
    /// </summary>
    /// <param name="typePropertiesMemory">ACTUAL memory usage of type properties cache in bytes.</param>
    /// <param name="propertyPathMemory">ACTUAL memory usage of property paths cache in bytes.</param>
    /// <param name="collectionTypeMemory">ACTUAL memory usage of collection types cache in bytes.</param>
    /// <param name="lruTrackingMemory">ACTUAL memory usage of LRU tracking in bytes.</param>
    /// <param name="lfuTrackingMemory">ACTUAL memory usage of LFU tracking in bytes.</param>
    /// <returns>A new CacheUsageOutput instance with calculated properties automatically available.</returns>
    public static CacheMemoryUsage FromValues(
        long typePropertiesMemory,
        long propertyPathMemory,
        long collectionTypeMemory,
        long lruTrackingMemory,
        long lfuTrackingMemory)
    {
        return new CacheMemoryUsage
        {
            TypePropertiesMemory = typePropertiesMemory,
            PropertyPathMemory = propertyPathMemory,
            CollectionTypeMemory = collectionTypeMemory,
            LruTrackingMemory = lruTrackingMemory,
            LfuTrackingMemory = lfuTrackingMemory
        };
    }

    /// <summary>
    /// Creates an empty CacheUsageOutput instance with all memory values set to zero.
    /// Useful for initialization or when no cache data is present.
    /// </summary>
    /// <returns>A new CacheUsageOutput instance with zero memory usage.</returns>
    public static CacheMemoryUsage Empty()
    {
        return new CacheMemoryUsage
        {
            TypePropertiesMemory = 0,
            PropertyPathMemory = 0,
            CollectionTypeMemory = 0,
            LruTrackingMemory = 0,
            LfuTrackingMemory = 0
        };
    }

    #endregion Factory Methods
}
