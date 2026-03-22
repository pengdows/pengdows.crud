#region

using pengdows.crud;

#endregion

namespace testbed.Cockroach;

public class CockroachDbTestProvider : TestProvider
{
    public CockroachDbTestProvider(IDatabaseContext context, IServiceProvider serviceProvider) : base(context,
        serviceProvider)
    {
    }

    public override async Task CreateTable()
    {
        var databaseContext = _context;
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
}
