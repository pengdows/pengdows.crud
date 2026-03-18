using System.Reflection;
using pengdows.crud.wrappers;

namespace pengdows.crud;

/// <summary>
/// Low-level mapping and metadata infrastructure for table gateways.
/// Internal framework use only — cast to this interface when framework code
/// needs direct reader mapping or setter compilation outside the gateway's
/// standard load methods.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
internal interface ITableGatewayInfrastructure<out TEntity>
{
    /// <summary>
    /// Materializes a <typeparamref name="TEntity"/> from the current row of a data reader.
    /// </summary>
    /// <param name="reader">The tracked reader.</param>
    /// <returns>A mapped entity instance.</returns>
    TEntity MapReaderToObject(ITrackedReader reader);

    /// <summary>
    /// Returns a compiled setter delegate for the specified property.
    /// </summary>
    /// <param name="prop">The property to create a setter for.</param>
    /// <returns>A thread-safe setter delegate.</returns>
    Action<object, object?> GetOrCreateSetter(PropertyInfo prop);
}
