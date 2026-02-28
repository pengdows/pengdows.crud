namespace pengdows.crud;

/// <summary>
/// Provides access to mapping metadata for entity types.
/// </summary>
public interface ITypeMapRegistry
{
    /// <summary>
    /// Retrieves table information for the specified entity type.
    /// </summary>
    /// <typeparam name="T">Entity type to inspect.</typeparam>
    /// <returns>Table metadata for the entity.</returns>
    ITableInfo GetTableInfo<T>();
}