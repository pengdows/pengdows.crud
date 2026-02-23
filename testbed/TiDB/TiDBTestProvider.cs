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
        var qp = _context.QuotePrefix;
        var qs = _context.QuoteSuffix;

        sqlContainer.Query.AppendFormat("DROP TABLE IF EXISTS {0}test_table{1}", qp, qs);
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
CREATE TABLE {0}test_table{1} (
    {0}id{1}          BIGINT       NOT NULL,
    {0}name{1}        VARCHAR(100) NOT NULL,
    {0}description{1} VARCHAR(1000) NOT NULL,
    {0}value{1}       INT          NOT NULL,
    {0}is_active{1}   BOOLEAN      NOT NULL,
    {0}created_at{1}  DATETIME     NOT NULL,
    {0}created_by{1}  VARCHAR(100) NOT NULL,
    {0}updated_at{1}  DATETIME     NOT NULL,
    {0}updated_by{1}  VARCHAR(100) NOT NULL,
    PRIMARY KEY ({0}id{1})
);", qp, qs);

        await sqlContainer.ExecuteNonQueryAsync();
    }
}