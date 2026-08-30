using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.tenant;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextAsyncCreationTests
{
    [Fact]
    public async Task CreateAsync_WithConfiguration_ReturnsInitializedContextAndDialect()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetScalarResult(42);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            EnableMetrics = true
        };

        await using var context = await DatabaseContext.CreateAsync(config, factory);

        Assert.NotNull(context);
        Assert.Equal("PostgreSQL", context.Name);
        Assert.NotNull(context.Dialect);
        Assert.Equal(SupportedDatabase.PostgreSql, context.Dialect.DatabaseType);
        Assert.Equal(DbMode.Standard, context.ConnectionMode);
        Assert.NotNull(context.Metrics);

        using var sc = context.CreateSqlContainer("SELECT 42");
        var result = await sc.ExecuteScalarRequiredAsync<int>();
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task CreateAsync_WithDataSource_ReturnsInitializedContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SupportsNativeDataSource = true;
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        var dataSource = (FakeDbDataSource)factory.CreateDataSource(config.ConnectionString);

        await using var context = await DatabaseContext.CreateAsync(config, dataSource, factory);

        Assert.NotNull(context);
        Assert.Equal("PostgreSQL", context.Name);
        Assert.Equal(SupportedDatabase.PostgreSql, context.Dialect.DatabaseType);
    }

    [Fact]
    public async Task CreateAsync_WithConnectionStringAndFactory_ReturnsInitializedContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connStr = "Data Source=test.db;EmulatedProduct=Sqlite";

        await using var context = await DatabaseContext.CreateAsync(connStr, factory);

        Assert.NotNull(context);
        Assert.Equal("SQLite", context.Name);
        Assert.Equal(SupportedDatabase.Sqlite, context.Dialect.DatabaseType);
    }

    [Fact]
    public async Task CreateAsync_WithPreCanceledToken_CancelsBeforeOpeningConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql"
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await DatabaseContext.CreateAsync(config, factory, cancellationToken: cts.Token);
        });

        Assert.Empty(factory.CreatedConnections);
    }

    [Fact]
    public async Task CreateAsync_WithInvalidConfiguration_ThrowsArgumentException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            await DatabaseContext.CreateAsync((IDatabaseContextConfiguration)null!, factory);
        });

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            var config = new DatabaseContextConfiguration { ConnectionString = "" };
            await DatabaseContext.CreateAsync(config, factory);
        });
    }

    [Fact]
    public async Task CreateAsync_WhenConnectionFails_ThrowsConnectionFailedException()
    {
        var factory = fakeDbFactory.CreateFailingFactory(
            SupportedDatabase.PostgreSql,
            ConnectionFailureMode.FailOnOpen,
            new InvalidOperationException("Connection open failed."));

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql"
        };

        var ex = await Assert.ThrowsAsync<ConnectionFailedException>(async () =>
        {
            await DatabaseContext.CreateAsync(config, factory);
        });

        Assert.Equal("InitConnect", ex.Phase);
    }

    [Fact]
    public async Task CreateAsync_WhenClaimUniqueFails_UnwindsInternallyCreatedDataSource()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql) { SupportsNativeDataSource = true };
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test_ds_unwind;EmulatedProduct=PostgreSql",
            EnforceUniqueConnectionString = true
        };

        await using var ctx1 = await DatabaseContext.CreateAsync(config, factory);

        var dataSourcesBefore = factory.CreatedDataSources.Count;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await DatabaseContext.CreateAsync(config, factory);
        });

        Assert.True(factory.CreatedDataSources.Count > dataSourcesBefore);
        Assert.True(factory.CreatedDataSources[^1].WasDisposed);
    }

    [Fact]
    public async Task CreateAsync_WithEnforceUniqueConnectionString_ThrowsOnDuplicateContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=unique_async_test;EmulatedProduct=PostgreSql",
            EnforceUniqueConnectionString = true
        };

        await using var ctx1 = await DatabaseContext.CreateAsync(config, factory);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await DatabaseContext.CreateAsync(config, factory);
        });
    }

    [Fact]
    public async Task CreateAsync_ProvesAsyncPathUsed_WithBlockedSynchronousExecution()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=localhost;Database=test;EmulatedProduct=MySql",
            DbMode = DbMode.Standard
        };

        var customFactory = new BlockingAsyncDbProviderFactory(SupportedDatabase.MySql);
        customFactory.ScalarResult = "3.04.0.1";

        await using var context = await DatabaseContext.CreateAsync(config, customFactory);

        Assert.Equal(SupportedDatabase.AuroraMySql, context.Dialect.DatabaseType);
        Assert.Equal("MySQL", context.Name);
    }

    [Fact]
    public async Task CreateAsync_SingleConnectionMode_InitializesPersistentConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection
        };

        await using var context = await DatabaseContext.CreateAsync(config, factory);

        Assert.Equal(DbMode.SingleConnection, context.ConnectionMode);
        Assert.NotNull(context.PersistentConnection);
    }

    [Fact]
    public async Task CreateAsync_PreventDatabaseUnloadMode_SetsUpSentinels()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=(localdb)\\mssqllocaldb;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.PreventDatabaseUnload
        };

        await using var context = await DatabaseContext.CreateAsync(config, factory);

        Assert.Equal(DbMode.PreventDatabaseUnload, context.ConnectionMode);
        Assert.True(context.GetSentinelSnapshot().Count > 0);
    }

    [Fact]
    public async Task DefaultDatabaseContextFactory_CreateAsync_CreatesAndInitializesContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql"
        };

        IDatabaseContextFactory contextFactory = new DefaultDatabaseContextFactory();
        var context = await contextFactory.CreateAsync(config, factory, NullLoggerFactory.Instance);

        Assert.NotNull(context);
        Assert.Equal("PostgreSQL", context.Name);

        if (context is IAsyncDisposable ad)
        {
            await ad.DisposeAsync();
        }
        else
        {
            context.Dispose();
        }
    }

    private sealed class BlockingAsyncDbProviderFactory : DbProviderFactory
    {
        private readonly fakeDbFactory _inner;
        public object? ScalarResult { get; set; }

        public BlockingAsyncDbProviderFactory(SupportedDatabase product)
        {
            _inner = new fakeDbFactory(product);
        }

        public override DbConnection CreateConnection()
        {
            var conn = (fakeDbConnection)_inner.CreateConnection();
            conn.BlockSynchronousCommandExecution = true;
            if (ScalarResult != null)
            {
                conn.SetScalarResultForCommand("SELECT @@aurora_version", ScalarResult);
            }
            return conn;
        }

        public override DbCommand CreateCommand() => _inner.CreateCommand();
        public override DbParameter CreateParameter() => _inner.CreateParameter();
        public override DbConnectionStringBuilder? CreateConnectionStringBuilder() => _inner.CreateConnectionStringBuilder();
    }
}
