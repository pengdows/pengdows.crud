using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using System.Data;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Verifies transaction resilience: rollback on exception and connection re-usability.
/// </summary>
[Collection("IntegrationTests")]
public class TransactionResilienceTests : DatabaseTestBase
{
    public TransactionResilienceTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateTestTableAsync();
    }

    [Fact]
    public async Task ThrowMidTransaction_RollsBack_AndConnectionIsReusable()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var auditResolver = GetAuditResolver();
            var helper = new TableGateway<TestTable, long>(context, auditResolver);
            var id = DateTime.UtcNow.Ticks;

            // 1. Start transaction and insert a row
            await using (var tx = context.BeginTransaction())
            {
                await helper.CreateAsync(new TestTable { Id = id, Name = NameEnum.Test, Value = 1 }, tx);
                
                // Verify row exists inside transaction
                var countInside = await tx.CreateSqlContainer($"SELECT COUNT(*) FROM {context.WrapObjectName("test_table")} WHERE {context.WrapObjectName("id")} = {id}").ExecuteScalarRequiredAsync<long>();
                Assert.Equal(1, countInside);

                // 2. Simulate failure (letting DisposeAsync handle the implicit rollback)
                // We don't call tx.Commit()
            }

            // 3. Assert rollback: row should NOT exist in the main context
            var countAfter = await context.CreateSqlContainer($"SELECT COUNT(*) FROM {context.WrapObjectName("test_table")} WHERE {context.WrapObjectName("id")} = {id}").ExecuteScalarRequiredAsync<long>();
            Assert.Equal(0, countAfter);

            // 4. Verify connection reuse: insert a new row on the main context (should work)
            var newId = id + 1;
            var success = await helper.CreateAsync(new TestTable { Id = newId, Name = NameEnum.Test2, Value = 2 }, context);
            Assert.True(success);

            var retrieved = await helper.RetrieveOneAsync(newId, context);
            Assert.NotNull(retrieved);
            Assert.Equal(NameEnum.Test2, retrieved!.Name);
        });
    }
}
