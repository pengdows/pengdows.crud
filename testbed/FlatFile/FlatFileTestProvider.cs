using pengdows.crud;

namespace testbed.FlatFile;

public class FlatFileTestProvider : TestProvider
{
    public FlatFileTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
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
        // pengdows.flatfile parses only ISO SQL - "DATETIME" (TestProvider's generic default) is
        // rejected as vendor syntax; "TIMESTAMP" is the standard form. See
        // pengdows.flatfile/CLAUDE.md's "Standards posture" and SQL_STANDARDS_STATUS.md.
        sqlContainer.Query.AppendFormat(@"
CREATE TABLE {0} (
    {1} BIGINT NOT NULL,
    {2} VARCHAR(100) NOT NULL,
    {3} VARCHAR(1000) NOT NULL,
    {4} INT NOT NULL,
    {5} BOOLEAN NOT NULL,
    {6} TIMESTAMP NOT NULL,
    {7} VARCHAR(100) NOT NULL,
    {8} TIMESTAMP NOT NULL,
    {9} VARCHAR(100) NOT NULL,
    PRIMARY KEY ({1})
);", tableName, idColumn, nameColumn, descriptionColumn, valueColumn, isActiveColumn, createdAtColumn,
            createdByColumn, updatedAtColumn, updatedByColumn);

        await sqlContainer.ExecuteNonQueryAsync();
    }
}
