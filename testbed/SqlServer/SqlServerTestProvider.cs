using System.Data;
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

            CheckOk("SQL Server identity return with enabled trigger: OK");
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
