#region

using System.Data;
using pengdows.crud;
using pengdows.crud.attributes;

#endregion

namespace testbed.PostgreSQL;

public class PostgreSQLTestProvider
    : TestProvider
{
    private readonly IDatabaseContext context;

    public PostgreSQLTestProvider(IDatabaseContext context, IServiceProvider serviceProvider) : base(context,
        serviceProvider)
    {
        this.context = context;
    }

    public override async Task CreateTable()
    {
        var databaseContext = context;
        var sqlContainer = databaseContext.CreateSqlContainer();
        var tableName = databaseContext.WrapObjectName("test_table");
        var idColumn = databaseContext.WrapObjectName("id");
        var nameColumn = databaseContext.WrapObjectName("name");
        var descriptionColumn = databaseContext.WrapObjectName("description");
        var valueColumn = databaseContext.WrapObjectName("value");
        var isActiveColumn = databaseContext.WrapObjectName("is_active");
        var createdAtColumn = databaseContext.WrapObjectName("created_at");
        var createdByColumn = databaseContext.WrapObjectName("created_by");
        var updatedAtColumn = databaseContext.WrapObjectName("updated_at");
        var updatedByColumn = databaseContext.WrapObjectName("updated_by");
        sqlContainer.Query.AppendFormat("DROP TABLE IF EXISTS {0}", tableName);
        try
        {
            await sqlContainer.ExecuteNonQueryAsync();
        }
        catch
        {
            // Table did not exist, ignore
        }

        sqlContainer.Clear();
        sqlContainer.Query.AppendFormat(@"
-- Create table
CREATE TABLE {0} (
    {1} SERIAL PRIMARY KEY,
    {2} VARCHAR(100) NOT NULL,
    {3} VARCHAR(1000) NOT NULL,
    {4} INT NOT NULL,
    {5} BOOLEAN NOT NULL,
    {6} TIMESTAMP NOT NULL,
    {7} VARCHAR(100) NOT NULL,
    {8} TIMESTAMP NOT NULL,
    {9} VARCHAR(100) NOT NULL
);
", tableName, idColumn, nameColumn, descriptionColumn, valueColumn, isActiveColumn, createdAtColumn,
            createdByColumn, updatedAtColumn, updatedByColumn);
        try
        {
            await sqlContainer.ExecuteNonQueryAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message + "\n --- Continuing anyways");
        }
    }

    protected override async Task RunAdditionalTestsAsync()
    {
        // GENERATED ALWAYS AS IDENTITY was introduced in PostgreSQL 10
        if (context.DataSourceInfo.ParsedVersion == null || context.DataSourceInfo.ParsedVersion.Major >= 10)
        {
            await TestExplicitIdentityUpsertAsync();
        }
        else
        {
            CheckSkip($"  [IdentityUpsert] Skipped GENERATED ALWAYS AS IDENTITY test on PostgreSQL {context.DataSourceInfo.DatabaseProductVersion} (requires PostgreSQL 10+)");
        }
    }

    private async Task TestExplicitIdentityUpsertAsync()
    {
        var tableName = context.WrapObjectName("postgres_explicit_identity_upsert");
        await using var container = context.CreateSqlContainer();

        container.Query.Append($"DROP TABLE IF EXISTS {tableName}");
        await container.ExecuteNonQueryAsync();

        try
        {
            container.Clear();
            container.Query.Append($"CREATE TABLE {tableName} (");
            container.Query.Append($"{context.WrapObjectName("id")} INTEGER GENERATED ALWAYS AS IDENTITY PRIMARY KEY, ");
            container.Query.Append($"{context.WrapObjectName("value")} VARCHAR(100) NOT NULL)");
            await container.ExecuteNonQueryAsync();

            var gateway = new TableGateway<ExplicitIdentityUpsertRow, int>(context);
            var row = new ExplicitIdentityUpsertRow { Id = 42, Value = "before" };
            await gateway.UpsertAsync(row);
            row.Value = "after";
            await gateway.UpsertAsync(row);

            var loaded = await gateway.RetrieveOneAsync(42);
            if (loaded?.Value != "after")
            {
                throw new Exception("PostgreSQL explicit identity upsert did not persist the updated value");
            }

            CheckOk("PostgreSQL explicit GENERATED ALWAYS identity upsert: OK");
        }
        finally
        {
            container.Clear();
            container.Query.Append($"DROP TABLE IF EXISTS {tableName}");
            await container.ExecuteNonQueryAsync();
        }
    }

    [Table("postgres_explicit_identity_upsert")]
    private class ExplicitIdentityUpsertRow
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("value", DbType.String)]
        public string Value { get; set; } = string.Empty;
    }
}
