using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextDataSourceTests
{
    [Fact]
    public void FakeDbDataSource_RegistersProviderFactory()
    {
        using var _ = new FakeDbDataSource("Data Source=fake-register-test", SupportedDatabase.Sqlite);
        var registered = DbProviderFactories.GetFactory("pengdows.crud.fakeDb");
        Assert.NotNull(registered);
    }

    [Fact]
    public void Constructor_UsesFactoryDataSourceWhenAvailable()
    {
        var factory = new DataSourceCapableFactory();
        using var ctx = new DatabaseContext("Data Source=:memory:", factory);

        Assert.NotNull(ctx.DataSource);
        Assert.IsType<TestDataSource>(ctx.DataSource);
    }

    [Fact]
    public void ReadOnlyConnections_UseReaderDataSource_WhenFactorySupportsCreation()
    {
        var connectionString = "Data Source=test;EmulatedProduct=SqlServer;Application Name=Widget";
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        var factory = new DataSourceCapableFactory();
        using var dataSource = factory.CreateDataSource(new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        });
        using var ctx = new DatabaseContext(config, dataSource, factory);

        var tracked = (TrackedConnection)ctx.GetConnection(ExecutionType.Read);
        try
        {
            var inner = GetInnerConnection(tracked);
            Assert.Contains("ApplicationIntent=ReadOnly", inner.ConnectionString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Application Name=Widget-ro", inner.ConnectionString, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ctx.CloseAndDisposeConnection(tracked);
        }
    }

    [Fact]
    public void ReadOnlyConnections_FallBackToFactory_WhenNoReaderDataSource()
    {
        var connectionString = "Data Source=test;EmulatedProduct=SqlServer;Application Name=Widget";
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        using var dataSource = new FakeDbDataSource(connectionString, SupportedDatabase.SqlServer);
        using var ctx = new DatabaseContext(config, dataSource, dataSource.Factory);

        var tracked = (TrackedConnection)ctx.GetConnection(ExecutionType.Read);
        try
        {
            var inner = GetInnerConnection(tracked);
            Assert.Contains("ApplicationIntent=ReadOnly", inner.ConnectionString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Application Name=Widget-ro", inner.ConnectionString, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ctx.CloseAndDisposeConnection(tracked);
        }
    }

    [Fact]
    public void ReadWriteConnections_FromDataSource_ApplyReadOnlyConnectionString()
    {
        var connectionString = "Data Source=test;EmulatedProduct=SqlServer;Application Name=Widget";
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            ProviderName = SupportedDatabase.SqlServer.ToString(),
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        using var dataSource = new FakeDbDataSource(connectionString, SupportedDatabase.SqlServer);
        using var ctx = new DatabaseContext(config, dataSource, dataSource.Factory);

        var tracked = (TrackedConnection)ctx.GetConnection(ExecutionType.Read);
        try
        {
            var inner = GetInnerConnection(tracked);
            Assert.Contains("ApplicationIntent=ReadOnly", inner.ConnectionString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Application Name=Widget-ro", inner.ConnectionString, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ctx.CloseAndDisposeConnection(tracked);
        }
    }

    [Fact]
    public void Dispose_DisposesInternallyCreatedDataSources()
    {
        var factory = new CountingDataSourceFactory();
        var context = new DatabaseContext("Data Source=:memory:", factory);

        Assert.NotEmpty(factory.CreatedSources);
        Assert.All(factory.CreatedSources, source => Assert.False(source.Disposed));

        context.Dispose();

        Assert.All(factory.CreatedSources, source => Assert.True(source.Disposed));
    }

    [Fact]
    public void Dispose_DoesNotDisposeProvidedPrimaryDataSource_ButDisposesOwnedReaderDataSource()
    {
        var factory = new CountingDataSourceFactory();
        var provided = new CountingDataSource("Data Source=primary");
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=primary;EmulatedProduct=SqlServer;Application Name=Widget",
            ReadOnlyConnectionString = "Data Source=replica;EmulatedProduct=SqlServer;Application Name=Widget",
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        var context = new DatabaseContext(config, provided, factory);
        context.Dispose();

        Assert.False(provided.Disposed);
        Assert.Contains(factory.CreatedSources, source => source.Disposed);
    }

    private static DbConnection GetInnerConnection(TrackedConnection tracked)
    {
        var field = typeof(TrackedConnection).GetField("_connection",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (DbConnection)field.GetValue(tracked)!;
    }

    private sealed class DataSourceCapableFactory : DbProviderFactory
    {
        public override DbConnection CreateConnection()
        {
            return new fakeDbConnection();
        }

        public override DbConnectionStringBuilder CreateConnectionStringBuilder()
        {
            return new DbConnectionStringBuilder();
        }

        public DbDataSource CreateDataSource(DbConnectionStringBuilder builder)
        {
            return new TestDataSource(builder.ConnectionString ?? string.Empty);
        }
    }

    private sealed class TestDataSource : DbDataSource
    {
        private readonly string _connectionString;

        public TestDataSource(string connectionString)
        {
            _connectionString = connectionString;
        }

        public override string ConnectionString => _connectionString;

        protected override DbConnection CreateDbConnection()
        {
            var conn = new fakeDbConnection();
            conn.ConnectionString = _connectionString;
            return conn;
        }

        protected override DbConnection OpenDbConnection()
        {
            var conn = CreateDbConnection();
            conn.Open();
            return conn;
        }

        protected override async ValueTask<DbConnection> OpenDbConnectionAsync(CancellationToken cancellationToken)
        {
            var conn = CreateDbConnection();
            await conn.OpenAsync(cancellationToken);
            return conn;
        }
    }

    private sealed class CountingDataSourceFactory : DbProviderFactory
    {
        private readonly List<CountingDataSource> _created = new();
        public IReadOnlyList<CountingDataSource> CreatedSources => _created;

        public override DbConnection CreateConnection()
        {
            return new fakeDbConnection();
        }

        public override DbConnectionStringBuilder CreateConnectionStringBuilder()
        {
            return new DbConnectionStringBuilder();
        }

        public DbDataSource CreateDataSource(DbConnectionStringBuilder builder)
        {
            var source = new CountingDataSource(builder.ConnectionString ?? string.Empty);
            _created.Add(source);
            return source;
        }
    }

    private sealed class CountingDataSource : DbDataSource
    {
        private readonly string _connectionString;
        public bool Disposed { get; private set; }

        public CountingDataSource(string connectionString)
        {
            _connectionString = connectionString;
        }

        public override string ConnectionString => _connectionString;

        protected override DbConnection CreateDbConnection()
        {
            var conn = new fakeDbConnection();
            conn.ConnectionString = _connectionString;
            return conn;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        protected override ValueTask DisposeAsyncCore()
        {
            Disposed = true;
            return base.DisposeAsyncCore();
        }
    }
}
