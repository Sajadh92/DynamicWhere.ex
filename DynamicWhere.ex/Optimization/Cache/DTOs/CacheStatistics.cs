namespace DynamicWhere.ex.Optimization.Cache.DTOs;

/// <summary>
/// Data Transfer Object containing comprehensive reflection cache statistics.
/// Provides detailed information about cache usage, ACTUAL memory consumption, and tracking efficiency.
/// </summary>
public class CacheStatistics
{
    #region Entries Count 

    /// <summary>
    /// Gets or sets the number of type properties currently cached.
    /// Represents the count of unique types that have their property information cached.
    /// </summary>
    public int TypePropertiesCount { get; set; }

    /// <summary>
    /// Gets or sets the number of property paths currently cached.
    /// Represents the count of validated property path strings stored in cache.
    /// </summary>
    public int PropertyPathCount { get; set; }

    /// <summary>
    /// Gets or sets the number of collection element types currently cached.
    /// Represents the count of collection types that have their element type information cached.
    /// </summary>
    public int CollectionTypeCount { get; set; }

    /// <summary>
    /// Gets the total number of cached entries across all cache types.
    /// </summary>
    public int TotalCachedEntries => TypePropertiesCount + PropertyPathCount + CollectionTypeCount;

    #endregion Entries Count

    #region Tracking Records

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

    /// <summary>
    /// Gets the total number of tracking records across all tracking mechanisms.
    /// </summary>
    public int TotalTrackingRecords => TypeAccessRecords + PathAccessRecords + CollectionAccessRecords +
                                       TypeFrequencyRecords + PathFrequencyRecords + CollectionFrequencyRecords;

    #endregion Tracking Records

    #region Memory Usage (ACTUAL) in Bytes

    /// <summary>
    /// Gets or sets the ACTUAL memory usage for type properties cache in bytes.
    /// This represents the real memory footprint measured from the cache contents.
    /// </summary>
    public long TypePropertiesMemoryBytes { get; set; }

    /// <summary>
    /// Gets or sets the ACTUAL memory usage for property paths cache in bytes.
    /// This represents the real memory footprint measured from the cache contents.
    /// </summary>
    public long PropertyPathMemoryBytes { get; set; }

    /// <summary>
    /// Gets or sets the ACTUAL memory usage for collection types cache in bytes.
    /// This represents the real memory footprint measured from the cache contents.
    /// </summary>
    public long CollectionTypeMemoryBytes { get; set; }

    /// <summary>
    /// Gets or sets the ACTUAL memory usage for LRU tracking records in bytes.
    /// This represents the real memory footprint of all LRU tracking dictionaries.
    /// </summary>
    public long LruTrackingMemoryBytes { get; set; }

    /// <summary>
    /// Gets or sets the ACTUAL memory usage for LFU tracking records in bytes.
    /// This represents the real memory footprint of all LFU tracking dictionaries.
    /// </summary>
    public long LfuTrackingMemoryBytes { get; set; }

    /// <summary>
    /// Gets the total ACTUAL memory usage in bytes across all caches and tracking mechanisms.
    /// </summary>
    public long TotalMemoryBytes => TypePropertiesMemoryBytes + PropertyPathMemoryBytes +
                                   CollectionTypeMemoryBytes + LruTrackingMemoryBytes + LfuTrackingMemoryBytes;

    #endregion Memory Usage (ACTUAL) in Bytes

    #region Memory Usage (ACTUAL) in MB

    /// <summary>
    /// Gets the ACTUAL memory usage for type properties cache in MB.
    /// </summary>
    public double TypePropertiesMemoryMB => Math.Round(TypePropertiesMemoryBytes / (1024.0 * 1024), 3);

    /// <summary>
    /// Gets the ACTUAL memory usage for property paths cache in MB.
    /// </summary>
    public double PropertyPathMemoryMB => Math.Round(PropertyPathMemoryBytes / (1024.0 * 1024), 3);

    /// <summary>
    /// Gets the ACTUAL memory usage for collection element types cache in MB.
    /// </summary>
    public double CollectionTypeMemoryMB => Math.Round(CollectionTypeMemoryBytes / (1024.0 * 1024), 3);

    /// <summary>
    /// Gets the ACTUAL memory usage for LRU tracking records in MB.
    /// </summary>
    public double LruTrackingMemoryMB => Math.Round(LruTrackingMemoryBytes / (1024.0 * 1024), 3);

    /// <summary>
    /// Gets the ACTUAL memory usage for LFU tracking records in MB.
    /// </summary>
    public double LfuTrackingMemoryMB => Math.Round(LfuTrackingMemoryBytes / (1024.0 * 1024), 3);

