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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        SnowflakeDebugLog.Log("[Snowflake] CreateTable start");

        // Snowflake supports DROP TABLE IF EXISTS
        sqlContainer.Query.AppendFormat("DROP TABLE IF EXISTS {0}", tableName);
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
CREATE TABLE {tableName} (
    {idColumn} BIGINT NOT NULL,
    {nameColumn} VARCHAR(100) NOT NULL,
    {descriptionColumn} VARCHAR(1000) NOT NULL,
    {valueColumn} INT NOT NULL,
    {isActiveColumn} BOOLEAN NOT NULL,
    {createdAtColumn} TIMESTAMP_NTZ NOT NULL,
    {createdByColumn} VARCHAR(100) NOT NULL,
    {updatedAtColumn} TIMESTAMP_NTZ NOT NULL,
    {updatedByColumn} VARCHAR(100) NOT NULL,
    PRIMARY KEY ({idColumn})
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
        var udfName = _context.WrapObjectName("udf_pengdows_add_one");

        // Create a scalar UDF that adds 1 to its integer argument.
        // Snowflake SQL UDF syntax: AS $$ expression $$.
        sc.Query.Append(
            $"CREATE OR REPLACE FUNCTION {udfName}(n INTEGER)\n" +
            "  RETURNS INTEGER\n" +
            "  LANGUAGE SQL\n" +
            "AS $$\n" +
            "  n + 1\n" +
            "$$");
        await sc.ExecuteNonQueryAsync();

        // Invoke inline in SELECT with a bound parameter (verifies parameterised UDF calls work).
        sc.Clear();
        sc.Query.Append($"SELECT {udfName}(");
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
        sc.Query.Append($"DROP FUNCTION {udfName}(INTEGER)");
        await sc.ExecuteNonQueryAsync();
    }
}
