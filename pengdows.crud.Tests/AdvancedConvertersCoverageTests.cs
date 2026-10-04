using System.Linq;
using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class AdvancedConvertersCoverageTests
{

    [Fact]
    public void MacAddressConverter_FormatsForPostgres_AndReadsPhysicalAddress()
    {
        // Ported from the 2.0 branch. Production behavior differs from 2.0: ConvertToProvider now
        // returns the underlying PhysicalAddress for every provider (including PostgreSQL) rather
        // than a colon-separated string — Npgsql's macaddr handler binds from PhysicalAddress
        // directly (see MacAddressConverter.cs remarks). Updated to assert the current contract.
        var converter = new MacAddressConverter();
        var mac = MacAddress.Parse("00:11:22:33:44:55");

        // PostgreSQL now gets the underlying PhysicalAddress like every other provider: Npgsql
        // rejects a string with NpgsqlDbType.MacAddr (confirmed live, BP-112).
        var providerValue = converter.ToProviderValue(mac, SupportedDatabase.PostgreSql);
        var providerPhysical = Assert.IsType<PhysicalAddress>(providerValue);
        Assert.Equal(mac.Address, providerPhysical);

        var physical = new PhysicalAddress(new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 });
        var fromPhysical = (MacAddress?)converter.FromProviderValue(physical, SupportedDatabase.Unknown);
        Assert.NotNull(fromPhysical);
        Assert.Equal(mac.ToString(), fromPhysical!.ToString());
    }

    [Fact]
    public void IntervalYearMonthConverter_FormatsIso_ForOracle()
    {
        // Ported from the 2.0 branch. Production behavior differs from 2.0: ConvertToProvider for
        // Oracle now renders Oracle's native INTERVAL YEAR TO MONTH literal syntax (+YYYY-MM) rather
        // than an ISO 8601 string (see IntervalYearMonthConverter.FormatOracle). ISO 8601 is still
        // used for PostgreSQL/Spanner/CockroachDb, and FromProviderValue still parses ISO text
        // regardless of provider, so the round-trip assertion below is unchanged.
        var converter = new IntervalYearMonthConverter();
        var interval = new IntervalYearMonth(2, 3);

        var providerValue = converter.ToProviderValue(interval, SupportedDatabase.Oracle);
        Assert.Equal("+0002-03", providerValue); // BP-124: Oracle literal format

        var parsed = (IntervalYearMonth?)converter.FromProviderValue("P2Y3M", SupportedDatabase.Oracle);
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed!.Value.Years);
        Assert.Equal(3, parsed.Value.Months);
    }

    [Fact]
    public void IntervalDaySecondConverter_FormatsIso_ForOracle()
    {
        // Ported from the 2.0 branch. Production behavior differs from 2.0: ConvertToProvider for
        // Oracle now renders Oracle's native INTERVAL DAY TO SECOND literal syntax
        // (+DDDDDDDDD HH:MM:SS.ffffff) rather than an ISO 8601 string (see
        // IntervalDaySecondConverter.FormatOracle). FromProviderValue still parses ISO text
        // regardless of provider, so the round-trip assertion below is unchanged.
        var converter = new IntervalDaySecondConverter();
        var interval = new IntervalDaySecond(1, new TimeSpan(2, 3, 4));

        var providerValue = converter.ToProviderValue(interval, SupportedDatabase.Oracle);
        Assert.Equal("+000000001 02:03:04.000000", providerValue); // BP-124: Oracle literal format

        var parsed = (IntervalDaySecond?)converter.FromProviderValue("P1DT2H3M4S", SupportedDatabase.Oracle);
        Assert.NotNull(parsed);
        Assert.Equal(1, parsed!.Value.Days);
        Assert.Equal(new TimeSpan(2, 3, 4), parsed.Value.Time);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_FormatsIso_ForPostgres()
    {
        var converter = new PostgreSqlIntervalConverter();
        var interval = new PostgreSqlInterval(0, 1, 0);

        var providerValue = converter.ToProviderValue(interval, SupportedDatabase.PostgreSql);
        Assert.Equal("P1D", providerValue);

        var parsed = (PostgreSqlInterval?)converter.FromProviderValue("P2D", SupportedDatabase.PostgreSql);
        Assert.NotNull(parsed);
        Assert.Equal(2, parsed!.Value.Days);
    }

    private static byte[] BuildEwkb(int srid)
    {
        var bytes = new byte[9];
        bytes[0] = 1;
        var type = 0x20000000u;
        BitConverter.GetBytes(type).CopyTo(bytes, 1);
        BitConverter.GetBytes(srid).CopyTo(bytes, 5);
        return bytes;
    }
}