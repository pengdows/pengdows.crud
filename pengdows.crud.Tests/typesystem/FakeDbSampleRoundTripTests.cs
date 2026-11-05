using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// Serialize and deserialize every type sample on every emulated database, through the library's
/// own write and read paths. Each sample (every CLR type in <see cref="TypeCompletenessTests.Samples"/>)
/// is written with <see cref="TableGateway{TEntity,TRowID}.CreateAsync"/> into a row whose column is
/// declared with the sample's DbType, then read back with <see cref="TableGateway{TEntity,TRowID}.RetrieveOneAsync"/>.
/// The value must come back the same. fakeDb's store keeps the bound value as it was bound, so this
/// proves the library's write and read are inverses for each declared type on each product; what a
/// real server does with the stored value is the job of the live integration matrix.
/// </summary>
[Collection("TypeRegistry")]
public class FakeDbSampleRoundTripTests
{
    private static readonly Dictionary<DbType, Type> RowTypes = new()
    {
        [DbType.Binary] = typeof(RoundTripBinary<>),
        [DbType.Boolean] = typeof(RoundTripBoolean<>),
        [DbType.Byte] = typeof(RoundTripByte<>),
        [DbType.Date] = typeof(RoundTripDate<>),
        [DbType.DateTime2] = typeof(RoundTripDateTime2<>),
        [DbType.DateTimeOffset] = typeof(RoundTripDateTimeOffset<>),
        [DbType.Decimal] = typeof(RoundTripDecimal<>),
        [DbType.Double] = typeof(RoundTripDouble<>),
        [DbType.Guid] = typeof(RoundTripGuid<>),
        [DbType.Int16] = typeof(RoundTripInt16<>),
        [DbType.Int32] = typeof(RoundTripInt32<>),
        [DbType.Int64] = typeof(RoundTripInt64<>),
        [DbType.Object] = typeof(RoundTripObject<>),
        [DbType.SByte] = typeof(RoundTripSByte<>),
        [DbType.Single] = typeof(RoundTripSingle<>),
        [DbType.String] = typeof(RoundTripString<>),
        [DbType.Time] = typeof(RoundTripTime<>),
        [DbType.UInt16] = typeof(RoundTripUInt16<>),
        [DbType.UInt32] = typeof(RoundTripUInt32<>),
        [DbType.UInt64] = typeof(RoundTripUInt64<>),
    };

