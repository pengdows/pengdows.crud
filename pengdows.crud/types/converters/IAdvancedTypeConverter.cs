// =============================================================================
// FILE: IAdvancedTypeConverter.cs
// PURPOSE: Interface and base class for advanced type converters.
//
// AI SUMMARY:
// - Defines contract for converting between .NET types and provider-specific values.
// - IAdvancedTypeConverter: Non-generic interface with ToProviderValue/FromProviderValue.
// - AdvancedTypeConverter<T>: Generic abstract base class reducing boilerplate.
// - ConvertToProvider(): Override in subclass for custom write conversion.
// - TryConvertFromProvider(): Override in subclass for custom read conversion.
// - Used by AdvancedTypeRegistry to configure DbParameters.
// - Implementations: GeometryConverter, GeographyConverter, InetConverter, etc.
// =============================================================================

using pengdows.crud.enums;

namespace pengdows.crud.types.converters;

public interface IAdvancedTypeConverter
{
    /// <summary>
    /// .NET type handled by this converter.
    /// </summary>
    Type TargetType { get; }

    /// <summary>
    /// Converts a .NET value into a provider-specific representation.
    /// </summary>
    /// <param name="value">The .NET value to convert.</param>
    /// <param name="provider">Target database provider.</param>
    /// <returns>Value suitable for DbParameter assignment.</returns>
    object? ToProviderValue(object value, SupportedDatabase provider);

    /// <summary>
    /// Converts a provider-specific value back into a .NET representation.
    /// </summary>
    /// <param name="value">Value from the provider.</param>
    /// <param name="provider">Source database provider.</param>
    /// <returns>Converted .NET value or null.</returns>
    object? FromProviderValue(object value, SupportedDatabase provider);
}

public abstract class AdvancedTypeConverter<T> : IAdvancedTypeConverter
{
    public Type TargetType => typeof(T);

    public virtual object? ToProviderValue(object value, SupportedDatabase provider)
    {
        if (value is null)
        {
            return null;
        }

        if (value is not T typed)
        {
            throw new ArgumentException(
                $"Value must be of type {typeof(T).FullName} but was {value.GetType().FullName}.",
                nameof(value));
        }

        return ConvertToProvider(typed, provider);
    }

    public virtual object? FromProviderValue(object value, SupportedDatabase provider)
    {
        if (value is null || value is DBNull)
        {
            return null;
        }

        return TryConvertFromProvider(value, provider, out var result) ? result : null;
    }

    protected virtual object? ConvertToProvider(T value, SupportedDatabase provider)
    {
        return value;
    }

    public virtual bool TryConvertFromProvider(object value, SupportedDatabase provider, out T result)
    {
        if (value is T typed)
        {
            result = typed;
            return true;
        }

        result = default!;
        return false;
    }
}
