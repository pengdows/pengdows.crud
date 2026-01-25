#region

using pengdows.crud;

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

        sqlContainer.Clear();
        sqlContainer.Query.AppendFormat(@"
-- Create table
CREATE TABLE {0}test_table{1} (
    {0}id{1} SERIAL PRIMARY KEY,
    {0}name{1} VARCHAR(100) NOT NULL,
    {0}description{1} VARCHAR(1000) NOT NULL,
    {0}value{1} INT NOT NULL,
    {0}is_active{1} BOOLEAN NOT NULL,
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