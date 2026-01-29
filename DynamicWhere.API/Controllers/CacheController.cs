using DynamicWhere.ex.Optimization.Cache.Config;
using DynamicWhere.ex.Optimization.Cache.DTOs;
using DynamicWhere.ex.Optimization.Cache.Enums;
using DynamicWhere.ex.Optimization.Cache.Input;
using DynamicWhere.ex.Optimization.Cache.Output;
using DynamicWhere.ex.Optimization.Cache.Source;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace DynamicWhere.API.Controllers;

/// <summary>
/// Controller for managing and monitoring DynamicWhere reflection cache.
/// Provides endpoints for cache configuration, statistics, and maintenance operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class CacheController : ControllerBase
{
    private readonly ILogger<CacheController> _logger;

    public CacheController(ILogger<CacheController> logger)
    {
        _logger = logger;
    }

    #region Configuration Endpoints

    /// <summary>
    /// Gets the current cache configuration settings.
    /// </summary>
    /// <returns>Current cache configuration including eviction strategy and thresholds.</returns>
    [HttpGet("configuration")]
    [SwaggerOperation(
        Summary = "Get cache configuration",
        Description = "Retrieves the current DynamicWhere cache configuration including max size, eviction strategy, and tracking settings.",
        Tags = new[] { "Configuration" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult<object> GetConfiguration()
    {
        try
        {
            var config = CacheExpose.GetCacheConfiguration();
            return Ok(new
            {
                configuration = config,
                summary = config.GetSummary(),
                validationIssues = config.ValidateConfiguration()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cache configuration");
            return StatusCode(500, new { error = "Failed to retrieve cache configuration", details = ex.Message });
        }
    }

    /// <summary>
    /// Gets the current cache options.
    /// </summary>
    /// <returns>Current cache options object.</returns>
    [HttpGet("configuration/options")]
    [SwaggerOperation(
        Summary = "Get cache options",
        Description = "Retrieves the detailed cache options currently in use.",
        Tags = new[] { "Configuration" }
    )]
    [ProducesResponseType(typeof(CacheOptions), StatusCodes.Status200OK)]
    public ActionResult<CacheOptions> GetCacheOptions()
    {
        try
        {
            var options = CacheExpose.GetCacheConfigOptions();
            return Ok(options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cache options");
            return StatusCode(500, new { error = "Failed to retrieve cache options", details = ex.Message });
        }
    }

    /// <summary>
    /// Updates the cache configuration with new options.
    /// </summary>
    /// <param name="options">New cache configuration options to apply.</param>
    /// <returns>Success message with updated configuration.</returns>
    [HttpPut("configuration")]
    [SwaggerOperation(
        Summary = "Update cache configuration",
        Description = "Updates the DynamicWhere cache configuration. Changes take effect immediately for new cache operations.",
        Tags = new[] { "Configuration" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult UpdateConfiguration([FromBody] CacheOptions options)
    {
        try
        {
            if (options == null)
                return BadRequest(new { error = "Cache options cannot be null" });

            CacheExpose.Configure(options);
            var updatedConfig = CacheExpose.GetCacheConfiguration();

            _logger.LogInformation("Cache configuration updated successfully");
            return Ok(new
            {
                message = "Cache configuration updated successfully",
                configuration = updatedConfig
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating cache configuration");
            return StatusCode(500, new { error = "Failed to update cache configuration", details = ex.Message });
        }
    }

    /// <summary>
    /// Gets the description of the current eviction strategy.
    /// </summary>
    /// <returns>Description of how the current eviction strategy works.</returns>
    [HttpGet("configuration/eviction-strategy/description")]
    [SwaggerOperation(
        Summary = "Get eviction strategy description",
        Description = "Returns a detailed description of the currently configured cache eviction strategy.",
        Tags = new[] { "Configuration" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult<object> GetEvictionStrategyDescription()
    {
        try
        {
            var description = CacheExpose.GetEvictionStrategyDescription();
            return Ok(new { evictionStrategy = description });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving eviction strategy description");
            return StatusCode(500, new { error = "Failed to retrieve eviction strategy description", details = ex.Message });
        }
    }

    #endregion

    #region Statistics and Monitoring Endpoints

    /// <summary>
    /// Gets comprehensive cache statistics.
    /// </summary>
    /// <returns>Detailed cache statistics including memory usage and tracking information.</returns>
    [HttpGet("statistics")]
    [SwaggerOperation(
        Summary = "Get cache statistics",
        Description = "Retrieves comprehensive statistics about cache usage, memory consumption, and tracking efficiency with data suitable for visualization.",
        Tags = new[] { "Statistics" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult<object> GetStatistics()
    {
        try
        {
            var stats = CacheExpose.GetCacheStatistics();
            var config = CacheExpose.GetCacheConfigOptions();

            // Prepare data for charts and graphs
            var response = new
            {
                // Raw statistics
                statistics = stats,

                // Summary from DynamicWhere class
                summary = stats.GetSummary(),

                // Memory distribution
                memoryDistribution = stats.GetMemoryDistribution(),

                // Chart data: Cache distribution
                cacheDistribution = new
                {
                    labels = new[] { "Type Properties", "Property Paths", "Collection Types" },
                    data = new[] { stats.TypePropertiesCount, stats.PropertyPathCount, stats.CollectionTypeCount },
                    percentages = new[]
                    {
                        stats.TotalCachedEntries > 0 ? (stats.TypePropertiesCount * 100.0 / stats.TotalCachedEntries) : 0,
                        stats.TotalCachedEntries > 0 ? (stats.PropertyPathCount * 100.0 / stats.TotalCachedEntries) : 0,
                        stats.TotalCachedEntries > 0 ? (stats.CollectionTypeCount * 100.0 / stats.TotalCachedEntries) : 0
                    }
                },

                // Chart data: Memory distribution (MB)
                memoryDistributionChart = new
                {
                    labels = new[] { "Type Properties", "Property Paths", "Collection Types", "LRU Tracking", "LFU Tracking" },
                    data = new[]
                    {
                        Math.Round(stats.TypePropertiesMemoryMB, 3),
                        Math.Round(stats.PropertyPathMemoryMB, 3),
                        Math.Round(stats.CollectionTypeMemoryMB, 3),
                        Math.Round(stats.LruTrackingMemoryMB, 3),
                        Math.Round(stats.LfuTrackingMemoryMB, 3)
                    },
                    totalMB = Math.Round(stats.TotalMemoryBytes / (1024.0 * 1024.0), 3)
                },

                // Chart data: Utilization percentages
                utilizationMetrics = new
                {
                    typePropertiesUtilization = Math.Round(stats.TypePropertiesCount * 100.0 / config.MaxCacheSize, 2),
                    propertyPathUtilization = Math.Round(stats.PropertyPathCount * 100.0 / config.MaxCacheSize, 2),
                    collectionTypeUtilization = Math.Round(stats.CollectionTypeCount * 100.0 / config.MaxCacheSize, 2),
                    averageUtilization = Math.Round(stats.CalculateUtilizationPercentage(config.MaxCacheSize), 2),
                    maxCacheSize = config.MaxCacheSize
                },

                // Performance metrics
                performanceMetrics = new
                {
                    memoryEfficiency = Math.Round(stats.CalculateMemoryEfficiency(), 2),
                    averageEntrySize = Math.Round(stats.CalculateAverageEntrySize(), 2),
                    entriesPerMB = Math.Round(stats.CalculateMemoryEfficiency(), 2)
                },

                // Recommendations based on current state
                recommendations = GenerateStatisticsRecommendations(stats, config)
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cache statistics");
            return StatusCode(500, new { error = "Failed to retrieve cache statistics", details = ex.Message });
        }
    }

    /// <summary>
    /// Gets detailed memory usage information.
    /// </summary>
    /// <returns>Breakdown of memory consumption by cache component.</returns>
    [HttpGet("memory-usage")]
    [SwaggerOperation(
        Summary = "Get memory usage",
        Description = "Retrieves detailed memory usage breakdown for all cache components with visualization data.",
        Tags = new[] { "Statistics" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult<object> GetMemoryUsage()
    {
        try
        {
            var memoryUsage = CacheExpose.GetMemoryUsage();

            var response = new
            {
                // Raw memory usage
                memoryUsage,

                // Summaries from DynamicWhere class
                detailedSummary = memoryUsage.GetDetailedSummary(),
                compactSummary = memoryUsage.GetCompactSummary(),

                // Memory distribution
                memoryDistribution = memoryUsage.GetMemoryDistribution(),

                // Optimization recommendations from DynamicWhere class
                optimizationRecommendations = memoryUsage.GetOptimizationRecommendations(),

                // Largest consumer analysis
                largestConsumer = memoryUsage.GetLargestMemoryConsumer(),

                // Health evaluation
                healthStatus = memoryUsage.EvaluateMemoryHealthStatus(),

                // Chart data: Memory breakdown (bytes)
                memoryBreakdown = new
                {
                    labels = new[] { "Type Properties", "Property Paths", "Collection Types", "LRU Tracking", "LFU Tracking" },
                    bytes = new[]
                    {
                        memoryUsage.TypePropertiesMemory,
                        memoryUsage.PropertyPathMemory,
                        memoryUsage.CollectionTypeMemory,
                        memoryUsage.LruTrackingMemory,
                        memoryUsage.LfuTrackingMemory
                    },
                    megabytes = new[]
                    {
                        Math.Round(memoryUsage.TypePropertiesMemory / (1024.0 * 1024.0), 3),
                        Math.Round(memoryUsage.PropertyPathMemory / (1024.0 * 1024.0), 3),
                        Math.Round(memoryUsage.CollectionTypeMemory / (1024.0 * 1024.0), 3),
                        Math.Round(memoryUsage.LruTrackingMemory / (1024.0 * 1024.0), 3),
                        Math.Round(memoryUsage.LfuTrackingMemory / (1024.0 * 1024.0), 3)
                    },
                    percentages = new[]
                    {
                        memoryUsage.TotalMemory > 0 ? Math.Round(memoryUsage.TypePropertiesMemory * 100.0 / memoryUsage.TotalMemory, 2) : 0,
                        memoryUsage.TotalMemory > 0 ? Math.Round(memoryUsage.PropertyPathMemory * 100.0 / memoryUsage.TotalMemory, 2) : 0,
                        memoryUsage.TotalMemory > 0 ? Math.Round(memoryUsage.CollectionTypeMemory * 100.0 / memoryUsage.TotalMemory, 2) : 0,
                        memoryUsage.TotalMemory > 0 ? Math.Round(memoryUsage.LruTrackingMemory * 100.0 / memoryUsage.TotalMemory, 2) : 0,
                        memoryUsage.TotalMemory > 0 ? Math.Round(memoryUsage.LfuTrackingMemory * 100.0 / memoryUsage.TotalMemory, 2) : 0
                    }
                },

                // Chart data: Cache vs Tracking
                cacheVsTracking = new
                {
                    labels = new[] { "Cache Data", "Tracking Overhead" },
                    bytes = new[] { memoryUsage.CacheOnlyMemory, memoryUsage.TrackingOnlyMemory },
                    megabytes = new[] { Math.Round(memoryUsage.CacheOnlyMemoryMB, 3), Math.Round(memoryUsage.TrackingOnlyMemoryMB, 3) },
                    percentages = new[]
                    {
                        memoryUsage.TotalMemory > 0 ? Math.Round(memoryUsage.CacheOnlyMemory * 100.0 / memoryUsage.TotalMemory, 2) : 0,
                        memoryUsage.TotalMemory > 0 ? Math.Round(memoryUsage.TrackingOnlyMemory * 100.0 / memoryUsage.TotalMemory, 2) : 0
                    }
                },

                // Efficiency metrics
                efficiencyMetrics = new
                {
                    trackingOverheadPercentage = Math.Round(memoryUsage.CalculateTrackingOverheadPercentage(), 2),
                    cacheEfficiencyRatio = Math.Round(memoryUsage.CalculateCacheEfficiencyRatio(), 2),
                    totalMemoryMB = Math.Round(memoryUsage.TotalMemoryMB, 3)
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving memory usage");
            return StatusCode(500, new { error = "Failed to retrieve memory usage", details = ex.Message });
        }
    }

    /// <summary>
    /// Gets cache entry counts for all cache types.
    /// </summary>
    /// <returns>Count of entries in each cache type.</returns>
    [HttpGet("counts")]
    [SwaggerOperation(
        Summary = "Get cache counts",
        Description = "Retrieves the number of entries in each cache type.",
        Tags = new[] { "Statistics" }
    )]
    [ProducesResponseType(typeof(CacheCounts), StatusCodes.Status200OK)]
    public ActionResult<CacheCounts> GetCounts()
    {
        try
        {
            var counts = CacheExpose.GetCacheCounts();
            return Ok(counts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cache counts");
            return StatusCode(500, new { error = "Failed to retrieve cache counts", details = ex.Message });
        }
    }

    /// <summary>
    /// Gets tracking entry counts (LRU and LFU).
    /// </summary>
    /// <returns>Count of tracking entries for each cache type.</returns>
    [HttpGet("tracking-counts")]
    [SwaggerOperation(
        Summary = "Get tracking counts",
        Description = "Retrieves the number of LRU and LFU tracking entries for each cache type.",
        Tags = new[] { "Statistics" }
    )]
    [ProducesResponseType(typeof(TrackingCounts), StatusCodes.Status200OK)]
    public ActionResult<TrackingCounts> GetTrackingCounts()
    {
        try
        {
            var counts = CacheExpose.GetTrackingCounts();
            return Ok(counts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tracking counts");
            return StatusCode(500, new { error = "Failed to retrieve tracking counts", details = ex.Message });
        }
    }

    /// <summary>
    /// Gets a quick health summary of the cache.
    /// </summary>
    /// <returns>Short health status string.</returns>
    [HttpGet("health/summary")]
    [SwaggerOperation(
        Summary = "Get quick health summary",
        Description = "Retrieves a brief health status summary suitable for monitoring dashboards.",
        Tags = new[] { "Health" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult<object> GetQuickHealthSummary()
    {
        try
        {
            var summary = CacheExpose.GetQuickHealthSummary();
            return Ok(new { healthSummary = summary });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving health summary");
            return StatusCode(500, new { error = "Failed to retrieve health summary", details = ex.Message });
        }
    }

    /// <summary>
    /// Gets all summaries from DynamicWhere cache classes in a single call.
    /// </summary>
    /// <returns>Comprehensive summaries from all cache components.</returns>
    [HttpGet("summaries/all")]
    [SwaggerOperation(
        Summary = "Get all cache summaries",
        Description = "Retrieves all summary information from DynamicWhere cache classes including configuration, statistics, memory usage, and performance evaluation.",
        Tags = new[] { "Health" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult<object> GetAllSummaries()
    {
        try
        {
            var config = CacheExpose.GetCacheConfiguration();
            var stats = CacheExpose.GetCacheStatistics();
            var memoryUsage = CacheExpose.GetMemoryUsage();
            var evaluation = CacheExpose.EvaluatePerformance();

            var response = new
            {
                // Configuration summaries
                configuration = new
                {
                    raw = config,
                    summary = config.GetSummary(),
                    validationIssues = config.ValidateConfiguration()
                },

                // Statistics summaries
                statistics = new
                {
                    raw = stats,
                    summary = stats.GetSummary(),
                    memoryDistribution = stats.GetMemoryDistribution(),
                    utilizationPercentage = stats.CalculateUtilizationPercentage(CacheExpose.GetCacheConfigOptions().MaxCacheSize),
                    memoryEfficiency = stats.CalculateMemoryEfficiency(),
                    averageEntrySize = stats.CalculateAverageEntrySize()
                },

                // Memory usage summaries
                memoryUsage = new
                {
                    raw = memoryUsage,
                    detailedSummary = memoryUsage.GetDetailedSummary(),
                    compactSummary = memoryUsage.GetCompactSummary(),
                    memoryDistribution = memoryUsage.GetMemoryDistribution(),
                    optimizationRecommendations = memoryUsage.GetOptimizationRecommendations(),
                    largestConsumer = memoryUsage.GetLargestMemoryConsumer(),
                    healthStatus = memoryUsage.EvaluateMemoryHealthStatus(),
                    trackingOverheadPercentage = memoryUsage.CalculateTrackingOverheadPercentage(),
                    cacheEfficiencyRatio = memoryUsage.CalculateCacheEfficiencyRatio()
                },

                // Performance evaluation summaries
                performance = new
                {
                    raw = evaluation,
                    summary = evaluation.GetSummary(),
                    performanceScore = evaluation.PerformanceScore,
                    recommendations = evaluation.Recommendations,
                    healthAlerts = evaluation.HealthAlerts,
                    timestamp = evaluation.Timestamp
                },

                // Quick health summary
                quickHealthSummary = CacheExpose.GetQuickHealthSummary(),

                // Consolidated recommendations
                consolidatedRecommendations = new
                {
                    configurationRecommendations = config.ValidateConfiguration(),
                    memoryRecommendations = memoryUsage.GetOptimizationRecommendations(),
                    performanceRecommendations = evaluation.Recommendations,
                    allRecommendations = config.ValidateConfiguration()
                        .Concat(memoryUsage.GetOptimizationRecommendations())
                        .Concat(evaluation.Recommendations)
                        .Distinct()
                        .ToList()
                },

                // Overall health assessment
                overallHealth = new
                {
                    status = evaluation.HealthAlerts.Count == 0 ? "Healthy" :
                        evaluation.HealthAlerts.Any(a => a.Contains("CRITICAL")) ? "Critical" :
                        evaluation.HealthAlerts.Any(a => a.Contains("WARNING")) ? "Warning" : "Info",
                    performanceScore = Math.Round(evaluation.PerformanceScore, 2),
                    memoryHealthStatus = memoryUsage.EvaluateMemoryHealthStatus(),
                    totalMemoryMB = Math.Round(memoryUsage.TotalMemoryMB, 2),
                    utilizationPercentage = Math.Round(stats.CalculateUtilizationPercentage(CacheExpose.GetCacheConfigOptions().MaxCacheSize), 2),
                    alertCount = evaluation.HealthAlerts.Count
                }
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all summaries");
            return StatusCode(500, new { error = "Failed to retrieve all summaries", details = ex.Message });
        }
    }

    #endregion

    #region Reports Endpoints

    /// <summary>
    /// Generates a comprehensive performance report.
    /// </summary>
    /// <returns>Formatted performance report with cache efficiency metrics.</returns>
    [HttpGet("reports/performance")]
    [SwaggerOperation(
        Summary = "Generate performance report",
        Description = "Generates a detailed performance analysis report including cache efficiency metrics.",
        Tags = new[] { "Reports" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult<object> GeneratePerformanceReport()
    {
        try
        {
            var report = CacheExpose.GeneratePerformanceReport();
            return Ok(new { report });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating performance report");
            return StatusCode(500, new { error = "Failed to generate performance report", details = ex.Message });
        }
    }

    /// <summary>
    /// Generates a compact status report.
    /// </summary>
    /// <returns>Compact status report suitable for logging or dashboards.</returns>
    [HttpGet("reports/status")]
    [SwaggerOperation(
        Summary = "Generate compact status report",
        Description = "Generates a compact status report with key performance indicators.",
        Tags = new[] { "Reports" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult<object> GenerateCompactStatusReport()
    {
        try
        {
            var report = CacheExpose.GenerateCompactStatusReport();
            return Ok(new { report });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating status report");
            return StatusCode(500, new { error = "Failed to generate status report", details = ex.Message });
        }
    }

    /// <summary>
    /// Generates a detailed cache analysis report.
    /// </summary>
    /// <returns>Formatted cache analysis report.</returns>
    [HttpGet("reports/analysis")]
    [SwaggerOperation(
        Summary = "Generate cache analysis report",
        Description = "Generates a detailed analysis of cache behavior and efficiency.",
        Tags = new[] { "Reports" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult<object> GenerateCacheAnalysisReport()
    {
        try
        {
            var report = CacheExpose.GenerateCacheAnalysisReport();
            return Ok(new { report });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating cache analysis report");
            return StatusCode(500, new { error = "Failed to generate cache analysis report", details = ex.Message });
        }
    }

    /// <summary>
    /// Generates health alerts based on configured thresholds.
    /// </summary>
    /// <param name="warningThresholdMB">Memory warning threshold in MB (default: 50)</param>
    /// <param name="criticalThresholdMB">Memory critical threshold in MB (default: 100)</param>
    /// <returns>List of health alerts, empty if no issues detected.</returns>
    [HttpGet("reports/health-alerts")]
    [SwaggerOperation(
        Summary = "Generate health alerts",
        Description = "Checks cache health and generates alerts if thresholds are exceeded.",
        Tags = new[] { "Health" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult<object> GenerateHealthAlerts(
        [FromQuery] double warningThresholdMB = 50.0,
        [FromQuery] double criticalThresholdMB = 100.0)
    {
        try
        {
            var config = CacheExpose.GetCacheConfigOptions();
            var input = HealthAlertsInput.Create(config, warningThresholdMB, criticalThresholdMB);
            var alerts = CacheExpose.GenerateHealthAlerts(input);

            return Ok(new
            {
                hasAlerts = alerts.Count > 0,
                alertCount = alerts.Count,
                alerts,
                thresholds = new
                {
                    warningMB = warningThresholdMB,
                    criticalMB = criticalThresholdMB
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating health alerts");
            return StatusCode(500, new { error = "Failed to generate health alerts", details = ex.Message });
        }
    }

    /// <summary>
    /// Generates a monitoring report in key-value format.
    /// </summary>
    /// <returns>Structured monitoring data suitable for automated systems.</returns>
    [HttpGet("reports/monitoring")]
    [SwaggerOperation(
        Summary = "Generate monitoring report",
        Description = "Generates a structured monitoring report for automated monitoring systems with time-series data.",
        Tags = new[] { "Reports" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult<object> GenerateMonitoringReport()
    {
        try
        {
            var report = CacheExpose.GenerateMonitoringReport();
            var stats = CacheExpose.GetCacheStatistics();
            var memoryUsage = CacheExpose.GetMemoryUsage();
            var config = CacheExpose.GetCacheConfigOptions();

            // Enhanced monitoring report with visualization data
            var enhancedReport = new
            {
                // Original report data
                coreMetrics = report,

                // Time-series data point
                timestamp = DateTime.UtcNow.ToString("o"),

                // Key metrics for dashboards
                keyMetrics = new
                {
                    health = stats.TotalMemoryBytes < 100 * 1024 * 1024 ? "Healthy" :
                             stats.TotalMemoryBytes < 200 * 1024 * 1024 ? "Warning" : "Critical",
                    utilizationPercentage = Math.Round(stats.CalculateUtilizationPercentage(config.MaxCacheSize), 2),
                    memoryUsageMB = Math.Round(stats.TotalMemoryBytes / (1024.0 * 1024.0), 3),
                    totalEntries = stats.TotalCachedEntries,
                    performanceScore = CalculateQuickPerformanceScore(stats, memoryUsage, config)
                },

                // Chart-ready data: Cache size trend
                cacheSizeTrend = new
                {
                    labels = new[] { "Type Properties", "Property Paths", "Collection Types" },
                    current = new[] { stats.TypePropertiesCount, stats.PropertyPathCount, stats.CollectionTypeCount },
                    capacity = new[] { config.MaxCacheSize, config.MaxCacheSize, config.MaxCacheSize },
                    utilizationPercent = new[]
                    {
                        Math.Round(stats.TypePropertiesCount * 100.0 / config.MaxCacheSize, 1),
                        Math.Round(stats.PropertyPathCount * 100.0 / config.MaxCacheSize, 1),
                        Math.Round(stats.CollectionTypeCount * 100.0 / config.MaxCacheSize, 1)
                    }
                },

                // Chart-ready data: Memory trend
                memoryTrend = new
                {
                    labels = new[] { "Cache", "Tracking" },
                    current = new[]
                    {
                        Math.Round(memoryUsage.CacheOnlyMemoryMB, 3),
                        Math.Round(memoryUsage.TrackingOnlyMemoryMB, 3)
                    },
                    percentages = new[]
                    {
                        Math.Round(memoryUsage.CacheOnlyMemory * 100.0 / memoryUsage.TotalMemory, 1),
                        Math.Round(memoryUsage.TrackingOnlyMemory * 100.0 / memoryUsage.TotalMemory, 1)
                    }
                },

                // Alert thresholds
                thresholds = new
                {
                    utilizationWarning = 75,
                    utilizationCritical = 90,
                    memoryWarningMB = 50,
                    memoryCriticalMB = 100
                },

                // Active alerts
                alerts = GenerateMonitoringAlerts(stats, memoryUsage, config),

                // Recommendations
                recommendations = GenerateQuickRecommendations(stats, memoryUsage, config)
            };

            return Ok(enhancedReport);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating monitoring report");
            return StatusCode(500, new { error = "Failed to generate monitoring report", details = ex.Message });
        }
    }

    #endregion

    #region Cache Management Endpoints

    /// <summary>
    /// Clears all cache databases.
    /// </summary>
    /// <returns>Success message.</returns>
    [HttpDelete("clear")]
    [SwaggerOperation(
        Summary = "Clear all caches",
        Description = "Clears all cache databases including type properties, property paths, and collection types. Use with caution as performance will be reduced until caches are repopulated.",
        Tags = new[] { "Management" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult ClearAllCaches()
    {
        try
        {
            var beforeCounts = CacheExpose.GetCacheCounts();
            CacheExpose.ClearAllCaches();
            var afterCounts = CacheExpose.GetCacheCounts();

            _logger.LogWarning("All caches cleared");
            return Ok(new
            {
                message = "All caches cleared successfully",
                before = beforeCounts,
                after = afterCounts
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing caches");
            return StatusCode(500, new { error = "Failed to clear caches", details = ex.Message });
        }
    }

    /// <summary>
    /// Clears a specific cache type.
    /// </summary>
    /// <param name="cacheType">Type of cache to clear (TypeProperties, PropertyPath, CollectionType)</param>
    /// <returns>Success message.</returns>
    [HttpDelete("clear/{cacheType}")]
    [SwaggerOperation(
        Summary = "Clear specific cache",
        Description = "Clears a specific cache database. Valid values: TypeProperties, PropertyPath, CollectionType.",
        Tags = new[] { "Management" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult ClearCache(CacheMemoryType cacheType)
    {
        try
        {
            var beforeCounts = CacheExpose.GetCacheCounts();
            CacheExpose.ClearCache(cacheType);
            var afterCounts = CacheExpose.GetCacheCounts();

            _logger.LogWarning("Cache {CacheType} cleared", cacheType);
            return Ok(new
            {
                message = $"Cache {cacheType} cleared successfully",
                cacheType = cacheType.ToString(),
                before = beforeCounts,
                after = afterCounts
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing cache {CacheType}", cacheType);
            return StatusCode(500, new { error = $"Failed to clear cache {cacheType}", details = ex.Message });
        }
    }

    /// <summary>
    /// Forces eviction on all cache types.
    /// </summary>
    /// <returns>Success message.</returns>
    [HttpPost("evict")]
    [SwaggerOperation(
        Summary = "Force cache eviction",
        Description = "Forces eviction on all cache types regardless of their current size. Useful for memory pressure scenarios.",
        Tags = new[] { "Management" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult ForceEviction()
    {
        try
        {
            var beforeCounts = CacheExpose.GetCacheCounts();
            CacheExpose.ForceEvictionOnAllCaches();
            var afterCounts = CacheExpose.GetCacheCounts();

            _logger.LogInformation("Forced eviction on all caches");
            return Ok(new
            {
                message = "Cache eviction completed successfully",
                before = beforeCounts,
                after = afterCounts,
                entriesRemoved = new
                {
                    typeProperties = beforeCounts.TypePropertiesCount - afterCounts.TypePropertiesCount,
                    propertyPath = beforeCounts.PropertyPathCount - afterCounts.PropertyPathCount,
                    collectionType = beforeCounts.CollectionTypeCount - afterCounts.CollectionTypeCount
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error forcing cache eviction");
            return StatusCode(500, new { error = "Failed to force cache eviction", details = ex.Message });
        }
    }

    /// <summary>
    /// Checks if a specific cache type is full.
    /// </summary>
    /// <param name="cacheType">Type of cache to check</param>
    /// <returns>Boolean indicating if cache is full.</returns>
    [HttpGet("is-full/{cacheType}")]
    [SwaggerOperation(
        Summary = "Check if cache is full",
        Description = "Checks if the specified cache type has reached its maximum configured size.",
        Tags = new[] { "Management" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult<object> IsCacheFull(CacheMemoryType cacheType)
    {
        try
        {
            var isFull = CacheExpose.IsCacheFull(cacheType);
            var config = CacheExpose.GetCacheConfigOptions();
            var counts = CacheExpose.GetCacheCounts();

            int currentCount = cacheType switch
            {
                CacheMemoryType.TypeProperties => counts.TypePropertiesCount,
                CacheMemoryType.PropertyPath => counts.PropertyPathCount,
                CacheMemoryType.CollectionElementType => counts.CollectionTypeCount,
                _ => 0
            };

            return Ok(new
            {
                cacheType = cacheType.ToString(),
                isFull,
                currentCount,
                maxSize = config.MaxCacheSize,
                utilizationPercentage = currentCount * 100.0 / config.MaxCacheSize
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if cache is full");
            return StatusCode(500, new { error = "Failed to check cache status", details = ex.Message });
        }
    }

    /// <summary>
    /// Checks if eviction is needed for a specific cache type.
    /// </summary>
    /// <param name="cacheType">Type of cache to check</param>
    /// <returns>Boolean indicating if eviction is needed.</returns>
    [HttpGet("eviction-needed/{cacheType}")]
    [SwaggerOperation(
        Summary = "Check if eviction is needed",
        Description = "Determines if cache eviction is needed for the specified cache type.",
        Tags = new[] { "Management" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult<object> IsEvictionNeeded(CacheMemoryType cacheType)
    {
        try
        {
            var evictionNeeded = CacheExpose.IsEvictionNeeded(cacheType);
            var counts = CacheExpose.GetCacheCounts();

            int currentCount = cacheType switch
            {
                CacheMemoryType.TypeProperties => counts.TypePropertiesCount,
                CacheMemoryType.PropertyPath => counts.PropertyPathCount,
                CacheMemoryType.CollectionElementType => counts.CollectionTypeCount,
                _ => 0
            };

            var evictionCount = evictionNeeded ? CacheExpose.CalculateEvictionCount(currentCount) : 0;

            return Ok(new
            {
                cacheType = cacheType.ToString(),
                evictionNeeded,
                currentCount,
                estimatedEvictionCount = evictionCount
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking eviction status");
            return StatusCode(500, new { error = "Failed to check eviction status", details = ex.Message });
        }
    }

    #endregion

    #region Performance and Advanced Endpoints

    /// <summary>
    /// Evaluates cache performance and provides recommendations.
    /// </summary>
    /// <returns>Comprehensive performance evaluation with recommendations.</returns>
    [HttpGet("performance/evaluation")]
    [SwaggerOperation(
        Summary = "Evaluate cache performance",
        Description = "Performs a comprehensive evaluation of cache performance and provides optimization recommendations with visualization data.",
        Tags = new[] { "Performance" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult<object> EvaluatePerformance()
    {
        try
        {
            var evaluation = CacheExpose.EvaluatePerformance();
            var config = CacheExpose.GetCacheConfigOptions();

            var response = new
            {
                // Core evaluation data
                evaluation,

                // Summary from DynamicWhere class
                evaluationSummary = evaluation.GetSummary(),

                // Chart data: Performance score breakdown
                performanceScoreBreakdown = new
                {
                    overallScore = Math.Round(evaluation.PerformanceScore, 2),
                    scoreCategory = evaluation.PerformanceScore >= 80 ? "Excellent" :
                                    evaluation.PerformanceScore >= 60 ? "Good" :
                                    evaluation.PerformanceScore >= 40 ? "Fair" : "Poor",
                    components = new
                    {
                        utilizationScore = Math.Round(evaluation.Statistics.CalculateUtilizationPercentage(config.MaxCacheSize), 2),
                        memoryEfficiency = Math.Round(evaluation.Statistics.CalculateMemoryEfficiency(), 2),
                        trackingOverhead = Math.Round(100 - evaluation.MemoryUsage.CalculateTrackingOverheadPercentage(), 2),
                        healthStatus = evaluation.HealthAlerts.Count == 0 ? 100 :
                                       evaluation.HealthAlerts.Any(a => a.Contains("CRITICAL")) ? 30 : 70
                    }
                },

                // Timeline data for monitoring (simulated trend)
                trendData = new
                {
                    timestamp = DateTime.UtcNow,
                    metrics = new
                    {
                        totalMemoryMB = Math.Round(evaluation.MemoryUsage.TotalMemoryMB, 3),
                        totalEntries = evaluation.Statistics.TotalCachedEntries,
                        performanceScore = Math.Round(evaluation.PerformanceScore, 2)
                    }
                },

                // Detailed recommendations with priority
                prioritizedRecommendations = evaluation.Recommendations
                    .Select((rec, index) => new
                    {
                        priority = index < 3 ? "High" : index < 6 ? "Medium" : "Low",
                        recommendation = rec,
                        category = CategorizeRecommendation(rec)
                    })
                    .ToList(),

                // Health status with severity
                healthStatus = new
                {
                    status = evaluation.HealthAlerts.Count == 0 ? "Healthy" :
                             evaluation.HealthAlerts.Any(a => a.Contains("CRITICAL")) ? "Critical" :
                             evaluation.HealthAlerts.Any(a => a.Contains("WARNING")) ? "Warning" : "Info",
                    alertCount = evaluation.HealthAlerts.Count,
                    alerts = evaluation.HealthAlerts.Select(alert => new
                    {
                        severity = alert.Contains("CRITICAL") ? "Critical" :
                                   alert.Contains("WARNING") ? "Warning" : "Info",
                        message = alert
                    }).ToList()
                },

                // Configuration impact analysis
                configurationImpact = new
                {
                    currentStrategy = config.EvictionStrategy.ToString(),
                    maxCacheSize = config.MaxCacheSize,
                    trackingEnabled = config.EnableLruTracking || config.EnableLfuTracking,
                    memoryImpact = config.EnableLruTracking && config.EnableLfuTracking ? "High" :
                                   config.EnableLruTracking || config.EnableLfuTracking ? "Medium" : "Low"
                },

                // Suggested optimizations with expected impact
                suggestedOptimizations = GenerateOptimizationSuggestions(evaluation, config)
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error evaluating cache performance");
            return StatusCode(500, new { error = "Failed to evaluate cache performance", details = ex.Message });
        }
    }

    /// <summary>
    /// Warm up cache for a specific type.
    /// </summary>
    /// <param name="request">Warmup request containing type name and optional property paths</param>
    /// <returns>Success message.</returns>
    [HttpPost("warmup")]
    [SwaggerOperation(
        Summary = "Warm up cache",
        Description = "Pre-warms the cache for frequently used types and property paths to improve performance.",
        Tags = new[] { "Performance" }
    )]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult WarmupCache([FromBody] CacheWarmupRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.TypeName))
                return BadRequest(new { error = "TypeName is required" });

            // Find the type by name
            var type = FindTypeByName(request.TypeName);
            if (type == null)
                return BadRequest(new { error = $"Type '{request.TypeName}' not found" });

            var beforeCounts = CacheExpose.GetCacheCounts();
            CacheExpose.WarmupCache(type, request.PropertyPaths?.ToArray() ?? []);
            var afterCounts = CacheExpose.GetCacheCounts();

            _logger.LogInformation("Cache warmed up for type {TypeName}", request.TypeName);
            return Ok(new
            {
                message = $"Cache warmed up successfully for type {request.TypeName}",
                typeName = request.TypeName,
                propertyPathsCount = request.PropertyPaths?.Count ?? 0,
                before = beforeCounts,
                after = afterCounts
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error warming up cache");
            return StatusCode(500, new { error = "Failed to warm up cache", details = ex.Message });
        }
    }

    /// <summary>
    /// Gets memory size constants used in calculations.
    /// </summary>
    /// <returns>Dictionary of memory size constants.</returns>
    [HttpGet("utility/memory-constants")]
    [SwaggerOperation(
        Summary = "Get memory size constants",
        Description = "Returns the memory size constants used in cache calculations for reference.",
        Tags = new[] { "Utility" }
    )]
    [ProducesResponseType(typeof(Dictionary<string, long>), StatusCodes.Status200OK)]
    public ActionResult<Dictionary<string, long>> GetMemorySizeConstants()
    {
        try
        {
            var constants = CacheExpose.GetMemorySizeConstants();
            return Ok(constants);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving memory constants");
            return StatusCode(500, new { error = "Failed to retrieve memory constants", details = ex.Message });
        }
    }

    #endregion

    #region Helper Methods

    private static Type? FindTypeByName(string typeName)
    {
        // Try to find the type in the current assembly and loaded assemblies
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            try
            {
                var type = assembly.GetType(typeName);
                if (type != null)
                    return type;

                // Try with namespace
                var types = assembly.GetTypes();
                type = types.FirstOrDefault(t =>
                    t.Name == typeName ||
                    t.FullName == typeName ||
                    t.FullName?.EndsWith("." + typeName) == true);

                if (type != null)
                    return type;
            }
            catch
            {
                // Skip assemblies that can't be loaded
            }
        }

        return null;
    }

    private static List<string> GenerateStatisticsRecommendations(CacheStatistics stats, CacheOptions config)
    {
        var recommendations = new List<string>();

        // Check utilization
        var avgUtilization = stats.CalculateUtilizationPercentage(config.MaxCacheSize);
        if (avgUtilization > 90)
        {
            recommendations.Add($"📊 Cache utilization is very high ({avgUtilization:F1}%). Consider increasing MaxCacheSize from {config.MaxCacheSize} to {config.MaxCacheSize * 2}.");
        }
        else if (avgUtilization < 20)
        {
            recommendations.Add($"📊 Cache utilization is low ({avgUtilization:F1}%). Consider reducing MaxCacheSize from {config.MaxCacheSize} to {config.MaxCacheSize / 2} to save memory.");
        }

        // Check memory efficiency
        var memoryEfficiency = stats.CalculateMemoryEfficiency();
        if (memoryEfficiency < 5)
        {
            recommendations.Add($"💾 Memory efficiency is low ({memoryEfficiency:F1} entries/MB). Consider optimizing cache structure or reducing tracking overhead.");
        }

        // Check tracking overhead
        var totalTracking = stats.LruTrackingMemoryBytes + stats.LfuTrackingMemoryBytes;
        var totalCache = stats.TypePropertiesMemoryBytes + stats.PropertyPathMemoryBytes + stats.CollectionTypeMemoryBytes;
        if (totalCache > 0)
        {
            var trackingPercentage = totalTracking * 100.0 / (totalCache + totalTracking);
            if (trackingPercentage > 50)
            {
                recommendations.Add($"⚠️ Tracking overhead is high ({trackingPercentage:F1}%). Consider switching to FIFO eviction strategy to eliminate tracking costs.");
            }
        }

        // Check imbalanced cache usage
        if (stats.TotalCachedEntries > 0)
        {
            var typePropsPercentage = stats.TypePropertiesCount * 100.0 / stats.TotalCachedEntries;
            var pathsPercentage = stats.PropertyPathCount * 100.0 / stats.TotalCachedEntries;

            if (typePropsPercentage > 70 || pathsPercentage > 70)
            {
                recommendations.Add($"⚖️ Cache usage is imbalanced. Consider separate cache size limits for different cache types.");
            }
        }

        // Memory usage recommendations
        var totalMemoryMB = stats.TotalMemoryBytes / (1024.0 * 1024.0);
        if (totalMemoryMB > 100)
        {
            recommendations.Add($"🔥 Total memory usage is high ({totalMemoryMB:F2} MB). Consider implementing more aggressive eviction or reducing cache sizes.");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add("✅ Cache statistics look healthy. No immediate optimizations needed.");
        }

        return recommendations;
    }

    private static string CategorizeRecommendation(string recommendation)
    {
        if (recommendation.Contains("memory", StringComparison.OrdinalIgnoreCase) ||
            recommendation.Contains("💾") ||
            recommendation.Contains("🔥"))
            return "Memory";

        if (recommendation.Contains("utilization", StringComparison.OrdinalIgnoreCase) ||
            recommendation.Contains("📊"))
            return "Capacity";

        if (recommendation.Contains("tracking", StringComparison.OrdinalIgnoreCase) ||
            recommendation.Contains("⚠️"))
            return "Performance";

        if (recommendation.Contains("eviction", StringComparison.OrdinalIgnoreCase))
            return "Strategy";

        if (recommendation.Contains("imbalanced", StringComparison.OrdinalIgnoreCase) ||
            recommendation.Contains("⚖️"))
            return "Distribution";

        return "General";
    }

    private static List<object> GenerateOptimizationSuggestions(CachePerformanceEvaluation evaluation, CacheOptions config)
    {
        var suggestions = new List<object>();

        // Analyze current configuration and provide actionable suggestions
        var stats = evaluation.Statistics;
        var memoryUsage = evaluation.MemoryUsage;

        // Strategy optimization
        if (config.EvictionStrategy == CacheEvictionStrategy.LFU && memoryUsage.TrackingOnlyMemory > memoryUsage.CacheOnlyMemory * 0.5)
        {
            suggestions.Add(new
            {
                category = "Strategy",
                priority = "High",
                current = "LFU with high tracking overhead",
                suggested = "Switch to LRU",
                expectedImpact = $"Reduce memory by ~{Math.Round(memoryUsage.LfuTrackingMemory / (1024.0 * 1024.0), 2)} MB",
                implementation = new
                {
                    code = "CacheExpose.Configure(options => { options.EvictionStrategy = CacheEvictionStrategy.LRU; });",
                    endpoint = "PUT /api/cache/configuration"
                }
            });
        }

        // Size optimization
        var avgUtilization = stats.CalculateUtilizationPercentage(config.MaxCacheSize);
        if (avgUtilization > 90)
        {
            suggestions.Add(new
            {
                category = "Capacity",
                priority = "High",
                current = $"MaxCacheSize: {config.MaxCacheSize}, Utilization: {avgUtilization:F1}%",
                suggested = $"Increase MaxCacheSize to {config.MaxCacheSize * 2}",
                expectedImpact = "Reduce eviction frequency, improve hit rate",
                implementation = new
                {
                    code = $"CacheExpose.Configure(options => {{ options.MaxCacheSize = {config.MaxCacheSize * 2}; }});",
                    endpoint = "PUT /api/cache/configuration"
                }
            });
        }
        else if (avgUtilization < 30)
        {
            suggestions.Add(new
            {
                category = "Memory",
                priority = "Medium",
                current = $"MaxCacheSize: {config.MaxCacheSize}, Utilization: {avgUtilization:F1}%",
                suggested = $"Reduce MaxCacheSize to {config.MaxCacheSize / 2}",
                expectedImpact = $"Save ~{Math.Round(memoryUsage.TotalMemoryMB * 0.5, 2)} MB of memory",
                implementation = new
                {
                    code = $"CacheExpose.Configure(options => {{ options.MaxCacheSize = {config.MaxCacheSize / 2}; }});",
                    endpoint = "PUT /api/cache/configuration"
                }
            });
        }

        // Tracking optimization
        if (config.EnableLruTracking && config.EnableLfuTracking)
        {
            suggestions.Add(new
            {
                category = "Performance",
                priority = "Medium",
                current = "Both LRU and LFU tracking enabled",
                suggested = "Disable one tracking mechanism",
                expectedImpact = $"Save ~{Math.Round(Math.Min(memoryUsage.LruTrackingMemory, memoryUsage.LfuTrackingMemory) / (1024.0 * 1024.0), 2)} MB",
                implementation = new
                {
                    code = "CacheExpose.Configure(options => { options.EvictionStrategy = CacheEvictionStrategy.LRU; /* This auto-disables LFU */ });",
                    endpoint = "PUT /api/cache/configuration"
                }
            });
        }

        // Warmup recommendation
        if (stats.TotalCachedEntries < config.MaxCacheSize * 0.1)
        {
            suggestions.Add(new
            {
                category = "Performance",
                priority = "Low",
                current = $"Only {stats.TotalCachedEntries} entries cached",
                suggested = "Warm up cache for frequently used types",
                expectedImpact = "Improve first-use performance",
                implementation = new
                {
                    code = "CacheExpose.WarmupCache<YourType>(\"Property.Path1\", \"Property.Path2\");",
                    endpoint = "POST /api/cache/warmup"
                }
            });
        }

        // If no optimizations needed
        if (suggestions.Count == 0)
        {
            suggestions.Add(new
            {
                category = "General",
                priority = "Info",
                current = "Configuration is well-optimized",
                suggested = "No changes needed",
                expectedImpact = "Continue monitoring for changes",
                implementation = new
                {
                    code = "// No action required",
                    endpoint = "GET /api/cache/performance/evaluation"
                }
            });
        }

        return suggestions;
    }

    private static double CalculateQuickPerformanceScore(CacheStatistics stats, CacheMemoryUsage memoryUsage, CacheOptions config)
    {
        var utilizationScore = Math.Min(100, stats.CalculateUtilizationPercentage(config.MaxCacheSize));
        var memoryEfficiencyScore = Math.Min(100, stats.CalculateMemoryEfficiency() / 10.0);
        var trackingOverheadScore = Math.Max(0, 100 - memoryUsage.CalculateTrackingOverheadPercentage());
        var totalMemoryMB = stats.TotalMemoryBytes / (1024.0 * 1024.0);
        var healthScore = totalMemoryMB < 50 ? 100 : totalMemoryMB < 100 ? 70 : 30;

        return Math.Round((utilizationScore + memoryEfficiencyScore + trackingOverheadScore + healthScore) / 4.0, 2);
    }

    private static List<object> GenerateMonitoringAlerts(CacheStatistics stats, CacheMemoryUsage memoryUsage, CacheOptions config)
    {
        var alerts = new List<object>();
        var totalMemoryMB = stats.TotalMemoryBytes / (1024.0 * 1024.0);
        var avgUtilization = stats.CalculateUtilizationPercentage(config.MaxCacheSize);

        // Memory alerts
        if (totalMemoryMB > 100)
        {
            alerts.Add(new
            {
                severity = "Critical",
                category = "Memory",
                message = $"Cache memory usage is critical: {totalMemoryMB:F2} MB",
                threshold = "100 MB",
                action = "Consider clearing caches or reducing MaxCacheSize"
            });
        }
        else if (totalMemoryMB > 50)
        {
            alerts.Add(new
            {
                severity = "Warning",
                category = "Memory",
                message = $"Cache memory usage is elevated: {totalMemoryMB:F2} MB",
                threshold = "50 MB",
                action = "Monitor closely and prepare to optimize"
            });
        }

        // Utilization alerts
        if (avgUtilization > 90)
        {
            alerts.Add(new
            {
                severity = "Critical",
                category = "Capacity",
                message = $"Cache utilization is very high: {avgUtilization:F1}%",
                threshold = "90%",
                action = "Increase MaxCacheSize immediately"
            });
        }
        else if (avgUtilization > 75)
        {
            alerts.Add(new
            {
                severity = "Warning",
                category = "Capacity",
                message = $"Cache utilization is high: {avgUtilization:F1}%",
                threshold = "75%",
                action = "Consider increasing MaxCacheSize"
            });
        }

        // Tracking overhead alerts
        var trackingOverhead = memoryUsage.CalculateTrackingOverheadPercentage();
        if (trackingOverhead > 60)
        {
            alerts.Add(new
            {
                severity = "Warning",
                category = "Performance",
                message = $"Tracking overhead is high: {trackingOverhead:F1}%",
                threshold = "60%",
                action = "Consider switching to FIFO eviction strategy"
            });
        }

        return alerts;
    }

    private static List<string> GenerateQuickRecommendations(CacheStatistics stats, CacheMemoryUsage memoryUsage, CacheOptions config)
    {
        var recommendations = new List<string>();
        var avgUtilization = stats.CalculateUtilizationPercentage(config.MaxCacheSize);
        var totalMemoryMB = stats.TotalMemoryBytes / (1024.0 * 1024.0);

        // Top 3 quick recommendations
        if (avgUtilization > 85)
        {
            recommendations.Add($"🔼 Increase MaxCacheSize from {config.MaxCacheSize} to {config.MaxCacheSize * 2}");
        }

        if (totalMemoryMB > 75)
        {
            recommendations.Add($"⚠️ Total memory usage is high ({totalMemoryMB:F1} MB) - consider clearing caches");
        }

        var trackingOverhead = memoryUsage.CalculateTrackingOverheadPercentage();
        if (trackingOverhead > 50)
        {
            recommendations.Add($"🔄 Switch to FIFO to save ~{Math.Round(memoryUsage.TrackingOnlyMemoryMB, 1)} MB");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add("✅ All systems optimal");
        }

        return [.. recommendations.Take(3)];
    }

    #endregion
}

/// <summary>
/// Request model for cache warmup operations.
/// </summary>
public class CacheWarmupRequest
{
    /// <summary>
    /// The full or simple name of the type to warm up (e.g., "Product" or "DynamicWhere.API.Models.Product").
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// Optional list of frequently used property paths for this type.
    /// </summary>
    public List<string>? PropertyPaths { get; set; }
}