    private static readonly MethodInfo RoundTripMethod = typeof(FakeDbSampleRoundTripTests)
        .GetMethod(nameof(RoundTripOne), BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public async Task EverySampleSerializesAndDeserializesOnEveryEmulatedDatabase()
    {
        var failures = new List<string>();
        var products = Enum.GetValues<SupportedDatabase>().Where(p => p != SupportedDatabase.Unknown);

        foreach (var sample in TypeCompletenessTests.Samples())
        {
            foreach (var product in products)
            {
                var outcome = await RunAsync(sample, product);
                if (outcome != null)
                {
                    failures.Add($"{product} {sample.Label} ({sample.DbType}): {outcome}");
                }
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} round trips failed:\n" + string.Join("\n", failures));
    }

    private static Task<string?> RunAsync(TypeCompletenessTests.Sample sample, SupportedDatabase product)
    {
        var rowType = RowTypes[sample.DbType].MakeGenericType(sample.Clr);
        var method = RoundTripMethod.MakeGenericMethod(rowType);
        return (Task<string?>)method.Invoke(null, new object[] { sample, product })!;
    }

    // The products that keep only microseconds, as docs/type-support-matrix.md documents them: Sybase ASE's
    // BIGDATETIME, SingleStore's TIME(6), Oracle's INTERVAL DAY TO SECOND(6) and the PostgreSQL family's interval. The 7th digit is truncated, never
    // rounded. The dialect formats these values to six digits when it binds them, so the store receives them cut.
    private static bool KeepsOnlyMicroseconds(SupportedDatabase product, Type clr)
    {
        var temporal = clr == typeof(DateTime) || clr == typeof(DateTimeOffset)
            || clr == typeof(TimeSpan) || clr == typeof(TimeOnly);
        return product switch
        {
            SupportedDatabase.SybaseASE => temporal,
            SupportedDatabase.SingleStore => clr == typeof(TimeSpan) || clr == typeof(TimeOnly),
            SupportedDatabase.Oracle => clr == typeof(TimeSpan) || clr == typeof(TimeOnly)
                || clr == typeof(IntervalDaySecond),
            // The PostgreSQL family's interval holds microseconds (the Npgsql interval, and the ISO text without Npgsql).
            SupportedDatabase.PostgreSql or SupportedDatabase.YugabyteDb or SupportedDatabase.AuroraPostgreSql =>
                clr == typeof(IntervalDaySecond),
            _ => false,
        };
    }

    // The same value with its fraction cut to microseconds, for a value the product stores at that precision.
    private static object? ToMicroseconds(object? value)
    {
        const long Microsecond = 10;
        return value switch
        {
            DateTime dt => new DateTime(dt.Ticks - dt.Ticks % Microsecond, dt.Kind),
            DateTimeOffset dto => new DateTimeOffset(dto.Ticks - dto.Ticks % Microsecond, dto.Offset),
            TimeSpan ts => new TimeSpan(ts.Ticks - ts.Ticks % Microsecond),
            TimeOnly to => new TimeOnly(to.Ticks - to.Ticks % Microsecond),
            IntervalDaySecond interval => IntervalDaySecond.FromTimeSpan(
                TimeSpan.FromTicks(interval.TotalTime.Ticks - interval.TotalTime.Ticks % Microsecond)),
            _ => value,
        };
    }

    // A DateTimeOffset is stored as its UTC instant on databases with no offset (docs/utc-and-time.md),
    // so the offset a value comes back with is not part of what must round-trip; the instant is.
    private static string Comparable(object? value) => value switch
    {
        DateTimeOffset offset => "instant:" + offset.UtcDateTime.Ticks,
        // An unspecified DateTime is UTC on every path (docs/utc-and-time.md), so its kind is not compared.
        DateTime dt when dt.Kind != DateTimeKind.Local => "instant:" + DateTime.SpecifyKind(dt, DateTimeKind.Utc).Ticks,
        _ => TypeCompletenessTests.Canonical(value),
    };

    // The refusals the library makes on purpose, with the reason its message names: SingleStore has no GEOMETRY type.
    private static string? RefusedByDesign(SupportedDatabase product, Type clr) =>
        product == SupportedDatabase.SingleStore && clr == typeof(pengdows.crud.types.valueobjects.Geometry) ? "GEOMETRY"
        : IsMySqlFamily(product) && clr == typeof(System.Collections.BitArray) ? "BIT(n)"
        : null;

    // A BitArray written to a MySQL-family column needs the column's declared width (NativeType = "BIT(n)"); a generic
    // column without one is refused, not stored as the characters of its bits.
    private static bool IsMySqlFamily(SupportedDatabase product) =>
        product is SupportedDatabase.MySql or SupportedDatabase.MariaDb or SupportedDatabase.TiDb
            or SupportedDatabase.SingleStore or SupportedDatabase.AuroraMySql;

    private static async Task<string?> RoundTripOne<TRow>(TypeCompletenessTests.Sample sample, SupportedDatabase product)
        where TRow : class, IRoundTripRow, new()
    {
        try
        {
            // Rows persist only when the factory's store is switched on; without it INSERT is discarded.
            var factory = new fakeDbFactory(product) { EnableDataPersistence = true };
            using var context = new DatabaseContext(
                $"Data Source=roundtrip;EmulatedProduct={product}", factory, new TypeMapRegistry());
            var gateway = new TableGateway<TRow, int>(context);

            // Informix has no NULL collection element, and CollectionLiteralFormat refuses one before any SQL runs.
            // A nullable-element array sample must therefore be refused on Informix, and nowhere else.
            if (product == SupportedDatabase.Informix && sample.Label.EndsWith("?[]", StringComparison.Ordinal))
            {
                IRoundTripRow refused = new TRow { };
                refused.Id = 1;
                refused.Value = sample.Value();
                try
                {
                    await gateway.CreateAsync((TRow)refused);
                    return "accepted a NULL collection element; Informix refuses it";
                }
                catch (ArgumentException ex) when (ex.Message.Contains("NULL", StringComparison.Ordinal))
                {
                    return null;
                }
            }

            // A value the product cannot hold is refused by the library, loudly, with the reason: the row is not written.
            if (RefusedByDesign(product, sample.Clr) is { } reason)
            {
                IRoundTripRow unstorable = new TRow { };
                unstorable.Id = 1;
                unstorable.Value = sample.Value();
                try
                {
                    await gateway.CreateAsync((TRow)unstorable);
                    return "accepted a value the product cannot hold: " + reason;
                }
                catch (NotSupportedException ex) when (ex.Message.Contains(reason, StringComparison.Ordinal))
                {
                    return null;
                }
            }

            // Streams and readers are consumed by reading them, so the expected text comes from its own
            // instance and the row is written with a second one.
            // A product that keeps only microseconds for a type stores the value cut to that precision.
            var expectedValue = sample.Value();
            if (KeepsOnlyMicroseconds(product, sample.Clr))
            {
                expectedValue = ToMicroseconds(expectedValue);
            }

            var expectedText = Comparable(expectedValue);
            IRoundTripRow row = new TRow { };
            row.Id = 1;
            row.Value = sample.Value();
            await gateway.CreateAsync((TRow)row);

            var back = await gateway.RetrieveOneAsync(1);
            if (back == null)
            {
                return "row not found after create";
            }

            var actualText = Comparable(back.Value);
            return expectedText == actualText ? null : $"wrote {expectedText}, read {actualText}";
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }
}
