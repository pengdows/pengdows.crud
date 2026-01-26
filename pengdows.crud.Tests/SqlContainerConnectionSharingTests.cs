using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.metrics;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlContainerConnectionSharingTests
{
    [Fact]
    public async Task ExecuteReaderSingleRowAsync_SingleConnection_UsesSharedConnection()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var inner = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite));
        var context = new RecordingContext(inner);

        await using var container = context.CreateSqlContainer("SELECT 1");
        await using var reader = await container.ExecuteReaderSingleRowAsync();

        Assert.True(context.LastIsShared.HasValue);
        Assert.True(context.LastIsShared.Value);
    }

    [Fact]
    public async Task ExecuteReaderSingleRowAsync_StandardMode_UsesEphemeralConnection()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        await using var inner = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.SqlServer));
        var context = new RecordingContext(inner);

        await using var container = context.CreateSqlContainer("SELECT 1");
        await using var reader = await container.ExecuteReaderSingleRowAsync();

        Assert.True(context.LastIsShared.HasValue);
        Assert.False(context.LastIsShared.Value);
    }

    private sealed class RecordingContext : IDatabaseContext, ISqlDialectProvider
    {
        private readonly DatabaseContext _context;

        public RecordingContext(DatabaseContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public bool? LastIsShared { get; private set; }

        public ISqlDialect Dialect => ((ISqlDialectProvider)_context).Dialect;

        public DbMode ConnectionMode => _context.ConnectionMode;
        public Guid RootId => _context.RootId;
        public ReadWriteMode ReadWriteMode => _context.ReadWriteMode;
        public string ConnectionString => _context.ConnectionString;
        public string Name
        {
            get => _context.Name;
            set => _context.Name = value;
        }
        public DbDataSource? DataSource => _context.DataSource;
        public ITypeMapRegistry TypeMapRegistry => _context.TypeMapRegistry;
        public IDataSourceInformation DataSourceInfo => _context.DataSourceInfo;
        public string SessionSettingsPreamble => _context.SessionSettingsPreamble;
        public ProcWrappingStyle ProcWrappingStyle => _context.ProcWrappingStyle;
        public int MaxParameterLimit => _context.MaxParameterLimit;
        public int MaxOutputParameters => _context.MaxOutputParameters;
        public long NumberOfOpenConnections => _context.NumberOfOpenConnections;
        public DatabaseMetrics Metrics => _context.Metrics;
        public SupportedDatabase Product => _context.Product;
        public long MaxNumberOfConnections => _context.MaxNumberOfConnections;
        public bool? ForceManualPrepare => _context.ForceManualPrepare;
        public bool? DisablePrepare => _context.DisablePrepare;
        public string QuotePrefix => _context.QuotePrefix;
        public string QuoteSuffix => _context.QuoteSuffix;
        public string CompositeIdentifierSeparator => _context.CompositeIdentifierSeparator;
        public bool IsReadOnlyConnection => _context.IsReadOnlyConnection;
        public bool RCSIEnabled => _context.RCSIEnabled;
        public bool SnapshotIsolationEnabled => _context.SnapshotIsolationEnabled;
        public bool IsDisposed => _context.IsDisposed;

        public event EventHandler<DatabaseMetrics> MetricsUpdated
        {
            add => _context.MetricsUpdated += value;
            remove => _context.MetricsUpdated -= value;
        }

        public string WrapObjectName(string name)
        {
            return _context.WrapObjectName(name);
        }

        public string MakeParameterName(DbParameter dbParameter)
        {
            return _context.MakeParameterName(dbParameter);
        }

        public string MakeParameterName(string parameterName)
        {
            return _context.MakeParameterName(parameterName);
        }

        public ILockerAsync GetLock()
        {
            return _context.GetLock();
        }

        public ISqlContainer CreateSqlContainer(string? query = null)
        {
            return SqlContainer.Create(this, query);
        }

        public DbParameter CreateDbParameter<T>(string? name, DbType type, T value)
        {
            return _context.CreateDbParameter(name, type, value);
        }

        public DbParameter CreateDbParameter<T>(string? name, DbType type, T value,
            ParameterDirection direction)
        {
            return _context.CreateDbParameter(name, type, value, direction);
        }

        public DbParameter CreateDbParameter<T>(DbType type, T value)
        {
            return _context.CreateDbParameter(type, value);
        }

        public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared = false)
        {
            LastIsShared = isShared;
            return _context.GetConnection(executionType, isShared);
        }

        public ITransactionContext BeginTransaction(IsolationLevel? isolationLevel = null,
            ExecutionType executionType = ExecutionType.Write, bool? readOnly = null)
        {
            return _context.BeginTransaction(isolationLevel, executionType, readOnly);
        }

        public ITransactionContext BeginTransaction(IsolationProfile isolationProfile,
            ExecutionType executionType = ExecutionType.Write, bool? readOnly = null)
        {
            return _context.BeginTransaction(isolationProfile, executionType, readOnly);
        }

        public string GenerateRandomName(int length = 5, int parameterNameMaxLength = 30)
        {
            return _context.GenerateRandomName(length, parameterNameMaxLength);
        }

        public void AssertIsWriteConnection()
        {
            _context.AssertIsWriteConnection();
        }

        public void AssertIsReadConnection()
        {
            _context.AssertIsReadConnection();
        }

        public void CloseAndDisposeConnection(ITrackedConnection? conn)
        {
            _context.CloseAndDisposeConnection(conn);
        }

        public ValueTask CloseAndDisposeConnectionAsync(ITrackedConnection? conn)
        {
            return _context.CloseAndDisposeConnectionAsync(conn);
        }

        public void Dispose()
        {
            _context.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            return _context.DisposeAsync();
        }
    }
}
