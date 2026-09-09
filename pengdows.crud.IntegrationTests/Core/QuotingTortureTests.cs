using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Verifies that the framework correctly quotes identifiers (tables, columns)
/// even when they contain reserved words, spaces, or mixed case.
/// </summary>
[Collection("IntegrationTests")]
public class QuotingTortureTests : DatabaseTestBase
{
    public QuotingTortureTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    // Spanner's identifier naming rules reject a space in a table/column name outright, even
    // through the PostgreSQL interface and even when properly double-quoted — verified live:
    // "P0001: ... table name not valid: Default Order." (the literal, unquoted identifier text
    // appears in Spanner's own error, not a quoting bug on pengdows.crud's side). This is a real
    // Spanner platform limitation distinct from every column-type/constraint gap found elsewhere
    // this session — real PostgreSQL, and every other provider this test runs against, accepts a
    // quoted identifier containing a space without complaint.
    private const string SpannerSkipReason =
        "Spanner's identifier naming rules reject a space in a table/column name, even when quoted";

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        if (provider == SupportedDatabase.Spanner)
        {
            Output.WriteLine($"Skipping torture-table setup for {provider} ({SpannerSkipReason})");
            return;
        }

        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTortureTableAsync();
    }

    [SkippableFact]
    public async Task TortureCRUD_HandlesEvilIdentifiersSuccessfully()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            if (provider == SupportedDatabase.Spanner)
            {
                Output.WriteLine($"Skipping torture CRUD test for {provider} ({SpannerSkipReason})");
                return;
            }

            // Arrange
            var helper = new TableGateway<TortureEntity, long>(context);
            var entity = new TortureEntity
            {
                Id = DateTime.UtcNow.Ticks,
                SelectValue = "Value for Select",
                FromValue = "Value for From",
                MixedCase = "Value for Mixed Case"
            };

            // Act: Create
            await helper.CreateAsync(entity, context);

            // Act: Retrieve
            var retrieved = await helper.RetrieveOneAsync(entity.Id, context);

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal(entity.SelectValue, retrieved!.SelectValue);
            Assert.Equal(entity.FromValue, retrieved.FromValue);
            Assert.Equal(entity.MixedCase, retrieved.MixedCase);

            // Act: Update
            retrieved.SelectValue = "Updated Select";
            await helper.UpdateAsync(retrieved, context);

            // Verify Update
            var updated = await helper.RetrieveOneAsync(entity.Id, context);
            Assert.Equal("Updated Select", updated!.SelectValue);

            // Act: Delete
            var deletedCount = await helper.DeleteAsync(entity.Id, context);
            Assert.Equal(1, deletedCount);
        });
    }
}