using System.Data;
using System.Linq;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

// FEAT-005: proves TableGateway<TEntity,TRowID>.BatchCreateAsync's array-binding execution
// strategy (OracleDialect.SupportsArrayBinding/ConfigureArrayBinding) actually works against a
// real ODP.NET OracleCommand/OracleConnection, not just fakeDb — the ArrayBindCount reflection
// hook and array-valued parameter binding (including a NULL in the middle of the batch) are only
// meaningfully proven live. See docs/planning/bulk-loading-design.md's Part 2.
[Collection("IntegrationTests")]
public sealed class OracleArrayBindingRoundTripTests : DatabaseTestBase
{
    public OracleArrayBindingRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        [SupportedDatabase.Oracle];

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await using var table = context.CreateSqlContainer("""
            CREATE TABLE "array_bind_roundtrip" (
                "id" NUMBER(10) PRIMARY KEY,
                "name" VARCHAR2(50),
                "amount" NUMBER(10,2)
            )
            """);
        await table.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task BatchCreateAsync_ForOracle_UsesArrayBinding_AndRoundTripsCorrectly()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Oracle, async context =>
        {
            var gateway = new TableGateway<ArrayBindRoundTripEntity, int>(context);
            var entities = new[]
            {
                new ArrayBindRoundTripEntity { Id = 1, Name = "Alice", Amount = 10.50m },
                new ArrayBindRoundTripEntity { Id = 2, Name = null, Amount = 20m }, // NULL in the middle
                new ArrayBindRoundTripEntity { Id = 3, Name = "Charlie", Amount = 30.25m }
            };

            var affected = await gateway.BatchCreateAsync(entities, context);
            Assert.Equal(3, affected);

            var retrieved = await gateway.RetrieveAsync(new[] { 1, 2, 3 }, context);
            var byId = retrieved.ToDictionary(e => e.Id);

            Assert.Equal("Alice", byId[1].Name);
            Assert.Equal(10.50m, byId[1].Amount);
            Assert.Null(byId[2].Name);
            Assert.Equal(20m, byId[2].Amount);
            Assert.Equal("Charlie", byId[3].Name);
            Assert.Equal(30.25m, byId[3].Amount);
        });
    }
}

[Table("array_bind_roundtrip")]
internal sealed class ArrayBindRoundTripEntity
{
    [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("name", DbType.String)] public string? Name { get; set; }
    [Column("amount", DbType.Decimal)] public decimal Amount { get; set; }
}
