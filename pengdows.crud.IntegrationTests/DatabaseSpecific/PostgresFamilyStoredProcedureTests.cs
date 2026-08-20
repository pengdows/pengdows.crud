using System.Data;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Proves, against REAL PostgreSQL-family servers, that <c>PostgresProcWrappingStrategy</c>'s
/// write-path branch (<c>CALL procedure_name(args)</c>, PostgreSQL 11+ real procedures) actually
/// executes — not just its read-path branch (<c>SELECT * FROM function_name()</c>).
/// <para>
/// Every other coverage of <c>ProcWrappingStyle.PostgreSQL</c> (unit tests, and the testbed/
/// integration checks that existed before this test) only ever created a <c>FUNCTION</c> and
/// called <see cref="ISqlContainer.WrapForStoredProc"/> with <see cref="ExecutionType.Read"/>.
/// The <c>CALL</c> branch was asserted only at the SQL-string level
/// (<c>ProcWrappingStyleTests.WrapTestPostgreSQL</c>) and never proven to execute against a real
/// server — the exact same category of gap found and fixed for Db2's <c>ProcWrappingStyle.Call</c>
/// (see <c>Db2StoredProcedureTests</c>). PostgreSQL returns a procedure's <c>INOUT</c> parameter
/// value as a one-row result set from <c>CALL</c> itself — no provider-level output-parameter
/// binding is needed, unlike SQL Server's <c>OUTPUT</c>.
/// </para>
/// <para>
/// Scoped to PostgreSQL, CockroachDB, and YugabyteDB — the three databases that share
/// <c>PostgresProcWrappingStrategy</c> via <c>ProcWrappingStyle.PostgreSQL</c>.
/// </para>
/// </summary>
[Collection("IntegrationTests")]
public class PostgresFamilyStoredProcedureTests : DatabaseTestBase
{
    public PostgresFamilyStoredProcedureTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return base.GetSupportedProviders()
            .Where(p => p is SupportedDatabase.PostgreSql or SupportedDatabase.CockroachDb or SupportedDatabase.YugabyteDb)
            .ToList();
    }

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        return Task.CompletedTask;
    }

    [SkippableFact]
    public Task StoredProc_Call_RealProcedureWithInoutParameter_ExecutesViaCall()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
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
