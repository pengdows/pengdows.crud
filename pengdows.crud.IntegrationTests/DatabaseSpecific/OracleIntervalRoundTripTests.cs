using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using pengdows.crud.types.valueobjects;
using Xunit;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.DatabaseSpecific;

[Collection("IntegrationTests")]
public sealed class OracleIntervalRoundTripTests : DatabaseTestBase
{
    public OracleIntervalRoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture) { }

    protected override IEnumerable<SupportedDatabase> GetSupportedProviders() =>
        [SupportedDatabase.Oracle];

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        await using var table = context.CreateSqlContainer("""
            CREATE TABLE "interval_roundtrip" (
                "id" NUMBER(10) PRIMARY KEY,
                "year_month" INTERVAL YEAR(4) TO MONTH NOT NULL,
                "day_second" INTERVAL DAY(9) TO SECOND(6) NOT NULL
            )
            """);
        await table.ExecuteNonQueryAsync();
    }

    [SkippableFact]
    public async Task OracleIntervals_RoundTripThroughCrudMapper()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Oracle, async context =>
        {
            var expected = new OracleIntervalEntity
            {
                Id = 1,
                YearMonth = new IntervalYearMonth(3, 6),
                DaySecond = new IntervalDaySecond(5, new TimeSpan(12, 30, 45) + TimeSpan.FromTicks(5000000))
            };

            var gateway = new TableGateway<OracleIntervalEntity, int>(context);
            await gateway.CreateAsync(expected, context);
            var actual = await gateway.RetrieveOneAsync(expected.Id, context);

            Assert.NotNull(actual);
            Assert.Equal(expected.YearMonth, actual!.YearMonth);
            Assert.Equal(expected.DaySecond, actual.DaySecond);
        });
    }
}

[Table("interval_roundtrip")]
internal sealed class OracleIntervalEntity
{
    [Id][Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("year_month", DbType.Object)] public IntervalYearMonth YearMonth { get; set; }
    [Column("day_second", DbType.Object)] public IntervalDaySecond DaySecond { get; set; }
}
