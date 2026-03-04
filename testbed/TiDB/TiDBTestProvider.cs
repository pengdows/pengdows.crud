using pengdows.crud;

namespace testbed.TiDB;

public class TiDBTestProvider : TestProvider
{
    public TiDBTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    public override async Task CreateTable()
    {
        var sqlContainer = _context.CreateSqlContainer();
        var tableName = _context.WrapObjectName("test_table");
        var idColumn = _context.WrapObjectName("id");
        var nameColumn = _context.WrapObjectName("name");
        var descriptionColumn = _context.WrapObjectName("description");
        var valueColumn = _context.WrapObjectName("value");
        var isActiveColumn = _context.WrapObjectName("is_active");
        var createdAtColumn = _context.WrapObjectName("created_at");
        var createdByColumn = _context.WrapObjectName("created_by");
        var updatedAtColumn = _context.WrapObjectName("updated_at");
        var updatedByColumn = _context.WrapObjectName("updated_by");

        sqlContainer.Query.AppendFormat("DROP TABLE IF EXISTS {0}", tableName);
        try
        {
            await sqlContainer.ExecuteNonQueryAsync();
        }
        catch
        {
            // Table did not exist — ignore.
        }

        sqlContainer.Clear();
        // TiDB is MySQL wire-compatible.  Use BIGINT PK with explicit IDs (no AUTO_INCREMENT)
        // so the shared Interlocked.Increment ID strategy in TestProvider works correctly.
        sqlContainer.Query.AppendFormat(@"
CREATE TABLE {0} (
    {1} BIGINT NOT NULL,
    {2} VARCHAR(100) NOT NULL,
    {3} VARCHAR(1000) NOT NULL,
    {4} INT NOT NULL,
    {5} BOOLEAN NOT NULL,
    {6} DATETIME NOT NULL,
    {7} VARCHAR(100) NOT NULL,
    {8} DATETIME NOT NULL,
    {9} VARCHAR(100) NOT NULL,
    PRIMARY KEY ({1})
);", tableName, idColumn, nameColumn, descriptionColumn, valueColumn, isActiveColumn, createdAtColumn,
            createdByColumn, updatedAtColumn, updatedByColumn);

        await sqlContainer.ExecuteNonQueryAsync();
    }
}
