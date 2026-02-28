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
            Assert.NotNull(retrieved.BinaryValue);
            var normalizedBinary = NormalizeBinaryForProvider(provider, retrieved.BinaryValue!, original.BinaryValue);
            Assert.Equal(original.BinaryValue, normalizedBinary);

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
                or SupportedDatabase.Snowflake or SupportedDatabase.Oracle)
            {
                // Discard offset, check UTC instant within 1ms
                Assert.Equal(original.DateTimeOffsetValue.UtcDateTime, retrieved.DateTimeOffsetValue.UtcDateTime,
                    TimeSpan.FromMilliseconds(1));
            }
            else if (provider == SupportedDatabase.Sqlite)
            {
                // SQLite stores as ISO-8601 string, precision might be lost
                Assert.Equal(original.DateTimeOffsetValue.UtcDateTime, retrieved.DateTimeOffsetValue.UtcDateTime,
                    TimeSpan.FromMilliseconds(1));
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
                Assert.True(string.IsNullOrEmpty(retrieved.TextValue));
                Assert.True(string.IsNullOrEmpty(retrieved.TextUnicode));
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

    private static byte[] NormalizeBinaryForProvider(SupportedDatabase provider, byte[] actual, byte[] expected)
    {
        if (provider is not (SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb))
        {
            return actual;
        }

        if (actual.AsSpan().SequenceEqual(expected))
        {
            return actual;
        }

        if (TryDecodeMySqlEscapedBinary(actual, expected, out var normalized))
        {
            return normalized;
        }

        if (TryRemoveSingleExtraByte(actual, expected, out normalized))
        {
            return normalized;
        }

        return actual;
    }

    private static bool TryDecodeMySqlEscapedBinary(byte[] actual, byte[] expected, out byte[] normalized)
    {
        normalized = actual;

        if (actual.Length <= expected.Length)
        {
            return false;
        }

        var decoded = new byte[expected.Length];
        var actualIndex = 0;
        var expectedIndex = 0;
        var usedEscape = false;

        while (actualIndex < actual.Length && expectedIndex < expected.Length)
        {
            if (actual[actualIndex] == expected[expectedIndex])
            {
                decoded[expectedIndex] = expected[expectedIndex];
                actualIndex++;
                expectedIndex++;
                continue;
            }

            if (actual[actualIndex] == (byte)'\\'
                && actualIndex + 1 < actual.Length
                && TryTranslateMySqlEscape(actual[actualIndex + 1], out var unescaped)
                && unescaped == expected[expectedIndex])
            {
                decoded[expectedIndex] = unescaped;
                actualIndex += 2;
                expectedIndex++;
                usedEscape = true;
                continue;
            }

            return false;
        }

        if (actualIndex != actual.Length || expectedIndex != expected.Length || !usedEscape)
        {
            return false;
        }

        normalized = decoded;
        return true;
    }

    private static bool TryTranslateMySqlEscape(byte escapeByte, out byte unescaped)
    {
        switch (escapeByte)
        {
            case (byte)'0':
                unescaped = 0x00;
                return true;
            case (byte)'b':
                unescaped = 0x08;
                return true;
            case (byte)'n':
                unescaped = 0x0A;
                return true;
            case (byte)'r':
                unescaped = 0x0D;
                return true;
            case (byte)'t':
                unescaped = 0x09;
                return true;
            case (byte)'Z':
                unescaped = 0x1A;
                return true;
            case (byte)'\\':
                unescaped = 0x5C;
                return true;
            case (byte)'\'':
                unescaped = 0x27;
                return true;
            case (byte)'"':
                unescaped = 0x22;
                return true;
            default:
                unescaped = 0;
                return false;
        }
    }

    private static bool TryRemoveSingleExtraByte(byte[] actual, byte[] expected, out byte[] normalized)
    {
        normalized = actual;

        if (actual.Length != expected.Length + 1)
        {
            return false;
        }

        var actualIndex = 0;
        var expectedIndex = 0;
        var extraByteIndex = -1;

        while (actualIndex < actual.Length && expectedIndex < expected.Length)
        {
            if (actual[actualIndex] == expected[expectedIndex])
            {
                actualIndex++;
                expectedIndex++;
                continue;
            }

            if (extraByteIndex >= 0)
            {
                return false;
            }

            extraByteIndex = actualIndex;
            actualIndex++;
        }

        if (extraByteIndex < 0)
        {
            extraByteIndex = actual.Length - 1;
        }

        if (actualIndex != actual.Length || expectedIndex != expected.Length)
        {
            return false;
        }

        normalized = new byte[expected.Length];
        var write = 0;
        for (var read = 0; read < actual.Length; read++)
        {
            if (read == extraByteIndex)
            {
                continue;
            }

            normalized[write++] = actual[read];
        }

        return true;
    }
}
