// =============================================================================
// FILE: ProviderPropertySetter.cs
// PURPOSE: Sets a provider-specific property (NpgsqlDbType, OracleDbType, DataTypeName,
//          OracleCommand.InitialLONGFetchSize) that the library can't reference at compile time.
//
// AI SUMMARY:
// - Set(target, property, value): assigns the property, with value parsed to its type (an enum
//   member by name, ignoring case; a string; an int). No-op when the property is missing,
//   read-only, or the value doesn't parse.
// - Runs per parameter (or per command), so the setter is compiled once per (target type,
//   property, value) and cached: no lookup, parse, boxing or PropertyInfo.SetValue per call.
// =============================================================================

using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;

namespace pengdows.crud.@internal;

internal static class ProviderPropertySetter
{
    // Cache key includes whether an absent member is allowed, so Set and TrySet never share a cached answer.
    private static readonly ConcurrentDictionary<(Type Target, string Property, string Value, bool Optional), Action<object>?> Setters = new();

    // Applies a provider setting. Throws when the setting cannot be applied: the member is missing, read-only, of an
    // unsupported type, or the text is not a valid value. Skipping any of those would leave the provider object with a
    // different type or value than the caller asked for, and the database would then act on that silently.
    public static void Set(object target, string property, string value)
    {
        var setter = Setters.GetOrAdd((target.GetType(), property, value, false),
            static key => Build(key.Target, key.Property, key.Value, optional: false));
        setter!(target);
    }

    // For a setting that only some provider types have. An absent member is the normal case for a generic parameter
    // (fakeDb, or a provider that lacks the property), so it returns false and changes nothing. Every other failure
    // still throws, so a member that exists but cannot take the value is never skipped.
    public static bool TrySet(object target, string property, string value)
    {
        var setter = Setters.GetOrAdd((target.GetType(), property, value, true),
            static key => Build(key.Target, key.Property, key.Value, optional: true));
        if (setter == null)
        {
            return false;
        }

        setter(target);
        return true;
    }

    private static Action<object>? Build(Type targetType, string propertyName, string text, bool optional)
    {
        var property = targetType.GetProperty(propertyName);
        if (property == null)
        {
            if (optional)
            {
                return null;
            }

            throw new InvalidOperationException(
                $"Provider type {targetType.FullName} has no property '{propertyName}'; the setting cannot be applied.");
        }

        if (!property.CanWrite)
        {
            throw new InvalidOperationException(
                $"Provider property {targetType.FullName}.{propertyName} is read-only; the setting cannot be applied.");
        }

        var valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        object? value;
        if (valueType.IsEnum)
        {
            if (!Enum.TryParse(valueType, text, true, out value))
            {
                throw new InvalidOperationException(
                    $"'{text}' is not a value of {valueType.FullName}; {targetType.FullName}.{propertyName} cannot be set.");
            }
        }
        else if (valueType == typeof(string))
        {
            value = text;
        }
        else if (valueType == typeof(int) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            value = number;
        }
        else
        {
            throw new InvalidOperationException(
                $"Provider property {targetType.FullName}.{propertyName} has type {valueType.FullName}, which the setter does not support.");
        }

        var target = Expression.Parameter(typeof(object), "target");
        var assign = Expression.Assign(
            Expression.Property(Expression.Convert(target, targetType), property),
            Expression.Constant(value, property.PropertyType));
        return Expression.Lambda<Action<object>>(assign, target).Compile();
    }
}
