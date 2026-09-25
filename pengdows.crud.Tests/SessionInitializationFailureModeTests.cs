using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SessionInitializationFailureModeTests
{
    // Same shape as 3.0 (nullable, default null), so the setting doesn't change type on upgrade.
    [Fact]
    public void SessionInitializationFailureMode_IsNullable_AndDefaultsToNull()
    {
        Assert.Equal(typeof(SessionInitializationFailureMode?),
            typeof(IDatabaseContextConfiguration).GetProperty(nameof(IDatabaseContextConfiguration.SessionInitializationFailureMode))!.PropertyType);
        Assert.Null(new DatabaseContextConfiguration().SessionInitializationFailureMode);
    }

    // On 2.0.x, null keeps 2.0.5's behavior for every context, read-only included: log the failure
    // and proceed. (3.0 resolves null to FailClosed for a read-only context; that stays a 3.0 change.)
    [Fact]
    public async Task OpenAsync_DefaultMode_ReadOnlyContext_SessionSettingsFailure_StaysBestEffort()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var initConnection = new fakeDbConnection();
        factory.Connections.Add(initConnection);

        var opConnection = new fakeDbConnection();
        opConnection.SetFailOnCommand();
        factory.Connections.Add(opConnection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        await using var context = new DatabaseContext(config, factory);

        var conn = context.GetConnection(ExecutionType.Read);
        try
        {
            var ex = await Record.ExceptionAsync(async () => await conn.OpenAsync());
            Assert.Null(ex);
        }
        finally
        {
            conn.Dispose();
        }
    }

    [Fact]
    public async Task OpenAsync_FailClosed_SessionSettingsFailure_ThrowsConnectionException()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        // First connection: used for DatabaseContext construction/detection — must succeed.
        var initConnection = new fakeDbConnection();
        factory.Connections.Add(initConnection);

        // Second connection: used for the actual operation. CreateCommand() (used both by
        // session-settings application AND by ordinary query execution) fails.
        var opConnection = new fakeDbConnection();
        opConnection.SetFailOnCommand();
        factory.Connections.Add(opConnection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            SessionInitializationFailureMode = SessionInitializationFailureMode.FailClosed
        };

        await using var context = new DatabaseContext(config, factory);

        // Internal accessor — exercises the async first-open path, where the double-swallow
        // bug lived (a second, redundant try/catch around ExecuteSessionSettingsAsync).
        var conn = context.GetConnection(ExecutionType.Write);
        try
        {
            await Assert.ThrowsAsync<ConnectionException>(async () => await conn.OpenAsync());
        }
        finally
        {
            conn.Dispose();
        }
    }

    [Fact]
    public async Task OpenAsync_BestEffort_SessionSettingsFailure_DoesNotThrow()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        var initConnection = new fakeDbConnection();
        factory.Connections.Add(initConnection);

        var opConnection = new fakeDbConnection();
        opConnection.SetFailOnCommand();
        factory.Connections.Add(opConnection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite
            // SessionInitializationFailureMode left at its default (BestEffort) — regression guard.
        };

        await using var context = new DatabaseContext(config, factory);

        var conn = context.GetConnection(ExecutionType.Write);
        try
        {
            var ex = await Record.ExceptionAsync(async () => await conn.OpenAsync());
            Assert.Null(ex);
        }
        finally
        {
            conn.Dispose();
        }
    }
}
