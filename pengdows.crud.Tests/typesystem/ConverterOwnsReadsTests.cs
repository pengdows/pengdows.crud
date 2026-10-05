using System;
using System.Text;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.types;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// DRY-010 stage 3: a type with a converter is read by its converter alone. Where the type's coercion
/// (consulted first, so shadowing the converter) accepted an input the converter didn't, the converter
/// now takes it, so deleting the coercion loses nothing: a RowVersion from its ulong, an interval
/// year-month from its month count, a JsonDocument from UTF-8 bytes, and MySQL's internal spatial format
/// (picked by the dialect traits' SpatialWireFormat, as the write side already does).
/// </summary>
public class ConverterOwnsReadsTests
{
    private static object? Read(Type type, object raw, SupportedDatabase provider = SupportedDatabase.PostgreSql) =>
        AdvancedTypeRegistry.Shared.GetConverter(type)!.FromProviderValue(raw, provider);

    [Fact]
    public void RowVersion_FromUlong_IsBigEndian()
    {
        var read = (RowVersion)Read(typeof(RowVersion), 0x0102030405060708UL, SupportedDatabase.SqlServer)!;

        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, read.ToArray());
    }

    [Theory]
    [InlineData(14)]
    [InlineData(14L)]
    public void IntervalYearMonth_FromMonthCount(object months)
    {
        Assert.Equal(14, ((IntervalYearMonth)Read(typeof(IntervalYearMonth), months, SupportedDatabase.Oracle)!).TotalMonths);
    }

    [Fact]
    public void JsonDocument_FromUtf8Bytes_AndBlankAsJsonNull()
    {
        Assert.Equal("{\"a\":1}", ((JsonDocument)Read(typeof(JsonDocument), Encoding.UTF8.GetBytes("{\"a\":1}"))!).RootElement.GetRawText());
        Assert.Equal(JsonValueKind.Null, ((JsonDocument)Read(typeof(JsonDocument), "")!).RootElement.ValueKind);
    }

    // MySQL's internal format: a 4-byte little-endian SRID, then WKB.
    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    public void Spatial_MySqlInternalFormat_IsReadForTheMySqlFamily(SupportedDatabase provider)
    {
        var wkb = Convert.FromHexString("0101000000000000000000F03F0000000000000040");
        var stored = new byte[4 + wkb.Length];
        BitConverter.GetBytes(4326).CopyTo(stored, 0);
        wkb.CopyTo(stored, 4);

        var geometry = (Geometry)Read(typeof(Geometry), stored, provider)!;
        var geography = (Geography)Read(typeof(Geography), stored, provider)!;

        foreach (var spatial in new SpatialValue[] { geometry, geography })
        {
            Assert.Equal(4326, spatial.Srid);
            Assert.Equal(Convert.ToHexString(wkb), Convert.ToHexString(spatial.WellKnownBinary.Span));
        }
    }
}