    /// <summary>
    /// Gets the total ACTUAL memory usage across all caches and tracking mechanisms in MB.
    /// </summary>
    public double TotalMemoryMB => Math.Round((TypePropertiesMemoryBytes + PropertyPathMemoryBytes +
                                             CollectionTypeMemoryBytes + LruTrackingMemoryBytes +
                                             LfuTrackingMemoryBytes) / (1024.0 * 1024), 3);

    #endregion Memory Usage (ACTUAL) in MB

    /// <summary>
    /// Calculates the cache utilization percentage based on the maximum cache size.
    /// </summary>
    /// <param name="maxCacheSize">The maximum allowed cache size per cache type.</param>
    /// <returns>The average utilization percentage across all cache types.</returns>
    public double CalculateUtilizationPercentage(int maxCacheSize)
    {
        if (maxCacheSize <= 0) return 0.0;

        var typeUtilization = (double)TypePropertiesCount / maxCacheSize * 100;
        var pathUtilization = (double)PropertyPathCount / maxCacheSize * 100;
        var collectionUtilization = (double)CollectionTypeCount / maxCacheSize * 100;

        return (typeUtilization + pathUtilization + collectionUtilization) / 3.0;
    }

    /// <summary>
    /// Calculates the memory efficiency ratio (cached entries per MB of memory used).
    /// Higher values indicate better memory efficiency.
    /// </summary>
    /// <returns>The number of cached entries per MB of memory used.</returns>
    public double CalculateMemoryEfficiency()
    {
        return TotalMemoryMB > 0 ? Math.Round(TotalCachedEntries / TotalMemoryMB, 2) : 0.0;
    }

    /// <summary>
    /// Calculates the average ACTUAL memory usage per cached entry in bytes.
    /// </summary>
    /// <returns>Average memory usage per entry in bytes.</returns>
    public double CalculateAverageEntrySize()
    {
        return TotalCachedEntries > 0 ? Math.Round((double)TotalMemoryBytes / TotalCachedEntries, 2) : 0.0;
    }

    /// <summary>
    /// Gets a breakdown of ACTUAL memory usage by cache type as percentages.
    /// </summary>
    /// <returns>A dictionary containing memory distribution percentages.</returns>
    public Dictionary<string, double> GetMemoryDistribution()
    {
        if (TotalMemoryMB == 0) return new Dictionary<string, double>();

        return new Dictionary<string, double>
        {
            ["TypeProperties"] = Math.Round(TypePropertiesMemoryMB / TotalMemoryMB * 100, 1),
            ["PropertyPaths"] = Math.Round(PropertyPathMemoryMB / TotalMemoryMB * 100, 1),
            ["CollectionTypes"] = Math.Round(CollectionTypeMemoryMB / TotalMemoryMB * 100, 1),
            ["LruTracking"] = Math.Round(LruTrackingMemoryMB / TotalMemoryMB * 100, 1),
            ["LfuTracking"] = Math.Round(LfuTrackingMemoryMB / TotalMemoryMB * 100, 1)
        };
    }

    /// <summary>
    /// Gets a detailed summary of cache statistics with ACTUAL memory measurements.
    /// </summary>
    /// <returns>A human-readable summary of all cache statistics with real memory usage.</returns>
    public string GetSummary()
    {
        var memoryDistribution = GetMemoryDistribution();
        var efficiency = CalculateMemoryEfficiency();
        var avgEntrySize = CalculateAverageEntrySize();

        return $@"Cache Statistics Summary (ACTUAL MEMORY):
=========================================
Cached Entries:
- Type Properties: {TypePropertiesCount:N0} ({TypePropertiesMemoryMB:F3} MB)
- Property Paths: {PropertyPathCount:N0} ({PropertyPathMemoryMB:F3} MB)
- Collection Types: {CollectionTypeCount:N0} ({CollectionTypeMemoryMB:F3} MB)
- Total Entries: {TotalCachedEntries:N0}
                  
ACTUAL Memory Usage:
- Type Properties Cache: {TypePropertiesMemoryMB:F3} MB ({memoryDistribution.GetValueOrDefault("TypeProperties", 0):F1}%) [{TypePropertiesMemoryBytes:N0} bytes]
- Property Paths Cache: {PropertyPathMemoryMB:F3} MB ({memoryDistribution.GetValueOrDefault("PropertyPaths", 0):F1}%) [{PropertyPathMemoryBytes:N0} bytes]
- Collection Types Cache: {CollectionTypeMemoryMB:F3} MB ({memoryDistribution.GetValueOrDefault("CollectionTypes", 0):F1}%) [{CollectionTypeMemoryBytes:N0} bytes]
- LRU Tracking: {LruTrackingMemoryMB:F3} MB ({memoryDistribution.GetValueOrDefault("LruTracking", 0):F1}%) [{LruTrackingMemoryBytes:N0} bytes]
- LFU Tracking: {LfuTrackingMemoryMB:F3} MB ({memoryDistribution.GetValueOrDefault("LfuTracking", 0):F1}%) [{LfuTrackingMemoryBytes:N0} bytes]
- Total Memory: {TotalMemoryMB:F3} MB [{TotalMemoryBytes:N0} bytes]
                  
LRU Tracking Records:
- Type Access Times: {TypeAccessRecords:N0}
- Path Access Times: {PathAccessRecords:N0}
- Collection Access Times: {CollectionAccessRecords:N0}
                  
LFU Tracking Records:
- Type Frequencies: {TypeFrequencyRecords:N0}
- Path Frequencies: {PathFrequencyRecords:N0}
- Collection Frequencies: {CollectionFrequencyRecords:N0}
                  
Performance Metrics:
- Total Tracking Records: {TotalTrackingRecords:N0}
- Memory Efficiency: {efficiency:F2} entries/MB
- Average Entry Size: {avgEntrySize:F2} bytes ({avgEntrySize / 1024:F3} KB)
- Memory per Entry Type:
    * Type Properties: {(TypePropertiesCount > 0 ? (double)TypePropertiesMemoryBytes / TypePropertiesCount : 0):F0} bytes/entry
    * Property Paths: {(PropertyPathCount > 0 ? (double)PropertyPathMemoryBytes / PropertyPathCount : 0):F0} bytes/entry
    * Collection Types: {(CollectionTypeCount > 0 ? (double)CollectionTypeMemoryBytes / CollectionTypeCount : 0):F0} bytes/entry";
    }

