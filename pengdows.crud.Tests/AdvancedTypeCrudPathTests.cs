using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Proves advanced value objects use the normal dialect parameter path. The
/// individual coercion tests validate algorithms; these tests validate wiring.
/// </summary>
public sealed class AdvancedTypeCrudPathTests
{
    [Fact]
    public void PostgreSqlNetworkValuesAreConvertedByCreateDbParameter()
    {
        var dialect = CreateDialect(SupportedDatabase.PostgreSql);

        var inet = dialect.CreateDbParameter("inet", DbType.String, new Inet(IPAddress.Parse("192.168.1.10"), 24));
        var cidr = dialect.CreateDbParameter("cidr", DbType.String, new Cidr(IPAddress.Parse("192.168.1.10"), 24));

        Assert.Equal("192.168.1.10/24", inet.Value);
        Assert.Equal("192.168.1.0/24", cidr.Value);
    }

    [Fact]
    public void JsonHStoreAndLongRangeUseCreateDbParameter()
    {
        var dialect = CreateDialect(SupportedDatabase.PostgreSql);
        var hstore = new HStore(new Dictionary<string, string?> { ["role"] = "admin" });
        var range = new Range<long>(1, 10);

        var jsonParameter = dialect.CreateDbParameter("json", DbType.String, new JsonValue("{\"enabled\":true}"));
        var hstoreParameter = dialect.CreateDbParameter("hstore", DbType.String, hstore);
        var rangeParameter = dialect.CreateDbParameter("range", DbType.String, range);

        Assert.Equal("{\"enabled\":true}", jsonParameter.Value);
        Assert.Equal("role=>admin", hstoreParameter.Value);
        Assert.Equal("[1,10)", rangeParameter.Value);
    }

    [Fact]
    public void SpatialValuesUseCreateDbParameterWithoutProviderCode()
    {
        var postgreSql = CreateDialect(SupportedDatabase.PostgreSql);

        var geography = Geography.FromWellKnownText("POINT(-87.6298 41.8781)", 4326);
        var geometry = Geometry.FromWellKnownText("POINT(10 20)", 0);

        var geographyParameter = postgreSql.CreateDbParameter("geography", DbType.Binary, geography);
        var geometryParameter = postgreSql.CreateDbParameter("geometry", DbType.Binary, geometry);

        Assert.NotNull(geographyParameter.Value);
        Assert.NotNull(geometryParameter.Value);
        Assert.Equal(geography.ToString(), geographyParameter.Value?.ToString());
        Assert.Equal(geometry.ToString(), geometryParameter.Value?.ToString());
    }

    [Fact]
    public void IntervalAndRowVersionValuesUseCreateDbParameter()
    {
        var oracle = CreateDialect(SupportedDatabase.Oracle);
        var sqlServer = CreateDialect(SupportedDatabase.SqlServer);

        var yearMonth = oracle.CreateDbParameter("ym", DbType.String, new IntervalYearMonth(2, 3));
        var daySecond = oracle.CreateDbParameter("ds", DbType.String, new IntervalDaySecond(4, TimeSpan.FromHours(5)));
        var rowVersion = sqlServer.CreateDbParameter("version", DbType.Binary, new RowVersion(new byte[8]));

        Assert.Equal("+0002-03", yearMonth.Value);
        Assert.NotNull(daySecond.Value);
        Assert.IsType<byte[]>(rowVersion.Value);
        Assert.Equal(8, ((byte[])rowVersion.Value).Length);
    }

