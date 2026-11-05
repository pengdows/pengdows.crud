using pengdows.crud.@internal;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.TypeFallback;

/// <summary>
/// DuckDB's interval is its own struct (months, days, micro-seconds), which its reader returns through GetInterval; its
/// plain read is a TimeSpan, which cannot hold months. The reader takes the struct's members as they are.
/// </summary>
public class DuckDbIntervalTests
{
    // A stand-in with DuckDB.NET's member names and types (the provider is not loaded in the unit tests).
    private readonly record struct DuckInterval(int Months, int Days, long Micros);

    [Fact]
    public void TheStructsMembersAreTheIntervalAsItIs()
    {
        var interval = DuckDbIntervalReader.ToPostgreSqlInterval(new DuckInterval(27, 3, 5_000_000L));

        Assert.Equal(new PostgreSqlInterval(27, 3, 5_000_000L), interval);
    }

    // DuckDB.NET's struct stores microseconds as UInt64 (its bindings), so the members are read as any integer.
    private readonly record struct DuckIntervalUnsignedMicros(int Months, int Days, ulong Micros);

    [Fact]
    public void AnUnsignedMicrosecondMemberIsReadAsItsValue()
    {
        var interval = DuckDbIntervalReader.ToPostgreSqlInterval(new DuckIntervalUnsignedMicros(14, 3, 5_000_000UL));

        Assert.Equal(new PostgreSqlInterval(14, 3, 5_000_000L), interval);
    }

    [Theory]
    [InlineData("DuckDB.NET.Data.DuckDBDataReader", true)]
    [InlineData("pengdows.crud.fakeDb.fakeDbDataReader", false)]
    [InlineData("Npgsql.NpgsqlDataReader", false)]
    [InlineData(null, false)]
    public void OnlyADuckDbReaderIsDuckDb(string? readerTypeName, bool expected)
    {
        Assert.Equal(expected, DuckDbIntervalReader.IsDuckDbReaderTypeName(readerTypeName));
    }
}
