#region

using System.Data;

#endregion

namespace pengdows.crud.wrappers;

/// <summary>
/// Represents a data reader that tracks its owning connection for proper disposal.
/// </summary>
public interface ITrackedReader : IDataReader, IAsyncDisposable
{
    /// <summary>
    /// Advances the reader to the next record asynchronously.
    /// </summary>
    /// <returns>True if another record is available.</returns>
    Task<bool> ReadAsync();
}
