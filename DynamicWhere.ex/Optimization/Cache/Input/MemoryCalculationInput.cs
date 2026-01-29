using DynamicWhere.ex.Optimization.Cache.Output;
using System.Collections.Concurrent;
using System.Reflection;

namespace DynamicWhere.ex.Optimization.Cache.Input;

/// <summary>
/// Input parameters for calculating actual memory usage of cache components.
/// </summary>
public class MemoryCalculationInput
{
    #region Cache Dictionaries

    /// <summary>
    /// Gets or sets the type properties cache to measure.
    /// </summary>
    public ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> TypePropertiesCache { get; set; } = null!;

    /// <summary>
    /// Gets or sets the property paths cache to measure.
    /// </summary>
    public ConcurrentDictionary<(Type, string), string> PropertyPathCache { get; set; } = null!;

    /// <summary>
    /// Gets or sets the collection element types cache to measure.
    /// </summary>
    public ConcurrentDictionary<Type, Type?> CollectionElementTypeCache { get; set; } = null!;

    #endregion Cache Dictionaries

    #region LRU Tracking Dictionaries

    /// <summary>
    /// Gets or sets the LRU tracking for type properties.
    /// </summary>
    public ConcurrentDictionary<Type, long> TypePropertiesAccessTime { get; set; } = null!;

    /// <summary>
    /// Gets or sets the LRU tracking for property paths.
    /// </summary>
    public ConcurrentDictionary<(Type, string), long> PropertyPathAccessTime { get; set; } = null!;

    /// <summary>
    /// Gets or sets the LRU tracking for collection types.
    /// </summary>
    public ConcurrentDictionary<Type, long> CollectionElementTypeAccessTime { get; set; } = null!;

    #endregion LRU Tracking Dictionaries

    #region LFU Tracking Dictionaries

    /// <summary>
    /// Gets or sets the LFU tracking for type properties.
    /// </summary>
    public ConcurrentDictionary<Type, long> TypePropertiesAccessCount { get; set; } = null!;

    /// <summary>
    /// Gets or sets the LFU tracking for property paths.
    /// </summary>
    public ConcurrentDictionary<(Type, string), long> PropertyPathAccessCount { get; set; } = null!;

    /// <summary>
    /// Gets or sets the LFU tracking for collection types.
    /// </summary>
    public ConcurrentDictionary<Type, long> CollectionElementTypeAccessCount { get; set; } = null!;

    #endregion LFU Tracking Dictionaries

    #region Factory Methods

    /// <summary>
    /// Creates a new MemoryCalculationInput instance from individual cache dictionaries.
    /// </summary>
    /// <param name="typePropertiesCache">The type properties cache to measure.</param>
    /// <param name="propertyPathCache">The property paths cache to measure.</param>
    /// <param name="collectionElementTypeCache">The collection element types cache to measure.</param>
    /// <param name="typePropertiesAccessTime">LRU tracking for type properties.</param>
    /// <param name="propertyPathAccessTime">LRU tracking for property paths.</param>
    /// <param name="collectionElementTypeAccessTime">LRU tracking for collection types.</param>
    /// <param name="typePropertiesAccessCount">LFU tracking for type properties.</param>
    /// <param name="propertyPathAccessCount">LFU tracking for property paths.</param>
    /// <param name="collectionElementTypeAccessCount">LFU tracking for collection types.</param>
    /// <returns>A new MemoryCalculationInput instance with the specified parameters.</returns>
    public static MemoryCalculationInput Create(
        ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> typePropertiesCache,
        ConcurrentDictionary<(Type, string), string> propertyPathCache,
        ConcurrentDictionary<Type, Type?> collectionElementTypeCache,
        ConcurrentDictionary<Type, long> typePropertiesAccessTime,
        ConcurrentDictionary<(Type, string), long> propertyPathAccessTime,
        ConcurrentDictionary<Type, long> collectionElementTypeAccessTime,
        ConcurrentDictionary<Type, long> typePropertiesAccessCount,
        ConcurrentDictionary<(Type, string), long> propertyPathAccessCount,
        ConcurrentDictionary<Type, long> collectionElementTypeAccessCount)
    {
        return new MemoryCalculationInput
        {
            TypePropertiesCache = typePropertiesCache,
            PropertyPathCache = propertyPathCache,
            CollectionElementTypeCache = collectionElementTypeCache,
            TypePropertiesAccessTime = typePropertiesAccessTime,
            PropertyPathAccessTime = propertyPathAccessTime,
            CollectionElementTypeAccessTime = collectionElementTypeAccessTime,
            TypePropertiesAccessCount = typePropertiesAccessCount,
            PropertyPathAccessCount = propertyPathAccessCount,
            CollectionElementTypeAccessCount = collectionElementTypeAccessCount
        };
    }

    /// <summary>
    /// Creates a new MemoryCalculationInput instance from CacheDatabases.
    /// </summary>
    /// <param name="databases">The cache databases containing all dictionaries.</param>
    /// <returns>A new MemoryCalculationInput instance with databases content.</returns>
    public static MemoryCalculationInput FromDatabases(CacheDatabases databases)
    {
        return Create(
            databases.TypePropertiesCache,
            databases.PropertyPathCache,
            databases.CollectionElementTypeCache,
            databases.TypePropertiesAccessTime,
            databases.PropertyPathAccessTime,
            databases.CollectionElementTypeAccessTime,
            databases.TypePropertiesAccessCount,
            databases.PropertyPathAccessCount,
            databases.CollectionElementTypeAccessCount);
    }

    #endregion Factory Methods

    #region Validation and Helper Methods

    /// <summary>
    /// Validates that all required parameters are properly set.
    /// </summary>
    /// <returns>True if all required parameters are set, false otherwise.</returns>
    public bool IsValid()
    {
        return TypePropertiesCache != null &&
               PropertyPathCache != null &&
               CollectionElementTypeCache != null &&
               TypePropertiesAccessTime != null &&
               PropertyPathAccessTime != null &&
               CollectionElementTypeAccessTime != null &&
               TypePropertiesAccessCount != null &&
               PropertyPathAccessCount != null &&
               CollectionElementTypeAccessCount != null;
    }

    /// <summary>
    /// Gets basic counts information for the caches being measured.
    /// </summary>
    /// <returns>A CacheCounts instance with current entry counts.</returns>
    public CacheCounts GetCacheCounts()
    {
        return CacheCounts.FromValues(
            TypePropertiesCache.Count,
            PropertyPathCache.Count,
            CollectionElementTypeCache.Count);
    }

    /// <summary>
    /// Gets tracking counts information for the caches being measured.
    /// </summary>
    /// <returns>A TrackingCounts instance with current tracking record counts.</returns>
    public TrackingCounts GetTrackingCounts()
    {
        return TrackingCounts.FromValues(
            TypePropertiesAccessTime.Count,
            PropertyPathAccessTime.Count,
            CollectionElementTypeAccessTime.Count,
            TypePropertiesAccessCount.Count,
            PropertyPathAccessCount.Count,
            CollectionElementTypeAccessCount.Count);
    }

    /// <summary>
    /// Gets a summary of what will be measured.
    /// </summary>
    /// <returns>A formatted string containing measurement scope information.</returns>
    public string GetMeasurementSummary()
    {
        var cacheCounts = GetCacheCounts();
        var trackingCounts = GetTrackingCounts();

        return $@"Memory Calculation Scope:
=========================
Cache Entries to Measure:
{cacheCounts.GetSummary()}

Tracking Records to Measure:
{trackingCounts.GetSummary()}";
    }

    #endregion Validation and Helper Methods
}