using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Optimization.Cache.Config;
using DynamicWhere.ex.Optimization.Cache.Input;
using System.Reflection;

namespace DynamicWhere.ex.Optimization.Cache.Source;

/// <summary>
/// Thread-safe cache for reflection-based operations to improve performance for frequently used types.
/// This class focuses exclusively on reflection operations and uses direct calls to specialized classes
/// for cache management, eviction, and access tracking.
/// </summary>
internal static class CacheReflection
{
    #region Configuration Management

    /// <summary>
    /// Current cache configuration options. Can be modified at runtime.
    /// </summary>
    private static CacheOptions _options = new();

    /// <summary>
    /// Lock object to ensure thread-safe configuration updates.
    /// </summary>
    private static readonly object _configLock = new();

    /// <summary>
    /// Configures the reflection cache with custom options.
    /// </summary>
    /// <param name="options">The cache configuration options.</param>
    /// <remarks>
    /// This method is thread-safe and can be called at any time to update cache behavior.
    /// Existing cache entries are not affected, but new operations will use the updated configuration.
    /// </remarks>
    public static void Configure(CacheOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        options.Validate();

        lock (_configLock)
        {
            _options = options.Clone();
        }
    }

    /// <summary>
    /// Gets the current cache configuration.
    /// </summary>
    /// <returns>A copy of the current cache configuration options.</returns>
    public static CacheOptions GetCacheConfigOptions()
    {
        lock (_configLock)
        {
            return _options.Clone();
        }
    }

    #endregion Configuration Management

    #region Type Properties Reflection

