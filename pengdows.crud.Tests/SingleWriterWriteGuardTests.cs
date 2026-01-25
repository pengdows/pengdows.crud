using System;
using System.Data.Common;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.strategies.connection;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class SingleWriterWriteGuardTests
{
    [Fact]
    public async Task Write_UsingNonWriterConnection_Throws()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext(config, factory);
        ReplaceStrategy(context, new MisroutingStrategy(context));

        await using var container = context.CreateSqlContainer("CREATE TABLE t(id INTEGER)");
        await Assert.ThrowsAsync<InvalidOperationException>(() => container.ExecuteNonQueryAsync());
    }

    private static void ReplaceStrategy(DatabaseContext context, IConnectionStrategy strategy)
    {
        var field = typeof(DatabaseContext).GetField("_connectionStrategy",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(context, strategy);
    }

    private sealed class MisroutingStrategy : IConnectionStrategy
    {
        private readonly DatabaseContext _ctx;

        public MisroutingStrategy(DatabaseContext ctx)
        {
            _ctx = ctx;
        }

        public ITrackedConnection GetConnection(ExecutionType executionType, bool isShared)
        {
            return _ctx.FactoryCreateConnection(null, isShared, true);
        }

        public void ReleaseConnection(ITrackedConnection? connection)
        {
            connection?.Dispose();
        }

        public async ValueTask ReleaseConnectionAsync(ITrackedConnection? connection)
        {
            if (connection is IAsyncDisposable ad)
            {
                await ad.DisposeAsync();
            }
            else
            {
                connection?.Dispose();
            }
        }

        public (ISqlDialect? dialect, IDataSourceInformation? dataSourceInfo) HandleDialectDetection(
            ITrackedConnection? initConnection,
            DbProviderFactory? factory,
            ILoggerFactory loggerFactory)
        {
            return (null, null);
        }

        public bool IsDisposed => false;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}