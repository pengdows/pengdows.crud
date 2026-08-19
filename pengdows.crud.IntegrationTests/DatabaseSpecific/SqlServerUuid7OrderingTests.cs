using System.Data;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

/// <summary>
/// Establishes the live SQL Server ordering behavior that prevents canonical UUIDv7 values from
/// providing chronological clustered-index locality when stored as <c>uniqueidentifier</c>.
/// </summary>
[Collection("IntegrationTests")]
public sealed class SqlServerUuid7OrderingTests : DatabaseTestBase
{
    public SqlServerUuid7OrderingTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders()
    {
        return [SupportedDatabase.SqlServer];
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await using var container = context.CreateSqlContainer(@"
CREATE TABLE [dbo].[uuid7_ordering_test] (
    [id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    [label] NVARCHAR(20) NOT NULL
)");
        await container.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task CanonicalUuid7Values_DoNotSortChronologicallyAsUniqueIdentifiers()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.SqlServer, async context =>
        {
            // Both values are valid UUIDv7 values. The first has the earlier timestamp, while the
            // second has the lower final six bytes that SQL Server gives higher comparison weight.
            var earlier = new Guid("018f0000-0000-7000-8000-ffffffffffff");
            var later = new Guid("018f0000-0001-7000-8000-000000000000");

            await InsertAsync(context, earlier, "earlier");
            await InsertAsync(context, later, "later");

            var orderedLabels = new List<string>();
            await using var query = context.CreateSqlContainer(
                "SELECT [label] FROM [dbo].[uuid7_ordering_test] ORDER BY [id]");
            await using var reader = await query.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                orderedLabels.Add(reader.GetString(0));
            }

            Assert.Equal(["later", "earlier"], orderedLabels);
        });
    }

    private static async Task InsertAsync(IDatabaseContext context, Guid id, string label)
    {
        await using var container = context.CreateSqlContainer(
            "INSERT INTO [dbo].[uuid7_ordering_test] ([id], [label]) VALUES (@id, @label)");
        container.AddParameterWithValue("id", DbType.Guid, id);
        container.AddParameterWithValue("label", DbType.String, label);
        await container.ExecuteNonQueryAsync();
    }
}
