using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using System.Data;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Verifies SQL container reuse: executing the same container multiple times with updated parameters,
/// and sequential commands on the same connection within a transaction.
/// </summary>
[Collection("IntegrationTests")]
public class SqlContainerReuseTests : DatabaseTestBase
{
    public SqlContainerReuseTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();
    }

    [SkippableFact]
    public async Task ReuseContainer_WithNewParameters_WorksSuccessfully()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var auditResolver = GetAuditResolver();
            var helper = new TableGateway<TestTable, long>(context, auditResolver);

            // 1. Create two entities
            var e1 = new TestTable { Id = 3001, Name = NameEnum.Test, Value = 100, CreatedOn = DateTime.UtcNow };
            var e2 = new TestTable { Id = 3002, Name = NameEnum.Test2, Value = 200, CreatedOn = DateTime.UtcNow };

            // 2. Prepare an insert container manually from the first entity
            await using var container = context.CreateSqlContainer();
            // We use the helper to build the insert SQL for us to ensure quoting is perfect
            // Note: TableGateway doesn't have a public "BuildInsertSql" but we can build it from attributes
            var table = IntegrationObjectNameHelper.Table(context, "test_table");
            var idCol = context.WrapObjectName("id");
            var nameCol = context.WrapObjectName("name");
            var valCol = context.WrapObjectName("value");
            var actCol = context.WrapObjectName("is_active");
            var dateCol = context.WrapObjectName("created_at");

            var pId = context.MakeParameterName("id");
            var pName = context.MakeParameterName("name");
            var pVal = context.MakeParameterName("value");
            var pAct = context.MakeParameterName("is_active");
            var pDate = context.MakeParameterName("created_at");

            var sql =
                $"INSERT INTO {table} ({idCol}, {nameCol}, {valCol}, {actCol}, {dateCol}) VALUES ({pId}, {pName}, {pVal}, {pAct}, {pDate})";
            container.Query.Append(sql);

            // 3. Execute with first set of params
            container.AddParameterWithValue("id", DbType.Int64, e1.Id);
            // name is a string column ([EnumColumn] with DbType.String stores the enum name); binding
            // an Int32 relied on an implicit int-to-varchar conversion that Sybase ASE rejects.
            container.AddParameterWithValue("name", DbType.String, e1.Name.ToString());
            container.AddParameterWithValue("value", DbType.Int32, e1.Value);
            container.AddParameterWithValue("is_active", DbType.Boolean, e1.IsActive);
            container.AddParameterWithValue("created_at", DbType.DateTime, e1.CreatedOn);
            await container.ExecuteNonQueryAsync();

            // 4. Update parameters and execute again (reuse container)
            container.SetParameterValue("id", e2.Id);
            container.SetParameterValue("name", e2.Name.ToString());
            container.SetParameterValue("value", e2.Value);
            // created_at remains the same
            await container.ExecuteNonQueryAsync();

            // 5. Verify both exist
            var rows = await helper.RetrieveAsync(new[] { 3001L, 3002L }, context);
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, r => r.Id == 3001L && r.Name == NameEnum.Test);
            Assert.Contains(rows, r => r.Id == 3002L && r.Name == NameEnum.Test2);
        });
    }

    [SkippableFact]
    public async Task SequentialCommands_OnSameConnection_WorksSuccessfully()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var auditResolver = GetAuditResolver();
            var helper = new TableGateway<TestTable, long>(context, auditResolver);

            // Use a transaction to force same connection
            await using var tx = context.BeginTransaction();

            var table = IntegrationObjectNameHelper.Table(context, "test_table");
            var idCol = context.WrapObjectName("id");
            var valCol = context.WrapObjectName("value");
            var pId = tx.MakeParameterName("id");
            var pVal = tx.MakeParameterName("val");

            // Command 1: Use framework for insertion (handles all dialect/audit complexity)
            var entity = new TestTable { Id = 4001, Name = NameEnum.Test, Value = 0 };
            await helper.CreateAsync(entity, tx);

            // Command 2: Parameterized manual UPDATE
            var updateSql = $"UPDATE {table} SET {valCol} = {pVal} WHERE {idCol} = {pId}";
            await using var c2 = tx.CreateSqlContainer(updateSql);
            c2.AddParameterWithValue("val", DbType.Int32, 42);
            c2.AddParameterWithValue("id", DbType.Int64, entity.Id);
            await c2.ExecuteNonQueryAsync();

            // Command 3: Parameterized manual SELECT
            var selectSql = $"SELECT {valCol} FROM {table} WHERE {idCol} = {pId}";
            await using var c3 = tx.CreateSqlContainer(selectSql);
            c3.AddParameterWithValue("id", DbType.Int64, entity.Id);
            var val = await c3.ExecuteScalarRequiredAsync<int>();

            Assert.Equal(42, val);

            tx.Commit();
        });
    }

    /// <summary>
    /// A reader read to EOF and disposed releases its connection cleanly, so the next command on
    /// the context runs (MySql.Data once failed the follow-up after an EOF disposal; ported from
    /// the testbed's reader-disposal check and run on every database).
    /// </summary>
    [SkippableFact]
    public async Task Reader_ReadToEndAndDisposed_ThenNextCommandRuns()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var helper = new TableGateway<TestTable, long>(context, GetAuditResolver());
            var entity = new TestTable { Id = 3101, Name = NameEnum.Test, Value = 42, Description = "reader-eof" };
            await helper.CreateAsync(entity, context);

            await using (var select = context.CreateSqlContainer())
            {
                select.Query.Append("SELECT ").Append(context.WrapObjectName("id"))
                    .Append(" FROM ").Append(IntegrationObjectNameHelper.Table(context, "test_table"))
                    .Append(" WHERE ").Append(context.WrapObjectName("id"))
                    .Append(" = ").Append(select.MakeParameterName("p0"));
                select.AddParameterWithValue("p0", DbType.Int64, entity.Id);

                await using var reader = await select.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.False(await reader.ReadAsync());
            }

            Assert.NotNull(await helper.RetrieveOneAsync(entity.Id, context));
        });
    }
}
