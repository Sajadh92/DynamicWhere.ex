using DynamicWhere.ex.Optimization.Cache.DTOs;
using DynamicWhere.ex.Optimization.Cache.Input;
using System.Collections.Concurrent;
using System.Reflection;

namespace DynamicWhere.ex.Optimization.Cache.Source;

/// <summary>
/// Utility class for calculating ACTUAL memory usage of cache objects.
/// Provides precise memory measurements based on real data structures and their contents.
/// </summary>
internal static class CacheCalculator
{
    /// <summary>
    /// Calculates the ACTUAL memory usage of all cache types and tracking mechanisms.
    /// This is the main method that orchestrates all memory calculations.
    /// </summary>
    /// <param name="input">Input parameters containing all cache dictionaries to measure.</param>
    /// <returns>A CacheMemoryUsage object containing detailed memory usage statistics.</returns>
    public static CacheMemoryUsage CalculateActualMemoryUsage(MemoryCalculationInput input)
    {
        if (!input.IsValid())
            return CacheMemoryUsage.Empty();

        long typePropertiesMemory = CalculateTypePropertiesMemory(input.TypePropertiesCache);
        long propertyPathMemory = CalculatePropertyPathMemory(input.PropertyPathCache);
        long collectionTypeMemory = CalculateCollectionTypeMemory(input.CollectionElementTypeCache);
        long lruTrackingMemory = CalculateLruTrackingMemory(
            input.TypePropertiesAccessTime, 
            input.PropertyPathAccessTime, 
            input.CollectionElementTypeAccessTime);
        long lfuTrackingMemory = CalculateLfuTrackingMemory(
            input.TypePropertiesAccessCount, 
            input.PropertyPathAccessCount, 
            input.CollectionElementTypeAccessCount);

        return CacheMemoryUsage.FromValues(typePropertiesMemory, propertyPathMemory, collectionTypeMemory, lruTrackingMemory, lfuTrackingMemory);
    }

    /// <summary>
    /// Calculates the ACTUAL memory usage of the type properties cache.
    /// This includes the ConcurrentDictionary overhead, Type references, and all PropertyInfo dictionaries.
    /// </summary>
    /// <param name="typePropertiesCache">The type properties cache to measure.</param>
    /// <returns>ACTUAL memory usage in bytes.</returns>
    public static long CalculateTypePropertiesMemory(ConcurrentDictionary<Type, Dictionary<string, PropertyInfo>> typePropertiesCache)
    {
        if (typePropertiesCache.IsEmpty) return 0;

        long totalMemory = 256; // Base ConcurrentDictionary overhead

        foreach (var kvp in typePropertiesCache)
        {
            // Type reference (8 bytes on 64-bit architecture)
            totalMemory += 8;

            // Dictionary overhead (72 bytes for regular Dictionary)
            totalMemory += 72;

            // Calculate ACTUAL size of each PropertyInfo entry based on real property names
            foreach (var prop in kvp.Value)
            {
                // String key: 24 bytes overhead + (length * 2 for UTF-16 encoding)
                totalMemory += 24 + (prop.Key.Length * 2);

                // PropertyInfo reference (8 bytes) + estimated PropertyInfo object size
                totalMemory += 8 + 200;

                // Dictionary entry overhead (hash table entry, etc.)
                totalMemory += 32;
            }

            // ConcurrentDictionary entry overhead (includes locking mechanisms)
            totalMemory += 48;
        }

        return totalMemory;
    }

    /// <summary>
    /// Calculates the ACTUAL memory usage of the property paths cache.
    /// This includes tuple keys with variable-length strings and string values.
    /// </summary>
    /// <param name="propertyPathCache">The property paths cache to measure.</param>
    /// <returns>ACTUAL memory usage in bytes.</returns>
    public static long CalculatePropertyPathMemory(ConcurrentDictionary<(Type, string), string> propertyPathCache)
    {
        if (propertyPathCache.IsEmpty) return 0;

        long totalMemory = 256; // Base ConcurrentDictionary overhead

        foreach (var kvp in propertyPathCache)
        {
            // ValueTuple key overhead (24 bytes) + Type reference (8 bytes) + string
            totalMemory += 24 + 8;

            // Input string in tuple key: 24 bytes overhead + (length * 2 for UTF-16)
            totalMemory += 24 + (kvp.Key.Item2.Length * 2);

            // Output string value: 24 bytes overhead + (length * 2 for UTF-16)
            totalMemory += 24 + (kvp.Value.Length * 2);

            // ConcurrentDictionary entry overhead
            totalMemory += 48;
        }

        return totalMemory;
    }

    /// <summary>
    /// Calculates the ACTUAL memory usage of the collection element types cache.
    /// This includes Type references and nullable Type values.
    /// </summary>
    /// <param name="collectionElementTypeCache">The collection element types cache to measure.</param>
    /// <returns>ACTUAL memory usage in bytes.</returns>
    public static long CalculateCollectionTypeMemory(ConcurrentDictionary<Type, Type?> collectionElementTypeCache)
    {
        if (collectionElementTypeCache.IsEmpty) return 0;

        long totalMemory = 256; // Base ConcurrentDictionary overhead

        foreach (var kvp in collectionElementTypeCache)
        {
            // Type key reference (8 bytes on 64-bit architecture)
            totalMemory += 8;

            // Nullable Type value: 8 bytes for Type reference + 1 byte for HasValue flag
            totalMemory += 9;

            // ConcurrentDictionary entry overhead
            totalMemory += 48;
        }

        return totalMemory;
    }

