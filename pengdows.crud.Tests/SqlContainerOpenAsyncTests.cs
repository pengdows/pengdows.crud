#region

using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SqlContainerOpenAsyncTests
{
    [Fact]
    public async Task ExecuteNonQueryAsync_OpensConnectionAsync_WhenClosed()
    {
        var factory = new CountingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        await using var ctx = new DatabaseContext(config, factory, null, new TypeMapRegistry());
        var before = factory.Connection.OpenAsyncCount;
        factory.Connection.Close();
        await using var container = ctx.CreateSqlContainer("SELECT 1");
        await container.ExecuteNonQueryAsync();
        Assert.Equal(before + 1, factory.Connection.OpenAsyncCount);
    }

    [Fact]
    public async Task ExecuteNonQueryAsync_DoesNotOpenConnection_WhenAlreadyOpen()
    {
        var factory = new CountingFactory();
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        await using var ctx = new DatabaseContext(config, factory, null, new TypeMapRegistry());
        var before = factory.Connection.OpenAsyncCount;
        await using var container = ctx.CreateSqlContainer("SELECT 1");
        await container.ExecuteNonQueryAsync();
        Assert.Equal(before, factory.Connection.OpenAsyncCount);
    }

    private sealed class CountingFactory : DbProviderFactory
    {
        public CountingConnection Connection { get; }

        public CountingFactory()
        {
            Connection = new CountingConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        }

        public override DbConnection CreateConnection()
        {
            return Connection;
        }

        public override DbCommand CreateCommand()
        {
            return new fakeDbCommand();
        }

        public override DbParameter CreateParameter()
        {
            return new fakeDbParameter();
        }
    }

    private sealed class CountingConnection : fakeDbConnection
    {
    }
}