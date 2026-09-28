using pengdows.crud.enums;
using pengdows.crud.infrastructure;
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
                // Capability: without supplementary-plane support (Informix) the emoji are left out;
                // the CJK text still exercises non-ASCII storage.
                TextUnicode = pengdows.crud.dialects.InternalSqlDialectExtensions
                    .SupportsSupplementaryCharacters(context.GetDialect())
                    ? "Unicode: 🚀 CJK: 漢字 📧"
                    : "Unicode: CJK: 漢字",
                TextNullable = null,
                IntValue = -1234567,
                LongValue = long.MaxValue - 100,
                DecimalValue = 1234567.89123456m,
                BoolValue = true,
                DateTimeOffsetValue = new DateTimeOffset(2026, 2, 21, 14, 30, 45, 123, TimeSpan.FromHours(-5)),
                GuidValue = Guid.NewGuid(),
                BinaryValue = new byte[] { 0x00, 0xFF, 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x7F }
            };

            // Act
            await helper.CreateAsync(original, context);
            var retrieved = await helper.RetrieveOneAsync(original.Id, context);

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal(original.Id, retrieved!.Id);
            // Capability: a dialect whose storage or provider drops trailing blanks
            // (PreservesTrailingWhitespace = false: Sybase ASE, Informix) must still keep the leading ones.
            Assert.Equal(
                context.Dialect.PreservesTrailingWhitespace ? original.TextValue : original.TextValue.TrimEnd(),
                retrieved.TextValue);
            Assert.Equal(original.TextUnicode, retrieved.TextUnicode);
            Assert.Equal(original.IntValue, retrieved.IntValue);
            Assert.Equal(original.LongValue, retrieved.LongValue);
            Assert.Equal(original.BoolValue, retrieved.BoolValue);
            Assert.Equal(original.GuidValue, retrieved.GuidValue);
            Assert.NotNull(retrieved.BinaryValue);
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

            // The instant must survive to the millisecond everywhere. Whether the offset itself is kept
            // depends on the column type (most databases store the UTC instant), and DateTimeOffset
            // equality compares instants anyway.
            Assert.Equal(original.DateTimeOffsetValue.UtcDateTime, retrieved.DateTimeOffsetValue.UtcDateTime,
                TimeSpan.FromMilliseconds(1));
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
                Assert.True(string.IsNullOrEmpty(retrieved.TextValue));
                Assert.True(string.IsNullOrEmpty(retrieved.TextUnicode));
                Assert.Null(retrieved.TextNullable);
            }
            else if (!context.Dialect.PreservesTrailingWhitespace)
            {
                // Capability: Sybase ASE stores '' in a varchar as a single blank, which differs from
                // '' only in trailing whitespace (PreservesTrailingWhitespace = false).
                Assert.Equal("", retrieved.TextValue?.TrimEnd());
                Assert.Equal("", retrieved.TextUnicode?.TrimEnd());
                Assert.Equal("", retrieved.TextNullable?.TrimEnd());
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
                if (pengdows.crud.dialects.InternalSqlDialectExtensions.PreservesEmptyBinary(context.GetDialect()))
                {
                    Assert.Empty(retrieved.BinaryValue);
                }
                else
                {
                    // Capability: Sybase ASE stores a zero-length binary as a single 0x00 byte
                    // (PreservesEmptyBinary = false), as it stores '' as a single blank.
                    Assert.Equal(new byte[] { 0x00 }, retrieved.BinaryValue);
                }
            }
        });
    }
}
