#region

using System.Data;
using System.Data.Common;
using System.Text;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;

#endregion

namespace pengdows.crud;

/// <summary>
/// Represents a composable, parameterized SQL container that supports dynamic query building,
/// safe parameter binding, and execution in the context of a tracked database connection.
/// </summary>
public interface ISqlContainer : ISafeAsyncDisposableBase
{
    StringBuilder Query { get; }
    int ParameterCount { get; }
    void AddParameter(DbParameter parameter);
    DbParameter AddParameterWithValue<T>(DbType type, T value);
    DbParameter AddParameterWithValue<T>(string? name, DbType type, T value);
    Task<int> ExecuteNonQueryAsync(CommandType commandType = CommandType.Text);
    Task<T?> ExecuteScalarAsync<T>(CommandType commandType = CommandType.Text);
    Task<ITrackedReader> ExecuteReaderAsync(CommandType commandType = CommandType.Text);
    void AddParameters(IEnumerable<DbParameter> list);
    DbCommand CreateCommand(ITrackedConnection conn);
    void Clear();
    string WrapForStoredProc(ExecutionType executionType, bool includeParameters = true);
    string WrapObjectName(string objectName);
    string MakeParameterName(DbParameter parameter);
}