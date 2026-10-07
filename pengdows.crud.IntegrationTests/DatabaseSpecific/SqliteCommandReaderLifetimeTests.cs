using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.enums;
using Microsoft.Data.Sqlite;
using Xunit;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

public sealed class SqliteCommandReaderLifetimeTests
{
    [SkippableFact]
    public async Task MicrosoftDataSqlite_DisposingCommandBeforeReader_PreventsReaderConsumption()
    {
        Skip.IfNot(IntegrationTestConfiguration.IsEnabled(SupportedDatabase.Sqlite), "Sqlite is not enabled for this test run.");

        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 42";
        await using var reader = await command.ExecuteReaderAsync();

        command.Dispose();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await reader.ReadAsync());
    }
}
