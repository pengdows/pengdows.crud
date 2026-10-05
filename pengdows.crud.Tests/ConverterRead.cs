using System;
using pengdows.crud.types;
using pengdows.crud.types.coercion;
using pengdows.crud.types.converters;

namespace pengdows.crud.Tests;

/// <summary>
/// DRY-010 stage 3: a type with an AdvancedTypeRegistry converter has no coercion; the converter alone
/// reads it. The coercion tests that covered those types run against the converter through this
/// adapter, with the TryRead shape the coercions had, so every input they accepted is still checked.
/// </summary>
internal sealed class ConverterRead<T>
{
    private readonly IAdvancedTypeConverter _converter =
        AdvancedTypeRegistry.Shared.GetConverter(typeof(T)) ??
        throw new InvalidOperationException($"No converter for {typeof(T).Name}.");

    public Type TargetType => typeof(T);

    public bool TryRead(in DbValue src, out T value)
    {
        if (src.IsNull)
        {
            value = default!;
            return false;
        }

        try
        {
            if (_converter.FromProviderValue(src.RawValue!, AdvancedCoercions.AnyDatabase) is T read)
            {
                value = read;
                return true;
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or ArgumentException or OverflowException)
        {
        }

        value = default!;
        return false;
    }

    public bool TryRead(in DbValue src, Type targetType, out object? value)
    {
        if ((targetType == typeof(T) || Nullable.GetUnderlyingType(targetType) == typeof(T)) && TryRead(src, out var typed))
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }
}
