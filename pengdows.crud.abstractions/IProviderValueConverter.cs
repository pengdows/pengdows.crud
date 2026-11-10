using pengdows.crud.enums;

namespace pengdows.crud;

/// <summary>
/// The escape hatch for an application type the library has no mapping for. Register one per type with
/// <c>ProviderValueConverters.Register</c>; the library then writes and reads that type through it, exactly as it does
/// its own types. Use it only for a type the library does not already map: the library owns every type it can.
/// </summary>
/// <typeparam name="T">The application type the converter handles.</typeparam>
public interface IProviderValueConverter<T>
{
    /// <summary>Converts a value to the value the database parameter takes. A null is never passed (it stays null).</summary>
    /// <param name="value">The application value (not null).</param>
    /// <param name="provider">The database the value is written to.</param>
    object? ToProvider(T value, SupportedDatabase provider);

    /// <summary>Converts the value a database returns back to the application type. A null is never passed.</summary>
    /// <param name="value">The value the database returned (not null or DBNull).</param>
    /// <param name="provider">The database the value was read from.</param>
    T FromProvider(object value, SupportedDatabase provider);
}
