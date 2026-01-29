using DynamicWhere.ex.Optimization.Cache.Config;

namespace DynamicWhere.ex.Optimization.Cache.Input;

/// <summary>
/// Input parameters for generating health alerts.
/// </summary>
public class HealthAlertsInput
{
    /// <summary>
    /// Gets or sets the current cache configuration.
    /// </summary>
    public CacheOptions Config { get; set; } = null!;

    /// <summary>
    /// Gets or sets the memory warning threshold in MB.
    /// Default value: 50.0 MB
    /// </summary>
    public double WarningThresholdMB { get; set; } = 50.0;

    /// <summary>
    /// Gets or sets the memory critical threshold in MB.
    /// Default value: 100.0 MB
    /// </summary>
    public double CriticalThresholdMB { get; set; } = 100.0;

    /// <summary>
    /// Creates a new HealthAlertsInput instance with default thresholds.
    /// </summary>
    /// <param name="config">The current cache configuration.</param>
    /// <returns>A new HealthAlertsInput instance with default thresholds.</returns>
    public static HealthAlertsInput WithDefaults(CacheOptions config)
    {
        return new HealthAlertsInput
        {
            Config = config,
            WarningThresholdMB = 50.0,
            CriticalThresholdMB = 100.0
        };
    }

    /// <summary>
    /// Creates a new HealthAlertsInput instance with custom thresholds.
    /// </summary>
    /// <param name="config">The current cache configuration.</param>
    /// <param name="warningThresholdMB">Memory warning threshold in MB.</param>
    /// <param name="criticalThresholdMB">Memory critical threshold in MB.</param>
    /// <returns>A new HealthAlertsInput instance with the specified parameters.</returns>
    public static HealthAlertsInput Create(CacheOptions config, double warningThresholdMB, double criticalThresholdMB)
    {
        return new HealthAlertsInput
        {
            Config = config,
            WarningThresholdMB = warningThresholdMB,
            CriticalThresholdMB = criticalThresholdMB
        };
    }

    /// <summary>
    /// Validates that all required parameters are properly set.
    /// </summary>
    /// <returns>True if all required parameters are set, false otherwise.</returns>
    public bool IsValid()
    {
        return Config != null &&
               WarningThresholdMB > 0 &&
               CriticalThresholdMB > 0 &&
               CriticalThresholdMB > WarningThresholdMB;
    }

    /// <summary>
    /// Gets a summary of the health alert configuration.
    /// </summary>
    /// <returns>A formatted string containing health alert configuration.</returns>
    public string GetSummary()
    {
        return $@"Health Alert Configuration:
==========================
Warning Threshold: {WarningThresholdMB:F1} MB
Critical Threshold: {CriticalThresholdMB:F1} MB
Cache Strategy: {Config?.EvictionStrategy}
Max Cache Size: {Config?.MaxCacheSize:N0}";
    }
}