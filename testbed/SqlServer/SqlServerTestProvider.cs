using System.Data;
using Microsoft.Data.SqlClient;
using pengdows.crud;
using pengdows.crud.attributes;

namespace testbed.SqlServer;

public sealed class SqlServerTestProvider : TestProvider
{
    public SqlServerTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    protected override async Task RunAdditionalTestsAsync()
    {
        await TestIdentityReturningWithTriggerAsync();
        await TestPagingRequiresOrderByAsync();
    }

    /// <summary>
    /// AUTO_CLOSE is OFF by default for ordinary SQL Server databases (unlike Firebird's
    /// RDB$LINGER=0 or Db2's implicit-activation default), so DbMode.Best correctly stays
    /// Standard without this. This override exists purely to empirically check: IF an
    /// application/DBA turns AUTO_CLOSE on for a specific database, does the same idle-unload
    /// probe methodology detect a real cost the way it does for Firebird? (It must not change
    /// SQL Server's own DbMode.Best resolution — that stays Standard; AUTO_CLOSE is opt-in.)
    /// </summary>
    protected override async Task<bool> TryEnableFastIdleUnloadAsync()
    {
        await using var sc = _context.CreateSqlContainer("ALTER DATABASE CURRENT SET AUTO_CLOSE ON");
        await sc.ExecuteNonQueryAsync();
        return true;
    }

    protected override void ClearProviderPoolForIdleUnloadProbe()
    {
        SqlConnection.ClearAllPools();
    }

    private Task TestPagingRequiresOrderByAsync()
    {
        using var container = _context.CreateSqlContainer();
        container.Query.Append("SELECT 1");

        try
        {
            _context.GetDialect().AppendPaging(container.Query, offset: 0, limit: 1);
            throw new Exception("SQL Server paging without ORDER BY was not rejected");
        }
        catch (InvalidOperationException exception)
            when (exception.Message.Contains("ORDER BY", StringComparison.Ordinal))
        {
            CheckOk("SqlServer.PagingWithoutOrderBy", "SQL Server paging without ORDER BY: rejected before execution");
        }

        return Task.CompletedTask;
    }

    private async Task TestIdentityReturningWithTriggerAsync()
    {
        var tableName = _context.WrapObjectName("sqlserver_trigger_identity_test");
        var auditTableName = _context.WrapObjectName("sqlserver_trigger_identity_audit");
        await using var container = _context.CreateSqlContainer();

        container.Query.Append($"DROP TABLE IF EXISTS {tableName}");
        await container.ExecuteNonQueryAsync();
        container.Clear();
        container.Query.Append($"DROP TABLE IF EXISTS {auditTableName}");
        await container.ExecuteNonQueryAsync();

        try
        {
            container.Clear();
            container.Query.Append($"CREATE TABLE {tableName} (");
            container.Query.Append($"{_context.WrapObjectName("id")} INT IDENTITY(1,1) PRIMARY KEY, ");
            container.Query.Append($"{_context.WrapObjectName("value")} NVARCHAR(100) NOT NULL)");
            await container.ExecuteNonQueryAsync();

            container.Clear();
            container.Query.Append($"CREATE TABLE {auditTableName} ({_context.WrapObjectName("id")} INT IDENTITY(1,1) PRIMARY KEY)");
            await container.ExecuteNonQueryAsync();

            container.Clear();
            container.Query.Append($"CREATE TRIGGER {_context.WrapObjectName("sqlserver_trigger_identity_test_insert")} ON {tableName} AFTER INSERT AS INSERT INTO {auditTableName} DEFAULT VALUES");
            await container.ExecuteNonQueryAsync();

            var gateway = new TableGateway<TriggerIdentityRow, int>(_context);
            var row = new TriggerIdentityRow { Value = "trigger-safe" };
            var created = await gateway.CreateAsync(row);
            if (!created || row.Id <= 0)
            {
                throw new Exception("SQL Server trigger-safe identity insert did not return an identity value");
            }

            CheckOk("SqlServer.TriggerIdentityReturn", "SQL Server identity return with enabled trigger: OK");
        }
        finally
        {
            container.Clear();
            container.Query.Append($"DROP TABLE IF EXISTS {tableName}");
            await container.ExecuteNonQueryAsync();
            container.Clear();
            container.Query.Append($"DROP TABLE IF EXISTS {auditTableName}");
            await container.ExecuteNonQueryAsync();
        }
    }

    [Table("sqlserver_trigger_identity_test")]
    private class TriggerIdentityRow
    {
        [Id(false)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }
}
