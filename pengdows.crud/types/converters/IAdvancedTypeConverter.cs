using System;
using pengdows.crud.enums;

namespace pengdows.crud.types.converters;

public interface IAdvancedTypeConverter
{
    Type TargetType { get; }
    object? ToProviderValue(object value, SupportedDatabase provider);
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
