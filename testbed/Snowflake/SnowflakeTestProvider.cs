using System.Data;
using pengdows.crud;
using pengdows.crud.enums;

namespace testbed.Snowflake;

public class SnowflakeTestProvider : TestProvider
{
    public SnowflakeTestProvider(IDatabaseContext context, IServiceProvider serviceProvider)
        : base(context, serviceProvider)
    {
    }

    public override async Task CreateTable()
    {
        var databaseContext = _context;
        var sqlContainer = databaseContext.CreateSqlContainer();
        var qp = databaseContext.QuotePrefix;
        var qs = databaseContext.QuoteSuffix;

        // Snowflake supports DROP TABLE IF EXISTS
        sqlContainer.Query.AppendFormat("DROP TABLE IF EXISTS {0}test_table{1}", qp, qs);
        try
        {
            await sqlContainer.ExecuteNonQueryAsync();
        }
        catch
        {
            // Ignore if table did not exist
        }

        sqlContainer.Clear();
        // Snowflake DDL: AUTOINCREMENT for identity, TIMESTAMP_NTZ for datetime, BOOLEAN for bool
        sqlContainer.Query.Append($@"
CREATE TABLE {qp}test_table{qs} (
    {qp}id{qs} BIGINT NOT NULL,
    {qp}name{qs} VARCHAR(100) NOT NULL,
    {qp}description{qs} VARCHAR(1000) NOT NULL,
    {qp}value{qs} INT NOT NULL,
    {qp}is_active{qs} BOOLEAN NOT NULL,
    {qp}created_at{qs} TIMESTAMP_NTZ NOT NULL,
    {qp}created_by{qs} VARCHAR(100) NOT NULL,
    {qp}updated_at{qs} TIMESTAMP_NTZ NOT NULL,
    {qp}updated_by{qs} VARCHAR(100) NOT NULL,
    PRIMARY KEY ({qp}id{qs})
)");
        await sqlContainer.ExecuteNonQueryAsync();
    }
}