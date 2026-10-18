using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// SafeNonBlockingReads on SQL Server with READ_COMMITTED_SNAPSHOT on and snapshot isolation off: the profile resolves to
/// ReadCommitted, and a reader really does not block on an uncommitted writer (row versioning). With neither on, the profile
/// is refused, and a plain ReadCommitted reader does block, which is why. Each test uses its own throwaway database.
/// </summary>
[Collection("IntegrationTests")]
public class SqlServerRcsiProfileTests : DatabaseTestBase
{
    public SqlServerRcsiProfileTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        base.GetSupportedProviders().Where(p => p == SupportedDatabase.SqlServer);

    private static async Task Exec(IDatabaseContext context, string sql)
    {
        await using var sc = context.CreateSqlContainer(sql);
        await sc.ExecuteNonQueryAsync();
    }

    private static async Task<DatabaseContext> CreateDatabase(IDatabaseContext admin, string name, bool rcsi)
    {
        await Exec(admin, $"CREATE DATABASE [{name}]");
        await Exec(admin, $"ALTER DATABASE [{name}] SET ALLOW_SNAPSHOT_ISOLATION OFF");
        await Exec(admin, $"ALTER DATABASE [{name}] SET READ_COMMITTED_SNAPSHOT {(rcsi ? "ON" : "OFF")} WITH ROLLBACK IMMEDIATE");

        var builder = new DbConnectionStringBuilder { ConnectionString = ((DatabaseContext)admin).RawConnectionString };
        builder.Remove("Database");
        builder.Remove("Initial Catalog");
        builder["Initial Catalog"] = name;
        return new DatabaseContext(builder.ConnectionString, SqlClientFactory.Instance);
    }

    private static async Task DropDatabase(IDatabaseContext admin, string name)
    {
        try
        {
            await Exec(admin, $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await Exec(admin, $"DROP DATABASE [{name}]");
        }
        catch (DatabaseException)
        {
            // best-effort cleanup of a throwaway database; the next run creates a new, uniquely named one
        }
    }

    [SkippableFact]
    public async Task WithRcsiOn_TheProfileRunsAsReadCommitted_AndAReaderDoesNotBlockOnAnUncommittedWriter()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.SqlServer, async admin =>
        {
            var name = "pengdows_rcsi_" + Guid.NewGuid().ToString("N");
            try
            {
                await using var ctx = await CreateDatabase(admin, name, rcsi: true);
                Assert.True(ctx.RCSIEnabled);
                Assert.False(ctx.SnapshotIsolationEnabled);
                await Exec(ctx, "CREATE TABLE t (id INT PRIMARY KEY, v INT NOT NULL)");
                await Exec(ctx, "INSERT INTO t (id, v) VALUES (1, 10)");

                using var writer = ctx.BeginTransaction();
                await using (var update = writer.CreateSqlContainer("UPDATE t SET v = 20 WHERE id = 1"))
                {
                    await update.ExecuteNonQueryAsync();
                }

                using var reader = ctx.BeginTransaction(IsolationProfile.SafeNonBlockingReads, ExecutionType.Read);
                Assert.Equal(IsolationLevel.ReadCommitted, reader.IsolationLevel);
                await using var select = reader.CreateSqlContainer("SELECT v FROM t WHERE id = 1");
                var read = select.ExecuteScalarRequiredAsync<int>().AsTask();

                var finished = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.Same(read, finished);
                Assert.Equal(10, await read);

                reader.Commit();
                writer.Rollback();
            }
            finally
            {
                await DropDatabase(admin, name);
            }
        });
    }

    [SkippableFact]
    public async Task WithNeitherOn_TheProfileIsRefused_AndAPlainReadCommittedReaderBlocks()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.SqlServer, async admin =>
        {
            var name = "pengdows_rcsi_" + Guid.NewGuid().ToString("N");
            try
            {
                await using var ctx = await CreateDatabase(admin, name, rcsi: false);
                Assert.False(ctx.RCSIEnabled);
                Assert.False(ctx.SnapshotIsolationEnabled);
                await Exec(ctx, "CREATE TABLE t (id INT PRIMARY KEY, v INT NOT NULL)");
                await Exec(ctx, "INSERT INTO t (id, v) VALUES (1, 10)");

                Assert.Throws<TransactionModeNotSupportedException>(
                    () => ctx.BeginTransaction(IsolationProfile.SafeNonBlockingReads, ExecutionType.Read));

                using var writer = ctx.BeginTransaction();
                await using (var update = writer.CreateSqlContainer("UPDATE t SET v = 20 WHERE id = 1"))
                {
                    await update.ExecuteNonQueryAsync();
                }

                using var reader = ctx.BeginTransaction(IsolationLevel.ReadCommitted, ExecutionType.Read);
                await using var select = reader.CreateSqlContainer("SELECT v FROM t WHERE id = 1");
                var read = select.ExecuteScalarRequiredAsync<int>().AsTask();

                var finished = await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(3)));
                Assert.NotSame(read, finished);

                writer.Rollback();
                Assert.Equal(10, await read);
                reader.Commit();
            }
            finally
            {
                await DropDatabase(admin, name);
            }
        });
    }
}