    /// <summary>
    /// Gets all properties for a type with case-insensitive lookup.
    /// Results are cached for performance.
    /// </summary>
    /// <param name="type">The type to get properties for.</param>
    /// <returns>A dictionary mapping property names (case-insensitive) to PropertyInfo objects.</returns>
    public static Dictionary<string, PropertyInfo> GetTypeProperties(Type type)
    {
        var config = GetCacheConfigOptions();

        // Update access tracking based on eviction strategy
        var accessTrackingInput = AccessTrackingInput<Type>.Create(type, config,
            CacheDatabase.TypePropertiesAccessTime,
            CacheDatabase.TypePropertiesAccessCount);
        CacheDatabase.UpdateAccessTracking(accessTrackingInput);

        return CacheDatabase.GetOrAddTypeProperties(type, t =>
        {
            // Check if eviction is needed and perform it if necessary
            CacheEviction.EvictTypePropertiesEntries(config);

            // Perform reflection to get all public instance properties
            var properties = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var propertyDict = new Dictionary<string, PropertyInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var prop in properties)
            {
                propertyDict[prop.Name] = prop;
            }

            return propertyDict;
        });
    }

    /// <summary>
    /// Finds a property by name (case-insensitive) for the specified type.
    /// </summary>
    /// <param name="type">The type to search in.</param>
    /// <param name="propertyName">The property name to find.</param>
    /// <returns>The PropertyInfo if found, null otherwise.</returns>
    public static PropertyInfo? FindProperty(Type type, string propertyName)
    {
        var properties = GetTypeProperties(type);
        properties.TryGetValue(propertyName, out var propertyInfo);
        return propertyInfo;
    }

    #endregion Type Properties Reflection

    #region Collection Type Analysis

    /// <summary>
    /// Checks if a type is a collection type that supports enumeration.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is a collection, false otherwise.</returns>
    public static bool IsCollectionType(Type type)
    {
        return GetCollectionElementType(type) != null;
    }

    /// <summary>
    /// Gets the element type for collection types, with caching.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>The element type if it's a collection, null otherwise.</returns>
    public static Type? GetCollectionElementType(Type type)
    {
        var config = GetCacheConfigOptions();

        // Update access tracking based on eviction strategy
        var accessTrackingInput = AccessTrackingInput<Type>.Create(type, config,
            CacheDatabase.CollectionElementTypeAccessTime,
            CacheDatabase.CollectionElementTypeAccessCount);
        CacheDatabase.UpdateAccessTracking(accessTrackingInput);

        return CacheDatabase.GetOrAddCollectionElementType(type, t =>
        {
            // Check if eviction is needed and perform it if necessary
            CacheEviction.EvictCollectionElementTypeEntries(config);

            // Analyze the type to determine if it's a collection and get its element type
            return AnalyzeCollectionElementType(t);
        });
    }

    /// <summary>
    /// Analyzes a type to determine its collection element type using reflection.
    /// </summary>
    /// <param name="type">The type to analyze.</param>
    /// <returns>The element type if it's a collection, null otherwise.</returns>
    private static Type? AnalyzeCollectionElementType(Type type)
    {
        // Handle arrays
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        // Handle generic collections
        if (type.IsGenericType)
        {
            var genericTypeDefinition = type.GetGenericTypeDefinition();
            if (genericTypeDefinition == typeof(List<>) ||
                genericTypeDefinition == typeof(ICollection<>) ||
                genericTypeDefinition == typeof(IEnumerable<>) ||
                genericTypeDefinition == typeof(IList<>) ||
                genericTypeDefinition == typeof(HashSet<>) ||
                genericTypeDefinition == typeof(ISet<>))
            {
                return type.GetGenericArguments()[0];
            }
        }

        return null;
    }

    #endregion Collection Type Analysis

    #region Property Path Validation

    /// <summary>
    /// Validates and normalizes a property path for the specified type, with caching.
    /// </summary>
    /// <param name="rootType">The root type to validate against.</param>
    /// <param name="propertyPath">The property path to validate.</param>
    /// <returns>The validated and normalized property path.</returns>
    /// <exception cref="LogicException">Thrown when the path is invalid.</exception>
    public static string ValidatePropertyPath(Type rootType, string propertyPath)
    {
        var cacheKey = (rootType, propertyPath);
        var config = GetCacheConfigOptions();

        // Update access tracking based on eviction strategy
        var accessTrackingInput = AccessTrackingInput<(Type, string)>.Create(cacheKey, config,
            CacheDatabase.PropertyPathAccessTime,
            CacheDatabase.PropertyPathAccessCount);
        CacheDatabase.UpdateAccessTracking(accessTrackingInput);

        return CacheDatabase.GetOrAddPropertyPath(cacheKey, key =>
        {
            // Check if eviction is needed and perform it if necessary
            CacheEviction.EvictPropertyPathEntries(config);

            // Perform the actual property path validation
            return ValidatePropertyPathInternal(key.Item1, key.Item2);
        });
    }

    /// <summary>
    /// Internal method that performs the actual property path validation using reflection.
    /// </summary>
    /// <param name="rootType">The root type to validate against.</param>
    /// <param name="propertyPath">The property path to validate.</param>
    /// <returns>The validated and normalized property path.</returns>
    /// <exception cref="LogicException">Thrown when the path is invalid.</exception>
    private static string ValidatePropertyPathInternal(Type rootType, string propertyPath)
    {
        // Split the property name by dots if it contains any, handling leading/trailing whitespaces and empty entries.
        List<string> names = propertyPath.Contains('.')
            ? propertyPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : new List<string> { propertyPath.Trim() };

        // Ensure that at least one valid property name is provided.
        if (names.Count == 0)
        {
            throw new LogicException(ErrorCode.InvalidField);
        }

        Type type = rootType;
        List<string> validatedNames = new();

        // Iterate through nested property names and validate each level using reflection.
        foreach (string propertyName in names)
        {
            // Use cached property lookup with reflection
            PropertyInfo? prop = FindProperty(type, propertyName) 
                ?? throw new LogicException(ErrorCode.InvalidField);
            
            validatedNames.Add(prop.Name);

            // Update the current type to the property's type, considering generic collection types.
            type = prop.PropertyType;

            // Check if it's a collection and get the element type using cached collection analysis
            var elementType = GetCollectionElementType(type);
            if (elementType != null)
            {
                type = elementType;
            }
        }

        // Return the validated property name as a single string.
        return string.Join('.', validatedNames);
    }

    #endregion Property Path Validation

    #region Field Type Analysis

    /// <summary>
    /// Gets the type of a field given a property path using cached reflection.
    /// </summary>
    /// <param name="rootType">The root type.</param>
    /// <param name="propertyPath">The property path.</param>
    /// <returns>The type of the field.</returns>
    /// <exception cref="LogicException">Thrown when the property path is invalid.</exception>
    public static Type GetFieldType(Type rootType, string propertyPath)
    {
        var cacheKey = (rootType, propertyPath);
        var config = GetCacheConfigOptions();

        // Update access tracking based on eviction strategy
        var accessTrackingInput = AccessTrackingInput<(Type, string)>.Create(cacheKey, config,
            CacheDatabase.PropertyPathAccessTime,
            CacheDatabase.PropertyPathAccessCount);
        CacheDatabase.UpdateAccessTracking(accessTrackingInput);

        // Reuse property path cache for field type resolution since the path is already validated
        // We just need to navigate to the final type
        return GetFieldTypeInternal(rootType, propertyPath);
    }

    /// <summary>
    /// Internal method that gets the field type for a property path using cached reflection.
    /// </summary>
    /// <param name="rootType">The root type.</param>
    /// <param name="propertyPath">The property path.</param>
    /// <returns>The type of the field.</returns>
    private static Type GetFieldTypeInternal(Type rootType, string propertyPath)
    {
        // Split the property path by dots to handle nested properties
        List<string> names = propertyPath.Contains('.')
            ? propertyPath.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : new List<string> { propertyPath.Trim() };

        Type type = rootType;

        // Navigate through nested properties
        foreach (string propertyName in names)
        {
            var prop = FindProperty(type, propertyName) ??
                throw new LogicException(ErrorCode.InvalidField);

            type = prop.PropertyType;

            // Check if it's a collection and get the element type
            var elementType = GetCollectionElementType(type);
            if (elementType != null)
            {
                type = elementType;
            }
        }

        return type;
    }

    /// <summary>
    /// Checks if a type is a simple type (primitive, string, DateTime, DateOnly, TimeOnly, Guid, decimal, TimeSpan, DateTimeOffset, or enum).
    /// Simple types are types that can be directly aggregated.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is a simple type, false otherwise.</returns>
    public static bool IsSimpleType(Type type)
    {
        // Unwrap nullable types to get the underlying type
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        // Check if it's a primitive type (int, bool, double, etc.)
        if (underlyingType.IsPrimitive)
            return true;

        // Check for common simple types
        if (underlyingType == typeof(string) ||
            underlyingType == typeof(decimal) ||
            underlyingType == typeof(DateTime) ||
            underlyingType == typeof(DateOnly) ||
            underlyingType == typeof(TimeOnly) ||
            underlyingType == typeof(DateTimeOffset) ||
            underlyingType == typeof(TimeSpan) ||
            underlyingType == typeof(Guid))
            return true;

        // Check if it's an enum
        if (underlyingType.IsEnum)
            return true;

        return false;
    }

    /// <summary>
    /// Checks if a type is a numeric type.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if the type is numeric, false otherwise.</returns>
    public static bool IsNumericType(Type type)
    {
        // Unwrap nullable types to get the underlying type
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        return underlyingType == typeof(byte) ||
               underlyingType == typeof(sbyte) ||
               underlyingType == typeof(short) ||
               underlyingType == typeof(ushort) ||
               underlyingType == typeof(int) ||
               underlyingType == typeof(uint) ||
               underlyingType == typeof(long) ||
               underlyingType == typeof(ulong) ||
               underlyingType == typeof(float) ||
               underlyingType == typeof(double) ||
               underlyingType == typeof(decimal);
    }

    #endregion Field Type Analysis
}