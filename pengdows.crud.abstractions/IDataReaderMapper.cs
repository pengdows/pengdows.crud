#region

using System.Data;

#endregion

namespace pengdows.crud;

/// <summary>
/// Maps data reader rows to objects using optional mapping options.
/// </summary>
public interface IDataReaderMapper
{
    /// <summary>
    /// Hydrates a list of objects from an <see cref="IDataReader"/> using default options.
    /// </summary>
    /// <typeparam name="T">Type of object to hydrate.</typeparam>
    /// <param name="reader">Data reader containing the rows to map.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of hydrated objects.</returns>
    Task<List<T>> LoadObjectsFromDataReaderAsync<T>(IDataReader reader, CancellationToken cancellationToken = default)
        where T : class, new();

    /// <summary>
    /// Hydrates a list of objects from an <see cref="IDataReader"/> using the provided <see cref="MapperOptions"/>.
    /// </summary>
    /// <typeparam name="T">Type of object to hydrate.</typeparam>
    /// <param name="reader">Data reader containing the rows to map.</param>
    /// <param name="options">Mapping options controlling hydration behavior.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of hydrated objects.</returns>
    Task<List<T>> LoadAsync<T>(IDataReader reader, MapperOptions options, CancellationToken cancellationToken = default)
        where T : class, new();

    /// <summary>
    /// Streams objects from an <see cref="IDataReader"/> using the provided <see cref="MapperOptions"/>.
    /// </summary>
    /// <typeparam name="T">Type of object to hydrate.</typeparam>
    /// <param name="reader">Data reader containing the rows to map.</param>
    /// <param name="options">Mapping options controlling hydration behavior.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async sequence of hydrated objects.</returns>
    IAsyncEnumerable<T> StreamAsync<T>(IDataReader reader, MapperOptions options, CancellationToken cancellationToken = default)
        where T : class, new();
}

