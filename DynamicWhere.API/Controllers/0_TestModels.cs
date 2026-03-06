namespace DynamicWhere.API.Controllers;

/// <summary>
/// Performance metrics for a single test.
/// </summary>
public class PerformanceMetrics
{
    /// <summary>
    /// Time taken to translate the query to SQL (in milliseconds).
    /// </summary>
    public double TranslationTimeMs { get; set; }

    /// <summary>
    /// Time taken to execute the query in database (in milliseconds).
    /// </summary>
    public double ExecutionTimeMs { get; set; }

    /// <summary>
    /// Total time (translation + execution) in milliseconds.
    /// </summary>
    public double TotalTimeMs { get; set; }

    /// <summary>
    /// Number of records returned by the query.
    /// </summary>
    public int RecordsReturned { get; set; }

    /// <summary>
    /// Generated SQL query string.
    /// </summary>
    public string? QueryGenerated { get; set; }

    /// <summary>
    /// Additional performance information.
    /// </summary>
    public Dictionary<string, object>? AdditionalInfo { get; set; }
}

/// <summary>
/// Result of a single performance test.
/// </summary>
public class PerformanceResult
{
    /// <summary>
    /// Name of the test.
    /// </summary>
    public string TestName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the test succeeded.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Optional message about the test.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Performance metrics.
    /// </summary>
    public PerformanceMetrics Metrics { get; set; } = new();

    /// <summary>
    /// Input data used for the test.
    /// </summary>
    public object? Input { get; set; }

    /// <summary>
    /// Output value produced by the operation.
    /// </summary>
    public object? Output { get; set; }
}

/// <summary>
/// Aggregated results from all tests.
/// </summary>
public class AllTestsResult
{
    /// <summary>
    /// Total number of tests executed.
    /// </summary>
    public int TotalTests { get; set; }

    /// <summary>
    /// Number of successful tests.
    /// </summary>
    public int SuccessfulTests { get; set; }

    /// <summary>
    /// Number of failed tests.
    /// </summary>
    public int FailedTests { get; set; }

    /// <summary>
    /// Total execution time for all tests (in milliseconds).
    /// </summary>
    public double TotalExecutionTimeMs { get; set; }

    /// <summary>
    /// Average translation time across all tests (in milliseconds).
    /// </summary>
    public double AverageTranslationTimeMs { get; set; }

    /// <summary>
    /// Average database execution time across all tests (in milliseconds).
    /// </summary>
    public double AverageExecutionTimeMs { get; set; }

    /// <summary>
    /// Average total time across all tests (in milliseconds).
    /// </summary>
    public double AverageTotalTimeMs { get; set; }

    /// <summary>
    /// Total number of records returned across all tests.
    /// </summary>
    public int TotalRecordsReturned { get; set; }

    /// <summary>
    /// Individual test results.
    /// </summary>
    public List<PerformanceResult> Results { get; set; } = [];
}
