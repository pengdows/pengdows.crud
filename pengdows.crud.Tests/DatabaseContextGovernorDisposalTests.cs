using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextGovernorDisposalTests
{
    [Fact]
    public async Task DisposeAsync_WaitsForOutstandingLease_ThenDisposesGovernors()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ProviderName = "fake",
            PoolAcquireTimeout = TimeSpan.FromMilliseconds(250)
        };

        var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
        var readerGovernor = GetGovernor(context, "_readerGovernor");

        var connection = context.GetConnection(ExecutionType.Read);
        var disposeTask = context.DisposeAsync().AsTask();

        Assert.False(disposeTask.IsCompleted);

        connection.Dispose();
        await disposeTask;

        Assert.Throws<ObjectDisposedException>(() => readerGovernor.Acquire());
    }

    private static PoolGovernor GetGovernor(DatabaseContext context, string fieldName)
    {
        var field = typeof(DatabaseContext).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var governor = field!.GetValue(context) as PoolGovernor;
        Assert.NotNull(governor);
        return governor!;
    }
}
