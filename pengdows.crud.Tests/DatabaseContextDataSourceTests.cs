using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextDataSourceTests
{
    [Fact]
    public void Constructor_UsesFactoryDataSourceWhenAvailable()
    {
        var factory = new DataSourceCapableFactory();
        using var ctx = new DatabaseContext("Data Source=:memory:", factory);

        Assert.NotNull(ctx.DataSource);
        Assert.IsType<TestDataSource>(ctx.DataSource);
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
}
