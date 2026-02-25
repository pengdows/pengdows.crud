using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using System.Data;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Verifies database-specific parameter binding behavior: markers, duplicate usage, and NULL semantics.
/// </summary>
[Collection("IntegrationTests")]
public class ParameterBindingTests : DatabaseTestBase
{
    public ParameterBindingTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateRoundTripTableAsync();
    }

    [SkippableFact]
    public async Task BindSameParameterMultipleTimes_WorksSuccessfully()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = new TableGateway<RoundTripEntity, long>(context);
            var id = DateTime.UtcNow.Ticks;
            await helper.CreateAsync(new RoundTripEntity { Id = id, TextValue = "DualBind" }, context);

            // Derive specific marker from MakeParameterName
            var marker = context.MakeParameterName("p").Substring(0, 1);
            if (!char.IsLetterOrDigit(marker[0]))
            {
                /* standard prefix */
            }
            else
            {
                marker = ""; /* positional or weird */
            }

            // Safer way: just use the context to format names
            var p0 = context.MakeParameterName("p0");
            var sql =
                $"SELECT COUNT(*) FROM {context.WrapObjectName("round_trip_entity")} WHERE {context.WrapObjectName("id")} = {p0} OR {context.WrapObjectName("id")} = {p0}";

            // Act
            await using var container = context.CreateSqlContainer(sql);
            container.AddParameterWithValue("p0", DbType.Int64, id);

            var count = await container.ExecuteScalarRequiredAsync<long>();

            // Assert
            Assert.Equal(1, count);
        });
    }

    [SkippableFact]
    public async Task NullSemantics_EqualityVsIsNull()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = new TableGateway<RoundTripEntity, long>(context);
            var id = DateTime.UtcNow.Ticks + 1;
            await helper.CreateAsync(new RoundTripEntity { Id = id, TextValue = "NullTest", TextNullable = null },
                context);

            var p0 = context.MakeParameterName("p0");
            var table = context.WrapObjectName("round_trip_entity");
            var col = context.WrapObjectName("text_nullable");

            // Standard SQL: col = NULL is UNKNOWN (false), should return 0
            var sqlEqual = $"SELECT COUNT(*) FROM {table} WHERE {col} = {p0}";
            await using var containerEqual = context.CreateSqlContainer(sqlEqual);
            containerEqual.AddParameterWithValue("p0", DbType.String, (string?)null);
            var countEqual = await containerEqual.ExecuteScalarRequiredAsync<long>();
            Assert.Equal(0, countEqual);

            // Standard SQL: col IS NULL is TRUE, should return 1
            var sqlIsNull = $"SELECT COUNT(*) FROM {table} WHERE {col} IS NULL";
            await using var containerIsNull = context.CreateSqlContainer(sqlIsNull);
            var countIsNull = await containerIsNull.ExecuteScalarRequiredAsync<long>();
            Assert.Equal(1, countIsNull);
        });
    }

    [SkippableFact]
    public async Task TypeMatrixBinding_SurvivesExecution()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Verifies that AddParameterWithValue with various DbTypes doesn't throw and binds correctly
            var pInt = context.MakeParameterName("pInt");
            var pLong = context.MakeParameterName("pLong");
            var pDecimal = context.MakeParameterName("pDecimal");
            var pBool = context.MakeParameterName("pBool");
            var pString = context.MakeParameterName("pString");

            var sql = provider == SupportedDatabase.Firebird
                ? $"SELECT CAST({pInt} AS INTEGER), CAST({pLong} AS BIGINT), CAST({pDecimal} AS DECIMAL(18,4)), CAST({pBool} AS SMALLINT), CAST({pString} AS VARCHAR(100)) FROM RDB$DATABASE"
                : $"SELECT {pInt}, {pLong}, {pDecimal}, {pBool}, {pString}";

            if (provider == SupportedDatabase.Oracle)
            {
                sql += " FROM DUAL";
            }

            await using var container = context.CreateSqlContainer(sql);
            container.AddParameterWithValue("pInt", DbType.Int32, 42);
            container.AddParameterWithValue("pLong", DbType.Int64, 1234567890123L);
            container.AddParameterWithValue("pDecimal", DbType.Decimal, 123.45m);
            container.AddParameterWithValue("pBool", DbType.Boolean, true);
            container.AddParameterWithValue("pString", DbType.String, "Hello");

            await using var reader = await container.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());

            // Just verify we can read them back - coercion is tested in RoundTripTests
            Assert.NotNull(reader.GetValue(0));
            Assert.NotNull(reader.GetValue(1));
            Assert.NotNull(reader.GetValue(2));
            Assert.NotNull(reader.GetValue(3));
            Assert.NotNull(reader.GetValue(4));
        });
    }
}