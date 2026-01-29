using DynamicWhere.ex;
using DynamicWhere.ex.Optimization.Cache.Config;
using DynamicWhere.ex.Optimization.Cache.DTOs;
using DynamicWhere.ex.Optimization.Cache.Enums;
using DynamicWhere.ex.Optimization.Cache.Input;

namespace DynamicWhere.ex.Optimization.Cache.Source;

/// <summary>
/// Handles all cache reporting, statistics generation, and output formatting.
/// This class is responsible for generating comprehensive reports about cache performance,
/// memory usage, and providing various output formats for monitoring and analysis.
/// </summary>
internal static class CacheReporting
{
    #region Statistics Generation

    /// <summary>
    /// Gets comprehensive cache statistics with actual memory measurements.
    /// </summary>
    /// <returns>A comprehensive DTO containing cache sizes, tracking statistics, and real memory usage.</returns>
    public static CacheStatistics GetCacheStatistics()
    {
        // Get all databases for memory calculation
        var databases = CacheDatabase.GetAllDatabases();

        // Calculate ACTUAL memory usage for each cache using the dedicated calculator
        var memoryCalculationInput = MemoryCalculationInput.FromDatabases(databases);
        var memoryUsage = CacheCalculator.CalculateActualMemoryUsage(memoryCalculationInput);

        // Get cache and tracking counts
        var cacheCounts = CacheDatabase.GetCacheCounts();
        var trackingCounts = CacheDatabase.GetTrackingCounts();

        return CacheStatistics.FromValues(
            cacheCounts.TypePropertiesCount,
            cacheCounts.PropertyPathCount,
            cacheCounts.CollectionTypeCount,
            trackingCounts.TypeAccessRecords,
            trackingCounts.PathAccessRecords,
            trackingCounts.CollectionAccessRecords,
            trackingCounts.TypeFrequencyRecords,
            trackingCounts.PathFrequencyRecords,
            trackingCounts.CollectionFrequencyRecords,
            memoryUsage.TypePropertiesMemory,
            memoryUsage.PropertyPathMemory,
            memoryUsage.CollectionTypeMemory,
            memoryUsage.LruTrackingMemory,
            memoryUsage.LfuTrackingMemory);
    }

    /// <summary>
    /// Gets detailed memory usage information without additional statistics.
    /// </summary>
    /// <returns>A CacheMemoryUsage containing detailed memory breakdown.</returns>
    public static CacheMemoryUsage GetMemoryUsage()
    {
        var databases = CacheDatabase.GetAllDatabases();
        var memoryCalculationInput = MemoryCalculationInput.FromDatabases(databases);
        return CacheCalculator.CalculateActualMemoryUsage(memoryCalculationInput);
    }

    /// <summary>
    /// Gets cache configuration information for monitoring purposes.
    /// </summary>
    /// <param name="config">The current cache configuration.</param>
    /// <returns>A comprehensive DTO containing the current cache configuration.</returns>
    public static CacheConfiguration GetCacheConfiguration(CacheOptions config)
    {
        return CacheConfiguration.FromValues(
            config.MaxCacheSize,
            config.LeastUsedThreshold,
            config.MostUsedThreshold,
            config.EvictionStrategy.ToString(),
            config.EnableLruTracking,
            config.EnableLfuTracking,
            config.AutoValidateConfiguration);
    }

    #endregion Statistics Generation

    #region Performance Reports

