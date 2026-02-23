using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using System.Data.Common;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Verifies that the framework correctly surfaces database-specific errors
/// as expected exceptions (DbException or derived).
/// </summary>
[Collection("IntegrationTests")]
public class DiagnosticsTests : DatabaseTestBase
{
    public DiagnosticsTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();
    }

    [SkippableFact]
    public async Task SyntaxError_SurfacesAsDbException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Act & Assert
            // Intentional syntax error: MISSING FROM or invalid keyword
            var sql = "SELECT * FROM";
            if (provider == SupportedDatabase.Oracle)
            {
                sql = "SELECT * FROM-INVALID";
            }

            await using var container = context.CreateSqlContainer(sql);

            await Assert.ThrowsAnyAsync<DbException>(async () => await container.ExecuteNonQueryAsync());
        });
    }

    [SkippableFact]
    public async Task UniqueConstraintViolation_SurfacesAsDbException()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (provider == SupportedDatabase.Snowflake)
            {
                Output.WriteLine("Skipping unique constraint test for Snowflake (constraints are not enforced)");
                return;
            }

            // Arrange
            var auditResolver = GetAuditResolver();
            var helper = new TableGateway<TestTable, long>(context, auditResolver);
            var id = DateTime.UtcNow.Ticks;
            var entity = new TestTable { Id = id, Name = NameEnum.Test, Value = 1 };
            await helper.CreateAsync(entity, context);

            // Act & Assert: Insert duplicate ID
            var duplicate = new TestTable { Id = id, Name = NameEnum.Test2, Value = 2 };

            await Assert.ThrowsAnyAsync<DbException>(async () => await helper.CreateAsync(duplicate, context));
        });
    }
}