using DynamicWhere.ex.Optimization.Cache.Config;

namespace DynamicWhere.ex.Optimization.Cache.DTOs;

/// <summary>
/// Represents a comprehensive cache performance evaluation.
/// </summary>
public class CachePerformanceEvaluation
{
    /// <summary>
    /// Current cache configuration.
    /// </summary>
    public CacheOptions Configuration { get; set; } = null!;

    /// <summary>
    /// Current cache statistics.
    /// </summary>
    public CacheStatistics Statistics { get; set; } = null!;

    /// <summary>
    /// Current memory usage breakdown.
    /// </summary>
    public CacheMemoryUsage MemoryUsage { get; set; } = null!;

    /// <summary>
    /// Overall performance score (0-100, higher is better).
    /// </summary>
    public double PerformanceScore { get; set; }

    /// <summary>
    /// List of optimization recommendations.
    /// </summary>
    public List<string> Recommendations { get; set; } = new();

    /// <summary>
    /// List of health alerts if any issues are detected.
    /// </summary>
    public List<string> HealthAlerts { get; set; } = new();

    /// <summary>
    /// Timestamp when the evaluation was performed.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Gets a summary of the performance evaluation.
    /// </summary>
    /// <returns>A formatted summary string.</returns>
    public string GetSummary()
    {
        return $@"Cache Performance Evaluation:
============================
Timestamp: {Timestamp:yyyy-MM-dd HH:mm:ss UTC}
Performance Score: {PerformanceScore:F1}/100
                    
Key Metrics:
- Total Entries: {Statistics.TotalCachedEntries:N0}
- Memory Usage: {Statistics.TotalMemoryMB:F2} MB
- Memory Efficiency: {Statistics.CalculateMemoryEfficiency():F2} entries/MB
- Tracking Overhead: {MemoryUsage.CalculateTrackingOverheadPercentage():F1}%
                    
Health Status: {MemoryUsage.EvaluateMemoryHealthStatus()}
                    
{(HealthAlerts.Count > 0 ? $"Alerts ({HealthAlerts.Count}):" : "No Alerts")}
{string.Join(Environment.NewLine, HealthAlerts.Take(5))}
                    
{(Recommendations.Count > 0 ? $"Recommendations ({Recommendations.Count}):" : "No Recommendations")}
{string.Join(Environment.NewLine, Recommendations.Take(3))}";
    }
}