    /// <summary>
    /// Generates a comprehensive performance report including cache efficiency metrics.
    /// </summary>
    /// <param name="config">The current cache configuration.</param>
    /// <returns>A formatted string containing detailed performance analysis.</returns>
    public static string GeneratePerformanceReport(CacheOptions config)
    {
        var statistics = GetCacheStatistics();
        var memoryUsage = GetMemoryUsage();
        var configOutput = GetCacheConfiguration(config);

        var utilizationPercentage = statistics.CalculateUtilizationPercentage(config.MaxCacheSize);
        var memoryEfficiency = statistics.CalculateMemoryEfficiency();
        var trackingOverhead = memoryUsage.CalculateTrackingOverheadPercentage();

        return $@"
===========================================
CACHE PERFORMANCE REPORT
===========================================

Cache Configuration:
  • Strategy: {config.EvictionStrategy} ({CacheEviction.GetEvictionStrategyDescription(config.EvictionStrategy)})
  • Max Cache Size: {config.MaxCacheSize:N0} entries per cache type
  • Eviction Threshold: Remove {config.LeastUsedThreshold}%, Keep {config.MostUsedThreshold}%
  • LRU Tracking: {(config.EnableLruTracking ? "Enabled" : "Disabled")}
  • LFU Tracking: {(config.EnableLfuTracking ? "Enabled" : "Disabled")}

Current Cache Status:
  • Total Cached Entries: {statistics.TotalCachedEntries:N0}
  • Cache Utilization: {utilizationPercentage:F1}% of maximum capacity
  • Total Memory Usage: {statistics.TotalMemoryMB:F3} MB ({statistics.TotalMemoryBytes:N0} bytes)
  • Memory Efficiency: {memoryEfficiency:F2} entries per MB

Memory Analysis:
{memoryUsage.GetDetailedSummary()}

Performance Metrics:
  • Tracking Overhead: {trackingOverhead:F1}% of total memory
  • Cache Efficiency Ratio: {memoryUsage.CalculateCacheEfficiencyRatio():F2}:1 (cache:tracking)
  • Average Entry Size: {statistics.CalculateAverageEntrySize():F2} bytes

Recommendations:
{string.Join(Environment.NewLine, memoryUsage.GetOptimizationRecommendations().Select(r => $"  • {r}"))}

===========================================";
    }

    /// <summary>
    /// Generates a compact status report suitable for logging or monitoring dashboards.
    /// </summary>
    /// <param name="config">The current cache configuration.</param>
    /// <returns>A compact string containing key performance indicators.</returns>
    public static string GenerateCompactStatusReport(CacheOptions config)
    {
        var statistics = GetCacheStatistics();
        var memoryUsage = GetMemoryUsage();
        var utilizationPercentage = statistics.CalculateUtilizationPercentage(config.MaxCacheSize);

        return $"Cache Status: {statistics.TotalCachedEntries:N0} entries | " +
               $"Utilization: {utilizationPercentage:F1}% | " +
               $"Memory: {statistics.TotalMemoryMB:F2}MB | " +
               $"Strategy: {config.EvictionStrategy} | " +
               $"Health: {memoryUsage.EvaluateMemoryHealthStatus()}";
    }

    #endregion Performance Reports

    #region Cache Analysis Reports

    /// <summary>
    /// Generates a detailed cache hit/miss analysis report.
    /// </summary>
    /// <param name="config">The current cache configuration.</param>
    /// <returns>A formatted string containing cache analysis.</returns>
    public static string GenerateCacheAnalysisReport(CacheOptions config)
    {
        var cacheCounts = CacheDatabase.GetCacheCounts();
        var trackingCounts = CacheDatabase.GetTrackingCounts();
        var memoryUsage = GetMemoryUsage();

        var typePropertiesUtilization = (double)cacheCounts.TypePropertiesCount / config.MaxCacheSize * 100;
        var propertyPathUtilization = (double)cacheCounts.PropertyPathCount / config.MaxCacheSize * 100;
        var collectionTypeUtilization = (double)cacheCounts.CollectionTypeCount / config.MaxCacheSize * 100;

        return $@"
===========================================
CACHE ANALYSIS REPORT
===========================================

Cache Utilization by Type:
  ?? Type Properties Cache:    {cacheCounts.TypePropertiesCount:N0} / {config.MaxCacheSize:N0} ({typePropertiesUtilization:F1}%)
  ?? Property Paths Cache:     {cacheCounts.PropertyPathCount:N0} / {config.MaxCacheSize:N0} ({propertyPathUtilization:F1}%)
  ?? Collection Types Cache:   {cacheCounts.CollectionTypeCount:N0} / {config.MaxCacheSize:N0} ({collectionTypeUtilization:F1}%)

Tracking Data Status:
  LRU Tracking Records:
    • Type Properties: {trackingCounts.TypeAccessRecords:N0}
    • Property Paths: {trackingCounts.PathAccessRecords:N0}
    • Collection Types: {trackingCounts.CollectionAccessRecords:N0}
  
  LFU Tracking Records:
    • Type Properties: {trackingCounts.TypeFrequencyRecords:N0}
    • Property Paths: {trackingCounts.PathFrequencyRecords:N0}
    • Collection Types: {trackingCounts.CollectionFrequencyRecords:N0}

Eviction Analysis:
  • Strategy: {config.EvictionStrategy}
  • Entries to evict when full: {CacheEviction.CalculateEvictionCount(config.MaxCacheSize, config)}
  • Type Properties needs eviction: {(CacheEviction.IsEvictionNeeded(CacheMemoryType.TypeProperties, config) ? "Yes" : "No")}
  • Property Paths needs eviction: {(CacheEviction.IsEvictionNeeded(CacheMemoryType.PropertyPath, config) ? "Yes" : "No")}
  • Collection Types needs eviction: {(CacheEviction.IsEvictionNeeded(CacheMemoryType.CollectionElementType, config) ? "Yes" : "No")}

Memory Distribution:
{GenerateMemoryDistributionChart(memoryUsage.GetMemoryDistribution())}

===========================================";
    }

