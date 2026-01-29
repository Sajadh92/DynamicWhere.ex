namespace DynamicWhere.ex.Optimization.Cache.DTOs;

/// <summary>
/// Data Transfer Object containing current reflection cache configuration settings.
/// Provides detailed information about cache behavior, eviction strategy, and operational parameters.
/// </summary>
public class CacheConfiguration
{
    /// <summary>
    /// Gets or sets the maximum number of entries allowed per cache type.
    /// When this limit is exceeded, cache eviction is triggered.
    /// </summary>
    public int MaxCacheSize { get; set; }

    /// <summary>
    /// Gets or sets the percentage of least used entries to remove during eviction.
    /// Valid range: 1-50%. Higher values remove more entries per eviction cycle.
    /// </summary>
    public int LeastUsedThreshold { get; set; }

    /// <summary>
    /// Gets or sets the percentage of most used entries to keep during eviction.
    /// This value should equal (100 - LeastUsedThreshold) for consistency.
    /// </summary>
    public int MostUsedThreshold { get; set; }

    /// <summary>
    /// Gets or sets the eviction strategy currently in use.
    /// Determines how cache entries are selected for removal when cache is full.
    /// </summary>
    public string EvictionStrategy { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether LRU (Least Recently Used) tracking is enabled.
    /// When true, access times are tracked for temporal-based eviction.
    /// </summary>
    public bool EnableLruTracking { get; set; }

    /// <summary>
    /// Gets or sets whether LFU (Least Frequently Used) tracking is enabled.
    /// When true, access frequencies are tracked for frequency-based eviction.
    /// </summary>
    public bool EnableLfuTracking { get; set; }

    /// <summary>
    /// Gets or sets whether automatic configuration validation is enabled.
    /// When true, configuration inconsistencies are automatically corrected.
    /// </summary>
    public bool AutoValidateConfiguration { get; set; }

    /// <summary>
    /// Gets a description of the eviction strategy and its characteristics.
    /// </summary>
    public string EvictionStrategyDescription => EvictionStrategy switch
    {
        "FIFO" => "First In, First Out - Simple, predictable eviction with minimal overhead",
        "LRU" => "Least Recently Used - Optimizes for temporal locality of access patterns",
        "LFU" => "Least Frequently Used - Optimizes for access frequency patterns",
        _ => "Unknown eviction strategy"
    };

    /// <summary>
    /// Gets whether any tracking mechanism is currently enabled.
    /// </summary>
    public bool IsTrackingEnabled => EnableLruTracking || EnableLfuTracking;

    /// <summary>
    /// Gets the memory overhead category based on current configuration.
    /// </summary>
    public string MemoryOverhead => EvictionStrategy switch
    {
        "FIFO" => "Minimal",
        "LRU" => "Low (timestamp tracking)",
        "LFU" => "Low (frequency tracking)",
        _ => "Unknown"
    };

    /// <summary>
    /// Validates the current configuration for consistency and correctness.
    /// </summary>
    /// <returns>A list of validation issues, or empty list if configuration is valid.</returns>
    public List<string> ValidateConfiguration()
    {
        var issues = new List<string>();

        if (MaxCacheSize <= 0)
        {
            issues.Add("MaxCacheSize must be greater than 0");
        }

        if (LeastUsedThreshold < 1 || LeastUsedThreshold > 50)
        {
            issues.Add("LeastUsedThreshold must be between 1 and 50 percent");
        }

        if (MostUsedThreshold < 50 || MostUsedThreshold > 99)
        {
            issues.Add("MostUsedThreshold must be between 50 and 99 percent");
        }

        if (LeastUsedThreshold + MostUsedThreshold != 100)
        {
            issues.Add($"LeastUsedThreshold ({LeastUsedThreshold}) + MostUsedThreshold ({MostUsedThreshold}) must equal 100");
        }

        if (EnableLruTracking && EnableLfuTracking)
        {
            issues.Add("Both LRU and LFU tracking cannot be enabled simultaneously");
        }

        var expectedLruTracking = EvictionStrategy == "LRU";
        var expectedLfuTracking = EvictionStrategy == "LFU";

        if (EnableLruTracking && !expectedLruTracking)
        {
            issues.Add($"LRU tracking is enabled but eviction strategy is {EvictionStrategy}");
        }

        if (EnableLfuTracking && !expectedLfuTracking)
        {
            issues.Add($"LFU tracking is enabled but eviction strategy is {EvictionStrategy}");
        }

        return issues;
    }

    /// <summary>
    /// Gets a detailed summary of cache configuration as a formatted string.
    /// </summary>
    /// <returns>A human-readable summary of all configuration settings.</returns>
    public string GetSummary()
    {
        var validationIssues = ValidateConfiguration();
        var validationStatus = validationIssues.Count == 0 ? "✓ Valid" : $"⚠ {validationIssues.Count} Issues";

        return $@"Cache Configuration Summary:
============================
Cache Settings:
    - Max Cache Size: {MaxCacheSize:N0} entries per cache type
    - Eviction Threshold: Remove {LeastUsedThreshold}%, Keep {MostUsedThreshold}%
    - Eviction Strategy: {EvictionStrategy}
    - Strategy Description: {EvictionStrategyDescription}
                  
Tracking Settings:
    - LRU Tracking: {(EnableLruTracking ? "Enabled" : "Disabled")}
    - LFU Tracking: {(EnableLfuTracking ? "Enabled" : "Disabled")}
    - Auto Validation: {(AutoValidateConfiguration ? "Enabled" : "Disabled")}
                  
Performance Characteristics:
    - Memory Overhead: {MemoryOverhead}
    - Tracking Enabled: {(IsTrackingEnabled ? "Yes" : "No")}
                  
Configuration Status: {validationStatus}";
    }

    /// <summary>
    /// Creates a new CacheConfiguration instance from the raw values.
    /// </summary>
    /// <param name="maxCacheSize">Maximum cache size per cache type.</param>
    /// <param name="leastUsedThreshold">Percentage of entries to remove during eviction.</param>
    /// <param name="mostUsedThreshold">Percentage of entries to keep during eviction.</param>
    /// <param name="evictionStrategy">Current eviction strategy name.</param>
    /// <param name="enableLruTracking">Whether LRU tracking is enabled.</param>
    /// <param name="enableLfuTracking">Whether LFU tracking is enabled.</param>
    /// <param name="autoValidateConfiguration">Whether auto-validation is enabled.</param>
    /// <returns>A new CacheConfiguration instance populated with the provided values.</returns>
    public static CacheConfiguration FromValues(
        int maxCacheSize,
        int leastUsedThreshold,
        int mostUsedThreshold,
        string evictionStrategy,
        bool enableLruTracking = false,
        bool enableLfuTracking = false,
        bool autoValidateConfiguration = true)
    {
        return new CacheConfiguration
        {
            MaxCacheSize = maxCacheSize,
            LeastUsedThreshold = leastUsedThreshold,
            MostUsedThreshold = mostUsedThreshold,
            EvictionStrategy = evictionStrategy,
            EnableLruTracking = enableLruTracking,
            EnableLfuTracking = enableLfuTracking,
            AutoValidateConfiguration = autoValidateConfiguration
        };
    }
}