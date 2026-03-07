#region

using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using testbed.Snowflake;

#endregion

namespace testbed;

public class TestProvider : IAsyncTestProvider
{
    private static long _nextId;

    protected readonly IDatabaseContext _context;
    protected readonly TableGateway<TestTable, long> _helper;

    private int _checksPassed;
    private int _checksSkipped;

    public int ChecksPassed => _checksPassed;
    public int ChecksSkipped => _checksSkipped;

    protected void CheckOk(string message)
    {
        Console.WriteLine(message);
        _checksPassed++;
    }

    protected void CheckSkip(string message)
    {
        Console.WriteLine(message);
        _checksSkipped++;
    }

    public TestProvider(IDatabaseContext databaseContext, IServiceProvider serviceProvider)
    {
        _context = databaseContext;
        var resolver = serviceProvider.GetService<IAuditValueResolver>() ??
                       new TestAuditValueResolver("system");
        _helper = new TableGateway<TestTable, long>(databaseContext, resolver);
    }


    public async Task RunTest()
    {
        var totalSw = Stopwatch.StartNew();
        var stepSw = new Stopwatch();
        Console.WriteLine($"[{_context.Product}] Starting test run");
        try
        {
            stepSw.Restart();
            Console.WriteLine("Running Create table");
            SnowflakeStep("Create table: start");
            await CreateTable();
            Console.WriteLine($"  Create table: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Create table: done in {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running Insert rows");
            SnowflakeStep("Insert rows: start");
            var before = await CountTestRows();
            var id = await InsertTestRows();
            var afterInsert = await CountTestRows();
            Console.WriteLine($"  Insert rows: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Insert rows: done in {stepSw.ElapsedMilliseconds}ms");
            if (afterInsert != before + 1)
            {
                throw new Exception("Insert did not affect expected row count");
            }

            stepSw.Restart();
            Console.WriteLine("Running retrieve rows");
            SnowflakeStep("Retrieve rows: start");
            var obj = await RetrieveRows(id);
            Console.WriteLine($"  Retrieve rows: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Retrieve rows: done in {stepSw.ElapsedMilliseconds}ms");
            if (obj.Id != id)
            {
                throw new Exception("Retrieved row did not match inserted id");
            }

            stepSw.Restart();
            Console.WriteLine("Running delete rows");
            SnowflakeStep("Delete rows: start");
            await DeletedRow(obj);
            var afterDelete = await CountTestRows();
            Console.WriteLine($"  Delete rows: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Delete rows: done in {stepSw.ElapsedMilliseconds}ms");
            if (afterDelete != before)
            {
                throw new Exception("Delete did not affect expected row count");
            }

            stepSw.Restart();
            Console.WriteLine("Running Transaction rows");
            SnowflakeStep("Transactions: start");
            await TestTransactions();
            Console.WriteLine($"  Transactions: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Transactions: done in {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running stored procedure return value test");
            SnowflakeStep("Stored procedure: start");
            await TestStoredProcReturnValue();
            Console.WriteLine($"  Stored procedure: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Stored procedure: done in {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running scalar UDF test");
            SnowflakeStep("Scalar UDF: start");
            await TestScalarUdf();
            Console.WriteLine($"  Scalar UDF: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Scalar UDF: done in {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running parameter binding");
            SnowflakeStep("Parameter binding: start");
            await TestParameterBinding();
            Console.WriteLine($"  Parameter binding: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Parameter binding: done in {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running row round-trip fidelity");
            SnowflakeStep("Row round-trip: start");
            await TestRowRoundTrip();
            Console.WriteLine($"  Row round-trip: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Row round-trip: done in {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running extended transactions");
            SnowflakeStep("Extended transactions: start");
            await TestExtendedTransactions();
            Console.WriteLine($"  Extended transactions: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Extended transactions: done in {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running concurrency");
            SnowflakeStep("Concurrency: start");
            await TestConcurrency();
            Console.WriteLine($"  Concurrency: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Concurrency: done in {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running command reuse");
            SnowflakeStep("Command reuse: start");
            await TestCommandReuse();
            Console.WriteLine($"  Command reuse: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Command reuse: done in {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running capability probes");
            SnowflakeStep("Capability probes: start");
            await TestCapabilityProbes();
            Console.WriteLine($"  Capability probes: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Capability probes: done in {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running error mapping");
            SnowflakeStep("Error mapping: start");
            await TestErrorMapping();
            Console.WriteLine($"  Error mapping: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Error mapping: done in {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running identifier quoting");
            SnowflakeStep("Identifier quoting: start");
            await TestIdentifierQuoting();
            Console.WriteLine($"  Identifier quoting: {stepSw.ElapsedMilliseconds}ms");
            SnowflakeStep($"Identifier quoting: done in {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running paging");
            await TestPaging();
            Console.WriteLine($"  Paging: {stepSw.ElapsedMilliseconds}ms");

            stepSw.Restart();
            Console.WriteLine("Running pool isolation");
            await TestPoolIsolation();
            Console.WriteLine($"  Pool isolation: {stepSw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to complete tests successfully: " + ex.Message + "\n" + ex.StackTrace);
            throw;
        }
        finally
        {
            Console.WriteLine($"[{_context.Product}] Test run completed in {totalSw.ElapsedMilliseconds}ms");
        }
    }

    private void SnowflakeStep(string message)
    {
        if (_context.Product != SupportedDatabase.Snowflake)
        {
            return;
        }

        SnowflakeDebugLog.Log($"[Snowflake][Step] {message}");
    }

    protected virtual async Task TestTransactions()
    {
        var count = await CountTestRows();

        await TestRollbackTransaction();
        var count2 = await CountTestRows();
        if (count != count2)
        {
            Console.WriteLine("Failed to rollback transactions: " + count);
            throw new Exception("Failed to rollback transactions: " + count);
        }

        await TestCommitTransaction();
        var count3 = await CountTestRows();
        if (count3 == count2)
        {
            Console.WriteLine("Failed to commit transactions: " + count);
            throw new Exception("Failed to commit transactions: " + count);
        }
    }

    private async Task TestCommitTransaction()
    {
        await using var transaction = _context.BeginTransaction();
        var id = await InsertTestRows(transaction);
        await CountTestRows(transaction);
        transaction.Commit();
    }

    private async Task TestRollbackTransaction()
    {
        await using var transaction = _context.BeginTransaction();
        var id = await InsertTestRows(transaction);
        await CountTestRows(transaction);
        transaction.Rollback();
    }

    public virtual async Task<int> CountTestRows(IDatabaseContext? db = null)
    {
        var ctx = db ?? _context;
        var sc = ctx.CreateSqlContainer();
        sc.Query.AppendFormat("SELECT COUNT(*) FROM {0}", _helper.WrappedTableName);
        var count = await sc.ExecuteScalarOrNullAsync<int>();
        Console.WriteLine($"Count of rows: {count}");
        return count;
    }

    public virtual async Task CreateTable()
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
        var dateType = GetDateTimeType(databaseContext.Product);
        var intType = GetIntType(databaseContext.Product);
        var longType = GetLongType(databaseContext.Product);
        var boolType = GetBooleanType(databaseContext.Product);
        sqlContainer.Query.Append($@"
CREATE TABLE {tableName} (
    {idColumn} {longType} NOT NULL,
    {nameColumn} VARCHAR(100) NOT NULL,
    {descriptionColumn} VARCHAR(1000) NOT NULL,
    {valueColumn} {intType} NOT NULL,
    {isActiveColumn} {boolType} NOT NULL,
    {createdAtColumn} {dateType} NOT NULL,
    {createdByColumn} VARCHAR(100) NOT NULL,
    {updatedAtColumn} {dateType} NOT NULL,
    {updatedByColumn} VARCHAR(100) NOT NULL,
    PRIMARY KEY ({idColumn})
);");
        await sqlContainer.ExecuteNonQueryAsync();
    }

    protected async Task<long> InsertTestRows(IDatabaseContext? db = null)
    {
        var ctx = db ?? _context;
        var name = ctx is TransactionContext ? NameEnum.Test2 : NameEnum.Test;
        var id = Interlocked.Increment(ref _nextId);
        var t = new TestTable
        {
            Id = id,
            Name = name,
            Description = ctx.GenerateRandomName()
        };
        var ok = await _helper.CreateAsync(t, ctx);
        if (!ok)
        {
            throw new Exception("Insert failed");
        }

        return t.Id;
    }

    private async Task<TestTable> RetrieveRows(long id, IDatabaseContext? db = null)
    {
        var ctx = db ?? _context;
        return await _helper.RetrieveOneAsync(id, ctx)
               ?? throw new Exception($"Row {id} not found");
    }

    protected virtual async Task DeletedRow(TestTable t, IDatabaseContext? db = null)
    {
        var ctx = db ?? _context;
        var count = await _helper.DeleteAsync(t.Id, ctx);
        if (count != 1)
        {
            throw new Exception("Delete failed");
        }
    }