    #endregion Cache Analysis Reports

    #region Monitoring and Alerting

    /// <summary>
    /// Checks cache health and generates alerts if thresholds are exceeded.
    /// </summary>
    /// <param name="input">Input parameters containing configuration and thresholds.</param>
    /// <returns>A list of alert messages, empty if no issues detected.</returns>
    public static List<string> GenerateHealthAlerts(HealthAlertsInput input)
    {
        if (!input.IsValid())
            return new List<string> { "? ERROR: Invalid health alerts input parameters" };

        var alerts = new List<string>();
        var statistics = GetCacheStatistics();
        var memoryUsage = GetMemoryUsage();
        var utilizationPercentage = statistics.CalculateUtilizationPercentage(input.Config.MaxCacheSize);

        // Memory usage alerts
        if (statistics.TotalMemoryMB >= input.CriticalThresholdMB)
        {
            alerts.Add($"?? CRITICAL: Total cache memory usage is {statistics.TotalMemoryMB:F2} MB (threshold: {input.CriticalThresholdMB} MB)");
        }
        else if (statistics.TotalMemoryMB >= input.WarningThresholdMB)
        {
            alerts.Add($"?? WARNING: Total cache memory usage is {statistics.TotalMemoryMB:F2} MB (threshold: {input.WarningThresholdMB} MB)");
        }

        // Cache utilization alerts
        if (utilizationPercentage >= 90)
        {
            alerts.Add($"?? WARNING: Cache utilization is high ({utilizationPercentage:F1}%) - eviction may occur frequently");
        }

        // Tracking overhead alerts
        var trackingOverhead = memoryUsage.CalculateTrackingOverheadPercentage();
        if (trackingOverhead >= 40)
        {
            alerts.Add($"?? WARNING: Tracking overhead is high ({trackingOverhead:F1}%) - consider switching to FIFO strategy");
        }

        // Memory efficiency alerts
        var memoryEfficiency = statistics.CalculateMemoryEfficiency();
        if (memoryEfficiency < 50)
        {
            alerts.Add($"?? WARNING: Memory efficiency is low ({memoryEfficiency:F2} entries/MB) - cache may not be cost-effective");
        }

        // Individual cache size alerts
        var cacheCounts = CacheDatabase.GetCacheCounts();
        if (cacheCounts.TypePropertiesCount >= input.Config.MaxCacheSize * 0.95)
        {
            alerts.Add("?? WARNING: Type Properties cache is near capacity");
        }
        if (cacheCounts.PropertyPathCount >= input.Config.MaxCacheSize * 0.95)
        {
            alerts.Add("?? WARNING: Property Paths cache is near capacity");
        }
        if (cacheCounts.CollectionTypeCount >= input.Config.MaxCacheSize * 0.95)
        {
            alerts.Add("?? WARNING: Collection Types cache is near capacity");
        }

        return alerts;
    }

