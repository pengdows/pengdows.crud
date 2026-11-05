using System;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

/// <summary>
/// Fractional-second precision is a property of the column the product stores a value in, and fakeDb has
/// no DDL to read it from, so each emulated product keeps the precision docs/type-support-matrix.md
/// documents: Sybase ASE's BIGDATETIME holds microseconds, SingleStore's TIME(6) and Oracle's INTERVAL DAY
/// TO SECOND(6) hold microseconds, and the 7th fraction digit is truncated, never rounded. PostgreSQL keeps
/// the full 100-nanosecond tick.
/// </summary>
public class FakeDataStorePrecisionTests
{
    private static async Task<object?> RoundTripAsync(SupportedDatabase product, object value)
    {
        var conn = new fakeDbConnection(new FakeDataStore())
        {
            EmulatedProduct = product,
            EnableDataPersistence = true,
        };
        conn.Open();

        using (var insert = conn.CreateCommand())
        {
            insert.CommandText = "INSERT INTO t (id, v) VALUES (@i0, @i1)";
            var pid = insert.CreateParameter();
            pid.ParameterName = "@i0";
            pid.Value = 1;
            insert.Parameters.Add(pid);
            var pv = insert.CreateParameter();
            pv.ParameterName = "@i1";
            pv.Value = value;
            insert.Parameters.Add(pv);
            await insert.ExecuteNonQueryAsync();
        }

        using var select = conn.CreateCommand();
        select.CommandText = "SELECT v FROM t WHERE id = @p0";
        var p = select.CreateParameter();
        p.ParameterName = "@p0";
        p.Value = 1;
        select.Parameters.Add(p);
        using var reader = await select.ExecuteReaderAsync();
        Assert.True(reader.Read());
        return reader.GetValue(0);
    }

    private static readonly DateTime SevenDigits = new(2026, 10, 5, 13, 45, 30, DateTimeKind.Utc);

    [Fact]
    public async Task SybaseAse_DateTime_KeepsMicrosecondsAndTruncatesTheSeventhDigit()
    {
        var value = SevenDigits.AddTicks(1234567);

        var stored = await RoundTripAsync(SupportedDatabase.SybaseASE, value);

        Assert.Equal(SevenDigits.AddTicks(1234560), stored);
    }

    [Fact]
    public async Task SingleStore_TimeSpan_KeepsMicrosecondsAndTruncatesTheSeventhDigit()
    {
        var value = new TimeSpan(0, 13, 45, 30).Add(TimeSpan.FromTicks(1234567));

        var stored = await RoundTripAsync(SupportedDatabase.SingleStore, value);

        Assert.Equal(new TimeSpan(0, 13, 45, 30).Add(TimeSpan.FromTicks(1234560)), stored);
    }

    [Fact]
    public async Task Oracle_TimeSpan_KeepsMicrosecondsAndTruncatesTheSeventhDigit()
    {
        var value = new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(1234567));

        var stored = await RoundTripAsync(SupportedDatabase.Oracle, value);

        Assert.Equal(new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(1234560)), stored);
    }

    [Fact]
    public async Task PostgreSql_DateTime_KeepsTheFullTick()
    {
        var value = SevenDigits.AddTicks(1234567);

        var stored = await RoundTripAsync(SupportedDatabase.PostgreSql, value);

        Assert.Equal(value, stored);
    }
}
