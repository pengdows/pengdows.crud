using System.Data;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;

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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        SnowflakeDebugLog.Log("[Snowflake] CreateTable start");

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
        Console.WriteLine($"[Snowflake] CreateTable completed in {sw.ElapsedMilliseconds}ms");
        SnowflakeDebugLog.Log($"[Snowflake] CreateTable completed in {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>
    /// Creates a minimal scalar UDF, calls it inline in a SELECT, asserts the result,
    /// then drops it. Exercises the full UDF lifecycle on a real Snowflake account.
    /// </summary>
    protected override async Task TestScalarUdf()
    {
        var sc = _context.CreateSqlContainer();

        // Create a scalar UDF that adds 1 to its integer argument.
        // Snowflake SQL UDF syntax: AS $$ expression $$.
        sc.Query.Append(
            "CREATE OR REPLACE FUNCTION udf_pengdows_add_one(n INTEGER)\n" +
            "  RETURNS INTEGER\n" +
            "  LANGUAGE SQL\n" +
            "AS $$\n" +
            "  n + 1\n" +
            "$$");
        await sc.ExecuteNonQueryAsync();

        // Invoke inline in SELECT with a bound parameter (verifies parameterised UDF calls work).
        sc.Clear();
        sc.Query.Append("SELECT udf_pengdows_add_one(");
        var p = sc.AddParameterWithValue("p0", DbType.Int32, 41);
        sc.Query.Append(sc.MakeParameterName(p));
        sc.Query.Append(")");
        var result = await sc.ExecuteScalarOrNullAsync<long>();
        if (result != 42)
        {
            throw new Exception($"[Snowflake UDF] Expected 42 but got {result}");
        }

        // Clean up — Snowflake DROP FUNCTION requires the full parameter type signature.
        sc.Clear();
        sc.Query.Append("DROP FUNCTION udf_pengdows_add_one(INTEGER)");
        await sc.ExecuteNonQueryAsync();
    }
}