    /// <summary>
    /// Creates a new CacheStatistics instance from the raw values with ACTUAL memory measurements.
    /// </summary>
    /// <param name="typePropertiesCount">Number of cached type properties.</param>
    /// <param name="propertyPathCount">Number of cached property paths.</param>
    /// <param name="collectionTypeCount">Number of cached collection types.</param>
    /// <param name="typeAccessRecords">Number of type LRU access records.</param>
    /// <param name="pathAccessRecords">Number of path LRU access records.</param>
    /// <param name="collectionAccessRecords">Number of collection LRU access records.</param>
    /// <param name="typeFrequencyRecords">Number of type LFU frequency records.</param>
    /// <param name="pathFrequencyRecords">Number of path LFU frequency records.</param>
    /// <param name="collectionFrequencyRecords">Number of collection LFU frequency records.</param>
    /// <param name="typePropertiesMemoryBytes">ACTUAL memory usage of type properties cache in bytes.</param>
    /// <param name="propertyPathMemoryBytes">ACTUAL memory usage of property paths cache in bytes.</param>
    /// <param name="collectionTypeMemoryBytes">ACTUAL memory usage of collection types cache in bytes.</param>
    /// <param name="lruTrackingMemoryBytes">ACTUAL memory usage of LRU tracking in bytes.</param>
    /// <param name="lfuTrackingMemoryBytes">ACTUAL memory usage of LFU tracking in bytes.</param>
    /// <returns>A new CacheStatistics instance populated with ACTUAL memory measurements.</returns>
    public static CacheStatistics FromValues(
        int typePropertiesCount,
        int propertyPathCount,
        int collectionTypeCount,
        int typeAccessRecords,
        int pathAccessRecords,
        int collectionAccessRecords,
        int typeFrequencyRecords,
        int pathFrequencyRecords,
        int collectionFrequencyRecords,
        long typePropertiesMemoryBytes,
        long propertyPathMemoryBytes,
        long collectionTypeMemoryBytes,
        long lruTrackingMemoryBytes,
        long lfuTrackingMemoryBytes)
    {
        return new CacheStatistics
        {
            TypePropertiesCount = typePropertiesCount,
            PropertyPathCount = propertyPathCount,
            CollectionTypeCount = collectionTypeCount,
            TypeAccessRecords = typeAccessRecords,
            PathAccessRecords = pathAccessRecords,
            CollectionAccessRecords = collectionAccessRecords,
            TypeFrequencyRecords = typeFrequencyRecords,
            PathFrequencyRecords = pathFrequencyRecords,
            CollectionFrequencyRecords = collectionFrequencyRecords,
            TypePropertiesMemoryBytes = typePropertiesMemoryBytes,
            PropertyPathMemoryBytes = propertyPathMemoryBytes,
            CollectionTypeMemoryBytes = collectionTypeMemoryBytes,
            LruTrackingMemoryBytes = lruTrackingMemoryBytes,
            LfuTrackingMemoryBytes = lfuTrackingMemoryBytes
        };
    }
}