using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using System.Data;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Verifies stored procedure invocation across the providers that support it: SQL Server return
/// value capture and OUTPUT parameters (<c>ProcWrappingStyle.Exec</c>), correct NotSupported
/// behavior for return-value capture on every other provider, Db2's CALL-based invocation with a
/// <c>WITH RETURN TO CALLER</c> cursor result set (<c>ProcWrappingStyle.Call</c>), and the
/// PostgreSQL family's (PostgreSQL/CockroachDB/YugabyteDB) CALL-based write path for a real
/// PG11+ procedure with an INOUT parameter (<c>ProcWrappingStyle.PostgreSQL</c>).
/// </summary>
[Collection("IntegrationTests")]
public class StoredProcedureTests : DatabaseTestBase
{
    public StoredProcedureTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        return Task.CompletedTask;
    }

    [SkippableFact]
    public async Task StoredProc_ReturnValueCapture_ThrowsWhereTheWrappingStyleCannotCaptureIt()
    {
        await RunTestAgainstAllProvidersAsync((provider, context) =>
        {
            // Only the Exec wrapping style (SQL Server) can capture @RETURN_VALUE; every other style
            // must refuse captureReturn: true rather than silently ignore it.
            if (context.ProcWrappingStyle != ProcWrappingStyle.Exec)
            {
                var container = context.CreateSqlContainer("SomeProc");
                Assert.Throws<NotSupportedException>(() =>
                    container.WrapForStoredProc(ExecutionType.Write, includeParameters: false,
                        captureReturn: true));
            }

            return Task.CompletedTask;
        });
    }

    [SkippableFact]
    public async Task StoredProc_ReturnValueCapture_WorksOnSqlServer()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.SqlServer }, async (provider, context) =>
        {

            // Arrange: Create a simple proc that returns 42
            var dropSql = "IF OBJECT_ID('dbo.TestReturnProc', 'P') IS NOT NULL DROP PROCEDURE dbo.TestReturnProc";
            var createSql = "CREATE PROCEDURE dbo.TestReturnProc AS BEGIN RETURN 42; END";

            await context.CreateSqlContainer(dropSql).ExecuteNonQueryAsync();
            await context.CreateSqlContainer(createSql).ExecuteNonQueryAsync();

            try
            {
                // Act: Use a container to call it and capture the return value
                var container = context.CreateSqlContainer("TestReturnProc");
                var wrappedSql =
                    container.WrapForStoredProc(ExecutionType.Write, includeParameters: false, captureReturn: true);

                await using var execContainer = context.CreateSqlContainer(wrappedSql);

                var returnValue = await execContainer.ExecuteScalarRequiredAsync<int>();

                // Assert
                Assert.Equal(42, returnValue);
            }
            finally
            {
                await context.CreateSqlContainer(dropSql).ExecuteNonQueryAsync();
            }
        });
    }

    [SkippableFact]
    public async Task StoredProc_OutputParameter_WorksOnSqlServer()
    {
        await RunTestAgainstProvidersAsync(new[] { SupportedDatabase.SqlServer }, async (provider, context) =>
        {
            const string dropSql =
                "IF OBJECT_ID('dbo.TestOutputProc', 'P') IS NOT NULL DROP PROCEDURE dbo.TestOutputProc";
            const string createSql =
                "CREATE PROCEDURE dbo.TestOutputProc @inputValue INT, @outputValue INT OUTPUT " +
                "AS BEGIN SET @outputValue = @inputValue + 1; END";

            await context.CreateSqlContainer(dropSql).ExecuteNonQueryAsync();
            await context.CreateSqlContainer(createSql).ExecuteNonQueryAsync();

            try
            {
                await using var container = context.CreateSqlContainer("TestOutputProc");
                container.AddParameterWithValue("inputValue", DbType.Int32, 41);
                var output = container.AddParameterWithValue("outputValue", DbType.Int32, 0,
                    ParameterDirection.Output);

                await container.ExecuteNonQueryAsync(CommandType.StoredProcedure);

                Assert.Equal(42, output.Value);
            }
            finally
            {
                await context.CreateSqlContainer(dropSql).ExecuteNonQueryAsync();
            }
        });
    }

    // ---- Ported from 3.0 (backport audit, 2026-09-25) ----

    /// <summary>
    /// Oracle requires stored procedures to be invoked inside a PL/SQL anonymous block
    /// (<c>ProcWrappingStyle.Oracle</c> — <c>BEGIN proc(args); END;</c>); ExecutionType is ignored,
    /// the same block syntax is used for reads and writes. Proven here via the "automatic" call
    /// pattern (<c>CommandType.StoredProcedure</c> triggers <c>WrapForStoredProc</c> internally,
    /// same shape as the SQL Server OUTPUT test above) with a real OUT parameter, against a live
    /// Oracle server — <c>fakeDb</c> cannot prove the PL/SQL block actually executes and binds an
    /// OUT parameter back into the caller's <c>DbParameter</c>.
    /// </summary>
    [SkippableFact]
    public Task StoredProc_AnonymousBlock_WithOutParameter_WorksOnOracle()
    {
        return RunTestAgainstProvidersAsync(new[] { SupportedDatabase.Oracle }, async (provider, context) =>
        {
            var procName = context.WrapObjectName("sp_pengdows_oracle_test");
            var createSql =
                $"CREATE OR REPLACE PROCEDURE {procName}(input_value IN NUMBER, output_value OUT NUMBER) AS\n" +
                "BEGIN\n" +
                "  output_value := input_value + 1;\n" +
                "END;";

            await context.CreateSqlContainer(createSql).ExecuteNonQueryAsync();

            try
            {
                await using var container = context.CreateSqlContainer("sp_pengdows_oracle_test");
                container.AddParameterWithValue("input_value", DbType.Int32, 41);
                var output = container.AddParameterWithValue("output_value", DbType.Int32, 0,
                    ParameterDirection.Output);

                await container.ExecuteNonQueryAsync(CommandType.StoredProcedure);

                Assert.Equal(42, Convert.ToInt32(output.Value));
                Output.WriteLine($"{provider}: PL/SQL anonymous block invoked, OUT result = {output.Value}");
            }
            finally
            {
                await context.CreateSqlContainer($"DROP PROCEDURE {procName}").ExecuteNonQueryAsync();
            }
        });
    }

    /// <summary>
    /// Proves, against REAL PostgreSQL-family servers, that <c>PostgresProcWrappingStrategy</c>'s
    /// write-path branch (<c>CALL procedure_name(args)</c>, PostgreSQL 11+ real procedures)
    /// actually executes — not just its read-path branch
    /// (<c>SELECT * FROM function_name()</c>, covered by the read-path checks elsewhere in this
    /// harness). PostgreSQL returns a procedure's INOUT parameter value as a one-row result set
    /// from CALL itself — no provider-level output-parameter binding is needed, unlike SQL
    /// Server's OUTPUT. Scoped to PostgreSQL, CockroachDB, and YugabyteDB — the three databases
    /// that share <c>PostgresProcWrappingStrategy</c> via <c>ProcWrappingStyle.PostgreSQL</c>.
    /// </summary>
    [SkippableFact]
    public Task StoredProc_Call_RealProcedureWithInoutParameter_ExecutesViaCall()
    {
        return RunTestAgainstProvidersAsync(
            new[] { SupportedDatabase.PostgreSql, SupportedDatabase.CockroachDb, SupportedDatabase.YugabyteDb },
            async (provider, context) =>
        {
            if (context.ProcWrappingStyle == ProcWrappingStyle.None)
            {
                Output.WriteLine($"Skipping real PROCEDURE test on {provider}: stored procedures are unsupported.");
                return;
            }

            if (provider == SupportedDatabase.PostgreSql && context.DataSourceInfo.ParsedVersion != null &&
                context.DataSourceInfo.ParsedVersion.Major < 11)
            {
                Output.WriteLine($"Skipping real PROCEDURE test on PostgreSQL {context.DataSourceInfo.ParsedVersion}: CREATE PROCEDURE requires PostgreSQL 11+");
                return;
            }

            var procName = context.WrapObjectName("sp_pengdows_family_test_proc");
            var createSql =
                $"CREATE OR REPLACE PROCEDURE {procName}(INOUT result INT)\n" +
                "LANGUAGE plpgsql\n" +
                "AS $$\n" +
                "BEGIN\n" +
                "  result := 42;\n" +
                "END;\n" +
                "$$";

            await context.CreateSqlContainer(createSql).ExecuteNonQueryAsync();

            try
            {
                await using var sc = context.CreateSqlContainer("sp_pengdows_family_test_proc");
                sc.AddParameterWithValue("result", DbType.Int32, DBNull.Value);
                var wrapped = sc.WrapForStoredProc(ExecutionType.Write);

                await using var execContainer = context.CreateSqlContainer(wrapped);
                execContainer.AddParameterWithValue("result", DbType.Int32, DBNull.Value);
                var result = await execContainer.ExecuteScalarRequiredAsync<int>();

                Assert.Equal(42, result);
                Output.WriteLine($"{provider}: real PROCEDURE invoked via CALL, INOUT result = {result}");
            }
            finally
            {
                await context.CreateSqlContainer($"DROP PROCEDURE {procName}").ExecuteNonQueryAsync();
            }
        });
    }

    /// <summary>
    /// Db2 LUW stored procedures are invoked via SQL-standard CALL syntax
    /// (<c>ProcWrappingStyle.Call</c> — same style as MySQL/MariaDB). Result sets are returned via
    /// a cursor declared <c>WITH RETURN TO CALLER</c> inside the procedure body, which the CALL
    /// statement's caller consumes like an ordinary query result set. <c>Db2Dialect</c> previously
    /// left <c>ProcWrappingStyle</c> at the <c>SqlDialect</c> base default of <c>None</c>, which
    /// silently disabled stored-procedure support through this library even though Db2 itself
    /// fully supports procedures — <c>fakeDb</c> can't prove the real SQL PL body/cursor syntax
    /// actually executes, only a live server can.
    /// </summary>
    [SkippableFact]
    public Task StoredProc_Call_ReturnsResultSetFromWithReturnCursor()
    {
        return RunTestAgainstProvidersAsync(new[] { SupportedDatabase.Db2 }, async (provider, context) =>
        {
            var procName = context.WrapObjectName("sp_pengdows_db2_test");
            var createSql =
                $"CREATE OR REPLACE PROCEDURE {procName}()\n" +
                "DYNAMIC RESULT SETS 1\n" +
                "LANGUAGE SQL\n" +
                "BEGIN\n" +
                "  DECLARE c1 CURSOR WITH RETURN TO CALLER FOR SELECT 42 FROM SYSIBM.SYSDUMMY1;\n" +
                "  OPEN c1;\n" +
                "END";

            await context.CreateSqlContainer(createSql).ExecuteNonQueryAsync();

            try
            {
                await using var sc = context.CreateSqlContainer("sp_pengdows_db2_test");
                var wrapped = sc.WrapForStoredProc(ExecutionType.Write);

                await using var execContainer = context.CreateSqlContainer(wrapped);
                var result = await execContainer.ExecuteScalarRequiredAsync<int>();

                Assert.Equal(42, result);
            }
            finally
            {
                await context.CreateSqlContainer($"DROP PROCEDURE {procName}").ExecuteNonQueryAsync();
            }
        });
    }

    /// <summary>
    /// Firebird invokes stored procedures via EXECUTE PROCEDURE syntax
    /// (<c>ProcWrappingStyle.ExecuteProcedure</c>) and — unlike every style except PostgreSQL's —
    /// ExecutionType is semantically significant: Read renders <c>SELECT * FROM proc(args)</c>
    /// (treating the procedure as a table function), Write renders
    /// <c>EXECUTE PROCEDURE proc(args)</c>. Both syntaxes are proven here, against a real Firebird
    /// server, for the same selectable procedure (a <c>RETURNS</c> clause + <c>SUSPEND</c>) —
    /// <c>fakeDb</c> can't prove either PSQL shape actually executes.
    /// </summary>
    [SkippableFact]
    public Task StoredProc_ExecuteProcedure_BothReadAndWriteSyntax_WorkOnFirebird()
    {
        return RunTestAgainstProvidersAsync(new[] { SupportedDatabase.Firebird }, async (provider, context) =>
        {
            var procName = context.WrapObjectName("sp_pengdows_fb_test");
            var createSql =
                $"CREATE OR ALTER PROCEDURE {procName} (input_value INTEGER)\n" +
                "RETURNS (output_value INTEGER)\n" +
                "AS\n" +
                "BEGIN\n" +
                "  output_value = input_value + 1;\n" +
                "  SUSPEND;\n" +
                "END";

            await context.CreateSqlContainer(createSql).ExecuteNonQueryAsync();

            try
            {
                // Read style: SELECT * FROM proc(args) — proc treated as a table function.
                await using (var readContainer = context.CreateSqlContainer("sp_pengdows_fb_test"))
                {
                    readContainer.AddParameterWithValue("input_value", DbType.Int32, 41);
                    var readResult =
                        await readContainer.ExecuteScalarRequiredAsync<int>(CommandType.StoredProcedure);
                    Assert.Equal(42, readResult);
                }

                // Write style: EXECUTE PROCEDURE proc(args) — explicit ExecutionType.Write wrap.
                await using (var writeSc = context.CreateSqlContainer("sp_pengdows_fb_test"))
                {
                    writeSc.AddParameterWithValue("input_value", DbType.Int32, 99);
                    var wrapped = writeSc.WrapForStoredProc(ExecutionType.Write);
                    Assert.StartsWith("EXECUTE PROCEDURE", wrapped, StringComparison.OrdinalIgnoreCase);

                    await using var execContainer = context.CreateSqlContainer(wrapped);
                    execContainer.AddParameterWithValue("input_value", DbType.Int32, 99);
                    var writeResult = await execContainer.ExecuteScalarRequiredAsync<int>();
                    Assert.Equal(100, writeResult);
                }

                Output.WriteLine($"{provider}: EXECUTE PROCEDURE read+write syntax both verified live.");
            }
            finally
            {
                await context.CreateSqlContainer($"DROP PROCEDURE {procName}").ExecuteNonQueryAsync();
            }
        });
    }

    /// <summary>
    /// Every database whose dialect has a <see cref="ProcWrappingStyle"/> creates, invokes through
    /// <c>WrapForStoredProc</c>, and drops a real procedure (ported from the testbed's
    /// TestStoredProcReturnValue when its check battery moved here). A database with a wrapping
    /// style but no case below fails, so a new database cannot silently go untested; one with
    /// <see cref="ProcWrappingStyle.None"/> has no procedure support (a capability, not a skip).
    /// </summary>
    [SkippableFact]
    public async Task StoredProc_WrapForStoredProc_InvokesARealProcedureOnEveryProcCapableDatabase()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (context.ProcWrappingStyle == ProcWrappingStyle.None)
            {
                Output.WriteLine($"{provider}: capability - ProcWrappingStyle.None, no stored procedure support");
                return;
            }

            await using var sc = context.CreateSqlContainer();
            switch (provider)
            {
                case SupportedDatabase.SqlServer:
                {
                    var name = context.WrapObjectName("dbo") + context.CompositeIdentifierSeparator +
                               context.WrapObjectName("ReturnFive");
                    sc.Query.Append($"CREATE OR ALTER PROCEDURE {name} AS BEGIN RETURN 5 END");
                    await sc.ExecuteNonQueryAsync();
                    Assert.Equal(5, await InvokeAsync(sc, "dbo.ReturnFive", ExecutionType.Read, captureReturn: true));
                    await DropProcAsync(sc, $"DROP PROCEDURE {name}");
                    break;
                }

                case SupportedDatabase.Snowflake:
                {
                    var name = context.WrapObjectName("sp_pengdows_test");
                    sc.Query.Append(
                        $"CREATE OR REPLACE PROCEDURE {name}()\n  RETURNS VARCHAR\n  LANGUAGE SQL\nAS $$\n" +
                        "  BEGIN\n    RETURN CURRENT_TIMESTAMP()::VARCHAR;\n  END\n$$");
                    await sc.ExecuteNonQueryAsync();
                    sc.Clear();
                    sc.Query.Append("sp_pengdows_test");
                    var wrapped = sc.WrapForStoredProc(ExecutionType.Read);
                    sc.Clear();
                    sc.Query.Append(wrapped);
                    Assert.False(string.IsNullOrWhiteSpace(await sc.ExecuteScalarOrNullAsync<string>()));
                    await DropProcAsync(sc, $"DROP PROCEDURE {name}()");
                    break;
                }

                case SupportedDatabase.MySql:
                case SupportedDatabase.AuroraMySql:
                case SupportedDatabase.MariaDb:
                {
                    // CALL `proc`() returns the body's SELECT as a result set. MySqlConnector and
                    // MySql.Data both send CREATE PROCEDURE ... BEGIN ... END as one statement.
                    var name = context.WrapObjectName("sp_pengdows_test");
                    sc.Query.Append($"CREATE PROCEDURE {name}()\nBEGIN\n  SELECT 42;\nEND");
                    await sc.ExecuteNonQueryAsync();
                    Assert.Equal(42, await InvokeAsync(sc, "sp_pengdows_test", ExecutionType.Write));
                    await DropProcAsync(sc, $"DROP PROCEDURE {name}");
                    break;
                }

                case SupportedDatabase.SingleStore:
                {
                    // SingleStore rejects MySQL's bare SELECT body ("unexpected end of function
                    // definition"); ECHO SELECT returns a result set from a procedure.
                    var name = context.WrapObjectName("sp_pengdows_test");
                    sc.Query.Append($"CREATE PROCEDURE {name}() AS\nBEGIN\n  ECHO SELECT 42;\nEND");
                    await sc.ExecuteNonQueryAsync();
                    Assert.Equal(42, await InvokeAsync(sc, "sp_pengdows_test", ExecutionType.Write));
                    await DropProcAsync(sc, $"DROP PROCEDURE {name}");
                    break;
                }

                case SupportedDatabase.SapHana:
                {
                    var name = context.WrapObjectName("sp_pengdows_test");
                    sc.Query.Append(
                        $"CREATE PROCEDURE {name} ()\nLANGUAGE SQLSCRIPT\nAS\nBEGIN\n" +
                        "  SELECT 42 AS RESULT_VAL FROM DUMMY;\nEND");
                    await sc.ExecuteNonQueryAsync();
                    Assert.Equal(42, await InvokeAsync(sc, "sp_pengdows_test", ExecutionType.Read));
                    await DropProcAsync(sc, $"DROP PROCEDURE {name}");
                    break;
                }

                case SupportedDatabase.PostgreSql:
                case SupportedDatabase.AuroraPostgreSql:
                case SupportedDatabase.CockroachDb:
                case SupportedDatabase.YugabyteDb:
                {
                    // Read path: SELECT * FROM "fn"(); a SQL function works on PostgreSQL,
                    // CockroachDB 22.2+ and YugabyteDB alike.
                    var name = context.WrapObjectName("fn_pengdows_test");
                    sc.Query.Append(
                        $"CREATE OR REPLACE FUNCTION {name}()\nRETURNS INTEGER\nLANGUAGE SQL\nAS $$\n  SELECT 42;\n$$");
                    await sc.ExecuteNonQueryAsync();
                    Assert.Equal(42, await InvokeAsync(sc, "fn_pengdows_test", ExecutionType.Read));
                    await DropProcAsync(sc, $"DROP FUNCTION {name}()");
                    break;
                }

                case SupportedDatabase.Oracle:
                {
                    // BEGIN "proc"; END; — Oracle procedures return no result set; the call must run.
                    var name = context.WrapObjectName("sp_pengdows_test");
                    sc.Query.Append($"CREATE OR REPLACE PROCEDURE {name} AS BEGIN NULL; END;");
                    await sc.ExecuteNonQueryAsync();
                    sc.Clear();
                    sc.Query.Append("sp_pengdows_test");
                    var wrapped = sc.WrapForStoredProc(ExecutionType.Write);
                    sc.Clear();
                    sc.Query.Append(wrapped);
                    await sc.ExecuteNonQueryAsync();
                    await DropProcAsync(sc, $"DROP PROCEDURE {name}");
                    break;
                }

                case SupportedDatabase.Firebird:
                {
                    // Selectable procedure (SUSPEND), read as SELECT * FROM "proc".
                    var name = context.WrapObjectName("sp_pengdows_test");
                    sc.Query.Append(
                        $"CREATE OR ALTER PROCEDURE {name}\nRETURNS (result_val INTEGER)\nAS\nBEGIN\n" +
                        "  result_val = 42;\n  SUSPEND;\nEND");
                    await sc.ExecuteNonQueryAsync();
                    Assert.Equal(42, await InvokeAsync(sc, "sp_pengdows_test", ExecutionType.Read));
                    await DropProcAsync(sc, $"DROP PROCEDURE {name}");
                    break;
                }

                case SupportedDatabase.InterBase:
                {
                    // InterBase rejects CREATE OR ALTER PROCEDURE and DROP ... IF EXISTS, so drop a
                    // leftover first (the database is persistent) and tolerate its absence.
                    var name = context.WrapObjectName("sp_pengdows_test");
                    sc.Query.Append($"DROP PROCEDURE {name}");
                    try
                    {
                        await sc.ExecuteNonQueryAsync();
                    }
                    catch (pengdows.crud.exceptions.DatabaseException)
                    {
                    }

                    sc.Clear();
                    sc.Query.Append(
                        $"CREATE PROCEDURE {name}\nRETURNS (result_val INTEGER)\nAS\nBEGIN\n" +
                        "  result_val = 42;\n  SUSPEND;\nEND");
                    await sc.ExecuteNonQueryAsync();
                    Assert.Equal(42, await InvokeAsync(sc, "sp_pengdows_test", ExecutionType.Read));
                    await DropProcAsync(sc, $"DROP PROCEDURE {name}");
                    break;
                }

                case SupportedDatabase.Db2:
                {
                    var name = context.WrapObjectName("sp_pengdows_test");
                    sc.Query.Append(
                        $"CREATE OR REPLACE PROCEDURE {name}()\nDYNAMIC RESULT SETS 1\nLANGUAGE SQL\nBEGIN\n" +
                        "  DECLARE c1 CURSOR WITH RETURN TO CALLER FOR SELECT 42 FROM SYSIBM.SYSDUMMY1;\n" +
                        "  OPEN c1;\nEND");
                    await sc.ExecuteNonQueryAsync();
                    Assert.Equal(42, await InvokeAsync(sc, "sp_pengdows_test", ExecutionType.Write));
                    await DropProcAsync(sc, $"DROP PROCEDURE {name}");
                    break;
                }

                case SupportedDatabase.SybaseASE:
                {
                    // ASE has no CREATE OR ALTER; the single-argument OBJECT_ID() is the form this
                    // build accepts (the two-argument overload fails, verified live).
                    var name = context.WrapObjectName("sp_pengdows_test");
                    sc.Query.Append($"IF OBJECT_ID('sp_pengdows_test') IS NOT NULL DROP PROCEDURE {name}");
                    await sc.ExecuteNonQueryAsync();
                    sc.Clear();
                    sc.Query.Append($"CREATE PROCEDURE {name} AS BEGIN RETURN 5 END");
                    await sc.ExecuteNonQueryAsync();
                    Assert.Equal(5, await InvokeAsync(sc, "sp_pengdows_test", ExecutionType.Read, captureReturn: true));
                    await DropProcAsync(sc, $"DROP PROCEDURE {name}");
                    break;
                }

                case SupportedDatabase.Informix:
                {
                    // EXECUTE PROCEDURE "proc"(?) returns the RETURNING value as a one-row result
                    // on both the read and the write path (verified live).
                    var name = context.WrapObjectName("sp_pengdows_test");
                    sc.Query.Append($"CREATE PROCEDURE {name}(a INT) RETURNING INT;\n  RETURN a + 41;\nEND PROCEDURE");
                    await sc.ExecuteNonQueryAsync();
                    foreach (var executionType in new[] { ExecutionType.Read, ExecutionType.Write })
                    {
                        sc.Clear();
                        sc.Query.Append("sp_pengdows_test");
                        sc.AddParameterWithValue("a", DbType.Int32, 1);
                        var wrapped = sc.WrapForStoredProc(executionType);
                        sc.Query.Clear();
                        sc.Query.Append(wrapped);
                        Assert.Equal(42, await sc.ExecuteScalarOrNullAsync<int>());
                    }

                    await DropProcAsync(sc, $"DROP PROCEDURE {name}");
                    break;
                }

                default:
                    Assert.Fail($"{provider} has ProcWrappingStyle.{context.ProcWrappingStyle} but no case here - add one.");
                    break;
            }
        });
    }

    private static async Task<int?> InvokeAsync(ISqlContainer sc, string procName, ExecutionType executionType,
        bool captureReturn = false)
    {
        sc.Clear();
        sc.Query.Append(procName);
        var wrapped = sc.WrapForStoredProc(executionType, captureReturn: captureReturn);
        sc.Clear();
        sc.Query.Append(wrapped);
        return await sc.ExecuteScalarOrNullAsync<int>();
    }

    private static async Task DropProcAsync(ISqlContainer sc, string dropSql)
    {
        sc.Clear();
        sc.Query.Append(dropSql);
        await sc.ExecuteNonQueryAsync();
    }
}
