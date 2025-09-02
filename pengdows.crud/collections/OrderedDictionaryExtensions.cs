namespace pengdows.crud.collections;

using System;
using System.Collections.Generic;
using System.Data.Common;
using Microsoft.Extensions.Logging;

// Extension methods for common DB parameter scenarios
public static class OrderedDictionaryExtensions
{
    // Optional logger for visibility into property access failures
    // Defaults to no-op; set via OrderedDictionaryExtensions.Logger if desired.
    private static Microsoft.Extensions.Logging.ILogger _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    public static Microsoft.Extensions.Logging.ILogger Logger
    {
        get => _logger;
        set => _logger = value ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }
    /// <summary>
    /// Creates a DB parameter dictionary from an object's properties
    /// </summary>
    public static OrderedDictionary<string, object?> FromObject<T>(T obj) where T : notnull
    {
        var dict = new OrderedDictionary<string, object?>();
        var props = typeof(T).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

        foreach (var prop in props)
        {
            if (!prop.CanRead)
            {
                continue;
            }

            if (prop.GetIndexParameters().Length != 0)
            {
                continue; // Skip indexers
            }

            object? value;
            try
            {
                value = prop.GetValue(obj);
            }
            catch (Exception ex)
            {
                // Log and skip properties that throw on access to avoid breaking caller scenarios
                _logger.LogWarning(ex, "Failed to read property {Property} on {Type}", prop.Name, typeof(T));
                continue;
            }

            dict[prop.Name] = value;
        }

        return dict;
    }

    /// <summary>
    /// Adds a parameter with automatic null handling for object values
    /// </summary>
    public static void AddParameter(this OrderedDictionary<string, object?> dict,
        string key, object? value)
    {
        dict[key] = value ?? DBNull.Value;
    }

    /// <summary>
    /// Tries to add a parameter, returning false if key already exists
    /// </summary>
    public static bool TryAddParameter(this OrderedDictionary<string, object?> dict,
        string key, object? value)
        => dict.TryAdd(key, value ?? DBNull.Value);

    /// <summary>
    /// Removes and returns the parameter value
    /// </summary>
    public static bool RemoveParameter(this OrderedDictionary<string, object?> dict,
        string key, out object? value)
    {
        return dict.Remove(key, out value);
    }

    /// <summary>
    /// Adds a DbParameter directly to the dictionary using its ParameterName as key.
    /// Note: Parameter name prefixes (@, :, ?) are automatically trimmed for clean key storage.
    /// </summary>
    public static void AddDbParameter(this OrderedDictionary<string, DbParameter> dict,
        DbParameter parameter)
    {
        if (parameter == null)
        {
            return;
        }

        var key = string.IsNullOrEmpty(parameter.ParameterName)
            ? throw new ArgumentException("Parameter must have a name")
            : parameter.ParameterName.TrimStart('@', ':', '?'); // Remove database-specific prefixes

        dict[key] = parameter;
    }

    /// <summary>
    /// Gets all parameters in insertion order for adding to a DbCommand
    /// </summary>
    public static IEnumerable<DbParameter> GetParametersInOrder(this OrderedDictionary<string, DbParameter> dict)
    {
        foreach (var kvp in dict)
        {
            yield return kvp.Value;
        }
    }
}