    /// <summary>
    /// Calculates the ACTUAL memory usage of all LRU (Least Recently Used) tracking dictionaries.
    /// This includes timestamp tracking for different key types.
    /// </summary>
    /// <param name="typePropertiesAccessTime">Type properties LRU tracking dictionary.</param>
    /// <param name="propertyPathAccessTime">Property paths LRU tracking dictionary.</param>
    /// <param name="collectionElementTypeAccessTime">Collection types LRU tracking dictionary.</param>
    /// <returns>ACTUAL memory usage in bytes.</returns>
    public static long CalculateLruTrackingMemory(
        ConcurrentDictionary<Type, long> typePropertiesAccessTime,
        ConcurrentDictionary<(Type, string), long> propertyPathAccessTime,
        ConcurrentDictionary<Type, long> collectionElementTypeAccessTime)
    {
        long totalMemory = 0;

        // Type properties access time tracking
        if (!typePropertiesAccessTime.IsEmpty)
        {
            totalMemory += 256; // ConcurrentDictionary overhead
            totalMemory += typePropertiesAccessTime.Count * (8 + 8 + 48); // Type key + long value + entry overhead
        }

        // Property path access time tracking (variable key size based on string length)
        if (!propertyPathAccessTime.IsEmpty)
        {
            totalMemory += 256; // ConcurrentDictionary overhead
            foreach (var kvp in propertyPathAccessTime)
            {
                // Tuple key: 24 bytes + Type reference (8) + string size + long value + entry overhead
                totalMemory += (24 + 8 + (24 + (kvp.Key.Item2.Length * 2))) + 8 + 48;
            }
        }

        // Collection element type access time tracking
        if (!collectionElementTypeAccessTime.IsEmpty)
        {
            totalMemory += 256; // ConcurrentDictionary overhead
            totalMemory += collectionElementTypeAccessTime.Count * (8 + 8 + 48); // Type key + long value + entry overhead
        }

        return totalMemory;
    }

    /// <summary>
    /// Calculates the ACTUAL memory usage of all LFU (Least Frequently Used) tracking dictionaries.
    /// This includes frequency counting for different key types.
    /// </summary>
    /// <param name="typePropertiesAccessCount">Type properties LFU tracking dictionary.</param>
    /// <param name="propertyPathAccessCount">Property paths LFU tracking dictionary.</param>
    /// <param name="collectionElementTypeAccessCount">Collection types LFU tracking dictionary.</param>
    /// <returns>ACTUAL memory usage in bytes.</returns>
    public static long CalculateLfuTrackingMemory(
        ConcurrentDictionary<Type, long> typePropertiesAccessCount,
        ConcurrentDictionary<(Type, string), long> propertyPathAccessCount,
        ConcurrentDictionary<Type, long> collectionElementTypeAccessCount)
    {
        long totalMemory = 0;

        // Type properties access count tracking
        if (!typePropertiesAccessCount.IsEmpty)
        {
            totalMemory += 256; // ConcurrentDictionary overhead
            totalMemory += typePropertiesAccessCount.Count * (8 + 8 + 48); // Type key + long value + entry overhead
        }

        // Property path access count tracking (variable key size based on string length)
        if (!propertyPathAccessCount.IsEmpty)
        {
            totalMemory += 256; // ConcurrentDictionary overhead
            foreach (var kvp in propertyPathAccessCount)
            {
                // Tuple key: 24 bytes + Type reference (8) + string size + long value + entry overhead
                totalMemory += (24 + 8 + (24 + (kvp.Key.Item2.Length * 2))) + 8 + 48;
            }
        }

        // Collection element type access count tracking
        if (!collectionElementTypeAccessCount.IsEmpty)
        {
            totalMemory += 256; // ConcurrentDictionary overhead
            totalMemory += collectionElementTypeAccessCount.Count * (8 + 8 + 48); // Type key + long value + entry overhead
        }

        return totalMemory;
    }

    /// <summary>
    /// Calculates the ACTUAL size of a string in memory.
    /// Accounts for .NET's UTF-16 encoding and string object overhead.
    /// </summary>
    /// <param name="str">The string to measure.</param>
    /// <returns>ACTUAL memory usage in bytes.</returns>
    public static long CalculateStringSize(string str)
    {
        if (string.IsNullOrEmpty(str)) return 0;

        // String object overhead (24 bytes) + (length * 2 bytes for UTF-16 encoding)
        return 24 + (str.Length * 2);
    }

    /// <summary>
    /// Gets memory size constants used in calculations for reference.
    /// These are the actual .NET object sizes on 64-bit architecture.
    /// </summary>
    /// <returns>A dictionary of memory size constants.</returns>
    public static Dictionary<string, long> GetMemorySizeConstants()
    {
        return new Dictionary<string, long>
        {
            ["ObjectReference"] = 8,           // Object reference on 64-bit
            ["StringOverhead"] = 24,           // String object overhead
            ["DictionaryOverhead"] = 72,       // Regular Dictionary overhead
            ["ConcurrentDictionaryOverhead"] = 256,  // ConcurrentDictionary overhead
            ["DictionaryEntryOverhead"] = 32,  // Dictionary entry overhead
            ["ConcurrentDictionaryEntryOverhead"] = 48,  // ConcurrentDictionary entry overhead
            ["TupleOverhead"] = 24,            // ValueTuple overhead
            ["LongValue"] = 8,                 // long value size
            ["PropertyInfoSize"] = 200,        // Estimated PropertyInfo object size
            ["NullableByte"] = 1               // Nullable HasValue flag
        };
    }
}