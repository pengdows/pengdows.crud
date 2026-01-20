using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.connection;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.strategies.connection;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class TransactionContextAdditionalBranchTests
{
    [Fact]
    public void Create_ReadOnlyContextWithWriteExecutionType_Throws()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            ReadWriteMode = ReadWriteMode.ReadOnly,
            DbMode = DbMode.SingleConnection
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite), NullLoggerFactory.Instance);

        Assert.Throws<NotSupportedException>(() =>
            TransactionContext.Create(ctx, IsolationLevel.ReadCommitted, ExecutionType.Write, false));
    }

    [Fact]
    public void CreateDbParameter_WithDirection_UsesDbTypeOverload()
    {
        using var ctx = CreateContext();
        using var tx = (TransactionContext)ctx.BeginTransaction();

        var param = tx.CreateDbParameter<int>(DbType.Int32, 5, ParameterDirection.Output);

        Assert.Equal(ParameterDirection.Output, param.Direction);
    }

    [Fact]
    public async Task CloseAndDisposeConnectionAsync_IgnoresTransactionConnection()
    {
        using var ctx = CreateContext();
        await using var tx = ctx.BeginTransaction();
        var conn = tx.GetConnection(ExecutionType.Write);

        await tx.CloseAndDisposeConnectionAsync(conn);

        Assert.Equal(ConnectionState.Open, conn.State);
    }

    [Fact]
    public async Task SavepointAsync_UsesNonDbCommandPath()
    {
        using var ctx = CreateContext();
        var strategy = new NonDbCommandConnectionStrategy();
        ReplaceStrategy(ctx, strategy);

        await using var tx = ctx.BeginTransaction();
        await tx.SavepointAsync("sp1");
        await tx.RollbackToSavepointAsync("sp1");

        Assert.NotNull(strategy.Connection.LastCommand);
        Assert.Equal(1, strategy.Connection.LastCommand!.ExecuteNonQueryCount);
    }

    private static DatabaseContext CreateContext()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection
        };
        return new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite), NullLoggerFactory.Instance);
    }

    private static void ReplaceStrategy(DatabaseContext context, IConnectionStrategy strategy)
    {
        var field = typeof(DatabaseContext).GetField("_connectionStrategy", BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(context, strategy);
    }

    private sealed class NonDbCommandConnectionStrategy : IConnectionStrategy
    {
        public NonDbCommandTrackedConnection Connection { get; } = new();

        public ValueTask ReleaseConnectionAsync(ITrackedConnection? connection)
        {
            return ValueTask.CompletedTask;
        }

        public void ReleaseConnection(ITrackedConnection? connection)
        {
        }

        public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared)
        {
            return Connection;
        }

        public (ISqlDialect? dialect, IDataSourceInformation? dataSourceInfo) HandleDialectDetection(
            ITrackedConnection? initConnection,
            DbProviderFactory? factory,
            ILoggerFactory loggerFactory)
        {
            return (null, null);
        }
    }

    private sealed class NonDbCommandTrackedConnection : ITrackedConnection
    {
        private ConnectionState _state = ConnectionState.Closed;

        public NonDbCommand? LastCommand { get; private set; }
        [AllowNull]
        public string ConnectionString { get; set; } = string.Empty;
        public int ConnectionTimeout => 0;
        public string Database => "test";
        public ConnectionState State => _state;
        public string DataSource => "test";
        public string ServerVersion => "1.0";
        public ConnectionLocalState LocalState { get; } = new();

        public IDbTransaction BeginTransaction()
        {
            return new StubTransaction(this, IsolationLevel.ReadCommitted);
        }

        public IDbTransaction BeginTransaction(IsolationLevel isolationLevel)
        {
            return new StubTransaction(this, isolationLevel);
        }

        public void ChangeDatabase(string databaseName)
        {
        }

        public void Close()
        {
            _state = ConnectionState.Closed;
        }

        public IDbCommand CreateCommand()
        {
            var command = new NonDbCommand();
            LastCommand = command;
            return command;
        }

        public void Open()
        {
            _state = ConnectionState.Open;
        }

        public Task OpenAsync(CancellationToken cancellationToken = default)
        {
            _state = ConnectionState.Open;
            return Task.CompletedTask;
        }

        public DataTable GetSchema(string dataSourceInformation)
        {
            return new DataTable();
        }

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public ILockerAsync GetLock()
        {
            return NoOpAsyncLocker.Instance;
        }

        public DataTable GetSchema()
        {
            return new DataTable();
        }
    }

    private sealed class StubTransaction : IDbTransaction
    {
        public StubTransaction(IDbConnection connection, IsolationLevel isolationLevel)
        {
            Connection = connection;
            IsolationLevel = isolationLevel;
        }

        public IDbConnection Connection { get; }
        public IsolationLevel IsolationLevel { get; }

        public void Commit()
        {
        }

        public void Rollback()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class NonDbCommand : IDbCommand
    {
        private readonly FakeParameterCollection _parameters = new();

        public int ExecuteNonQueryCount { get; private set; }
        [AllowNull]
        public string CommandText { get; set; } = string.Empty;
        public int CommandTimeout { get; set; }
        public CommandType CommandType { get; set; }
        public IDbConnection? Connection { get; set; }
        public IDataParameterCollection Parameters => _parameters;
        public IDbTransaction? Transaction { get; set; }
        public UpdateRowSource UpdatedRowSource { get; set; }

        public void Cancel()
        {
        }

        public IDbDataParameter CreateParameter()
        {
            return new fakeDbParameter();
        }

        public void Dispose()
        {
        }

        public int ExecuteNonQuery()
        {
            ExecuteNonQueryCount++;
            return 1;
        }

        public IDataReader ExecuteReader()
        {
            throw new NotSupportedException();
        }

        public IDataReader ExecuteReader(CommandBehavior behavior)
        {
            throw new NotSupportedException();
        }

        public object? ExecuteScalar()
        {
            throw new NotSupportedException();
        }

        public void Prepare()
        {
        }
    }
}
