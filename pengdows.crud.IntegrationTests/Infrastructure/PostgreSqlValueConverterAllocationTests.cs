using System.Net;
using System.Net.NetworkInformation;
using NpgsqlTypes;
using pengdows.crud.enums;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;

namespace pengdows.crud.IntegrationTests.Infrastructure;

/// <summary>
/// The PostgreSQL-family value types (ranges, intervals, inet, cidr, MAC addresses) convert to and from Npgsql's own
/// types. That used to be reflection on every value (GetProperty and GetValue to read, GetConstructor and Invoke with
/// an object[] to write), about a kilobyte per value. Both directions now use accessors compiled once per type, so a
/// conversion allocates little more than the value it produces. No database is needed: these are Npgsql's real types.
/// </summary>
public class PostgreSqlValueConverterAllocationTests
{
    private const SupportedDatabase Postgres = SupportedDatabase.PostgreSql;

    // Bytes allocated per call once warm.
    private static long PerCall(Action call)
    {
        const int calls = 2000;
        for (var i = 0; i < 300; i++)
        {
            call();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < calls; i++)
        {
            call();
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / calls;
    }

    private static void AtMost(long budget, Action call)
    {
        var bytes = PerCall(call);
        Assert.True(bytes <= budget, $"{bytes} B per call, budget {budget} B");
    }

    private static readonly DateTime Lower = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Upper = new(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    // ---- reads: what the provider returned, into the library's value ----

    [Fact]
    public void ReadRangeOfInt_AllocatesNothingBeyondTheResult()
    {
        var converter = new PostgreSqlRangeConverter<int>();
        object provider = new NpgsqlRange<int>(1, true, 10, false);

        AtMost(32, () => converter.TryConvertFromProvider(provider, Postgres, out _));
    }

    [Fact]
    public void ReadRangeOfDateTime_AllocatesNothingBeyondTheResult()
    {
        var converter = new PostgreSqlRangeConverter<DateTime>();
        object provider = new NpgsqlRange<DateTime>(Lower, true, Upper, false);

        AtMost(32, () => converter.TryConvertFromProvider(provider, Postgres, out _));
    }

    // tstzrange comes back as a UTC NpgsqlRange<DateTime>, read into a Range<DateTimeOffset>.
    [Fact]
    public void ReadRangeOfDateTimeOffsetFromDateTimeRange_AllocatesLittle()
    {
        var converter = new PostgreSqlRangeConverter<DateTimeOffset>();
        object provider = new NpgsqlRange<DateTime>(Lower, true, Upper, false);

        AtMost(96, () => converter.TryConvertFromProvider(provider, Postgres, out _));
    }

    [Fact]
    public void ReadInterval_AllocatesNothingBeyondTheResult()
    {
        var converter = new PostgreSqlIntervalConverter();
        object provider = new NpgsqlInterval(14, 3, 3_723_123_456);

        AtMost(32, () => converter.TryConvertFromProvider(provider, Postgres, out _));
    }

    [Fact]
    public void ReadInet_AllocatesNothingBeyondTheResult()
    {
        var converter = new InetConverter();
        object provider = new NpgsqlInet(IPAddress.Parse("192.168.0.1"), 24);

        AtMost(64, () => converter.TryConvertFromProvider(provider, Postgres, out _));
    }

    [Fact]
    public void ReadCidr_AllocatesNothingBeyondTheResult()
    {
        var converter = new CidrConverter();
        object provider = new NpgsqlCidr(IPAddress.Parse("10.0.0.0"), 8);

        // A Cidr holds its canonical (masked) network address: a new IPAddress and its byte array, so about 72 B is
        // the value itself.
        AtMost(80, () => converter.TryConvertFromProvider(provider, Postgres, out _));
    }

    [Fact]
    public void ReadMacAddress_AllocatesLittle()
    {
        var converter = new MacAddressConverter();
        object provider = PhysicalAddress.Parse("08002B010203");

        AtMost(64, () => converter.TryConvertFromProvider(provider, Postgres, out _));
    }

    // ---- writes: the library's value, into what the provider is given ----

    [Fact]
    public void WriteRangeOfInt_AllocatesLittleBeyondTheProviderValue()
    {
        var converter = new PostgreSqlRangeConverter<int>();
        object value = new Range<int>(1, 10, true, false);

        AtMost(128, () => converter.ToProviderValue(value, Postgres));
    }

    [Fact]
    public void WriteRangeOfDateTime_AllocatesLittleBeyondTheProviderValue()
    {
        var converter = new PostgreSqlRangeConverter<DateTime>();
        object value = new Range<DateTime>(Lower, Upper, true, false);

        AtMost(128, () => converter.ToProviderValue(value, Postgres));
    }

    [Fact]
    public void WriteInterval_AllocatesLittleBeyondTheProviderValue()
    {
        var converter = new PostgreSqlIntervalConverter();
        object value = new PostgreSqlInterval(14, 3, 3_723_123_456);

        AtMost(96, () => converter.ToProviderValue(value, Postgres));
    }

    [Fact]
    public void WriteInet_AllocatesLittleBeyondTheProviderValue()
    {
        var converter = new InetConverter();
        object value = Inet.Parse("192.168.0.1/24");

        AtMost(96, () => converter.ToProviderValue(value, Postgres));
    }

    [Fact]
    public void WriteCidr_AllocatesLittleBeyondTheProviderValue()
    {
        var converter = new CidrConverter();
        object value = Cidr.Parse("10.0.0.0/8");

        AtMost(96, () => converter.ToProviderValue(value, Postgres));
    }
}
