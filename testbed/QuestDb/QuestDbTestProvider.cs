#region

using pengdows.crud;

#endregion

namespace testbed.QuestDb;

public class QuestDbTestProvider : TestProvider
{
    public QuestDbTestProvider(IDatabaseContext context, IServiceProvider serviceProvider) : base(context,
        serviceProvider)
    {
    }

    /// <summary>
    /// QuestDB DDL is limited — reserved-word column names can break even when quoted.
    /// Skip the identifier quoting torture test.
    /// </summary>
    protected override Task TestIdentifierQuoting()
    {
        Console.WriteLine("  [QuestDB] Skipping identifier quoting test (limited DDL support)");
        return Task.CompletedTask;
    }

    public override async Task CreateTable()
    {
        var databaseContext = _context;
        var sqlContainer = databaseContext.CreateSqlContainer();
        var qp = databaseContext.QuotePrefix;
        var qs = databaseContext.QuoteSuffix;

        // QuestDB syntax for dropping and creating tables
        sqlContainer.Query.AppendFormat(@"DROP TABLE IF EXISTS {0}test_table{1}", qp, qs);
        try
        {
            await sqlContainer.ExecuteNonQueryAsync();
        }
        catch
        {
            // Ignore
        }

        sqlContainer.Clear();
        // QuestDB: Use long for id (no SERIAL), TIMESTAMP for time
        sqlContainer.Query.AppendFormat(@"
CREATE TABLE {0}test_table{1} (
    {0}id{1} LONG,
    {0}name{1} SYMBOL,
    {0}description{1} STRING,
    {0}value{1} INT,
    {0}is_active{1} BOOLEAN,
    {0}created_at{1} TIMESTAMP,
    {0}created_by{1} STRING,
    {0}updated_at{1} TIMESTAMP,
    {0}updated_by{1} STRING
) timestamp(created_at);
", qp, qs);
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
