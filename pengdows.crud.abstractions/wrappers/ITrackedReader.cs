#region

using System.Data;

#endregion

namespace pengdows.crud.wrappers;

public interface ITrackedReader : IDataReader, IAsyncDisposable
{
    Task<bool> ReadAsync();
}