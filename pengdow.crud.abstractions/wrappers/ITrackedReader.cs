#region

using System.Data;

#endregion

namespace pengdow.crud.wrappers;

public interface ITrackedReader : IDataReader, IAsyncDisposable
{
    Task<bool> ReadAsync();
}