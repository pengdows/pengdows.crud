using pengdows.crud.enums;
using pengdows.crud.IntegrationTests.Infrastructure;
using System.Data;
using testbed;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Integration tests for full row round-trip fidelity using RoundTripEntity.
/// Verifies that data survives storage and retrieval across all providers.
/// </summary>
[Collection("IntegrationTests")]
public class RoundTripTests : DatabaseTestBase
{
    public RoundTripTests(ITestOutputHelper output, IntegrationTestFixture fixture) : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var tableCreator = new TestTableCreator(context);
        await tableCreator.CreateRoundTripTableAsync();
    }

    [SkippableFact]
    public async Task RoundTrip_FullEntity_PreservesDataFidelity()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = new TableGateway<RoundTripEntity, long>(context);
            var original = new RoundTripEntity
            {
                Id = DateTime.UtcNow.Ticks,
                TextValue = "  Leading and trailing whitespace  ",
                TextUnicode = "Unicode: 🚀 CJK: 漢字 📧",
                TextNullable = null,
                IntValue = -1234567,
                LongValue = long.MaxValue - 100,
                DecimalValue = 1234567.89123456m,
                BoolValue = true,
                DateTimeOffsetValue = provider == SupportedDatabase.Firebird
                    ? new DateTimeOffset(2026, 2, 21, 19, 30, 45, 123, TimeSpan.Zero)
                    : new DateTimeOffset(2026, 2, 21, 14, 30, 45, 123, TimeSpan.FromHours(-5)),
                GuidValue = Guid.NewGuid(),
                BinaryValue = new byte[] { 0x00, 0xFF, 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x7F }
            };

            // Act
            await helper.CreateAsync(original, context);
            var retrieved = await helper.RetrieveOneAsync(original.Id, context);

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal(original.Id, retrieved!.Id);
            Assert.Equal(original.TextValue, retrieved.TextValue);
            Assert.Equal(original.TextUnicode, retrieved.TextUnicode);
            Assert.Equal(original.IntValue, retrieved.IntValue);
            Assert.Equal(original.LongValue, retrieved.LongValue);
            Assert.Equal(original.BoolValue, retrieved.BoolValue);
            Assert.Equal(original.GuidValue, retrieved.GuidValue);
            Assert.Equal(original.BinaryValue, retrieved.BinaryValue);

            // Nullable string handling
            if (provider == SupportedDatabase.Oracle)
            {
                // Oracle coerces '' to NULL, so we test null first
                Assert.Null(retrieved.TextNullable);
            }
            else
            {
                Assert.Null(retrieved.TextNullable);
            }

            // Decimal precision assertions
            if (provider == SupportedDatabase.Sqlite)
            {
                // SQLite uses REAL (double) for decimals
                Assert.Equal((double)original.DecimalValue, (double)retrieved.DecimalValue, 0.000001);
            }
            else
            {
                Assert.Equal(original.DecimalValue, retrieved.DecimalValue);
            }

            // DateTimeOffset assertions
            if (provider is SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.Firebird
                or SupportedDatabase.Snowflake)
            {
                // Discard offset, check UTC instant within 1ms
                Assert.Equal(original.DateTimeOffsetValue.UtcDateTime, retrieved.DateTimeOffsetValue.UtcDateTime, TimeSpan.FromMilliseconds(1));
            }
            else if (provider == SupportedDatabase.Sqlite)
            {
                // SQLite stores as ISO-8601 string, precision might be lost
                Assert.Equal(original.DateTimeOffsetValue.UtcDateTime, retrieved.DateTimeOffsetValue.UtcDateTime, TimeSpan.FromMilliseconds(1));
            }
            else
            {
                // Exact match for tz-aware providers
                Assert.Equal(original.DateTimeOffsetValue, retrieved.DateTimeOffsetValue);
            }
        });
    }

    [SkippableFact]
    public async Task RoundTrip_EmptyValues_HandledCorrectly()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            // Arrange
            var helper = new TableGateway<RoundTripEntity, long>(context);
            var original = new RoundTripEntity
            {
                Id = DateTime.UtcNow.Ticks + 1,
                TextValue = "",
                TextUnicode = "",
                TextNullable = "",
                IntValue = 0,
                LongValue = 0,
                DecimalValue = 0m,
                BoolValue = false,
                DateTimeOffsetValue = DateTimeOffset.UnixEpoch,
                GuidValue = Guid.Empty,
                BinaryValue = Array.Empty<byte>()
            };

            // Act
            await helper.CreateAsync(original, context);
            var retrieved = await helper.RetrieveOneAsync(original.Id, context);

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal(original.Id, retrieved!.Id);
            
            if (provider == SupportedDatabase.Oracle)
            {
                // Oracle treats empty string as NULL
                Assert.Null(retrieved.TextValue);
                Assert.Null(retrieved.TextUnicode);
                Assert.Null(retrieved.TextNullable);
            }
            else
            {
                Assert.Equal("", retrieved.TextValue);
                Assert.Equal("", retrieved.TextUnicode);
                Assert.Equal("", retrieved.TextNullable);
            }

            Assert.Equal(0, retrieved.IntValue);
            Assert.Equal(0, retrieved.LongValue);
            Assert.Equal(0m, retrieved.DecimalValue);
            Assert.False(retrieved.BoolValue);
            Assert.Equal(Guid.Empty, retrieved.GuidValue);
            
            // Note: some providers return null for empty binary, others empty array
            if (retrieved.BinaryValue != null)
            {
                Assert.Empty(retrieved.BinaryValue);
            }
        });
    }
}
