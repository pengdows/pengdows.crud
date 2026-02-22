using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using System.Data;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Verifies stored procedure return value capture (SQL Server) and 
/// correct NotSupported behavior on other providers.
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
                        container.WrapForStoredProc(ExecutionType.Write, includeParameters: false, captureReturn: true));
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
                var wrappedSql = container.WrapForStoredProc(ExecutionType.Write, includeParameters: false, captureReturn: true);
                
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
}
