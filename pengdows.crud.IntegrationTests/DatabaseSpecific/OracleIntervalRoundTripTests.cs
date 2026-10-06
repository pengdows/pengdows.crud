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
        await DropTableIfExistsAsync(context, "interval_roundtrip");
        await using var table = context.CreateSqlContainer("""
            CREATE TABLE "interval_roundtrip" (
                "id" NUMBER(10) PRIMARY KEY,
                "year_month" INTERVAL YEAR(4) TO MONTH NOT NULL,
                "day_second" INTERVAL DAY(9) TO SECOND(6) NOT NULL,
                "day_second7" INTERVAL DAY(9) TO SECOND(7) NULL,
                "span6" INTERVAL DAY(9) TO SECOND(6) NULL
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

    // Oracle rounds fractional seconds a column can't hold (confirmed live: .9999999 into SECOND(6)
    // became the next second), so an IntervalDaySecond is sent with six digits, Oracle's default
    // precision: truncated, never rounded. A null one binds typed (it failed with ORA-50028).
    [SkippableFact]
    public async Task DaySecond_IsTruncatedNeverRounded_AndNullBinds()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Oracle, async context =>
        {
            var row = new OracleIntervalEntity
            {
                Id = 2,
                YearMonth = new IntervalYearMonth(0, 1),
                DaySecond = new IntervalDaySecond(1, new TimeSpan(2, 3, 4) + TimeSpan.FromTicks(9999999)),
                DaySecond7 = null,
                Span6 = null
            };

            var gateway = new TableGateway<OracleIntervalEntity, int>(context);
            await gateway.CreateAsync(row, context);
            var actual = await gateway.RetrieveOneAsync(row.Id, context);

            Assert.NotNull(actual);
            Assert.Null(actual!.DaySecond7);
            Assert.Null(actual.Span6);
            Assert.Equal(new IntervalDaySecond(1, new TimeSpan(2, 3, 4) + TimeSpan.FromTicks(9999990)), actual.DaySecond);
        });
    }

    // DRY-028: a TimeSpan was sent with all its digits and Oracle rounded it into SECOND(6); the gateway
    // now learns the column's scale and truncates the value to it.
    [SkippableFact]
    public async Task TimeSpan_IntoSixDigitColumn_IsTruncatedNeverRounded()
    {
        await RunTestAgainstProviderAsync(SupportedDatabase.Oracle, async context =>
        {
            var row = new OracleIntervalEntity
            {
                Id = 3,
                YearMonth = new IntervalYearMonth(0, 1),
                DaySecond = new IntervalDaySecond(0, TimeSpan.Zero),
                Span6 = new TimeSpan(1, 23, 59, 59) + TimeSpan.FromTicks(9999999)
            };

            var gateway = new TableGateway<OracleIntervalEntity, int>(context);
            await gateway.CreateAsync(row, context);
            var actual = await gateway.RetrieveOneAsync(row.Id, context);

            Assert.Equal(new TimeSpan(1, 23, 59, 59) + TimeSpan.FromTicks(9999990), actual!.Span6);
        });
    }
}

[Table("interval_roundtrip")]
internal sealed class OracleIntervalEntity
{
    [Id][Column("id", DbType.Int32)] public int Id { get; set; }
    [Column("year_month", DbType.Object)] public IntervalYearMonth YearMonth { get; set; }
    [Column("day_second", DbType.Object)] public IntervalDaySecond DaySecond { get; set; }
    [Column("day_second7", DbType.Object)] public IntervalDaySecond? DaySecond7 { get; set; }
    [Column("span6", DbType.Object)] public TimeSpan? Span6 { get; set; }
}
