using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.threading;
using pengdows.crud.wrappers;

namespace pengdows.crud.Tests.Mocks;

public sealed class SpyDatabaseContext : IDatabaseContext, IContextIdentity
{
    private readonly IDatabaseContext _inner;

    public SpyDatabaseContext(IDatabaseContext inner)
    {
        _inner = inner;
    }

    public int CreateDbParameterCalls { get; private set; }

    public DbMode ConnectionMode => _inner.ConnectionMode;

    public ITypeMapRegistry TypeMapRegistry => _inner.TypeMapRegistry;

    public IDataSourceInformation DataSourceInfo => _inner.DataSourceInfo;

    public string SessionSettingsPreamble => _inner.SessionSettingsPreamble;

    public ProcWrappingStyle ProcWrappingStyle
    {
        get => _inner.ProcWrappingStyle;
        set => _inner.ProcWrappingStyle = value;
    }

    public int MaxParameterLimit => _inner.MaxParameterLimit;

    public long NumberOfOpenConnections => _inner.NumberOfOpenConnections;

    public string QuotePrefix => _inner.QuotePrefix;

    public string QuoteSuffix => _inner.QuoteSuffix;

    public string CompositeIdentifierSeparator => _inner.CompositeIdentifierSeparator;

    public string WrapObjectName(string name) => _inner.WrapObjectName(name);

    public string MakeParameterName(DbParameter dbParameter) => _inner.MakeParameterName(dbParameter);

    public string MakeParameterName(string parameterName) => _inner.MakeParameterName(parameterName);

    public SupportedDatabase Product => _inner.Product;

    public long MaxNumberOfConnections => _inner.MaxNumberOfConnections;

    public bool IsReadOnlyConnection => _inner.IsReadOnlyConnection;

    public bool RCSIEnabled => _inner.RCSIEnabled;

    public bool IsDisposed => _inner.IsDisposed;

    public Guid RootId => ((IContextIdentity)_inner).RootId;

    public ILockerAsync GetLock() => _inner.GetLock();

    public ISqlContainer CreateSqlContainer(string? query = null) => _inner.CreateSqlContainer(query);

    public DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
    {
        CreateDbParameterCalls++;
        return _inner.CreateDbParameter(name, type, value);
    }

    public DbParameter CreateDbParameter<T>(DbType type, T value)
    {
        CreateDbParameterCalls++;
        return _inner.CreateDbParameter(type, value);
    }

    public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false)
        => _inner.GetConnection(executionType, isShared);

    public ITransactionContext BeginTransaction(IsolationLevel? isolationLevel = null, ExecutionType executionType = ExecutionType.Write)
        => _inner.BeginTransaction(isolationLevel, executionType);

    public ITransactionContext BeginTransaction(IsolationProfile isolationProfile, ExecutionType executionType = ExecutionType.Write)
        => _inner.BeginTransaction(isolationProfile, executionType);

    public string GenerateRandomName(int length = 5, int parameterNameMaxLength = 30)
        => _inner.GenerateRandomName(length, parameterNameMaxLength);

    public void AssertIsWriteConnection() => _inner.AssertIsWriteConnection();

    public void AssertIsReadConnection() => _inner.AssertIsReadConnection();

    public void CloseAndDisposeConnection(ITrackedConnection? conn) => _inner.CloseAndDisposeConnection(conn);

    public ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? conn) => _inner.CloseAndDisposeConnectionAsync(conn);

    public void Dispose() => _inner.Dispose();

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
