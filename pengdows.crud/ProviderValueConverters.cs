using System;
using System.Collections.Generic;
using pengdows.crud.enums;
using pengdows.crud.types;
using pengdows.crud.types.converters;

namespace pengdows.crud;

/// <summary>
/// Registers the application's converter for a type the library does not map (the escape hatch). Registration is
/// process-wide and happens once per type: a second registration for the same type is refused rather than silently
/// replacing the first, because the converter is used by every context in the process.
/// </summary>
public static class ProviderValueConverters
{
    private static readonly HashSet<Type> s_registered = new();
    private static readonly object s_gate = new();

    /// <summary>Registers the converter for <typeparamref name="T"/>.</summary>
    /// <exception cref="InvalidOperationException">A converter for <typeparamref name="T"/> is already registered.</exception>
    public static void Register<T>(IProviderValueConverter<T> converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        lock (s_gate)
        {
            if (!s_registered.Add(typeof(T)))
            {
                throw new InvalidOperationException($"A converter for {typeof(T).FullName} is already registered.");
            }

            AdvancedTypeRegistry.Shared.RegisterConverter(new ApplicationConverter<T>(converter));
        }
    }

    /// <summary>True when the application registered a converter for <paramref name="type"/>.</summary>
    internal static bool IsRegistered(Type type)
    {
        lock (s_gate)
        {
            return s_registered.Contains(type);
        }
    }

    private sealed class ApplicationConverter<T> : AdvancedTypeConverter<T>
    {
        private readonly IProviderValueConverter<T> _converter;

        public ApplicationConverter(IProviderValueConverter<T> converter) => _converter = converter;

        protected override object? ConvertToProvider(T value, SupportedDatabase provider) =>
            _converter.ToProvider(value, provider);

        public override bool TryConvertFromProvider(object value, SupportedDatabase provider, out T result)
        {
            result = _converter.FromProvider(value, provider);
            return true;
        }
    }
}
