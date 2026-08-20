using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Proves, against a REAL Db2 instance, that pengdows.crud can call a Db2 stored procedure via
/// <c>ProcWrappingStyle.Call</c> (<c>CALL "proc"()</c> — the same SQL-standard style already used
/// for MySQL/MariaDB) and consume the result set the procedure returns through a cursor declared
/// <c>WITH RETURN TO CALLER</c>. <c>Db2Dialect</c> previously left <c>ProcWrappingStyle</c> at the
/// <c>SqlDialect</c> base default of <c>None</c>, which silently disabled stored-procedure support
/// through this library even though Db2 itself fully supports procedures — <c>fakeDb</c> can't
/// prove the real SQL PL body/cursor syntax actually executes, only a live server can.
/// </summary>
[Collection("IntegrationTests")]
public class Db2StoredProcedureTests : DatabaseTestBase
{
    public Db2StoredProcedureTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return base.GetSupportedProviders().Where(p => p == SupportedDatabase.Db2).ToList();
    }

    protected override Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        return Task.CompletedTask;
    }

    [SkippableFact]
    public Task StoredProc_Call_ReturnsResultSetFromWithReturnCursor()
    {
        return RunTestAgainstAllProvidersAsync(async (provider, context) =>
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
}