    /// <summary>
    /// Generates a continuous monitoring report for automated systems.
    /// </summary>
    /// <param name="config">The current cache configuration.</param>
    /// <returns>A structured monitoring report in key-value format.</returns>
    public static Dictionary<string, object> GenerateMonitoringReport(CacheOptions config)
    {
        var statistics = GetCacheStatistics();
        var memoryUsage = GetMemoryUsage();
        var cacheCounts = CacheDatabase.GetCacheCounts();
        var trackingCounts = CacheDatabase.GetTrackingCounts();

        return new Dictionary<string, object>
        {
            ["timestamp"] = DateTime.UtcNow,
            ["cache_strategy"] = config.EvictionStrategy.ToString(),
            ["total_entries"] = statistics.TotalCachedEntries,
            ["total_memory_mb"] = statistics.TotalMemoryMB,
            ["total_memory_bytes"] = statistics.TotalMemoryBytes,
            ["memory_efficiency"] = statistics.CalculateMemoryEfficiency(),
            ["utilization_percentage"] = statistics.CalculateUtilizationPercentage(config.MaxCacheSize),
            ["tracking_overhead_percentage"] = memoryUsage.CalculateTrackingOverheadPercentage(),
            ["cache_efficiency_ratio"] = memoryUsage.CalculateCacheEfficiencyRatio(),
            ["health_status"] = memoryUsage.EvaluateMemoryHealthStatus(),
            ["type_properties_count"] = cacheCounts.TypePropertiesCount,
            ["property_path_count"] = cacheCounts.PropertyPathCount,
            ["collection_type_count"] = cacheCounts.CollectionTypeCount,
            ["type_access_records"] = trackingCounts.TypeAccessRecords,
            ["path_access_records"] = trackingCounts.PathAccessRecords,
            ["collection_access_records"] = trackingCounts.CollectionAccessRecords,
            ["type_frequency_records"] = trackingCounts.TypeFrequencyRecords,
            ["path_frequency_records"] = trackingCounts.PathFrequencyRecords,
            ["collection_frequency_records"] = trackingCounts.CollectionFrequencyRecords
        };
    }

    #endregion Monitoring and Alerting

    #region Utility Methods

    /// <summary>
    /// Generates an ASCII chart showing memory distribution.
    /// </summary>
    /// <param name="distribution">Memory distribution percentages.</param>
    /// <returns>ASCII chart string.</returns>
    private static string GenerateMemoryDistributionChart(Dictionary<string, double> distribution)
    {
        const int maxBarLength = 40;
        var chart = new System.Text.StringBuilder();
        chart.AppendLine("Memory Distribution:");

        foreach (var kvp in distribution.OrderByDescending(x => x.Value))
        {
            var barLength = (int)(kvp.Value / 100.0 * maxBarLength);
            var bar = new string('?', Math.Max(0, barLength));
            var padding = new string(' ', Math.Max(0, maxBarLength - barLength));
            chart.AppendLine($"  {kvp.Key,-15} |{bar}{padding}| {kvp.Value:F1}%");
        }

        return chart.ToString();
    }

    /// <summary>
    /// Formats bytes into a human-readable string with appropriate units.
    /// </summary>
    /// <param name="bytes">The number of bytes.</param>
    /// <returns>A formatted string with appropriate units (B, KB, MB).</returns>
    public static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F2} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F3} GB"
        };
    }

    /// <summary>
    /// Gets a quick health summary suitable for status displays.
    /// </summary>
    /// <param name="config">The current cache configuration.</param>
    /// <returns>A short health status string.</returns>
    public static string GetQuickHealthSummary()
    {
        var memoryUsage = GetMemoryUsage();
        var statistics = GetCacheStatistics();

        var healthStatus = memoryUsage.EvaluateMemoryHealthStatus();
        var statusIcon = healthStatus.Contains("CRITICAL") ? "??" :
                        healthStatus.Contains("WARNING") ? "??" : "?";

        return $"{statusIcon} {statistics.TotalCachedEntries:N0} entries, {FormatBytes(statistics.TotalMemoryBytes)}";
    }

    #endregion Utility Methods
}