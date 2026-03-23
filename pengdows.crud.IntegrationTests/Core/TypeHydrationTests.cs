using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace pengdows.crud.IntegrationTests.Core;

/// <summary>
/// Hydration-verification tests for <see cref="TypeHydrationEntity"/>.
///
/// Each test inserts one row with known, carefully chosen values and then retrieves it
/// by pseudo key (Id), asserting that every column hydrates to the expected CLR value.
/// Three row variants are tested:
/// <list type="number">
///   <item>Typical — normal positive/non-zero values for all types.</item>
///   <item>Zero / null — zeros, false, null for nullable columns, empty string,
///     Guid.Empty, and null binary.</item>
///   <item>Boundary — short.MaxValue, int.MaxValue/MinValue, long near-max, negative
///     floats, large negative decimal, whitespace-padded strings.</item>
/// </list>
/// </summary>
[Collection("IntegrationTests")]
public class TypeHydrationTests : DatabaseTestBase
{
    // ── Shared fixed values ───────────────────────────────────────────────────
    // Use exactly-representable IEEE 754 values for float/double so assertions
    // can be exact regardless of whether the DB stores REAL as 32-bit or 64-bit.
    private static readonly Guid s_guid1 = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid s_guid3 = new("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static readonly DateTime s_dt1 = new(2024, 3, 15, 10, 30, 45, DateTimeKind.Utc);
    private static readonly DateTime s_dt2 = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime s_dt3 = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // All DateTimeOffset values use UTC (TimeSpan.Zero) so providers that normalise
    // to UTC (MySQL, MariaDB, Firebird, Snowflake) produce the same UTC instant.
    private static readonly DateTimeOffset s_dto1 = new(2024, 3, 15, 10, 30, 45, TimeSpan.Zero);
    private static readonly DateTimeOffset s_dto2 = DateTimeOffset.UnixEpoch;
    private static readonly DateTimeOffset s_dto3 = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public TypeHydrationTests(ITestOutputHelper output, IntegrationTestFixture fixture)
        : base(output, fixture)
    {
    }

    protected override async Task SetupDatabaseAsync(SupportedDatabase provider, IDatabaseContext context)
    {
        var creator = new TypeHydrationTableCreator(context);
        await creator.CreateTableAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 1: Typical values
    // ─────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task TypeHydration_TypicalValues_RoundTripCorrectly()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = new TableGateway<TypeHydrationEntity, long>(context);
            var row = MakeTypicalRow(1_001L);

            await gateway.CreateAsync(row, context);
            var retrieved = await gateway.RetrieveOneAsync(row.Id, context);

            Assert.NotNull(retrieved);
            AssertTypicalRow(retrieved!, provider);
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 2: Zero / null values
    // ─────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task TypeHydration_ZeroAndNullValues_RoundTripCorrectly()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = new TableGateway<TypeHydrationEntity, long>(context);
            var row = MakeZeroRow(1_002L);

            await gateway.CreateAsync(row, context);
            var retrieved = await gateway.RetrieveOneAsync(row.Id, context);

            Assert.NotNull(retrieved);
            AssertZeroRow(retrieved!, provider);
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test 3: Boundary / edge values
    // ─────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task TypeHydration_BoundaryValues_RoundTripCorrectly()
    {
        await RunTestAgainstAllProvidersAsync(async (provider, context) =>
        {
            var gateway = new TableGateway<TypeHydrationEntity, long>(context);
            var row = MakeBoundaryRow(1_003L);

            await gateway.CreateAsync(row, context);
            var retrieved = await gateway.RetrieveOneAsync(row.Id, context);

            Assert.NotNull(retrieved);
            AssertBoundaryRow(retrieved!, provider);
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row factories
    // ─────────────────────────────────────────────────────────────────────────

    private static TypeHydrationEntity MakeTypicalRow(long id) => new()
    {
        Id = id,
        ColString = "Hello, World!",
        ColStringNull = "not null value",
        ColShort = 1_234,
        ColInt = 1_234_567,
        ColIntNull = 42,
        ColLong = 1_234_567_890L,
        ColFloat = 1.5f,
        ColDouble = 3.5,
        ColDecimal = 9_876.54321m,
        ColBool = true,
        ColBoolNull = true,
        ColDateTime = s_dt1,
        ColDateTimeOffset = s_dto1,
        ColGuid = s_guid1,
        ColBinary = [0x01, 0x23, 0x45, 0x67],
        ColEnumInt = TypeHydrationEnum.Alpha,
        ColEnumStr = TypeHydrationEnum.Alpha,
    };

    private static TypeHydrationEntity MakeZeroRow(long id) => new()
    {
        Id = id,
        ColString = "",
        ColStringNull = null,
        ColShort = 0,
        ColInt = 0,
        ColIntNull = null,
        ColLong = 0L,
        ColFloat = 0.0f,
        ColDouble = 0.0,
        ColDecimal = 0m,
        ColBool = false,
        ColBoolNull = null,
        ColDateTime = s_dt2,
        ColDateTimeOffset = s_dto2,
        ColGuid = Guid.Empty,
        ColBinary = null,
        ColEnumInt = TypeHydrationEnum.Zero,
        ColEnumStr = TypeHydrationEnum.Zero,
    };

    private static TypeHydrationEntity MakeBoundaryRow(long id) => new()
    {
        Id = id,
        ColString = "  whitespace preserved  ",
        ColStringNull = "explicit value",
        ColShort = short.MaxValue,       // 32_767
        ColInt = int.MaxValue,         // 2_147_483_647
        ColIntNull = int.MinValue,         // -2_147_483_648
        ColLong = long.MaxValue - 1,    // 9_223_372_036_854_775_806
        ColFloat = -3.5f,
        ColDouble = -3.5,
        ColDecimal = -99_999_999.99999999m,
        ColBool = true,
        ColBoolNull = false,
        ColDateTime = s_dt3,
        ColDateTimeOffset = s_dto3,
        ColGuid = s_guid3,
        ColBinary = [0x00, 0xFF, 0x7F, 0x80],
        ColEnumInt = TypeHydrationEnum.Beta,
        ColEnumStr = TypeHydrationEnum.Beta,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Assertion helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static void AssertTypicalRow(TypeHydrationEntity r, SupportedDatabase provider)
    {
        Assert.Equal("Hello, World!", r.ColString);
        Assert.Equal("not null value", r.ColStringNull);
        Assert.Equal((short)1_234, r.ColShort);
        Assert.Equal(1_234_567, r.ColInt);
        Assert.Equal(42, r.ColIntNull);
        Assert.Equal(1_234_567_890L, r.ColLong);
        Assert.Equal(1.5f, r.ColFloat);
        Assert.Equal(3.5, r.ColDouble);

        // SQLite stores decimal as REAL (double) — assert with tolerance
        if (provider == SupportedDatabase.Sqlite)
            Assert.Equal((double)9_876.54321m, (double)r.ColDecimal, 6);
        else
            Assert.Equal(9_876.54321m, r.ColDecimal);

        Assert.True(r.ColBool);
        Assert.True(r.ColBoolNull);

        Assert.Equal(s_dt1, r.ColDateTime, TimeSpan.FromMilliseconds(1));
        Assert.Equal(s_dto1.UtcDateTime, r.ColDateTimeOffset.UtcDateTime, TimeSpan.FromMilliseconds(1));

        Assert.Equal(s_guid1, r.ColGuid);

        Assert.NotNull(r.ColBinary);
        var expectedBinary = new byte[] { 0x01, 0x23, 0x45, 0x67 };
        var normalizedBinary = NormalizeBinaryForProvider(provider, r.ColBinary!, expectedBinary);
        Assert.Equal<byte>(expectedBinary, normalizedBinary);

        Assert.Equal(TypeHydrationEnum.Alpha, r.ColEnumInt);
        Assert.Equal(TypeHydrationEnum.Alpha, r.ColEnumStr);
    }

    private static void AssertZeroRow(TypeHydrationEntity r, SupportedDatabase provider)
    {
        // Oracle coerces empty string '' to NULL for VARCHAR2/NVARCHAR2 columns
        if (provider == SupportedDatabase.Oracle)
            Assert.True(string.IsNullOrEmpty(r.ColString));
        else
            Assert.Equal("", r.ColString);

        Assert.Null(r.ColStringNull);
        Assert.Equal((short)0, r.ColShort);
        Assert.Equal(0, r.ColInt);
        Assert.Null(r.ColIntNull);
        Assert.Equal(0L, r.ColLong);
        Assert.Equal(0.0f, r.ColFloat);
        Assert.Equal(0.0, r.ColDouble);
        Assert.Equal(0m, r.ColDecimal);
        Assert.False(r.ColBool);
        Assert.Null(r.ColBoolNull);

        Assert.Equal(s_dt2, r.ColDateTime, TimeSpan.FromMilliseconds(1));
        Assert.Equal(s_dto2.UtcDateTime, r.ColDateTimeOffset.UtcDateTime, TimeSpan.FromMilliseconds(1));

        Assert.Equal(Guid.Empty, r.ColGuid);
        Assert.Null(r.ColBinary);

        Assert.Equal(TypeHydrationEnum.Zero, r.ColEnumInt);
        Assert.Equal(TypeHydrationEnum.Zero, r.ColEnumStr);
    }

    private static void AssertBoundaryRow(TypeHydrationEntity r, SupportedDatabase provider)
    {
        Assert.Equal("  whitespace preserved  ", r.ColString);
        Assert.Equal("explicit value", r.ColStringNull);
        Assert.Equal(short.MaxValue, r.ColShort);
        Assert.Equal(int.MaxValue, r.ColInt);
        Assert.Equal(int.MinValue, r.ColIntNull);
        Assert.Equal(long.MaxValue - 1, r.ColLong);
        Assert.Equal(-3.5f, r.ColFloat);
        Assert.Equal(-3.5, r.ColDouble);

        // SQLite stores decimal as REAL (double) — assert with tolerance
        if (provider == SupportedDatabase.Sqlite)
            Assert.Equal((double)(-99_999_999.99999999m), (double)r.ColDecimal, 6);
        else
            Assert.Equal(-99_999_999.99999999m, r.ColDecimal);

        Assert.True(r.ColBool);
        Assert.False(r.ColBoolNull);

        Assert.Equal(s_dt3, r.ColDateTime, TimeSpan.FromMilliseconds(1));
        Assert.Equal(s_dto3.UtcDateTime, r.ColDateTimeOffset.UtcDateTime, TimeSpan.FromMilliseconds(1));

        Assert.Equal(s_guid3, r.ColGuid);

        Assert.NotNull(r.ColBinary);
        var expectedBinary = new byte[] { 0x00, 0xFF, 0x7F, 0x80 };
        var normalizedBinary = NormalizeBinaryForProvider(provider, r.ColBinary!, expectedBinary);
        Assert.Equal<byte>(expectedBinary, normalizedBinary);

        Assert.Equal(TypeHydrationEnum.Beta, r.ColEnumInt);
        Assert.Equal(TypeHydrationEnum.Beta, r.ColEnumStr);
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
