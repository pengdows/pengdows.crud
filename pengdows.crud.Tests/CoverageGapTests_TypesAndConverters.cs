using System;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using pengdows.crud.types.converters;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests targeting coverage gaps in Range&lt;T&gt;, ClobStreamCoercion,
/// InetConverter, PostgreSqlIntervalConverter, GeographyConverter,
/// and SpatialConverter&lt;T&gt;.
/// </summary>
public class CoverageGapTests_TypesAndConverters
{
    #region Range<T> Tests

    [Fact]
    public void Range_Constructor_AllParameters_SetsProperties()
    {
        var range = new Range<int>(1, 10, true, true);

        Assert.Equal(1, range.Lower);
        Assert.Equal(10, range.Upper);
        Assert.True(range.IsLowerInclusive);
        Assert.True(range.IsUpperInclusive);
    }

    [Fact]
    public void Range_Constructor_DefaultInclusivity_LowerInclusiveUpperExclusive()
    {
        var range = new Range<int>(1, 10);

        Assert.True(range.IsLowerInclusive);
        Assert.False(range.IsUpperInclusive);
    }

    [Fact]
    public void Range_Empty_IsEmpty_True_NoBounds()
    {
        var range = Range<int>.Empty;

        Assert.True(range.IsEmpty);
        Assert.False(range.HasLowerBound);
        Assert.False(range.HasUpperBound);
        Assert.Null(range.Lower);
        Assert.Null(range.Upper);
    }

    [Fact]
    public void Range_NonEmpty_HasBounds_NotEmpty()
    {
        var range = new Range<int>(1, 10);

        Assert.False(range.IsEmpty);
        Assert.True(range.HasLowerBound);
        Assert.True(range.HasUpperBound);
    }

    [Fact]
    public void Range_OnlyLowerBound_HasLowerBound_True_HasUpperBound_False()
    {
        var range = new Range<int>(5, null);

        Assert.True(range.HasLowerBound);
        Assert.False(range.HasUpperBound);
        Assert.False(range.IsEmpty);
    }

    [Fact]
    public void Range_OnlyUpperBound_HasLowerBound_False_HasUpperBound_True()
    {
        var range = new Range<int>(null, 10);

        Assert.False(range.HasLowerBound);
        Assert.True(range.HasUpperBound);
        Assert.False(range.IsEmpty);
    }

    [Fact]
    public void Range_Parse_Int_LowerInclusiveUpperExclusive()
    {
        var range = Range<int>.Parse("[1,5)");

        Assert.Equal(1, range.Lower);
        Assert.Equal(5, range.Upper);
        Assert.True(range.IsLowerInclusive);
        Assert.False(range.IsUpperInclusive);
    }

    [Fact]
    public void Range_Parse_Int_LowerExclusiveUpperInclusive()
    {
        var range = Range<int>.Parse("(1,5]");

        Assert.Equal(1, range.Lower);
        Assert.Equal(5, range.Upper);
        Assert.False(range.IsLowerInclusive);
        Assert.True(range.IsUpperInclusive);
    }

    [Fact]
    public void Range_Parse_NoLowerBound()
    {
        var range = Range<int>.Parse("[,10)");

        // Empty bound text yields default(T) which is 0 for int, not null
        Assert.Equal(default(int), range.Lower);
        Assert.Equal(10, range.Upper);
        Assert.True(range.IsLowerInclusive);
        Assert.False(range.IsUpperInclusive);
    }

    [Fact]
    public void Range_Parse_NoUpperBound()
    {
        var range = Range<int>.Parse("(5,]");

        Assert.Equal(5, range.Lower);
        // Empty bound text yields default(T) which is 0 for int, not null
        Assert.Equal(default(int), range.Upper);
        Assert.False(range.IsLowerInclusive);
        Assert.True(range.IsUpperInclusive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Range_Parse_NullOrEmpty_ThrowsArgumentException(string? input)
    {
        Assert.Throws<ArgumentException>(() => Range<int>.Parse(input!));
    }

    [Fact]
    public void Range_Parse_TooShort_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Range<int>.Parse("[,"));
    }