    [Fact]
    public void AllRegisteredAdvancedClrTypesUseTheNormalCrudParameterPath()
    {
        var dialect = CreateDialect(SupportedDatabase.PostgreSql);
        using var document = JsonDocument.Parse("{\"active\":true}");
        var values = new (DbType Type, object Value)[]
        {
            (DbType.Object, new[] { 1, 2, 3 }),
            (DbType.Object, new[] { "one", "two" }),
            (DbType.Object, new JsonValue("{\"active\":true}")),
            (DbType.Object, document),
            (DbType.Object, document.RootElement),
            (DbType.Object, new HStore(new Dictionary<string, string?> { ["role"] = "admin" })),
            (DbType.Object, new Range<int>(1, 2)),
            (DbType.Object, new Range<DateTime>(DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1))),
            (DbType.Object, new Range<long>(1, 2)),
            (DbType.Object, new Inet(IPAddress.Parse("192.168.1.1"))),
            (DbType.Object, new Cidr(IPAddress.Parse("192.168.1.1"), 24)),
            (DbType.Object, new MacAddress(PhysicalAddress.Parse("08002B010203"))),
            (DbType.Object, new PostgreSqlInterval(1, 2, 3)),
            (DbType.Object, new IntervalYearMonth(1, 2)),
            (DbType.Object, new IntervalDaySecond(1, TimeSpan.FromSeconds(2))),
            (DbType.Object, Geometry.FromWellKnownText("POINT(10 20)", 0)),
            (DbType.Object, Geography.FromWellKnownText("POINT(-87 41)", 4326)),
            (DbType.Object, new MemoryStream(new byte[] { 1, 2 })),
            (DbType.Object, new StringReader("text"))
        };

        foreach (var (type, value) in values)
        {
            var parameter = dialect.CreateDbParameter("p", type, value);
            Assert.NotNull(parameter.Value);
            Assert.NotEqual(DBNull.Value, parameter.Value);
        }
    }

    [Fact]
    public void ProviderIndependentAdvancedTypesRoundTripThroughTheRegistry()
    {
        var registry = CoercionRegistry.Shared;
        var values = new object[]
        {
            new[] { 1, 2, 3 },
            new[] { "one", "two" },
            new JsonValue("{\"active\":true}"),
            new HStore(new Dictionary<string, string?> { ["role"] = "admin" }),
            new Range<int>(1, 2),
            new Range<DateTime>(DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(1)),
            new Range<long>(1, 2),
            new Inet(IPAddress.Parse("192.168.1.1")),
            new Cidr(IPAddress.Parse("192.168.1.1"), 24),
            new MacAddress(PhysicalAddress.Parse("08002B010203")),
            new PostgreSqlInterval(1, 2, 3),
            new IntervalYearMonth(1, 2),
            new IntervalDaySecond(1, TimeSpan.FromSeconds(2)),
            new RowVersion(new byte[8])
        };

        foreach (var provider in Enum.GetValues<SupportedDatabase>())
        {
            if (provider == SupportedDatabase.Unknown)
            {
                continue;
            }

            var dialect = CreateDialect(provider);
            foreach (var value in values)
            {
                var parameter = dialect.CreateDbParameter("p", DbType.Object, value);
                Assert.True(
                    registry.TryRead(
                        new DbValue(parameter.Value),
                        value.GetType(),
                        out var roundTripped,
                        provider),
                    $"No read coercion for {value.GetType().Name} from {provider}.");
                Assert.NotNull(roundTripped);
            }
        }

        var postgreSql = CreateDialect(SupportedDatabase.PostgreSql);
        foreach (var value in new object[]
                 {
                     Geometry.FromWellKnownText("POINT(10 20)", 0),
                     Geography.FromWellKnownText("POINT(-87 41)", 4326)
                 })
        {
            var parameter = postgreSql.CreateDbParameter("p", DbType.Object, value);
            Assert.True(
                registry.TryRead(new DbValue(parameter.Value), value.GetType(), out var roundTripped,
                    SupportedDatabase.PostgreSql),
                $"No spatial read coercion for {value.GetType().Name}.");
            Assert.NotNull(roundTripped);
        }
    }

    private static ISqlDialect CreateDialect(SupportedDatabase provider)
    {
        return SqlDialectFactory.CreateDialectForType(
            provider,
            new fakeDbFactory(provider),
            NullLogger<SqlDialect>.Instance);
    }
}
