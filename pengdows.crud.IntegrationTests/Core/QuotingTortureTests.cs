using pengdows.crud.enums;
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

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTortureTableAsync();
    }

    [SkippableFact]
    public async Task TortureCRUD_HandlesEvilIdentifiersSuccessfully()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
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