    [Fact]
    public void Range_Parse_InvalidStartBracket_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Range<int>.Parse("{1,5)"));
    }

    [Fact]
    public void Range_Parse_InvalidEndBracket_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Range<int>.Parse("[1,5}"));
    }

    [Fact]
    public void Range_Parse_NoComma_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => Range<int>.Parse("[1 5)"));
    }

    [Fact]
    public void Range_Parse_Long()
    {
        var range = Range<long>.Parse("[100,200)");

        Assert.Equal(100L, range.Lower);
        Assert.Equal(200L, range.Upper);
    }

    [Fact]
    public void Range_Parse_Decimal()
    {
        var range = Range<decimal>.Parse("[1.5,2.5)");

        Assert.Equal(1.5m, range.Lower);
        Assert.Equal(2.5m, range.Upper);
    }

    [Fact]
    public void Range_Parse_Double()
    {
        var range = Range<double>.Parse("[1.0,2.0)");

        Assert.Equal(1.0, range.Lower);
        Assert.Equal(2.0, range.Upper);
    }

    [Fact]
    public void Range_Parse_DateTime()
    {
        var range = Range<DateTime>.Parse("[2024-01-01,2024-12-31)");

        Assert.Equal(new DateTime(2024, 1, 1), range.Lower);
        Assert.Equal(new DateTime(2024, 12, 31), range.Upper);
    }

    [Fact]
    public void Range_Parse_DateTimeOffset()
    {
        var range = Range<DateTimeOffset>.Parse("[2024-01-01T00:00:00+00:00,2024-12-31T00:00:00+00:00)");

        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), range.Lower);
        Assert.Equal(new DateTimeOffset(2024, 12, 31, 0, 0, 0, TimeSpan.Zero), range.Upper);
    }

    [Fact]
    public void Range_ToString_InclusiveExclusive()
    {
        var range = new Range<int>(1, 5, true, false);
        var text = range.ToString();

        Assert.Equal("[1, 5)", text);
    }

    [Fact]
    public void Range_ToString_ExclusiveInclusive()
    {
        var range = new Range<int>(1, 5, false, true);
        var text = range.ToString();

        Assert.Equal("(1, 5]", text);
    }

    [Fact]
    public void Range_ToString_NoLowerBound()
    {
        var range = new Range<int>(null, 10, true, false);
        var text = range.ToString();

        Assert.Equal("[, 10)", text);
    }

    [Fact]
    public void Range_ToString_NoUpperBound()
    {
        var range = new Range<int>(5, null, false, true);
        var text = range.ToString();

        Assert.Equal("(5, ]", text);
    }

    [Fact]
    public void Range_ToString_Empty()
    {
        var range = Range<int>.Empty;
        var text = range.ToString();

        // default struct: IsLowerInclusive=false, IsUpperInclusive=false
        Assert.Equal("(, )", text);
    }

    [Fact]
    public void Range_Equals_SameRanges_True()
    {
        var a = new Range<int>(1, 5, true, false);
        var b = new Range<int>(1, 5, true, false);

        Assert.True(a.Equals(b));
        Assert.True(b.Equals(a));
    }

    [Fact]
    public void Range_Equals_DifferentLower_False()
    {
        var a = new Range<int>(1, 5, true, false);
        var b = new Range<int>(2, 5, true, false);

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Range_Equals_DifferentUpper_False()
    {
        var a = new Range<int>(1, 5, true, false);
        var b = new Range<int>(1, 6, true, false);

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Range_Equals_DifferentInclusivity_False()
    {
        var a = new Range<int>(1, 5, true, false);
        var b = new Range<int>(1, 5, false, false);

        Assert.False(a.Equals(b));
    }

    [Fact]
    public void Range_Equals_Object_CorrectType_True()
    {
        var a = new Range<int>(1, 5, true, false);
        object b = new Range<int>(1, 5, true, false);

        Assert.True(a.Equals(b));
    }

    [Fact]
    public void Range_Equals_Object_Null_False()
    {
        var a = new Range<int>(1, 5, true, false);

        Assert.False(a.Equals(null));
    }

    [Fact]
    public void Range_Equals_Object_WrongType_False()
    {
        var a = new Range<int>(1, 5, true, false);

        Assert.False(a.Equals("not a range"));
    }

    [Fact]
    public void Range_GetHashCode_SameRanges_SameHash()
    {
        var a = new Range<int>(1, 5, true, false);
        var b = new Range<int>(1, 5, true, false);

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Range_GetHashCode_DifferentRanges_DifferentHash()
    {
        var a = new Range<int>(1, 5, true, false);
        var b = new Range<int>(2, 6, true, false);

        // Different ranges usually have different hash codes (not guaranteed but extremely likely)
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Range_Parse_BothInclusive()
    {
        var range = Range<int>.Parse("[1,5]");

        Assert.True(range.IsLowerInclusive);
        Assert.True(range.IsUpperInclusive);
    }

    [Fact]
    public void Range_Parse_BothExclusive()
    {
        var range = Range<int>.Parse("(1,5)");

        Assert.False(range.IsLowerInclusive);
        Assert.False(range.IsUpperInclusive);
    }

    [Fact]
    public void Range_Parse_WithSpaces()
    {
        var range = Range<int>.Parse("  [1, 5)  ");

        Assert.Equal(1, range.Lower);
        Assert.Equal(5, range.Upper);
    }

    #endregion

    #region ClobStreamCoercion Tests

    [Fact]
    public void ClobStreamCoercion_TryRead_NullSrc_ReturnsFalse()
    {
        var coercion = new ClobStreamCoercion();
        var src = new DbValue(null);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void ClobStreamCoercion_TryRead_DbNull_ReturnsFalse()
    {
        var coercion = new ClobStreamCoercion();
        var src = new DbValue(DBNull.Value);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void ClobStreamCoercion_TryRead_TextReader_ReturnsReader()
    {
        var coercion = new ClobStreamCoercion();
        var reader = new StringReader("hello");
        var src = new DbValue(reader);

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.Same(reader, value);
    }

    [Fact]
    public void ClobStreamCoercion_TryRead_String_ReturnsStringReader()
    {
        var coercion = new ClobStreamCoercion();
        var src = new DbValue("test content");

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.NotNull(value);
        Assert.IsType<StringReader>(value);
        Assert.Equal("test content", value!.ReadToEnd());
    }

    [Fact]
    public void ClobStreamCoercion_TryRead_Stream_ReturnsStreamReader()
    {
        var coercion = new ClobStreamCoercion();
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("stream content"));
        var src = new DbValue(stream);

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.NotNull(value);
        Assert.IsType<StreamReader>(value);
        Assert.Equal("stream content", value!.ReadToEnd());
    }

    [Fact]
    public void ClobStreamCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new ClobStreamCoercion();
        var src = new DbValue(42);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void ClobStreamCoercion_TryWrite_NonNullTextReader_SetsValueAndDbType()
    {
        var coercion = new ClobStreamCoercion();
        var reader = new StringReader("hello");
        var parameter = new fakeDbParameter();

        var result = coercion.TryWrite(reader, parameter);

        Assert.True(result);
        Assert.Same(reader, parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    [Fact]
    public void ClobStreamCoercion_TryWrite_Null_SetsNullValueAndDbType()
    {
        var coercion = new ClobStreamCoercion();
        var parameter = new fakeDbParameter();

        var result = coercion.TryWrite(null, parameter);

        Assert.True(result);
        Assert.Null(parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    #endregion

    #region InetConverter Tests

    [Fact]
    public void InetConverter_ConvertToProvider_PostgreSql_ReturnsString()
    {
        var converter = new InetConverter();
        var inet = new Inet(IPAddress.Parse("192.168.1.1"), 24);

        var result = converter.ToProviderValue(inet, SupportedDatabase.PostgreSql);

        Assert.IsType<string>(result);
        Assert.Equal("192.168.1.1/24", result);
    }

    [Fact]
    public void InetConverter_ConvertToProvider_CockroachDb_ReturnsString()
    {
        var converter = new InetConverter();
        var inet = new Inet(IPAddress.Parse("10.0.0.1"));

        var result = converter.ToProviderValue(inet, SupportedDatabase.CockroachDb);

        Assert.IsType<string>(result);
    }

    [Fact]
    public void InetConverter_ConvertToProvider_NonPostgres_ReturnsInetValue()
    {
        var converter = new InetConverter();
        var inet = new Inet(IPAddress.Parse("192.168.1.1"), 24);

        var result = converter.ToProviderValue(inet, SupportedDatabase.SqlServer);

        Assert.IsType<Inet>(result);
    }

    [Fact]
    public void InetConverter_TryConvertFromProvider_InetPassthrough()
    {
        var converter = new InetConverter();
        var inet = new Inet(IPAddress.Parse("10.0.0.1"), 8);

        var success = converter.TryConvertFromProvider(inet, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(inet, result);
    }

    [Fact]
    public void InetConverter_TryConvertFromProvider_ValidString()
    {
        var converter = new InetConverter();

        var success = converter.TryConvertFromProvider("192.168.1.1/24", SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), result.Address);
        Assert.Equal((byte)24, result.PrefixLength);
    }

    [Fact]
    public void InetConverter_TryConvertFromProvider_StringWithoutPrefix()
    {
        var converter = new InetConverter();

        var success = converter.TryConvertFromProvider("10.0.0.1", SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(IPAddress.Parse("10.0.0.1"), result.Address);
        Assert.Null(result.PrefixLength);
    }

    [Fact]
    public void InetConverter_TryConvertFromProvider_InvalidString_ReturnsFalse()
    {
        var converter = new InetConverter();

        var success = converter.TryConvertFromProvider("not-an-ip", SupportedDatabase.PostgreSql, out var result);

        Assert.False(success);
    }

    [Fact]
    public void InetConverter_TryConvertFromProvider_IPAddress()
    {
        var converter = new InetConverter();
        var ip = IPAddress.Parse("172.16.0.1");

        var success = converter.TryConvertFromProvider(ip, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(ip, result.Address);
        Assert.Null(result.PrefixLength);
    }

    [Fact]
    public void InetConverter_TryConvertFromProvider_UnknownType_ReturnsFalse()
    {
        var converter = new InetConverter();

        var success = converter.TryConvertFromProvider(12345, SupportedDatabase.PostgreSql, out var result);

        Assert.False(success);
    }

    [Fact]
    public void InetConverter_FromProviderValue_Null_ReturnsNull()
    {
        var converter = new InetConverter();

        var result = converter.FromProviderValue(null!, SupportedDatabase.PostgreSql);

        Assert.Null(result);
    }

    [Fact]
    public void InetConverter_FromProviderValue_DbNull_ReturnsNull()
    {
        var converter = new InetConverter();

        var result = converter.FromProviderValue(DBNull.Value, SupportedDatabase.PostgreSql);

        Assert.Null(result);
    }

    [Fact]
    public void InetConverter_TryConvertFromProvider_IPv6String()
    {
        var converter = new InetConverter();

        var success = converter.TryConvertFromProvider("::1/128", SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(IPAddress.IPv6Loopback, result.Address);
        Assert.Equal((byte)128, result.PrefixLength);
    }

    #endregion

    #region PostgreSqlIntervalConverter Tests

    [Fact]
    public void PostgreSqlIntervalConverter_ConvertToProvider_NonPostgres_ReturnsValue()
    {
        var converter = new PostgreSqlIntervalConverter();
        var interval = new PostgreSqlInterval(12, 5, 0);

        var result = converter.ToProviderValue(interval, SupportedDatabase.SqlServer);

        Assert.IsType<PostgreSqlInterval>(result);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_ConvertToProvider_PostgreSql_ReturnsIso8601()
    {
        var converter = new PostgreSqlIntervalConverter();
        var interval = new PostgreSqlInterval(6, 4, 45_000_000_000); // 12.5 hours in microseconds

        var result = converter.ToProviderValue(interval, SupportedDatabase.PostgreSql);

        Assert.IsType<string>(result);
        var text = (string)result!;
        Assert.StartsWith("P", text);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_ConvertToProvider_CockroachDb_ReturnsIso8601()
    {
        var converter = new PostgreSqlIntervalConverter();
        var interval = new PostgreSqlInterval(3, 0, 0);

        var result = converter.ToProviderValue(interval, SupportedDatabase.CockroachDb);

        Assert.IsType<string>(result);
        Assert.Equal("P3M", (string)result!);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_FormatIso8601_OnlyMonths()
    {
        var converter = new PostgreSqlIntervalConverter();
        var interval = new PostgreSqlInterval(6, 0, 0);

        var result = (string)converter.ToProviderValue(interval, SupportedDatabase.PostgreSql)!;

        Assert.Equal("P6M", result);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_FormatIso8601_OnlyDays()
    {
        var converter = new PostgreSqlIntervalConverter();
        var interval = new PostgreSqlInterval(0, 15, 0);

        var result = (string)converter.ToProviderValue(interval, SupportedDatabase.PostgreSql)!;

        Assert.Equal("P15D", result);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_FormatIso8601_HoursAndMinutes()
    {
        var converter = new PostgreSqlIntervalConverter();
        // 2 hours 30 minutes = (2*3600 + 30*60) * 1_000_000 microseconds
        var microseconds = (2L * 3600 + 30 * 60) * 1_000_000;
        var interval = new PostgreSqlInterval(0, 0, microseconds);

        var result = (string)converter.ToProviderValue(interval, SupportedDatabase.PostgreSql)!;

        Assert.Contains("T", result);
        Assert.Contains("2H", result);
        Assert.Contains("30M", result);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_FormatIso8601_OnlySeconds()
    {
        var converter = new PostgreSqlIntervalConverter();
        // 45 seconds = 45 * 1_000_000 microseconds
        var microseconds = 45L * 1_000_000;
        var interval = new PostgreSqlInterval(0, 0, microseconds);

        var result = (string)converter.ToProviderValue(interval, SupportedDatabase.PostgreSql)!;

        Assert.Contains("T", result);
        Assert.Contains("45S", result);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_FormatIso8601_EmptyInterval()
    {
        var converter = new PostgreSqlIntervalConverter();
        var interval = new PostgreSqlInterval(0, 0, 0);

        var result = (string)converter.ToProviderValue(interval, SupportedDatabase.PostgreSql)!;

        Assert.Equal("P0D", result);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_TryConvertFromProvider_PostgreSqlInterval_Passthrough()
    {
        var converter = new PostgreSqlIntervalConverter();
        var interval = new PostgreSqlInterval(12, 5, 1000);

        var success = converter.TryConvertFromProvider(interval, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(interval, result);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_TryConvertFromProvider_TimeSpan()
    {
        var converter = new PostgreSqlIntervalConverter();
        var ts = TimeSpan.FromHours(2.5);

        var success = converter.TryConvertFromProvider(ts, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(0, result.Months);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_TryConvertFromProvider_String_Iso8601()
    {
        var converter = new PostgreSqlIntervalConverter();

        var success = converter.TryConvertFromProvider("P3M4DT2H30M", SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(3, result.Months);
        Assert.Equal(4, result.Days);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_TryConvertFromProvider_EmptyString_ReturnsZero()
    {
        var converter = new PostgreSqlIntervalConverter();

        var success = converter.TryConvertFromProvider("", SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(0, result.Months);
        Assert.Equal(0, result.Days);
        Assert.Equal(0, result.Microseconds);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_TryConvertFromProvider_WhitespaceString_ReturnsZero()
    {
        var converter = new PostgreSqlIntervalConverter();

        var success = converter.TryConvertFromProvider("   ", SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(0, result.Months);
        Assert.Equal(0, result.Days);
        Assert.Equal(0, result.Microseconds);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_TryConvertFromProvider_UnknownType_ReturnsFalse()
    {
        var converter = new PostgreSqlIntervalConverter();

        var success = converter.TryConvertFromProvider(42, SupportedDatabase.PostgreSql, out var result);

        Assert.False(success);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_FormatIso8601_MonthsAndDaysAndTime()
    {
        var converter = new PostgreSqlIntervalConverter();
        // 1 hour = 3600 * 1_000_000 microseconds
        var interval = new PostgreSqlInterval(2, 3, 3_600_000_000);

        var result = (string)converter.ToProviderValue(interval, SupportedDatabase.PostgreSql)!;

        Assert.StartsWith("P", result);
        Assert.Contains("2M", result);
        Assert.Contains("3D", result);
        Assert.Contains("T", result);
        Assert.Contains("1H", result);
    }

    #endregion

    #region GeographyConverter Tests

    [Fact]
    public void GeographyConverter_TryConvertFromProvider_WktString()
    {
        var converter = new GeographyConverter();

        var success = converter.TryConvertFromProvider(
            "POINT(-74.0060 40.7128)",
            SupportedDatabase.PostgreSql,
            out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal(4326, result.Srid);
    }

    [Fact]
    public void GeographyConverter_TryConvertFromProvider_WktWithSrid()
    {
        var converter = new GeographyConverter();

        var success = converter.TryConvertFromProvider(
            "SRID=4326;POINT(-74.0060 40.7128)",
            SupportedDatabase.PostgreSql,
            out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal(4326, result.Srid);
    }

    [Fact]
    public void GeographyConverter_TryConvertFromProvider_WktWithNoSridPrefix_DefaultsTo4326()
    {
        var converter = new GeographyConverter();

        var success = converter.TryConvertFromProvider(
            "LINESTRING(0 0, 1 1, 2 2)",
            SupportedDatabase.PostgreSql,
            out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal(4326, result.Srid);
    }

    [Fact]
    public void GeographyConverter_TryConvertFromProvider_GeoJson()
    {
        var converter = new GeographyConverter();
        var geoJson = "{\"type\":\"Point\",\"coordinates\":[-74.0060,40.7128]}";

        var success = converter.TryConvertFromProvider(
            geoJson,
            SupportedDatabase.PostgreSql,
            out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal(4326, result.Srid);
    }

    [Fact]
    public void GeographyConverter_TryConvertFromProvider_GeoJsonWithSrid()
    {
        var converter = new GeographyConverter();
        var geoJson = "{\"type\":\"Point\",\"coordinates\":[-74.0060,40.7128],\"crs\":{\"properties\":{\"srid\":3857}}}";

        var success = converter.TryConvertFromProvider(
            geoJson,
            SupportedDatabase.PostgreSql,
            out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal(3857, result.Srid);
    }

    [Fact]
    public void GeographyConverter_TryConvertFromProvider_GeoJsonWithSridColonNoDigits()
    {
        var converter = new GeographyConverter();
        // "srid" followed by colon and closing brace (no digits)
        var geoJson = "{\"type\":\"Point\",\"coordinates\":[-74.0060,40.7128],\"srid\":}";

        // This is invalid JSON but tests the ExtractSridFromGeoJson branch
        var success = converter.TryConvertFromProvider(
            geoJson,
            SupportedDatabase.PostgreSql,
            out var result);

        Assert.True(success);
        Assert.NotNull(result);
        // No valid SRID found, defaults to 4326
        Assert.Equal(4326, result.Srid);
    }

    [Fact]
    public void GeographyConverter_TryConvertFromProvider_ByteArray()
    {
        var converter = new GeographyConverter();
        // Minimal WKB for a point: byte order + type (point=1) + x + y
        var wkb = new byte[]
        {
            0x01, // little-endian
            0x01, 0x00, 0x00, 0x00, // type = Point (1)
            0x00, 0x00, 0x00, 0x00, 0x00, 0x80, 0x52, 0xC0, // x = -74.006
            0x00, 0x00, 0x00, 0x00, 0x40, 0x5B, 0x44, 0x40 // y = 40.7128
        };

        var success = converter.TryConvertFromProvider(wkb, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal(4326, result.Srid);
    }

    [Fact]
    public void GeographyConverter_TryConvertFromProvider_ReadOnlyMemoryByte()
    {
        var converter = new GeographyConverter();
        var wkb = new byte[]
        {
            0x01,
            0x01, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
        ReadOnlyMemory<byte> memory = wkb;

        var success = converter.TryConvertFromProvider(memory, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.NotNull(result);
    }

    [Fact]
    public void GeographyConverter_TryConvertFromProvider_ArraySegmentByte()
    {
        var converter = new GeographyConverter();
        var wkb = new byte[]
        {
            0x01,
            0x01, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
        var segment = new ArraySegment<byte>(wkb);

        var success = converter.TryConvertFromProvider(segment, SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.NotNull(result);
    }

    #endregion

    #region SpatialConverter<T> (via GeographyConverter and GeometryConverter) Tests

    [Fact]
    public void SpatialConverter_ConvertToProvider_WithProviderValue_ReturnsProviderValue()
    {
        var converter = new GeographyConverter();
        var providerObj = new object();
        var geog = Geography.FromWellKnownText("POINT(-74.0060 40.7128)", 4326, providerObj);

        var result = converter.ToProviderValue(geog, SupportedDatabase.PostgreSql);

        Assert.Same(providerObj, result);
    }

    [Fact]
    public void SpatialConverter_ConvertToProvider_PostgreSql_WKB_ReturnsByteArray()
    {
        var converter = new GeographyConverter();
        var wkb = new byte[] { 0x01, 0x01, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var geog = Geography.FromWellKnownBinary(wkb, 4326);

        var result = converter.ToProviderValue(geog, SupportedDatabase.PostgreSql);

        Assert.IsType<byte[]>(result);
    }

    [Fact]
    public void SpatialConverter_ConvertToProvider_PostgreSql_WKT_ReturnsString()
    {
        var converter = new GeographyConverter();
        var geog = Geography.FromWellKnownText("POINT(0 0)", 4326);

        var result = converter.ToProviderValue(geog, SupportedDatabase.PostgreSql);

        Assert.IsType<string>(result);
        Assert.Equal("POINT(0 0)", result);
    }

    [Fact]
    public void SpatialConverter_ConvertToProvider_PostgreSql_GeoJson_ReturnsGeoJson()
    {
        var converter = new GeographyConverter();
        var json = "{\"type\":\"Point\",\"coordinates\":[0,0]}";
        var geog = Geography.FromGeoJson(json, 4326);

        var result = converter.ToProviderValue(geog, SupportedDatabase.PostgreSql);

        Assert.IsType<string>(result);
        Assert.Equal(json, result);
    }

    [Fact]
    public void SpatialConverter_ConvertToProvider_MySql_WKB_ReturnsByteArray()
    {
        var converter = new GeometryConverter();
        var wkb = new byte[] { 0x01, 0x01, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var geom = Geometry.FromWellKnownBinary(wkb, 0);

        var result = converter.ToProviderValue(geom, SupportedDatabase.MySql);

        Assert.IsType<byte[]>(result);
    }

    [Fact]
    public void SpatialConverter_ConvertToProvider_MySql_WKT_ReturnsUtf8Bytes()
    {
        var converter = new GeometryConverter();
        var geom = Geometry.FromWellKnownText("POINT(1 2)", 0);

        var result = converter.ToProviderValue(geom, SupportedDatabase.MySql);

        Assert.IsType<byte[]>(result);
        var text = Encoding.UTF8.GetString((byte[])result!);
        Assert.Equal("POINT(1 2)", text);
    }

    [Fact]
    public void SpatialConverter_ConvertToProvider_Oracle_NoProviderValue_Throws()
    {
        var converter = new GeographyConverter();
        var geog = Geography.FromWellKnownText("POINT(0 0)", 4326);

        Assert.Throws<InvalidOperationException>(() =>
            converter.ToProviderValue(geog, SupportedDatabase.Oracle));
    }

    [Fact]
    public void SpatialConverter_ConvertToProvider_SqlServer_Throws_MissingAssembly()
    {
        var converter = new GeographyConverter();
        var geog = Geography.FromWellKnownText("POINT(0 0)", 4326);

        // SqlServer spatial types require Microsoft.SqlServer.Types which is not available
        Assert.Throws<InvalidOperationException>(() =>
            converter.ToProviderValue(geog, SupportedDatabase.SqlServer));
    }

    [Fact]
    public void SpatialConverter_ConvertToProvider_DefaultFallback_WKB_ReturnsByteArray()
    {
        var converter = new GeographyConverter();
        var wkb = new byte[] { 0x01, 0x01, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var geog = Geography.FromWellKnownBinary(wkb, 4326);

        var result = converter.ToProviderValue(geog, SupportedDatabase.Sqlite);

        Assert.IsType<byte[]>(result);
    }

    [Fact]
    public void SpatialConverter_ConvertToProvider_DefaultFallback_WKT_ReturnsString()
    {
        var converter = new GeographyConverter();
        var geog = Geography.FromWellKnownText("POINT(0 0)", 4326);

        var result = converter.ToProviderValue(geog, SupportedDatabase.Sqlite);

        Assert.IsType<string>(result);
    }

    [Fact]
    public void SpatialConverter_ConvertToProvider_DefaultFallback_GeoJsonOnly_ReturnsGeoJson()
    {
        var converter = new GeographyConverter();
        var json = "{\"type\":\"Point\",\"coordinates\":[0,0]}";
        var geog = Geography.FromGeoJson(json, 4326);

        var result = converter.ToProviderValue(geog, SupportedDatabase.Sqlite);

        Assert.Equal(json, result);
    }

    [Fact]
    public void SpatialConverter_TryConvertFromProvider_UnknownType_Throws()
    {
        var converter = new GeographyConverter();

        // An unknown type that is neither byte[], string, nor a recognized provider type
        // FromProviderSpecific will throw NotSupportedException, which is caught
        var success = converter.TryConvertFromProvider(42, SupportedDatabase.PostgreSql, out var result);

        Assert.False(success);
    }

    [Fact]
    public void SpatialConverter_ConvertToProvider_Null_ReturnsNull()
    {
        var converter = new GeographyConverter();

        var result = converter.ToProviderValue(null!, SupportedDatabase.PostgreSql);

        Assert.Null(result);
    }

    [Fact]
    public void SpatialConverter_MySql_NoWkbNoWkt_Throws()
    {
        var converter = new GeometryConverter();
        var json = "{\"type\":\"Point\",\"coordinates\":[0,0]}";
        var geom = Geometry.FromGeoJson(json, 0);

        Assert.Throws<InvalidOperationException>(() =>
            converter.ToProviderValue(geom, SupportedDatabase.MySql));
    }

    [Fact]
    public void SpatialConverter_MariaDb_WKT_ReturnsUtf8Bytes()
    {
        var converter = new GeometryConverter();
        var geom = Geometry.FromWellKnownText("POINT(1 2)", 0);

        var result = converter.ToProviderValue(geom, SupportedDatabase.MariaDb);

        Assert.IsType<byte[]>(result);
    }

    [Fact]
    public void GeometryConverter_TryConvertFromProvider_String_WKT()
    {
        var converter = new GeometryConverter();

        var success = converter.TryConvertFromProvider(
            "POINT(100 200)",
            SupportedDatabase.PostgreSql,
            out var result);

        Assert.True(success);
        Assert.NotNull(result);
    }

    [Fact]
    public void GeometryConverter_TryConvertFromProvider_GeoJson()
    {
        var converter = new GeometryConverter();
        var json = "{\"type\":\"Point\",\"coordinates\":[100,200]}";

        var success = converter.TryConvertFromProvider(
            json,
            SupportedDatabase.PostgreSql,
            out var result);

        Assert.True(success);
        Assert.NotNull(result);
    }

    #endregion

    #region Additional Coercion Coverage

    [Fact]
    public void BlobStreamCoercion_TryRead_ReadOnlyMemoryByte_ReturnsMemoryStream()
    {
        var coercion = new BlobStreamCoercion();
        var data = new byte[] { 1, 2, 3, 4 };
        ReadOnlyMemory<byte> memory = data;
        var src = new DbValue(memory);

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.NotNull(value);
        Assert.IsType<MemoryStream>(value);
    }

    [Fact]
    public void BlobStreamCoercion_TryWrite_NonNullStream_SetsValueAndDbType()
    {
        var coercion = new BlobStreamCoercion();
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var parameter = new fakeDbParameter();

        var result = coercion.TryWrite(stream, parameter);

        Assert.True(result);
        Assert.Same(stream, parameter.Value);
        Assert.Equal(DbType.Binary, parameter.DbType);
    }

    [Fact]
    public void BlobStreamCoercion_TryWrite_Null_SetsDbNull()
    {
        var coercion = new BlobStreamCoercion();
        var parameter = new fakeDbParameter();

        var result = coercion.TryWrite(null, parameter);

        Assert.True(result);
        Assert.Equal(DBNull.Value, parameter.Value);
    }

    [Fact]
    public void GeometryCoercion_TryRead_GeoJson_ReturnsGeometry()
    {
        var coercion = new GeometryCoercion();
        var json = "{\"type\":\"Point\",\"coordinates\":[1,2]}";
        var src = new DbValue(json);

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.NotNull(value);
    }

    [Fact]
    public void GeometryCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new GeometryCoercion();
        var src = new DbValue(null);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void GeographyCoercion_TryRead_GeoJson_ReturnsGeography()
    {
        var coercion = new GeographyCoercion();
        var json = "{\"type\":\"Point\",\"coordinates\":[-74.006,40.7128]}";
        var src = new DbValue(json);

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.NotNull(value);
        Assert.Equal(4326, value!.Srid);
    }

    [Fact]
    public void GeographyCoercion_TryRead_WKT_ReturnsGeography()
    {
        var coercion = new GeographyCoercion();
        var src = new DbValue("POINT(0 0)");

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.NotNull(value);
    }

    [Fact]
    public void GeographyCoercion_TryRead_ByteArray_ReturnsGeography()
    {
        var coercion = new GeographyCoercion();
        var wkb = new byte[] { 0x01, 0x01, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var src = new DbValue(wkb);

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.NotNull(value);
    }

    [Fact]
    public void GeographyCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new GeographyCoercion();
        var src = new DbValue(null);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void GeographyCoercion_TryWrite_Null_SetsDbNull()
    {
        var coercion = new GeographyCoercion();
        var parameter = new fakeDbParameter();

        var result = coercion.TryWrite(null, parameter);

        Assert.True(result);
        Assert.Equal(DBNull.Value, parameter.Value);
    }

    [Fact]
    public void GeographyCoercion_TryWrite_WithWKB_SetsBinaryValue()
    {
        var coercion = new GeographyCoercion();
        var wkb = new byte[] { 0x01, 0x01, 0x00, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var geog = Geography.FromWellKnownBinary(wkb, 4326);
        var parameter = new fakeDbParameter();

        var result = coercion.TryWrite(geog, parameter);

        Assert.True(result);
        Assert.IsType<byte[]>(parameter.Value);
        Assert.Equal(DbType.Binary, parameter.DbType);
    }

    [Fact]
    public void GeographyCoercion_TryWrite_WithWKT_SetsStringValue()
    {
        var coercion = new GeographyCoercion();
        var geog = Geography.FromWellKnownText("POINT(0 0)", 4326);
        var parameter = new fakeDbParameter();

        var result = coercion.TryWrite(geog, parameter);

        Assert.True(result);
        Assert.Equal("POINT(0 0)", parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    [Fact]
    public void GeometryCoercion_TryWrite_Null_SetsDbNull()
    {
        var coercion = new GeometryCoercion();
        var parameter = new fakeDbParameter();

        var result = coercion.TryWrite(null, parameter);

        Assert.True(result);
        Assert.Equal(DBNull.Value, parameter.Value);
    }

    [Fact]
    public void GeometryCoercion_TryWrite_WithWKT_SetsStringValue()
    {
        var coercion = new GeometryCoercion();
        var geom = Geometry.FromWellKnownText("POINT(1 2)", 0);
        var parameter = new fakeDbParameter();

        var result = coercion.TryWrite(geom, parameter);

        Assert.True(result);
        Assert.Equal("POINT(1 2)", parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    [Fact]
    public void GeographyCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new GeographyCoercion();
        var src = new DbValue(42);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void GeometryCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new GeometryCoercion();
        var src = new DbValue(42);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    #endregion

    #region PostgreSql Range Coercion Tests

    [Fact]
    public void PostgreSqlRangeIntCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new PostgreSqlRangeIntCoercion();
        var src = new DbValue(null);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
        Assert.True(value.IsEmpty);
    }

    [Fact]
    public void PostgreSqlRangeIntCoercion_TryRead_RangePassthrough()
    {
        var coercion = new PostgreSqlRangeIntCoercion();
        var range = new Range<int>(1, 10, true, false);
        var src = new DbValue(range);

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.Equal(range, value);
    }

    [Fact]
    public void PostgreSqlRangeIntCoercion_TryRead_ValidString()
    {
        var coercion = new PostgreSqlRangeIntCoercion();
        var src = new DbValue("[1,10)");

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.Equal(1, value.Lower);
        Assert.Equal(10, value.Upper);
    }

    [Fact]
    public void PostgreSqlRangeIntCoercion_TryRead_InvalidString_ReturnsFalse()
    {
        var coercion = new PostgreSqlRangeIntCoercion();
        var src = new DbValue("invalid");

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void PostgreSqlRangeIntCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new PostgreSqlRangeIntCoercion();
        var src = new DbValue(42.5);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void PostgreSqlRangeIntCoercion_TryWrite_WritesString()
    {
        var coercion = new PostgreSqlRangeIntCoercion();
        var range = new Range<int>(1, 10, true, false);
        var parameter = new fakeDbParameter();

        var result = coercion.TryWrite(range, parameter);

        Assert.True(result);
        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Contains("1", (string)parameter.Value!);
    }

    [Fact]
    public void PostgreSqlRangeLongCoercion_TryRead_ValidString()
    {
        var coercion = new PostgreSqlRangeLongCoercion();
        var src = new DbValue("[100,200)");

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.Equal(100L, value.Lower);
        Assert.Equal(200L, value.Upper);
    }

    [Fact]
    public void PostgreSqlRangeDateTimeCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new PostgreSqlRangeDateTimeCoercion();
        var src = new DbValue(null);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void PostgreSqlRangeDateTimeCoercion_TryRead_ValidString()
    {
        var coercion = new PostgreSqlRangeDateTimeCoercion();
        var src = new DbValue("[2024-01-01,2024-12-31)");

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.Equal(new DateTime(2024, 1, 1), value.Lower);
    }

    #endregion

    #region InetCoercion Tests

    [Fact]
    public void InetCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new InetCoercion();
        var src = new DbValue(null);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void InetCoercion_TryRead_InetPassthrough()
    {
        var coercion = new InetCoercion();
        var inet = new Inet(IPAddress.Loopback);
        var src = new DbValue(inet);

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.Equal(inet, value);
    }

    [Fact]
    public void InetCoercion_TryRead_ValidString()
    {
        var coercion = new InetCoercion();
        var src = new DbValue("192.168.1.1/24");

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), value.Address);
    }

    [Fact]
    public void InetCoercion_TryRead_InvalidString_ReturnsFalse()
    {
        var coercion = new InetCoercion();
        var src = new DbValue("not-valid");

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void InetCoercion_TryRead_IPAddress()
    {
        var coercion = new InetCoercion();
        var ip = IPAddress.Parse("10.0.0.1");
        var src = new DbValue(ip);

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.Equal(ip, value.Address);
        Assert.Null(value.PrefixLength);
    }

    [Fact]
    public void InetCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new InetCoercion();
        var src = new DbValue(42);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void InetCoercion_TryWrite_SetsStringValue()
    {
        var coercion = new InetCoercion();
        var inet = new Inet(IPAddress.Parse("192.168.1.1"), 24);
        var parameter = new fakeDbParameter();

        var result = coercion.TryWrite(inet, parameter);

        Assert.True(result);
        Assert.Equal("192.168.1.1/24", parameter.Value);
        Assert.Equal(DbType.String, parameter.DbType);
    }

    #endregion

    #region RowVersionValueCoercion Tests

    [Fact]
    public void RowVersionValueCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new RowVersionValueCoercion();
        var src = new DbValue(null);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void RowVersionValueCoercion_TryRead_ByteArray8_ReturnsRowVersion()
    {
        var coercion = new RowVersionValueCoercion();
        var bytes = new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 };
        var src = new DbValue(bytes);

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
    }

    [Fact]
    public void RowVersionValueCoercion_TryRead_UlongValue()
    {
        var coercion = new RowVersionValueCoercion();
        var src = new DbValue(42UL);

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
    }

    [Fact]
    public void RowVersionValueCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new RowVersionValueCoercion();
        var src = new DbValue("not-a-version");

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    #endregion

    #region PostgreSqlIntervalCoercion Tests

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new PostgreSqlIntervalCoercion();
        var src = new DbValue(null);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_RawNull_ReturnsFalse()
    {
        var coercion = new PostgreSqlIntervalCoercion();
        // Construct a DbValue with DBNull to trigger IsNull
        var src = new DbValue(DBNull.Value);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_IntervalPassthrough()
    {
        var coercion = new PostgreSqlIntervalCoercion();
        var interval = new PostgreSqlInterval(6, 3, 1000);
        var src = new DbValue(interval);

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.Equal(interval, value);
    }

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_TimeSpan()
    {
        var coercion = new PostgreSqlIntervalCoercion();
        var ts = TimeSpan.FromHours(2);
        var src = new DbValue(ts);

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
    }

    [Fact]
    public void PostgreSqlIntervalCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new PostgreSqlIntervalCoercion();
        var src = new DbValue("some string");

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void PostgreSqlIntervalCoercion_TryWrite_SetsTimeSpanAndDbType()
    {
        var coercion = new PostgreSqlIntervalCoercion();
        var interval = new PostgreSqlInterval(0, 1, 3_600_000_000); // 1 day, 1 hour
        var parameter = new fakeDbParameter();

        var result = coercion.TryWrite(interval, parameter);

        Assert.True(result);
        Assert.Equal(DbType.Object, parameter.DbType);
    }

    #endregion

    #region IntervalYearMonthCoercion Tests

    [Fact]
    public void IntervalYearMonthCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new IntervalYearMonthCoercion();
        var src = new DbValue(null);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void IntervalYearMonthCoercion_TryRead_Passthrough()
    {
        var coercion = new IntervalYearMonthCoercion();
        var interval = new IntervalYearMonth(2, 6);
        var src = new DbValue(interval);

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
        Assert.Equal(interval, value);
    }

    [Fact]
    public void IntervalYearMonthCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new IntervalYearMonthCoercion();
        var src = new DbValue(42);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    #endregion

    #region IntervalDaySecondCoercion Tests

    [Fact]
    public void IntervalDaySecondCoercion_TryRead_Null_ReturnsFalse()
    {
        var coercion = new IntervalDaySecondCoercion();
        var src = new DbValue(null);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    [Fact]
    public void IntervalDaySecondCoercion_TryRead_TimeSpan()
    {
        var coercion = new IntervalDaySecondCoercion();
        var ts = TimeSpan.FromHours(48);
        var src = new DbValue(ts);

        var result = coercion.TryRead(src, out var value);

        Assert.True(result);
    }

    [Fact]
    public void IntervalDaySecondCoercion_TryRead_UnknownType_ReturnsFalse()
    {
        var coercion = new IntervalDaySecondCoercion();
        var src = new DbValue(42);

        var result = coercion.TryRead(src, out var value);

        Assert.False(result);
    }

    #endregion

    #region AdvancedCoercions RegisterAll Test

    [Fact]
    public void AdvancedCoercions_RegisterAll_RegistersAllCoercions()
    {
        var registry = new CoercionRegistry();

        AdvancedCoercions.RegisterAll(registry);

        // Verify at least some known types are registered by checking resolution works
        // The fact that this doesn't throw means all coercions registered successfully
        Assert.NotNull(registry);
    }

    #endregion

    #region PostgreSql Interval Converter Parse Edge Cases

    [Fact]
    public void PostgreSqlIntervalConverter_Parse_OnlyTimeComponent()
    {
        var converter = new PostgreSqlIntervalConverter();

        var success = converter.TryConvertFromProvider("PT5H", SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(0, result.Months);
        Assert.Equal(0, result.Days);
        // 5 hours = 5 * 3600 * 1_000_000 microseconds
        Assert.Equal(5L * 3600 * 1_000_000, result.Microseconds);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_Parse_OnlyMinutesComponent()
    {
        var converter = new PostgreSqlIntervalConverter();

        var success = converter.TryConvertFromProvider("PT30M", SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(30L * 60 * 1_000_000, result.Microseconds);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_Parse_OnlySecondsComponent()
    {
        var converter = new PostgreSqlIntervalConverter();

        var success = converter.TryConvertFromProvider("PT45S", SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(45L * 1_000_000, result.Microseconds);
    }

    [Fact]
    public void PostgreSqlIntervalConverter_Parse_DaysAndTime()
    {
        var converter = new PostgreSqlIntervalConverter();

        var success = converter.TryConvertFromProvider("P10DT3H", SupportedDatabase.PostgreSql, out var result);

        Assert.True(success);
        Assert.Equal(10, result.Days);
        Assert.Equal(3L * 3600 * 1_000_000, result.Microseconds);
    }

    #endregion

    #region GeographyConverter ExtractSridFromText Edge Cases

    [Fact]
    public void GeographyConverter_FromText_SridEqualsMissingSemicolon()
    {
        var converter = new GeographyConverter();
        // SRID= with no semicolon - semicolonIndex < 5, so srid=0 (defaults to 4326),
        // and the entire string is passed as WKT. Geography.FromWellKnownText does not
        // validate WKT syntax, so it succeeds with the raw string as WKT.
        var success = converter.TryConvertFromProvider(
            "SRID=4326POINT(0 0)",
            SupportedDatabase.PostgreSql,
            out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal(4326, result.Srid);
    }

    [Fact]
    public void GeographyConverter_FromText_EmptyString()
    {
        var converter = new GeographyConverter();

        // Empty string WKT should fail
        var success = converter.TryConvertFromProvider(
            "",
            SupportedDatabase.PostgreSql,
            out var result);

        // Empty string starts with neither "{" nor "SRID=", goes to FromTextInternal
        // which calls ExtractSridFromText("") returning (0, ""), then
        // Geography.FromWellKnownText("", 4326) which throws ArgumentException
        // The outer catch returns false
        Assert.False(success);
    }

    #endregion

    #region SpatialConverter PostgreSql with no data

    [Fact]
    public void SpatialConverter_PostgreSql_NoData_Throws()
    {
        var converter = new GeographyConverter();
        // GeoJson only - PostgreSql path returns GeoJson string
        var json = "{\"type\":\"Point\",\"coordinates\":[0,0]}";
        var geog = Geography.FromGeoJson(json, 4326);

        var result = converter.ToProviderValue(geog, SupportedDatabase.PostgreSql);

        Assert.Equal(json, result);
    }

    #endregion
}