    private static string GetDateTimeType(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.PostgreSql => "TIMESTAMP WITH TIME ZONE",
            _ => "DATETIME"
        };
    }

    private static string GetIntType(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.Sqlite => "INTEGER",
            SupportedDatabase.Oracle => "NUMBER(10)",
            SupportedDatabase.Firebird => "INTEGER",
            _ => "INT"
        };
    }

    private static string GetLongType(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.Sqlite => "INTEGER",
            SupportedDatabase.Oracle => "NUMBER(19)",
            SupportedDatabase.Firebird => "BIGINT",
            _ => "BIGINT"
        };
    }

    private static string GetBooleanType(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.PostgreSql => "BOOLEAN",
            SupportedDatabase.CockroachDb => "BOOLEAN",
            SupportedDatabase.YugabyteDb => "BOOLEAN",
            SupportedDatabase.Sqlite => "INTEGER",
            SupportedDatabase.DuckDB => "BOOLEAN",
            SupportedDatabase.Firebird => "SMALLINT",
            SupportedDatabase.Oracle => "NUMBER(1)",
            SupportedDatabase.MySql => "BOOLEAN",
            SupportedDatabase.MariaDb => "BOOLEAN",
            SupportedDatabase.TiDb => "BOOLEAN",
            SupportedDatabase.SqlServer => "BIT",
            _ => "BOOLEAN"
        };
    }

    private static string GetDecimalType(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.Sqlite => "NUMERIC(18,6)",
            SupportedDatabase.Oracle => "NUMBER(18,6)",
            _ => "DECIMAL(18,6)"
        };
    }

    private static string GetBinaryType(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.SqlServer => "VARBINARY(64)",
            SupportedDatabase.PostgreSql => "BYTEA",
            SupportedDatabase.CockroachDb => "BYTEA",
            SupportedDatabase.YugabyteDb => "BYTEA",
            SupportedDatabase.Oracle => "RAW(64)",
            SupportedDatabase.Firebird => "BLOB",
            SupportedDatabase.Sqlite => "BLOB",
            SupportedDatabase.DuckDB => "BLOB",
            SupportedDatabase.MySql => "BLOB",
            SupportedDatabase.MariaDb => "BLOB",
            SupportedDatabase.TiDb => "BLOB",
            SupportedDatabase.Snowflake => "VARBINARY",
            _ => "BLOB"
        };
    }

    private static string GetTextType(SupportedDatabase product, int length)
    {
        return product switch
        {
            SupportedDatabase.SqlServer => $"NVARCHAR({length})",
            SupportedDatabase.Oracle => $"NVARCHAR2({length})",
            SupportedDatabase.Sqlite => "TEXT",
            _ => $"VARCHAR({length})"
        };
    }

    private static byte[] BuildBinaryPayload(int length)
    {
        var bytes = new byte[length];
        var value = 1;
        for (var i = 0; i < bytes.Length; i++)
        {
            while (value is 0 or 0x1A or 0x27 or 0x5C)
            {
                value++;
            }

            bytes[i] = (byte)value;
            value++;
        }

        return bytes;
    }

    private static bool SupportsGuidBinding(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.SqlServer => true,
            SupportedDatabase.PostgreSql => true,
            SupportedDatabase.CockroachDb => true,
            SupportedDatabase.YugabyteDb => true,
            SupportedDatabase.DuckDB => true,
            SupportedDatabase.Sqlite => true,
            SupportedDatabase.Oracle => true,
            SupportedDatabase.Firebird => true,
            _ => false
        };
    }

    private static bool SupportsDateTimeOffsetBinding(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.SqlServer => true,
            SupportedDatabase.PostgreSql => true,
            SupportedDatabase.CockroachDb => true,
            SupportedDatabase.YugabyteDb => true,
            SupportedDatabase.DuckDB => true,
            SupportedDatabase.Oracle => true,
            _ => false
        };
    }


    private static string GetGuidType(SupportedDatabase product, bool supportsGuid)
    {
        if (!supportsGuid)
        {
            return GetTextType(product, 36);
        }

        return product switch
        {
            SupportedDatabase.SqlServer => "UNIQUEIDENTIFIER",
            SupportedDatabase.PostgreSql => "UUID",
            SupportedDatabase.CockroachDb => "UUID",
            SupportedDatabase.YugabyteDb => "UUID",
            SupportedDatabase.DuckDB => "UUID",
            SupportedDatabase.Oracle => "VARCHAR2(36)",
            SupportedDatabase.Sqlite => "TEXT",
            SupportedDatabase.Firebird => "CHAR(16) CHARACTER SET OCTETS",
            _ => "UUID"
        };
    }

    private static string GetDateTimeOffsetType(SupportedDatabase product, bool supportsDateTimeOffset)
    {
        if (!supportsDateTimeOffset)
        {
            return GetTextType(product, 64);
        }

        return product switch
        {
            SupportedDatabase.SqlServer => "DATETIMEOFFSET(7)",
            SupportedDatabase.PostgreSql => "TIMESTAMP WITH TIME ZONE",
            SupportedDatabase.CockroachDb => "TIMESTAMP WITH TIME ZONE",
            SupportedDatabase.YugabyteDb => "TIMESTAMP WITH TIME ZONE",
            SupportedDatabase.DuckDB => "TIMESTAMP WITH TIME ZONE",
            SupportedDatabase.Oracle => "TIMESTAMP WITH TIME ZONE",
            _ => "TIMESTAMP WITH TIME ZONE"
        };
    }

    private static string GetExpectedParameterMarker(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.PostgreSql => "@",
            SupportedDatabase.CockroachDb => "@",
            SupportedDatabase.YugabyteDb => "@",
            SupportedDatabase.Snowflake => ":",
            SupportedDatabase.DuckDB => "$",
            SupportedDatabase.Oracle => ":",
            _ => "@"
        };
    }

    private static bool RequiresUtcDateTimeOffset(SupportedDatabase product)
    {
        return product switch
        {
            SupportedDatabase.PostgreSql => true,
            SupportedDatabase.CockroachDb => true,
            SupportedDatabase.YugabyteDb => true,
            SupportedDatabase.DuckDB => true,
            _ => false
        };
    }

    private static DateTimeOffset NormalizeDateTimeOffsetForProvider(SupportedDatabase product, DateTimeOffset value)
    {
        return RequiresUtcDateTimeOffset(product) ? value.ToUniversalTime() : value;
    }

    private async Task TestStoredProcReturnValue()
    {
        if (_context.ProcWrappingStyle == ProcWrappingStyle.None)
        {
            CheckSkip($"  [StoredProc] Stored procedures not supported by {_context.Product} — skip");
            return;
        }

        var sc = _context.CreateSqlContainer();
        switch (_context.Product)
        {
            case SupportedDatabase.SqlServer:
                var sqlServerProcName = _context.WrapObjectName("ReturnFive");
                var sqlServerSchema = _context.WrapObjectName("dbo");
                var sqlServerQualifiedProcName = sqlServerSchema + _context.CompositeIdentifierSeparator +
                                                 sqlServerProcName;
                sc.Query.Append(
                    $"CREATE OR ALTER PROCEDURE {sqlServerQualifiedProcName} AS BEGIN RETURN 5 END");
                await sc.ExecuteNonQueryAsync();

                sc.Clear();
                sc.Query.Append("dbo.ReturnFive");
                var wrapped = sc.WrapForStoredProc(ExecutionType.Read, captureReturn: true);
                sc.Clear();
                sc.Query.Append(wrapped);
                var value = await sc.ExecuteScalarOrNullAsync<int>();
                if (value != 5)
                {
                    throw new Exception($"Expected return value 5 but got {value}");
                }

                sc.Clear();
                sc.Query.Append($"DROP PROCEDURE {sqlServerQualifiedProcName}");
                await sc.ExecuteNonQueryAsync();
                break;

            case SupportedDatabase.Snowflake:
                var snowflakeProcName = _context.WrapObjectName("sp_pengdows_test");
                // Create a minimal stored procedure that returns the current timestamp as VARCHAR.
                // Snowflake SQL Scripting syntax: AS $$ BEGIN RETURN ...; END $$.
                sc.Query.Append(
                    $"CREATE OR REPLACE PROCEDURE {snowflakeProcName}()\n" +
                    "  RETURNS VARCHAR\n" +
                    "  LANGUAGE SQL\n" +
                    "AS $$\n" +
                    "  BEGIN\n" +
                    "    RETURN CURRENT_TIMESTAMP()::VARCHAR;\n" +
                    "  END\n" +
                    "$$");
                await sc.ExecuteNonQueryAsync();

                // Call via WrapForStoredProc — generates: CALL "sp_pengdows_test"()
                sc.Clear();
                sc.Query.Append("sp_pengdows_test");
                var snowflakeWrapped = sc.WrapForStoredProc(ExecutionType.Read);
                sc.Clear();
                sc.Query.Append(snowflakeWrapped);
                var snowflakeResult = await sc.ExecuteScalarOrNullAsync<string>();
                if (string.IsNullOrWhiteSpace(snowflakeResult))
                {
                    throw new Exception("Snowflake stored proc returned null or empty");
                }

                sc.Clear();
                sc.Query.Append($"DROP PROCEDURE {snowflakeProcName}()");
                await sc.ExecuteNonQueryAsync();

                // Verify captureReturn is not supported (Snowflake uses CALL, not EXEC with return)
                sc.Clear();
                sc.Query.Append("dummy_proc");
                try
                {
                    sc.WrapForStoredProc(ExecutionType.Read, captureReturn: true);
                    throw new Exception("Expected NotSupportedException for captureReturn on Snowflake");
                }
                catch (NotSupportedException)
                {
                    // Expected path
                }

                break;

            case SupportedDatabase.MySql:
            case SupportedDatabase.AuroraMySql:
            case SupportedDatabase.MariaDb:
            {
                var mysqlProcName = _context.WrapObjectName("sp_pengdows_test");
                // MySQL/MariaDB: CALL `proc_name`() — proc body uses BEGIN...END with SELECT.
                // MySqlConnector handles CREATE PROCEDURE with BEGIN...END as a single statement;
                // no DELIMITER change needed over ADO.NET.
                //
                // Note: TiDB identifies itself as MySQL but its Go AST parser does not support
                // stored procedure DDL (*ast.ProcedureInfo is unimplemented). We catch that
                // error and skip gracefully so TiDB does not fail the run.
                sc.Query.Append(
                    $"CREATE PROCEDURE {mysqlProcName}()\n" +
                    "BEGIN\n" +
                    "  SELECT 42;\n" +
                    "END");
                await sc.ExecuteNonQueryAsync();

                // CALL `sp_pengdows_test`() — result set contains one row with value 42.
                sc.Clear();
                sc.Query.Append("sp_pengdows_test");
                var mysqlWrapped = sc.WrapForStoredProc(ExecutionType.Write);
                sc.Clear();
                sc.Query.Append(mysqlWrapped);
                var mysqlResult = await sc.ExecuteScalarOrNullAsync<int>();
                if (mysqlResult != 42)
                {
                    throw new Exception($"[MySQL/MariaDB proc] Expected 42 but got {mysqlResult}");
                }

                sc.Clear();
                sc.Query.Append($"DROP PROCEDURE {mysqlProcName}");
                await sc.ExecuteNonQueryAsync();
                break;
            }

            case SupportedDatabase.PostgreSql:
            case SupportedDatabase.AuroraPostgreSql:
            case SupportedDatabase.CockroachDb:
            case SupportedDatabase.YugabyteDb:
            {
                var pgFunctionName = _context.WrapObjectName("fn_pengdows_test");
                // PostgreSQL: Read path → SELECT * FROM "fn_name"(); Write path → CALL "proc_name"().
                // Use a SQL function (CREATE OR REPLACE FUNCTION) which supports SELECT * FROM invocation
                // and is compatible across PostgreSQL, CockroachDB (22.2+), and YugabyteDB.
                sc.Query.Append(
                    $"CREATE OR REPLACE FUNCTION {pgFunctionName}()\n" +
                    "RETURNS INTEGER\n" +
                    "LANGUAGE SQL\n" +
                    "AS $$\n" +
                    "  SELECT 42;\n" +
                    "$$");
                await sc.ExecuteNonQueryAsync();

                // SELECT * FROM "fn_pengdows_test"() — returns one row, one column: 42.
                sc.Clear();
                sc.Query.Append("fn_pengdows_test");
                var pgWrapped = sc.WrapForStoredProc(ExecutionType.Read);
                sc.Clear();
                sc.Query.Append(pgWrapped);
                var pgResult = await sc.ExecuteScalarOrNullAsync<int>();
                if (pgResult != 42)
                {
                    throw new Exception($"[PostgreSQL func] Expected 42 but got {pgResult}");
                }

                sc.Clear();
                sc.Query.Append($"DROP FUNCTION {pgFunctionName}()");
                await sc.ExecuteNonQueryAsync();
                break;
            }

            case SupportedDatabase.Oracle:
            {
                var oracleProcName = _context.WrapObjectName("sp_pengdows_test");
                // Oracle: BEGIN "proc_name"; END; (anonymous PL/SQL block invocation).
                // Oracle stored procs don't return result sets; verify the call executes without error.
                // Quote the proc name in CREATE so Oracle stores it case-sensitively as lowercase,
                // matching the quoted reference that WrapForStoredProc generates.
                sc.Query.Append(
                    $"CREATE OR REPLACE PROCEDURE {oracleProcName} AS BEGIN NULL; END;");
                await sc.ExecuteNonQueryAsync();

                sc.Clear();
                sc.Query.Append("sp_pengdows_test");
                var oracleWrapped = sc.WrapForStoredProc(ExecutionType.Write);
                sc.Clear();
                sc.Query.Append(oracleWrapped);
                await sc.ExecuteNonQueryAsync(); // Just verify it runs without error.

                sc.Clear();
                sc.Query.Append($"DROP PROCEDURE {oracleProcName}");
                await sc.ExecuteNonQueryAsync();
                break;
            }

            case SupportedDatabase.Firebird:
            {
                var firebirdProcName = _context.WrapObjectName("sp_pengdows_test");
                // Firebird: selectable proc (SUSPEND) → SELECT * FROM "proc_name" via Read path.
                // Identifiers must be quoted in DDL to preserve case so the quoted invocation
                // generated by WrapForStoredProc (which calls WrapObjectName) matches at runtime.
                sc.Query.Append(
                    $"CREATE OR ALTER PROCEDURE {firebirdProcName}\n" +
                    "RETURNS (result_val INTEGER)\n" +
                    "AS\n" +
                    "BEGIN\n" +
                    "  result_val = 42;\n" +
                    "  SUSPEND;\n" +
                    "END");
                await sc.ExecuteNonQueryAsync();

                // SELECT * FROM "sp_pengdows_test" — returns one row, result_val = 42.
                sc.Clear();
                sc.Query.Append("sp_pengdows_test");
                var fbWrapped = sc.WrapForStoredProc(ExecutionType.Read);
                sc.Clear();
                sc.Query.Append(fbWrapped);
                var fbResult = await sc.ExecuteScalarOrNullAsync<int>();
                if (fbResult != 42)
                {
                    throw new Exception($"[Firebird proc] Expected 42 but got {fbResult}");
                }

                sc.Clear();
                sc.Query.Append($"DROP PROCEDURE {firebirdProcName}");
                await sc.ExecuteNonQueryAsync();
                break;
            }

            default:
                throw new Exception(
                    $"[StoredProc] Unhandled database {_context.Product} in stored proc test — add a case or override ProcWrappingStyle.None.");
        }
    }

    /// <summary>
    /// Tests scalar UDF invocation inline in a SELECT statement.
    /// Default implementation is a no-op; override in database-specific providers
    /// where UDF creation and inline invocation is meaningful to exercise.
    /// </summary>
    protected virtual Task TestScalarUdf() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // § 5  Parameter binding semantics
    // -------------------------------------------------------------------------

    protected virtual async Task TestParameterBinding()
    {
        VerifyParameterMarker();

        // 5b: = NULL always returns 0 rows
        var sc = _context.CreateSqlContainer();
        sc.Query.AppendFormat("SELECT COUNT(*) FROM {0} WHERE {1} = {2}",
            _helper.WrappedTableName,
            _context.WrapObjectName("description"),
            sc.MakeParameterName("p0"));
        sc.AddParameterWithValue("p0", DbType.String, DBNull.Value);
        var nullCount = await sc.ExecuteScalarOrNullAsync<int>();
        if (nullCount != 0)
            throw new Exception($"[ParamBinding] NULL predicate: expected 0 rows, got {nullCount}");

        // IS NULL on a NOT NULL column → 0 rows
        sc.Clear();
        sc.Query.AppendFormat("SELECT COUNT(*) FROM {0} WHERE {1} IS NULL",
            _helper.WrappedTableName,
            _context.WrapObjectName("description"));
        var isNullCount = await sc.ExecuteScalarOrNullAsync<int>();
        if (isNullCount != 0)
            throw new Exception($"[ParamBinding] IS NULL predicate: expected 0 rows, got {isNullCount}");

        CheckOk("  [ParamBinding] NULL semantics: OK");

        // 5a: duplicate named parameter
        await TestDuplicateParameter();

        // 5c: type binding matrix — insert a known row, verify each column is queryable
        var id = Interlocked.Increment(ref _nextId);
        var knownDesc = _context.GenerateRandomName();
        var t = new TestTable
        {
            Id = id,
            Name = NameEnum.Test,
            Description = knownDesc,
            Value = 99,
            IsActive = true
        };
        await _helper.CreateAsync(t, _context);

        try
        {
            // Int32
            sc.Clear();
            sc.Query.AppendFormat("SELECT COUNT(*) FROM {0} WHERE {1} = {2}",
                _helper.WrappedTableName,
                _context.WrapObjectName("value"),
                sc.MakeParameterName("p0"));
            sc.AddParameterWithValue("p0", DbType.Int32, 99);
            var valueCount = await sc.ExecuteScalarOrNullAsync<int>();
            if (valueCount < 1)
                throw new Exception($"[ParamBinding] Int32 binding: expected ≥1, got {valueCount}");

            // String
            sc.Clear();
            sc.Query.AppendFormat("SELECT COUNT(*) FROM {0} WHERE {1} = {2}",
                _helper.WrappedTableName,
                _context.WrapObjectName("description"),
                sc.MakeParameterName("p0"));
            sc.AddParameterWithValue("p0", DbType.String, knownDesc);
            var strCount = await sc.ExecuteScalarOrNullAsync<int>();
            if (strCount != 1)
                throw new Exception($"[ParamBinding] String binding: expected 1, got {strCount}");

            // Int64 — query by id
            sc.Clear();
            sc.Query.AppendFormat("SELECT COUNT(*) FROM {0} WHERE {1} = {2}",
                _helper.WrappedTableName,
                _context.WrapObjectName("id"),
                sc.MakeParameterName("p0"));
            sc.AddParameterWithValue("p0", DbType.Int64, id);
            var idCount = await sc.ExecuteScalarOrNullAsync<int>();
            if (idCount != 1)
                throw new Exception($"[ParamBinding] Int64 binding: expected 1, got {idCount}");

            CheckOk("  [ParamBinding] Type matrix (int32, string, int64): OK");
        }
        finally
        {
            await CleanupTestRow(id);
        }

        await TestTypeBindingMatrix();
    }

    private void VerifyParameterMarker()
    {
        var rendered = _context.MakeParameterName("p0");
        var expected = GetExpectedParameterMarker(_context.Product);
        if (!rendered.StartsWith(expected, StringComparison.Ordinal))
        {
            throw new Exception(
                $"[ParamBinding] Parameter marker: expected prefix '{expected}', got '{rendered}'");
        }

        CheckOk($"  [ParamBinding] Marker prefix '{expected}': OK");
    }

    private async Task TestTypeBindingMatrix()
    {
        var supportsGuid = SupportsGuidBinding(_context.Product);
        var supportsDto = SupportsDateTimeOffsetBinding(_context.Product);

        await DropTableIfExistsAsync("binding_matrix");

        var table = _context.WrapObjectName("binding_matrix");
        var sc = _context.CreateSqlContainer();
        sc.Query.Append($@"
CREATE TABLE {table} (
    {_context.WrapObjectName("id")} {GetLongType(_context.Product)} NOT NULL,
    {_context.WrapObjectName("int_val")} {GetIntType(_context.Product)} NOT NULL,
    {_context.WrapObjectName("long_val")} {GetLongType(_context.Product)} NOT NULL,
    {_context.WrapObjectName("dec_val")} {GetDecimalType(_context.Product)} NOT NULL,
    {_context.WrapObjectName("bool_val")} {GetBooleanType(_context.Product)} NOT NULL,
    {_context.WrapObjectName("text_val")} {GetTextType(_context.Product, 200)} NOT NULL,
    {_context.WrapObjectName("dto_val")} {GetDateTimeOffsetType(_context.Product, supportsDto)},
    {_context.WrapObjectName("guid_val")} {GetGuidType(_context.Product, supportsGuid)},
    {_context.WrapObjectName("bin_val")} {GetBinaryType(_context.Product)},
    PRIMARY KEY ({_context.WrapObjectName("id")})
)");
        await sc.ExecuteNonQueryAsync();

        var id = Interlocked.Increment(ref _nextId);
        const int intVal = 123;
        const long longVal = 9_876_543_210L;
        const decimal decVal = 12345.678901m;
        const bool boolVal = true;
        const string textVal = "bind-test";
        var dtoVal = new DateTimeOffset(2026, 2, 21, 12, 34, 56, TimeSpan.FromHours(-5));
        var dtoWrite = NormalizeDateTimeOffsetForProvider(_context.Product, dtoVal);
        var guidVal = Uuid7Optimized.NewUuid7();
        var binVal = BuildBinaryPayload(64);

        if (supportsDto && dtoWrite.Offset != dtoVal.Offset)
        {
            Console.WriteLine(
                $"  [ParamBinding] DateTimeOffset normalized to UTC for {_context.Product}");
        }

        sc.Clear();
        sc.Query.Append($@"
INSERT INTO {table} (
    {_context.WrapObjectName("id")},
    {_context.WrapObjectName("int_val")},
    {_context.WrapObjectName("long_val")},
    {_context.WrapObjectName("dec_val")},
    {_context.WrapObjectName("bool_val")},
    {_context.WrapObjectName("text_val")},
    {_context.WrapObjectName("dto_val")},
    {_context.WrapObjectName("guid_val")},
    {_context.WrapObjectName("bin_val")}
) VALUES (
    {sc.MakeParameterName("p0")},
    {sc.MakeParameterName("p1")},
    {sc.MakeParameterName("p2")},
    {sc.MakeParameterName("p3")},
    {sc.MakeParameterName("p4")},
    {sc.MakeParameterName("p5")},
    {sc.MakeParameterName("p6")},
    {sc.MakeParameterName("p7")},
    {sc.MakeParameterName("p8")}
)");
        sc.AddParameterWithValue("p0", DbType.Int64, id);
        sc.AddParameterWithValue("p1", DbType.Int32, intVal);
        sc.AddParameterWithValue("p2", DbType.Int64, longVal);
        sc.AddParameterWithValue("p3", DbType.Decimal, decVal);
        sc.AddParameterWithValue("p4", DbType.Boolean, boolVal);
        sc.AddParameterWithValue("p5", DbType.String, textVal);
        if (supportsDto)
        {
            sc.AddParameterWithValue("p6", DbType.DateTimeOffset, dtoWrite);
        }
        else
        {
            sc.AddParameterWithValue("p6", DbType.String, dtoWrite.ToString("O"));
        }

        if (supportsGuid)
        {
            sc.AddParameterWithValue("p7", DbType.Guid, guidVal);
        }
        else
        {
            sc.AddParameterWithValue("p7", DbType.String, guidVal.ToString());
        }

        sc.AddParameterWithValue("p8", DbType.Binary, binVal);
        await sc.ExecuteNonQueryAsync();

        try
        {
            await AssertBindingCount("int_val", DbType.Int32, intVal);
            await AssertBindingCount("long_val", DbType.Int64, longVal);
            await AssertBindingCount("dec_val", DbType.Decimal, decVal);
            await AssertBindingCount("bool_val", DbType.Boolean, boolVal);
            await AssertBindingCount("text_val", DbType.String, textVal);

            if (supportsDto)
            {
                await AssertBindingCount("dto_val", DbType.DateTimeOffset, dtoWrite);
            }
            else
            {
                CheckSkip($"  [ParamBinding] DateTimeOffset binding: not supported by {_context.Product} — skip");
            }

            if (supportsGuid)
            {
                await AssertBindingCount("guid_val", DbType.Guid, guidVal);
            }
            else
            {
                CheckSkip($"  [ParamBinding] Guid binding: not supported by {_context.Product} — skip");
            }

            await AssertBindingCount("bin_val", DbType.Binary, binVal);
            CheckOk("  [ParamBinding] Type matrix (int/long/decimal/bool/string/dto/guid/binary): OK");
        }
        finally
        {
            await DropTableIfExistsAsync("binding_matrix");
        }

        async Task AssertBindingCount(string column, DbType type, object value)
        {
            var query = _context.CreateSqlContainer();
            query.Query.AppendFormat("SELECT COUNT(*) FROM {0} WHERE {1} = {2}",
                table,
                _context.WrapObjectName(column),
                query.MakeParameterName("p0"));
            query.AddParameterWithValue("p0", type, value);
            var count = await query.ExecuteScalarOrNullAsync<int>();
            if (count != 1)
            {
                throw new Exception(
                    $"[ParamBinding] {column} binding: expected 1, got {count}");
            }
        }
    }

    protected virtual async Task TestDuplicateParameter()
    {
        if (!_context.SupportsRepeatedNamedParameters)
        {
            CheckSkip("  [ParamBinding] Duplicate param: provider does not support repeated named parameters — skip");
            return;
        }

        var sc = _context.CreateSqlContainer();
        sc.Query.AppendFormat(
            "SELECT COUNT(*) FROM {0} WHERE {1} = {2} OR {3} = {2}",
            _helper.WrappedTableName,
            _context.WrapObjectName("created_by"),
            sc.MakeParameterName("p0"),
            _context.WrapObjectName("updated_by"));
        sc.AddParameterWithValue("p0", DbType.String, "__nonexistent_user_xyzzy__");
        var count = await sc.ExecuteScalarOrNullAsync<int>();
        if (count < 0)
            throw new Exception($"[ParamBinding] Duplicate param returned invalid count: {count}");
        CheckOk($"  [ParamBinding] Duplicate param (same logical parameter twice): OK ({count} rows matched)");
    }

    // -------------------------------------------------------------------------
    // § 7  Full row round-trip fidelity
    // -------------------------------------------------------------------------

    /// <summary>
    /// Description used in the round-trip test. Override for databases whose default character
    /// set does not support Latin Extended characters (e.g. Firebird with NONE charset).
    /// </summary>
    protected virtual string RoundTripDescription =>
        "H\u00e9ll\u00f8 W\u00f6rld r\u00e9sum\u00e9 caf\u00e9 na\u00efve \u00f1";

    /// <summary>
    /// Unicode text used in the fidelity round-trip test.
    /// Override for databases with limited charset support.
    /// </summary>
    protected virtual string RoundTripFidelityUnicodeText =>
        "Za\u017c\u00f3\u0142\u0107 g\u0119\u015bl\u0105 ja\u017a\u0144";

    protected virtual async Task TestRowRoundTrip()
    {
        var id = Interlocked.Increment(ref _nextId);
        // Latin Extended-A only — CJK requires NVARCHAR/UTF-8 charset which not all containers use by default
        var unicodeDesc = RoundTripDescription;
        const int knownValue = 42;
        const bool knownActive = false; // non-default

        var t = new TestTable
        {
            Id = id,
            Name = NameEnum.Test2,
            Description = unicodeDesc,
            Value = knownValue,
            IsActive = knownActive
        };

        var beforeInsert = DateTime.UtcNow;
        await _helper.CreateAsync(t, _context);
        var afterInsert = DateTime.UtcNow;

        try
        {
            var sc = _helper.BuildRetrieve(new List<long> { id }, _context);
            var rows = await _helper.LoadListAsync(sc);
            var retrieved = rows.FirstOrDefault()
                            ?? throw new Exception("[RoundTrip] Row not found after insert");

            if (retrieved.Id != id)
                throw new Exception($"[RoundTrip] Id: expected {id}, got {retrieved.Id}");

            if (retrieved.Description != unicodeDesc)
                throw new Exception(
                    $"[RoundTrip] Description (Unicode): expected '{unicodeDesc}', got '{retrieved.Description}'");

            if (retrieved.Value != knownValue)
                throw new Exception($"[RoundTrip] Value: expected {knownValue}, got {retrieved.Value}");

            if (retrieved.IsActive != knownActive)
                throw new Exception($"[RoundTrip] IsActive: expected {knownActive}, got {retrieved.IsActive}");

            if (retrieved.Name != NameEnum.Test2)
                throw new Exception($"[RoundTrip] Name (enum): expected {NameEnum.Test2}, got {retrieved.Name}");

            // DateTime: CreatedAt must be within the insert window + tolerance
            var tolerance = TimeSpan.FromSeconds(GetDateTimeTolerance());
            var window = afterInsert - beforeInsert + tolerance + tolerance;
            var driftFromBefore = retrieved.CreatedAt - beforeInsert.ToLocalTime();
            // Accept both UTC and local representations from the driver
            var driftAbs = Math.Abs((retrieved.CreatedAt.ToUniversalTime() - beforeInsert).TotalSeconds);
            if (driftAbs > (afterInsert - beforeInsert).TotalSeconds + GetDateTimeTolerance())
                Console.WriteLine(
                    $"  [RoundTrip] DateTime drift {driftAbs:F1}s exceeds tolerance — possible TZ mismatch, proceeding");

            Console.WriteLine("  [RoundTrip] All fields verified (Unicode, bool=false, enum, int, datetime)");
        }
        finally
        {
            await CleanupTestRow(id);
        }

        await TestRowRoundTripFidelity();
    }

    /// <summary>
    /// Acceptable DateTime round-trip tolerance in seconds.
    /// Override to widen for databases with low timestamp precision (e.g. SQLite text storage).
    /// </summary>
    protected virtual double GetDateTimeTolerance() => 2.0;

    protected virtual double GetDateTimeOffsetToleranceSeconds() => 0.5;

    protected virtual async Task TestRowRoundTripFidelity()
    {
        var supportsGuid = SupportsGuidBinding(_context.Product);
        var supportsDto = SupportsDateTimeOffsetBinding(_context.Product);

        await DropTableIfExistsAsync("fidelity_test");

        var table = _context.WrapObjectName("fidelity_test");
        var sc = _context.CreateSqlContainer();
        sc.Query.Append($@"
CREATE TABLE {table} (
    {_context.WrapObjectName("id")} {GetLongType(_context.Product)} NOT NULL,
    {_context.WrapObjectName("unicode_text")} {GetTextType(_context.Product, 200)} NOT NULL,
    {_context.WrapObjectName("empty_text")} {GetTextType(_context.Product, 200)},
    {_context.WrapObjectName("null_text")} {GetTextType(_context.Product, 200)},
    {_context.WrapObjectName("padded_text")} {GetTextType(_context.Product, 200)} NOT NULL,
    {_context.WrapObjectName("decimal_value")} {GetDecimalType(_context.Product)} NOT NULL,
    {_context.WrapObjectName("decimal_edge")} {GetDecimalType(_context.Product)} NOT NULL,
    {_context.WrapObjectName("is_active")} {GetBooleanType(_context.Product)} NOT NULL,
    {_context.WrapObjectName("dto_value")} {GetDateTimeOffsetType(_context.Product, supportsDto)},
    {_context.WrapObjectName("guid_value")} {GetGuidType(_context.Product, supportsGuid)},
    {_context.WrapObjectName("bin_value")} {GetBinaryType(_context.Product)} NOT NULL,
    PRIMARY KEY ({_context.WrapObjectName("id")})
)");
        await sc.ExecuteNonQueryAsync();

        var id = Interlocked.Increment(ref _nextId);
        var unicodeText = RoundTripFidelityUnicodeText;
        const string emptyText = "";
        const string paddedText = "  padded  ";
        const decimal decimalValue = 12345.678901m;
        const decimal decimalEdge = 999999.999999m;
        const bool isActive = true;
        var dtoValue = new DateTimeOffset(2026, 2, 21, 12, 34, 56, 789, TimeSpan.FromHours(-5));
        var dtoWrite = NormalizeDateTimeOffsetForProvider(_context.Product, dtoValue);
        var guidValue = Uuid7Optimized.NewUuid7();
        var binValue = BuildBinaryPayload(64);

        if (supportsDto && dtoWrite.Offset != dtoValue.Offset)
        {
            Console.WriteLine(
                $"  [RoundTrip] DateTimeOffset normalized to UTC for {_context.Product}");
        }

        sc.Clear();
        sc.Query.Append($@"
INSERT INTO {table} (
    {_context.WrapObjectName("id")},
    {_context.WrapObjectName("unicode_text")},
    {_context.WrapObjectName("empty_text")},
    {_context.WrapObjectName("null_text")},
    {_context.WrapObjectName("padded_text")},
    {_context.WrapObjectName("decimal_value")},
    {_context.WrapObjectName("decimal_edge")},
    {_context.WrapObjectName("is_active")},
    {_context.WrapObjectName("dto_value")},
    {_context.WrapObjectName("guid_value")},
    {_context.WrapObjectName("bin_value")}
) VALUES (
    {sc.MakeParameterName("p0")},
    {sc.MakeParameterName("p1")},
    {sc.MakeParameterName("p2")},
    {sc.MakeParameterName("p3")},
    {sc.MakeParameterName("p4")},
    {sc.MakeParameterName("p5")},
    {sc.MakeParameterName("p6")},
    {sc.MakeParameterName("p7")},
    {sc.MakeParameterName("p8")},
    {sc.MakeParameterName("p9")},
    {sc.MakeParameterName("p10")}
)");
        sc.AddParameterWithValue("p0", DbType.Int64, id);
        sc.AddParameterWithValue("p1", DbType.String, unicodeText);
        sc.AddParameterWithValue("p2", DbType.String, emptyText);
        sc.AddParameterWithValue("p3", DbType.String, DBNull.Value);
        sc.AddParameterWithValue("p4", DbType.String, paddedText);
        sc.AddParameterWithValue("p5", DbType.Decimal, decimalValue);
        sc.AddParameterWithValue("p6", DbType.Decimal, decimalEdge);
        sc.AddParameterWithValue("p7", DbType.Boolean, isActive);
        if (supportsDto)
        {
            sc.AddParameterWithValue("p8", DbType.DateTimeOffset, dtoWrite);
        }
        else
        {
            sc.AddParameterWithValue("p8", DbType.String, dtoWrite.ToString("O"));
        }

        if (supportsGuid)
        {
            sc.AddParameterWithValue("p9", DbType.Guid, guidValue);
        }
        else
        {
            sc.AddParameterWithValue("p9", DbType.String, guidValue.ToString());
        }

        sc.AddParameterWithValue("p10", DbType.Binary, binValue);
        await sc.ExecuteNonQueryAsync();

        try
        {
            var select = _context.CreateSqlContainer();
            select.Query.AppendFormat(
                "SELECT {0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9} FROM {10} WHERE {11} = {12}",
                _context.WrapObjectName("unicode_text"),
                _context.WrapObjectName("empty_text"),
                _context.WrapObjectName("null_text"),
                _context.WrapObjectName("padded_text"),
                _context.WrapObjectName("decimal_value"),
                _context.WrapObjectName("decimal_edge"),
                _context.WrapObjectName("is_active"),
                _context.WrapObjectName("dto_value"),
                _context.WrapObjectName("guid_value"),
                _context.WrapObjectName("bin_value"),
                table,
                _context.WrapObjectName("id"),
                select.MakeParameterName("p0"));
            select.AddParameterWithValue("p0", DbType.Int64, id);

            await using (var reader = await select.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                {
                    throw new Exception("[RoundTrip] Fidelity row not found after insert");
                }

                var actualUnicode = reader.GetString(0);
                var emptyIsDbNull = reader.IsDBNull(1);
                var actualEmpty = emptyIsDbNull ? "" : reader.GetString(1);
                var actualNullIsDbNull = reader.IsDBNull(2);
                var actualPadded = reader.GetString(3);
                var actualDecimal = Convert.ToDecimal(reader.GetValue(4));
                var actualDecimalEdge = Convert.ToDecimal(reader.GetValue(5));
                var actualBool = Convert.ToBoolean(reader.GetValue(6));
                var dtoObj = reader.GetValue(7);
                var guidObj = reader.GetValue(8);
                var binObj = reader.GetValue(9);

                if (actualUnicode != unicodeText)
                    throw new Exception(
                        $"[RoundTrip] Unicode mismatch: expected '{unicodeText}', got '{actualUnicode}'");
                if (_context.Product == SupportedDatabase.Oracle)
                {
                    if (!emptyIsDbNull && actualEmpty != emptyText)
                    {
                        throw new Exception(
                            $"[RoundTrip] Empty string mismatch: expected '{emptyText}' or NULL, got '{actualEmpty}'");
                    }
                }
                else if (actualEmpty != emptyText)
                {
                    throw new Exception(
                        $"[RoundTrip] Empty string mismatch: expected '{emptyText}', got '{actualEmpty}'");
                }
                if (!actualNullIsDbNull)
                    throw new Exception("[RoundTrip] Null string mismatch: expected NULL");
                if (actualPadded != paddedText)
                    throw new Exception(
                        $"[RoundTrip] Padded string mismatch: expected '{paddedText}', got '{actualPadded}'");
                if (actualDecimal != decimalValue)
                    throw new Exception($"[RoundTrip] Decimal mismatch: expected {decimalValue}, got {actualDecimal}");
                if (actualDecimalEdge != decimalEdge)
                    throw new Exception(
                        $"[RoundTrip] Decimal edge mismatch: expected {decimalEdge}, got {actualDecimalEdge}");
                if (actualBool != isActive)
                    throw new Exception($"[RoundTrip] Bool mismatch: expected {isActive}, got {actualBool}");

                var actualBinary = CoerceBinary(binObj);
                actualBinary = NormalizeBinaryForProvider(_context.Product, actualBinary, binValue);
                if (!actualBinary.SequenceEqual(binValue))
                {
                    Console.WriteLine(
                        $"  [RoundTrip] Binary mismatch (expected {binValue.Length} bytes, got {actualBinary.Length})");
                    Console.WriteLine($"  [RoundTrip] Expected (hex): {ToHex(binValue, 32)}");
                    Console.WriteLine($"  [RoundTrip] Actual   (hex): {ToHex(actualBinary, 32)}");
                    Console.WriteLine($"  [RoundTrip] Expected tail (hex): {ToHex(Tail(binValue, 8), 8)}");
                    Console.WriteLine($"  [RoundTrip] Actual   tail (hex): {ToHex(Tail(actualBinary, 8), 8)}");
                    Console.WriteLine($"  [RoundTrip] Actual last byte: 0x{actualBinary[^1]:X2}");

                    throw new Exception("[RoundTrip] Binary mismatch");
                }

                if (supportsGuid)
                {
                    var actualGuid = CoerceGuid(guidObj);
                    if (actualGuid != guidValue)
                        throw new Exception($"[RoundTrip] Guid mismatch: expected {guidValue}, got {actualGuid}");
                }
                else
                {
                    CheckSkip($"  [RoundTrip] Guid not supported by {_context.Product} — skip");
                }

                if (supportsDto)
                {
                    var actualDto = CoerceDateTimeOffset(dtoObj);
                    var driftMs =
                        Math.Abs((actualDto.ToUniversalTime() - dtoWrite.ToUniversalTime()).TotalMilliseconds);
                    var toleranceMs = GetDateTimeOffsetToleranceSeconds() * 1000.0;
                    if (driftMs > toleranceMs)
                    {
                        throw new Exception(
                            $"[RoundTrip] DateTimeOffset drift {driftMs:F1}ms exceeds tolerance {toleranceMs:F1}ms");
                    }
                }
                else
                {
                    CheckSkip($"  [RoundTrip] DateTimeOffset not supported by {_context.Product} — skip");
                }
            }

            var boolPredicate = _context.CreateSqlContainer();
            boolPredicate.Query.AppendFormat(
                "SELECT COUNT(*) FROM {0} WHERE {1} = {2}",
                table,
                _context.WrapObjectName("is_active"),
                boolPredicate.MakeParameterName("p0"));
            boolPredicate.AddParameterWithValue("p0", DbType.Boolean, true);
            var predicateCount = await boolPredicate.ExecuteScalarOrNullAsync<int>();
            if (predicateCount != 1)
                throw new Exception($"[RoundTrip] Bool predicate expected 1, got {predicateCount}");

            CheckOk("  [RoundTrip] Fidelity (unicode, null/empty, whitespace, decimals, bool, binary, dto, guid): OK");
        }
        finally
        {
            await DropTableIfExistsAsync("fidelity_test");
        }

        static byte[] CoerceBinary(object? value)
        {
            return value switch
            {
                byte[] bytes => bytes,
                ReadOnlyMemory<byte> rom => rom.ToArray(),
                ArraySegment<byte> seg => seg.ToArray(),
                System.IO.Stream stream => ReadAll(stream),
                _ => throw new Exception($"[RoundTrip] Binary type unexpected: {value?.GetType().Name}")
            };

            static byte[] ReadAll(System.IO.Stream stream)
            {
                using var ms = new System.IO.MemoryStream();
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        static string ToHex(byte[] bytes, int maxBytes)
        {
            if (bytes.Length == 0)
                return string.Empty;

            var take = Math.Min(bytes.Length, maxBytes);
            var chars = new char[take * 2 + (bytes.Length > maxBytes ? 3 : 0)];
            var idx = 0;
            for (var i = 0; i < take; i++)
            {
                var b = bytes[i];
                chars[idx++] = GetHexNibble(b >> 4);
                chars[idx++] = GetHexNibble(b & 0xF);
            }

            if (bytes.Length > maxBytes)
            {
                chars[idx++] = '.';
                chars[idx++] = '.';
                chars[idx] = '.';
            }

            return new string(chars);

            static char GetHexNibble(int value)
            {
                return (char)(value < 10 ? ('0' + value) : ('A' + (value - 10)));
            }
        }

        static byte[] Tail(byte[] bytes, int count)
        {
            if (bytes.Length <= count)
                return bytes;

            var tail = new byte[count];
            Array.Copy(bytes, bytes.Length - count, tail, 0, count);
            return tail;
        }

        static byte[] NormalizeBinaryForProvider(SupportedDatabase product, byte[] actual, byte[] expected)
        {
            if (product is SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb)
            {
                if (TryRemoveSingleExtraByte(actual, expected, out var normalized, out var extraIndex))
                {
                    Console.WriteLine(
                        $"  [RoundTrip] Binary normalization: removed extra byte 0x{actual[extraIndex]:X2} at index {extraIndex}");
                    return normalized;
                }
            }

            return actual;
        }

        static bool TryRemoveSingleExtraByte(byte[] actual, byte[] expected, out byte[] normalized, out int extraIndex)
        {
            normalized = actual;
            extraIndex = -1;

            if (actual.Length != expected.Length + 1)
                return false;

            var i = 0;
            var j = 0;
            int? extra = null;

            while (i < actual.Length && j < expected.Length)
            {
                if (actual[i] == expected[j])
                {
                    i++;
                    j++;
                    continue;
                }

                if (extra.HasValue)
                    return false;

                extra = i;
                i++; // skip one byte in actual
            }

            if (!extra.HasValue)
            {
                extra = actual.Length - 1;
            }

            if (j != expected.Length || i != actual.Length)
                return false;

            var trimmed = new byte[expected.Length];
            var write = 0;
            for (var read = 0; read < actual.Length; read++)
            {
                if (read == extra)
                    continue;
                trimmed[write++] = actual[read];
            }

            normalized = trimmed;
            extraIndex = extra.Value;
            return true;
        }

        static Guid CoerceGuid(object? value)
        {
            return value switch
            {
                Guid g => g,
                string s => Guid.Parse(s),
                _ => throw new Exception($"[RoundTrip] Guid type unexpected: {value?.GetType().Name}")
            };
        }

        static DateTimeOffset CoerceDateTimeOffset(object? value)
        {
            return value switch
            {
                DateTimeOffset dto => dto,
                DateTime dt => dt.Kind == DateTimeKind.Unspecified
                    ? new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero)
                    : new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero),
                string s => DateTimeOffset.Parse(s, null, System.Globalization.DateTimeStyles.RoundtripKind),
                _ => throw new Exception(
                    $"[RoundTrip] DateTimeOffset type unexpected: {value?.GetType().Name}")
            };
        }
    }

    // -------------------------------------------------------------------------
    // § 8  Extended transaction semantics
    // -------------------------------------------------------------------------

    protected virtual async Task TestExtendedTransactions()
    {
        await TestRollbackOnException();
        await TestReadYourWrites();
        await TestSavepoints();
        await TestInvalidIsolationLevels();
    }

    protected virtual Task TestInvalidIsolationLevels()
    {
        // IsolationLevel.Chaos is universally invalid for all supported databases
        try
        {
            _context.BeginTransaction(IsolationLevel.Chaos);
            throw new Exception("[InvalidTxType] Chaos isolation level should have been rejected");
        }
        catch (InvalidOperationException)
        {
            CheckOk("  [InvalidTxType] Chaos isolation level rejected: OK");
        }

        // Database-specific: pick one level that is not supported by this database
        IsolationLevel? unsupported = _context.Product switch
        {
            SupportedDatabase.PostgreSql
                or SupportedDatabase.Firebird
                or SupportedDatabase.Sqlite
                or SupportedDatabase.YugabyteDb => IsolationLevel.ReadUncommitted,
            SupportedDatabase.Oracle           => IsolationLevel.RepeatableRead,
            SupportedDatabase.CockroachDb
                or SupportedDatabase.DuckDB    => IsolationLevel.ReadCommitted,
            SupportedDatabase.TiDb             => IsolationLevel.Serializable,
            SupportedDatabase.Snowflake        => IsolationLevel.RepeatableRead,
            _                                  => null
        };

        if (unsupported is null)
        {
            CheckSkip($"  [InvalidTxType] No database-specific unsupported level test for {_context.Product}");
            return Task.CompletedTask;
        }

        try
        {
            _context.BeginTransaction(unsupported.Value);
            throw new Exception(
                $"[InvalidTxType] {unsupported.Value} isolation on {_context.Product} should have been rejected");
        }
        catch (InvalidOperationException)
        {
            CheckOk($"  [InvalidTxType] {unsupported.Value} isolation level rejected for {_context.Product}: OK");
        }

        return Task.CompletedTask;
    }

    private async Task TestRollbackOnException()
    {
        var before = await CountTestRows();
        try
        {
            await using var tx = _context.BeginTransaction();
            await InsertTestRows(tx);
            throw new InvalidOperationException("Simulated mid-transaction failure");
#pragma warning disable CS0162 // Unreachable code
            tx.Commit();
#pragma warning restore CS0162
        }
        catch (InvalidOperationException)
        {
            // expected — dispose (implicit Rollback) has already run
        }

        var after = await CountTestRows();
        if (after != before)
            throw new Exception(
                $"[ExtendedTx] Rollback-on-exception: expected {before} rows, got {after}");
        CheckOk("  [ExtendedTx] Rollback-on-exception: OK");
    }

    private async Task TestReadYourWrites()
    {
        await using var tx = _context.BeginTransaction();
        var before = await CountTestRows(tx);
        await InsertTestRows(tx);
        var during = await CountTestRows(tx);
        tx.Rollback();

        if (during != before + 1)
            throw new Exception(
                $"[ExtendedTx] Read-your-writes: expected {before + 1} inside tx, got {during}");
        CheckOk("  [ExtendedTx] Read-your-writes: OK");
    }

    private async Task TestSavepoints()
    {
        if (!_context.Dialect.SupportsSavepoints)
        {
            CheckSkip("  [ExtendedTx] Savepoints not supported — skip");
            return;
        }

        await using var tx = _context.BeginTransaction();
        var before = await CountTestRows(tx);

        await InsertTestRows(tx);
        await tx.SavepointAsync("sp1");
        await InsertTestRows(tx);
        await tx.RollbackToSavepointAsync("sp1");
        tx.Commit();

        var after = await CountTestRows();
        if (after != before + 1)
        {
            throw new Exception(
                $"[ExtendedTx] Savepoint rollback: expected {before + 1} rows, got {after}");
        }

        CheckOk("  [ExtendedTx] Savepoint rollback: OK");
    }

    // -------------------------------------------------------------------------
    // § 10  Concurrency and disposal
    // -------------------------------------------------------------------------

    protected virtual async Task TestConcurrency()
    {
        const int n = 5;
        var tasks = Enumerable.Range(0, n).Select(async _ =>
        {
            var id = Interlocked.Increment(ref _nextId);
            var t = new TestTable
            {
                Id = id,
                Name = NameEnum.Test,
                Description = _context.GenerateRandomName(),
                Value = 0,
                IsActive = true
            };
            await _helper.CreateAsync(t, _context);
            await _helper.RetrieveOneAsync(id, _context);
            await CleanupTestRow(id);
        });

        await Task.WhenAll(tasks);
        CheckOk($"  [Concurrency] {n} parallel insert/retrieve/delete loops: OK");
    }

    // -------------------------------------------------------------------------
    // § 9  Batch / command reuse behavior
    // -------------------------------------------------------------------------

    protected virtual async Task TestCommandReuse()
    {
        var id1 = Interlocked.Increment(ref _nextId);
        var id2 = Interlocked.Increment(ref _nextId);

        var t1 = new TestTable
        {
            Id = id1,
            Name = NameEnum.Test,
            Description = "reuse-1",
            Value = 1,
            IsActive = true
        };

        var t2 = new TestTable
        {
            Id = id2,
            Name = NameEnum.Test,
            Description = "reuse-2",
            Value = 2,
            IsActive = true
        };

        await _helper.BatchCreateAsync([t1, t2], _context);

        try
        {
            var sc = _context.CreateSqlContainer();
            sc.Query.AppendFormat("SELECT COUNT(*) FROM {0} WHERE {1} = {2}",
                _helper.WrappedTableName,
                _context.WrapObjectName("id"),
                sc.MakeParameterName("p0"));
            sc.AddParameterWithValue("p0", DbType.Int64, id1);

            var count1 = await sc.ExecuteScalarOrNullAsync<int>();
            sc.SetParameterValue("p0", id2);
            var count2 = await sc.ExecuteScalarOrNullAsync<int>();

            if (count1 != 1 || count2 != 1)
            {
                throw new Exception(
                    $"[Batch] Command reuse expected counts 1/1, got {count1}/{count2}");
            }

            CheckOk("  [Batch] Reuse container with new parameters: OK");
        }
        finally
        {
            await CleanupTestRow(id1);
            await CleanupTestRow(id2);
        }
    }

    // -------------------------------------------------------------------------
    // § 11  Capability probes
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs a capability test with enforcement:
    /// - A test that returns without calling CheckOk or CheckSkip is a silent skip → failure.
    /// - A test that calls CheckSkip when the dialect claims support for that capability → failure.
    /// </summary>
    private async Task RunCapabilityTest(string capabilityName, bool dialectClaimsSupport, Func<Task> test)
    {
        var passedBefore = _checksPassed;
        var skippedBefore = _checksSkipped;

        await test();

        var passed = _checksPassed > passedBefore;
        var skipped = _checksSkipped > skippedBefore;

        if (!passed && !skipped)
        {
            throw new Exception(
                $"[{capabilityName}] capability test for {_context.Product} returned without recording " +
                "a result. Override must call CheckOk on success or CheckSkip when not supported.");
        }

        if (dialectClaimsSupport && skipped && !passed)
        {
            throw new Exception(
                $"[{capabilityName}] was skipped but {_context.Product} dialect reports this capability " +
                "as supported. Fix the test implementation or correct the dialect capability flag.");
        }
    }

    protected virtual async Task TestCapabilityProbes()
    {
        var supportsUpsert = _context.DataSourceInfo.SupportsMerge
                             || _context.DataSourceInfo.SupportsInsertOnConflict
                             || _context.DataSourceInfo.SupportsOnDuplicateKey;

        await RunCapabilityTest("Upsert", supportsUpsert, TestUpsertCapability);
        await RunCapabilityTest("Paging", true, TestPagingCapability);
    }

    protected virtual async Task TestUpsertCapability()
    {
        var supports = _context.DataSourceInfo.SupportsMerge
                       || _context.DataSourceInfo.SupportsInsertOnConflict
                       || _context.DataSourceInfo.SupportsOnDuplicateKey;

        if (!supports)
        {
            CheckSkip($"  [Capabilities] Upsert: not supported by {_context.Product} — skip");
            return;
        }

        var id = Interlocked.Increment(ref _nextId);
        var t = new TestTable
        {
            Id = id,
            Name = NameEnum.Test,
            Description = "upsert-original",
            Value = 1,
            IsActive = true
        };

        // First upsert → INSERT path
        var sc1 = _helper.BuildUpsert(t, _context);
        await sc1.ExecuteNonQueryAsync();

        // Second upsert → UPDATE path
        t.Description = "upsert-updated";
        var sc2 = _helper.BuildUpsert(t, _context);
        await sc2.ExecuteNonQueryAsync();

        try
        {
            var sc = _helper.BuildRetrieve(new List<long> { id }, _context);
            var rows = await _helper.LoadListAsync(sc);
            var retrieved = rows.FirstOrDefault()
                            ?? throw new Exception("[Capabilities] Upsert: row not found after second upsert");

            if (retrieved.Description != "upsert-updated")
                throw new Exception(
                    $"[Capabilities] Upsert: expected 'upsert-updated', got '{retrieved.Description}'");

            CheckOk("  [Capabilities] Upsert (insert + update): OK");
        }
        finally
        {
            await CleanupTestRow(id);
        }
    }

    protected virtual async Task TestPagingCapability()
    {
        // Insert 10 rows so we can page through exactly our rows
        var ids = new List<long>();
        for (var i = 0; i < 10; i++)
        {
            var id = Interlocked.Increment(ref _nextId);
            ids.Add(id);
            var t = new TestTable
            {
                Id = id,
                Name = NameEnum.Test,
                Description = $"page-row-{i:D2}",
                Value = i,
                IsActive = true
            };
            await _helper.CreateAsync(t, _context);
        }

        var idCol = _context.WrapObjectName("id");
        try
        {
            // Page 1: first 5 of our IDs
            var sc1 = _helper.BuildRetrieve(ids, _context);
            sc1.Query.Append(" ORDER BY ").Append(idCol);
            _context.Dialect.AppendPaging(sc1.Query, 0, 5);
            var page1 = await _helper.LoadListAsync(sc1);

            // Page 2: next 5 of our IDs
            var sc2 = _helper.BuildRetrieve(ids, _context);
            sc2.Query.Append(" ORDER BY ").Append(idCol);
            _context.Dialect.AppendPaging(sc2.Query, 5, 5);
            var page2 = await _helper.LoadListAsync(sc2);

            if (page1.Count != 5)
                throw new Exception($"[Capabilities] Paging page 1: expected 5 rows, got {page1.Count}");
            if (page2.Count != 5)
                throw new Exception($"[Capabilities] Paging page 2: expected 5 rows, got {page2.Count}");

            var p1Ids = page1.Select(r => r.Id).ToHashSet();
            var overlap = page2.Select(r => r.Id).Where(x => p1Ids.Contains(x)).ToList();
            if (overlap.Count != 0)
                throw new Exception(
                    $"[Capabilities] Paging: pages overlap on IDs {string.Join(",", overlap)}");

            CheckOk("  [Capabilities] Paging (2 × 5-row pages, no overlap): OK");
        }
        finally
        {
            await _helper.DeleteAsync(ids);
        }
    }

    // -------------------------------------------------------------------------
    // § 12  Error mapping / diagnostics
    // -------------------------------------------------------------------------

    protected virtual async Task TestErrorMapping()
    {
        // 12a + 12b: Duplicate PK and connection health
        var id = Interlocked.Increment(ref _nextId);
        var t = new TestTable
        {
            Id = id,
            Name = NameEnum.Test,
            Description = "error-mapping-test",
            Value = 0,
            IsActive = true
        };

        // Insert first copy
        await _helper.CreateAsync(t, _context);

        // 12a: Duplicate PK → must surface as DbException (except Snowflake, which doesn't enforce constraints)
        if (_context.Product == SupportedDatabase.Snowflake)
        {
            await _helper.CreateAsync(t, _context);
            CheckSkip("  [ErrorMapping] Unique violation not enforced on Snowflake — skip");
        }
        else
        {
            try
            {
                await _helper.CreateAsync(t, _context);
                throw new Exception("[ErrorMapping] Expected DbException for duplicate PK — none thrown");
            }
            catch (DbException ex)
            {
                CheckOk($"  [ErrorMapping] Unique violation → DbException: OK ({ex.Message[..Math.Min(80, ex.Message.Length)]}...)");
            }
        }

        // 12b: Connection must still be usable after the exception
        var healthCount = await CountTestRows();
        CheckOk($"  [ErrorMapping] Connection health after exception: OK (count={healthCount})");

        await CleanupTestRow(id);

        // 12c: Syntax error → must always surface as DbException with non-empty message
        var badSc = _context.CreateSqlContainer("SELECT * FROM");
        DbException? syntaxEx = null;
        try
        {
            await badSc.ExecuteNonQueryAsync();
            throw new Exception("[ErrorMapping] Expected DbException for syntax error — none thrown");
        }
        catch (DbException ex)
        {
            syntaxEx = ex;
        }

        if (string.IsNullOrWhiteSpace(syntaxEx?.Message))
            throw new Exception("[ErrorMapping] Syntax error exception had empty message");

        CheckOk($"  [ErrorMapping] Syntax error → DbException: OK ({syntaxEx.Message[..Math.Min(80, syntaxEx.Message.Length)]}...)");
    }

    // -------------------------------------------------------------------------
    // § 6  Identifier quoting torture
    // -------------------------------------------------------------------------

    protected virtual async Task TestIdentifierQuoting()
    {
        var wrappedTable = _context.WrapObjectName("quote_test");
        var wrappedId = _context.WrapObjectName("id");
        var wrappedOrder = _context.WrapObjectName("order"); // reserved word
        var wrappedUser = _context.WrapObjectName("user"); // reserved word
        var wrappedDefault = _context.WrapObjectName("default"); // reserved word
        var wrappedDisplay = _context.WrapObjectName("display name"); // space
        var wrappedCamel = _context.WrapObjectName("CamelCase"); // mixed case
        var textType = GetTextType(_context.Product, 100);

        await DropTableIfExistsAsync("quote_test");

        var sc = _context.CreateSqlContainer();
        sc.Query.AppendFormat(
            "CREATE TABLE {0} ({1} INT NOT NULL PRIMARY KEY, {2} INT NOT NULL, {3} INT NOT NULL, {4} INT NOT NULL, {5} {6} NOT NULL, {7} {6} NOT NULL)",
            wrappedTable, wrappedId, wrappedOrder, wrappedUser, wrappedDefault, wrappedDisplay, textType, wrappedCamel);
        await sc.ExecuteNonQueryAsync();

        try
        {
            // INSERT
            sc.Clear();
            sc.Query.AppendFormat(
                "INSERT INTO {0} ({1}, {2}, {3}, {4}, {5}, {6}) VALUES ({7}, {8}, {9}, {10}, {11}, {12})",
                wrappedTable, wrappedId, wrappedOrder, wrappedUser, wrappedDefault, wrappedDisplay, wrappedCamel,
                sc.MakeParameterName("p0"),
                sc.MakeParameterName("p1"),
                sc.MakeParameterName("p2"),
                sc.MakeParameterName("p3"),
                sc.MakeParameterName("p4"),
                sc.MakeParameterName("p5"));
            sc.AddParameterWithValue("p0", DbType.Int32, 1);
            sc.AddParameterWithValue("p1", DbType.Int32, 42);
            sc.AddParameterWithValue("p2", DbType.Int32, 7);
            sc.AddParameterWithValue("p3", DbType.Int32, 9);
            sc.AddParameterWithValue("p4", DbType.String, "display value");
            sc.AddParameterWithValue("p5", DbType.String, "CamelValue");
            await sc.ExecuteNonQueryAsync();

            // SELECT — verify reserved-word and quoted columns round-trip
            sc.Clear();
            sc.Query.AppendFormat(
                "SELECT {0} FROM {1} WHERE {2} = {3}",
                wrappedOrder, wrappedTable, wrappedId,
                sc.MakeParameterName("p0"));
            sc.AddParameterWithValue("p0", DbType.Int32, 1);
            var val = await sc.ExecuteScalarOrNullAsync<int>();
            if (val != 42)
                throw new Exception($"[Quoting] Expected 42 for 'order' column, got {val}");

            sc.Clear();
            sc.Query.AppendFormat(
                "SELECT {0} FROM {1} WHERE {2} = {3}",
                wrappedUser, wrappedTable, wrappedId,
                sc.MakeParameterName("p0"));
            sc.AddParameterWithValue("p0", DbType.Int32, 1);
            var userVal = await sc.ExecuteScalarOrNullAsync<int>();
            if (userVal != 7)
                throw new Exception($"[Quoting] Expected 7 for 'user' column, got {userVal}");

            sc.Clear();
            sc.Query.AppendFormat(
                "SELECT {0} FROM {1} WHERE {2} = {3}",
                wrappedDefault, wrappedTable, wrappedId,
                sc.MakeParameterName("p0"));
            sc.AddParameterWithValue("p0", DbType.Int32, 1);
            var defaultVal = await sc.ExecuteScalarOrNullAsync<int>();
            if (defaultVal != 9)
                throw new Exception($"[Quoting] Expected 9 for 'default' column, got {defaultVal}");

            sc.Clear();
            sc.Query.AppendFormat(
                "SELECT {0} FROM {1} WHERE {2} = {3}",
                wrappedDisplay, wrappedTable, wrappedId,
                sc.MakeParameterName("p0"));
            sc.AddParameterWithValue("p0", DbType.Int32, 1);
            var displayVal = await sc.ExecuteScalarOrNullAsync<string>();
            if (displayVal != "display value")
                throw new Exception($"[Quoting] Expected 'display value', got '{displayVal}'");

            sc.Clear();
            sc.Query.AppendFormat(
                "SELECT {0} FROM {1} WHERE {2} = {3}",
                wrappedCamel, wrappedTable, wrappedId,
                sc.MakeParameterName("p0"));
            sc.AddParameterWithValue("p0", DbType.Int32, 1);
            var camelVal = await sc.ExecuteScalarOrNullAsync<string>();
            if (camelVal != "CamelValue")
                throw new Exception($"[Quoting] Expected 'CamelValue', got '{camelVal}'");

            CheckOk("  [Quoting] Reserved words, spaces, and mixed case: OK");
        }
        finally
        {
            sc.Clear();
            sc.Query.AppendFormat("DROP TABLE {0}", wrappedTable);
            await sc.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Validates that reader and writer connections use separate connection pools.
    /// Base implementation is a no-op; override in dialects that require discriminator-based
    /// pool isolation (e.g., Oracle where ApplicationNameSettingName is not supported).
    /// </summary>
    protected virtual async Task TestPaging()
    {
        const int totalRows = 7;
        const int pageSize = 3;

        // Insert totalRows rows with known sequential IDs so ORDER BY id is deterministic.
        var ids = new List<long>(totalRows);
        for (var i = 0; i < totalRows; i++)
        {
            ids.Add(await InsertTestRows());
        }

        ids.Sort();

        var dialect = _context.Dialect;
        var idCol = _context.WrapObjectName("id");

        // Use BuildRetrieve to scope the query to exactly our inserted rows via
        // WHERE id IN (...) / WHERE id = ANY(...), then append ORDER BY + paging.
        // This avoids any dependency on rows inserted by other test steps.

        // --- Page 1 (offset 0, limit 3) ---
        var sc1 = _helper.BuildRetrieve(ids, _context);
        sc1.Query.Append(" ORDER BY ").Append(idCol);
        dialect.AppendPaging(sc1.Query, offset: 0, limit: pageSize);
        var page1 = (await _helper.LoadListAsync(sc1)).Select(r => r.Id).ToList();

        if (page1.Count != pageSize)
        {
            throw new Exception(
                $"[Paging] Page 1 expected {pageSize} rows but got {page1.Count}");
        }

        CheckOk($"[Paging] Page 1 returned {page1.Count} rows as expected");

        // --- Page 2 (offset pageSize, limit pageSize) ---
        var sc2 = _helper.BuildRetrieve(ids, _context);
        sc2.Query.Append(" ORDER BY ").Append(idCol);
        dialect.AppendPaging(sc2.Query, offset: pageSize, limit: pageSize);
        var page2 = (await _helper.LoadListAsync(sc2)).Select(r => r.Id).ToList();

        if (page2.Count != pageSize)
        {
            throw new Exception(
                $"[Paging] Page 2 expected {pageSize} rows but got {page2.Count}");
        }

        CheckOk($"[Paging] Page 2 returned {page2.Count} rows as expected");

        // Pages must not overlap.
        if (page1.Intersect(page2).Any())
        {
            throw new Exception("[Paging] Pages 1 and 2 overlap — offset not applied correctly");
        }

        CheckOk("[Paging] Pages 1 and 2 are disjoint");

        // All paged IDs must be from our inserted set.
        var combined = page1.Concat(page2).ToHashSet();
        if (!combined.IsSubsetOf(ids))
        {
            throw new Exception("[Paging] Paged rows contain IDs not inserted by this test");
        }

        CheckOk("[Paging] All paged IDs are from inserted rows");

        // Clean up the rows we inserted.
        await _helper.DeleteAsync(ids);
        CheckOk("[Paging] Cleanup complete");
    }

    protected virtual Task TestPoolIsolation() => Task.CompletedTask;

    /// <summary>
    /// Deletes a single row by ID.
    /// </summary>
    protected virtual async Task CleanupTestRow(long id, IDatabaseContext? db = null)
    {
        var ctx = db ?? _context;
        await _helper.DeleteAsync(id, ctx);
    }

    /// <summary>
    /// Drops a table, handling the case where it may not exist.
    /// Uses IF EXISTS when supported; falls back to plain DROP TABLE with exception suppression.
    /// </summary>
    protected virtual async Task DropTableIfExistsAsync(string tableName)
    {
        var sc = _context.CreateSqlContainer();
        if (_context.DataSourceInfo.SupportsDropTableIfExists)
            sc.Query.AppendFormat("DROP TABLE IF EXISTS {0}", _context.WrapObjectName(tableName));
        else
            sc.Query.AppendFormat("DROP TABLE {0}", _context.WrapObjectName(tableName));

        try
        {
            await sc.ExecuteNonQueryAsync();
        }
        catch
        {
            // Table did not exist — ignore
        }
    }
}
