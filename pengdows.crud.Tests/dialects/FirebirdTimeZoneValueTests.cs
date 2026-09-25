using System;
using System.Data;
using System.Reflection;
using FirebirdSql.Data.Types;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// CONFIRMED live (Firebird 5.0.4, FirebirdClient 10.3.3): the driver rejects DbType.DateTimeOffset
/// ("Invalid data type: 27"), and a plain DateTime cannot be written to a TIMESTAMP WITH TIME ZONE
/// column ("Incorrect time zone value"). An FbZonedDateTime holding the UTC instant in zone "UTC",
/// with DbType.Object, is encoded by the driver against whichever column type the server describes:
/// a TIMESTAMP WITH TIME ZONE column stores the instant, and a plain TIMESTAMP column stores the UTC
/// wall time - byte-for-byte what the UTC-DateTime coercion stored before, under any session time
/// zone. Firebird 3 has no zoned types, so it keeps the UTC-DateTime coercion.
/// </summary>
public class FirebirdTimeZoneValueTests
{
    private static readonly DateTimeOffset Value = new(2026, 2, 21, 12, 34, 56, TimeSpan.FromHours(-5));

    private static FirebirdDialect CreateDialect(Version? version)
    {
        var dialect = new FirebirdDialect(new fakeDbFactory(SupportedDatabase.Firebird), NullLogger<FirebirdDialect>.Instance);
        if (version != null)
        {
            typeof(SqlDialect).GetField("_productInfo", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(dialect, new DatabaseProductInfo
                {
                    ProductName = "Firebird",
                    ProductVersion = version.ToString(),
                    ParsedVersion = version,
                    DatabaseType = SupportedDatabase.Firebird,
                    StandardCompliance = dialect.DetermineStandardCompliance(version)
                });
        }

        return dialect;
    }

    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void CreateDbParameter_DateTimeOffset_OnFirebird4Plus_SendsZonedUtcInstant(int major)
    {
        var param = CreateDialect(new Version(major, 0)).CreateDbParameter("p", DbType.DateTimeOffset, Value);

        Assert.Equal(DbType.Object, param.DbType);
        var zoned = Assert.IsType<FbZonedDateTime>(param.Value);
        Assert.Equal("UTC", zoned.TimeZone);
        Assert.Equal(DateTimeKind.Utc, zoned.DateTime.Kind);
        Assert.Equal(Value.UtcDateTime, zoned.DateTime);
    }

    [Fact]
    public void CreateDbParameter_DateTimeOffset_OnFirebird3_KeepsUtcDateTime()
    {
        var param = CreateDialect(new Version(3, 0)).CreateDbParameter("p", DbType.DateTimeOffset, Value);

        Assert.Equal(DbType.DateTime, param.DbType);
        var stored = Assert.IsType<DateTime>(param.Value);
        Assert.Equal(DateTimeKind.Unspecified, stored.Kind);
        Assert.Equal(Value.UtcDateTime, DateTime.SpecifyKind(stored, DateTimeKind.Utc));
    }

    [Fact]
    public void CreateDbParameter_DateTimeOffset_BeforeDetection_KeepsUtcDateTime()
    {
        var param = CreateDialect(null).CreateDbParameter("p", DbType.DateTimeOffset, Value);

        Assert.Equal(DbType.DateTime, param.DbType);
        Assert.IsType<DateTime>(param.Value);
    }

    [Fact]
    public void CreateDbParameter_NullDateTimeOffset_OnFirebird5_IsDbNull()
    {
        var param = CreateDialect(new Version(5, 0)).CreateDbParameter<DateTimeOffset?>("p", DbType.DateTimeOffset, null);

        Assert.Equal(DBNull.Value, param.Value);
    }

    // CONFIRMED live: once FirebirdClient holds an FbZonedDateTime with DbType.Object it reports the
    // parameter's DbType as Binary, and SqlContainer.CloneParameter (cached gateway templates)
    // re-creates the parameter from that DbType and value. An already-zoned value must stay an
    // Object parameter carrying the same FbZonedDateTime.
    [Fact]
    public void CreateDbParameter_AlreadyZonedValue_WithDriverReportedDbType_StaysZoned()
    {
        var zoned = new FbZonedDateTime(DateTime.SpecifyKind(Value.UtcDateTime, DateTimeKind.Utc), "UTC");

        var param = CreateDialect(new Version(5, 0)).CreateDbParameter<object>("p", DbType.Binary, zoned);

        Assert.Equal(DbType.Object, param.DbType);
        Assert.Equal(zoned, Assert.IsType<FbZonedDateTime>(param.Value));
    }

    [Fact]
    public void Coerce_FbZonedDateTime_ToDateTimeOffset_IsTheUtcInstant()
    {
        var zoned = new FbZonedDateTime(DateTime.SpecifyKind(Value.UtcDateTime, DateTimeKind.Utc), "UTC");

        var result = TypeCoercionHelper.Coerce(zoned, typeof(FbZonedDateTime), typeof(DateTimeOffset));

        var dto = Assert.IsType<DateTimeOffset>(result);
        Assert.Equal(Value.UtcDateTime, dto.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, dto.Offset);
    }

    [Fact]
    public void Coerce_FbZonedDateTime_ToDateTime_IsUtc()
    {
        var zoned = new FbZonedDateTime(DateTime.SpecifyKind(Value.UtcDateTime, DateTimeKind.Utc), "UTC");

        var result = TypeCoercionHelper.Coerce(zoned, typeof(FbZonedDateTime), typeof(DateTime));

        var dt = Assert.IsType<DateTime>(result);
        Assert.Equal(DateTimeKind.Utc, dt.Kind);
        Assert.Equal(Value.UtcDateTime, dt);
    }
}
