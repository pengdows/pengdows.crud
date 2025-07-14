#region

using pengdows.crud;

#endregion

namespace testbed.Cockroach;

public class CockroachDbTestProvider : TestProvider
{
    private readonly IDatabaseContext _context;

    public CockroachDbTestProvider(IDatabaseContext context, IServiceProvider serviceProvider) : base(context,
        serviceProvider)
    {
        _context = context;
    }

    public override async Task CreateTable()
    {
        var databaseContext = _context;
        var sqlContainer = databaseContext.CreateSqlContainer();
        var qp = databaseContext.QuotePrefix;
        var qs = databaseContext.QuoteSuffix;
        sqlContainer.Query.AppendFormat(@"DROP TABLE IF EXISTS {0}test_table{1}", qp, qs);
        try
        {
            await sqlContainer.ExecuteNonQueryAsync();
        }
        catch
        {
            // Table did not exist, ignore
        }

        sqlContainer.Query.Clear();
        sqlContainer.Query.AppendFormat(@"
-- Create table
CREATE TABLE {0}test_table{1} (
    {0}id{1} SERIAL PRIMARY KEY,
    {0}name{1} VARCHAR(100) NOT NULL,
    {0}description{1} VARCHAR(1000) NOT NULL,
    {0}created_at{1} TIMESTAMP NOT NULL,
    {0}created_by{1} VARCHAR(100) NOT NULL,
    {0}updated_at{1} TIMESTAMP NOT NULL,
    {0}updated_by{1} VARCHAR(100) NOT NULL
);
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