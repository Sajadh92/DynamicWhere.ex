namespace DynamicWhere.ex.Optimization.Cache.Output;

/// <summary>
/// Represents the count of entries in all tracking databases.
/// This class replaces tuple return types for better traceability and maintainability.
/// </summary>
public class TrackingCounts
{
    #region LRU Tracking Records

    /// <summary>
    /// Gets or sets the number of type access time records (LRU tracking).
    /// Indicates how many type entries have LRU access tracking data.
    /// </summary>
    public int TypeAccessRecords { get; set; }

    /// <summary>
    /// Gets or sets the number of property path access time records (LRU tracking).
    /// Indicates how many property path entries have LRU access tracking data.
    /// </summary>
    public int PathAccessRecords { get; set; }

    /// <summary>
    /// Gets or sets the number of collection type access time records (LRU tracking).
    /// Indicates how many collection type entries have LRU access tracking data.
    /// </summary>
    public int CollectionAccessRecords { get; set; }

    #endregion LRU Tracking Records

    #region LFU Tracking Records

    /// <summary>
    /// Gets or sets the number of type access frequency records (LFU tracking).
    /// Indicates how many type entries have LFU frequency tracking data.
    /// </summary>
    public int TypeFrequencyRecords { get; set; }

    /// <summary>
    /// Gets or sets the number of property path access frequency records (LFU tracking).
    /// Indicates how many property path entries have LFU frequency tracking data.
    /// </summary>
    public int PathFrequencyRecords { get; set; }

    /// <summary>
    /// Gets or sets the number of collection type access frequency records (LFU tracking).
    /// Indicates how many collection type entries have LFU frequency tracking data.
    /// </summary>
    public int CollectionFrequencyRecords { get; set; }

    #endregion LFU Tracking Records

    #region Calculated Properties

    /// <summary>
    /// Gets the total number of LRU tracking records across all cache types.
    /// </summary>
    public int TotalLruRecords => TypeAccessRecords + PathAccessRecords + CollectionAccessRecords;

    /// <summary>
    /// Gets the total number of LFU tracking records across all cache types.
    /// </summary>
    public int TotalLfuRecords => TypeFrequencyRecords + PathFrequencyRecords + CollectionFrequencyRecords;

    /// <summary>
    /// Gets the total number of tracking records across all tracking mechanisms.
    /// </summary>
    public int TotalTrackingRecords => TotalLruRecords + TotalLfuRecords;

    #endregion Calculated Properties

    /// <summary>
    /// Creates a new TrackingCounts instance from individual count values.
    /// </summary>
    /// <param name="typeAccessRecords">Number of type LRU access records.</param>
    /// <param name="pathAccessRecords">Number of path LRU access records.</param>
    /// <param name="collectionAccessRecords">Number of collection LRU access records.</param>
    /// <param name="typeFrequencyRecords">Number of type LFU frequency records.</param>
    /// <param name="pathFrequencyRecords">Number of path LFU frequency records.</param>
    /// <param name="collectionFrequencyRecords">Number of collection LFU frequency records.</param>
    /// <returns>A new TrackingCounts instance with the specified values.</returns>
    public static TrackingCounts FromValues(
        int typeAccessRecords,
        int pathAccessRecords,
        int collectionAccessRecords,
        int typeFrequencyRecords,
        int pathFrequencyRecords,
        int collectionFrequencyRecords)
    {
        return new TrackingCounts
        {
            TypeAccessRecords = typeAccessRecords,
            PathAccessRecords = pathAccessRecords,
            CollectionAccessRecords = collectionAccessRecords,
            TypeFrequencyRecords = typeFrequencyRecords,
            PathFrequencyRecords = pathFrequencyRecords,
            CollectionFrequencyRecords = collectionFrequencyRecords
        };
    }

    /// <summary>
    /// Gets a summary of tracking counts.
    /// </summary>
    /// <returns>A formatted string containing tracking count information.</returns>
    public string GetSummary()
    {
        return $@"Tracking Counts Summary:
========================
LRU Tracking Records:
  - Type Properties: {TypeAccessRecords:N0}
  - Property Paths: {PathAccessRecords:N0}
  - Collection Types: {CollectionAccessRecords:N0}
  - Total LRU: {TotalLruRecords:N0}

LFU Tracking Records:
  - Type Properties: {TypeFrequencyRecords:N0}
  - Property Paths: {PathFrequencyRecords:N0}
  - Collection Types: {CollectionFrequencyRecords:N0}
  - Total LFU: {TotalLfuRecords:N0}

Total Tracking Records: {TotalTrackingRecords:N0}";
    }
}