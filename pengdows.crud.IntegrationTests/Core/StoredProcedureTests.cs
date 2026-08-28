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
    public async Task StoredProc_ReturnValueCapture_WorksOnSqlServer()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (provider != SupportedDatabase.SqlServer)
            {
                // Verify that captureReturn: true throws on dialects that don't support it (non-SqlServer)
                // Note: SQL Server uses Exec style and is the only one currently supporting @RETURN_VALUE capture.
                if (context.ProcWrappingStyle != ProcWrappingStyle.Exec)
                {
                    var container = context.CreateSqlContainer("SomeProc");
                    Assert.Throws<NotSupportedException>(() =>
                        container.WrapForStoredProc(ExecutionType.Write, includeParameters: false,
                            captureReturn: true));
                }

                return;
            }

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
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (provider != SupportedDatabase.SqlServer)
            {
                return;
            }

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
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (provider != SupportedDatabase.Db2)
            {
                return;
            }

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
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (provider is not (SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb
                or SupportedDatabase.YugabyteDb))
            {
                return;
            }

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
}
