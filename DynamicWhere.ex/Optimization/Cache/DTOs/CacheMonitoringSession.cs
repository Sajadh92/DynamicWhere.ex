using DynamicWhere.ex;
using DynamicWhere.ex.Optimization.Cache.Source;

namespace DynamicWhere.ex.Optimization.Cache.DTOs;

/// <summary>
/// Provides continuous monitoring capabilities for cache performance.
/// </summary>
public class CacheMonitoringSession
{
    private readonly DateTime _sessionStart;
    private readonly List<CachePerformanceEvaluation> _history = new();

    /// <summary>
    /// Initializes a new cache monitoring session.
    /// </summary>
    public CacheMonitoringSession()
    {
        _sessionStart = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a performance snapshot.
    /// </summary>
    public void RecordSnapshot()
    {
        _history.Add(CacheExpose.EvaluatePerformance());
    }

    /// <summary>
    /// Gets the performance trend over the monitoring session.
    /// </summary>
    /// <returns>A summary of performance trends.</returns>
    public string GetPerformanceTrend()
    {
        if (_history.Count < 2)
            return "Insufficient data for trend analysis. Need at least 2 snapshots.";

        var first = _history.First();
        var last = _history.Last();
        var scoreTrend = last.PerformanceScore - first.PerformanceScore;
        var memoryTrend = last.Statistics.TotalMemoryMB - first.Statistics.TotalMemoryMB;
        var entriesTrend = last.Statistics.TotalCachedEntries - first.Statistics.TotalCachedEntries;

        return $@"Performance Trend Analysis
Session Duration: {DateTime.UtcNow - _sessionStart:hh\:mm\:ss}
Snapshots: {_history.Count}

Performance Score: {scoreTrend:+#.#;-#.#;0} ({first.PerformanceScore:F1} → {last.PerformanceScore:F1})
Memory Usage: {memoryTrend:+#.##;-#.##;0} MB ({first.Statistics.TotalMemoryMB:F2} → {last.Statistics.TotalMemoryMB:F2})
Cache Entries: {entriesTrend:+#;-#;0} ({first.Statistics.TotalCachedEntries} → {last.Statistics.TotalCachedEntries})

Trend Summary: {GetTrendSummary(scoreTrend, memoryTrend, entriesTrend)}";
    }

    private static string GetTrendSummary(double scoreTrend, double memoryTrend, int entriesTrend)
    {
        if (scoreTrend > 5) return "📈 Performance improving";
        if (scoreTrend < -5) return "📉 Performance declining";
        if (memoryTrend > 10) return "⚠️ Memory usage increasing significantly";
        if (entriesTrend > 1000) return "📊 Cache growing rapidly";
        return "📊 Performance stable";
    }

    /// <summary>
    /// Gets the complete monitoring history.
    /// </summary>
    /// <returns>List of all recorded performance evaluations.</returns>
    public List<CachePerformanceEvaluation> GetHistory()
    {
        return _history.ToList();
    }
}