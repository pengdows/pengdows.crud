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
    private static readonly ConcurrentDictionary<(Type Target, string Property, string Value), Action<object>?> Setters = new();

    public static void Set(object target, string property, string value)
    {
        var setter = Setters.GetOrAdd((target.GetType(), property, value),
            static key => Build(key.Target, key.Property, key.Value));
        setter?.Invoke(target);
    }

    private static Action<object>? Build(Type targetType, string propertyName, string text)
    {
        var property = targetType.GetProperty(propertyName);
        if (property == null || !property.CanWrite)
        {
            return null;
        }

        var valueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        object? value;
        if (valueType.IsEnum)
        {
            if (!Enum.TryParse(valueType, text, true, out value))
            {
                return null;
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
            return null;
        }

        var target = Expression.Parameter(typeof(object), "target");
        var assign = Expression.Assign(
            Expression.Property(Expression.Convert(target, targetType), property),
            Expression.Constant(value, property.PropertyType));
        return Expression.Lambda<Action<object>>(assign, target).Compile();
    }
}
