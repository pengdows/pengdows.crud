using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Threading.Tasks;
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

    // BP-124: live 3.0 integration test PortableAdvancedTypeRoundTripTests failed on 2.0.6 with
    // SqlServer "No mapping exists from object type System.IO.MemoryStream" and PostgreSql
    // "Writing values of 'System.IO.StringReader' is not supported" — Stream/TextReader values
    // reached the provider raw because only AdvancedTypes.IsMappedType types were coerced.
    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.Sqlite)]
    [InlineData(SupportedDatabase.Oracle)]
    [InlineData(SupportedDatabase.Firebird)]
    [InlineData(SupportedDatabase.DuckDB)]
    public void BuildCreate_StreamTextReaderAndJsonDocument_BindProviderSafeValues(SupportedDatabase provider)
    {
        var factory = new fakeDbFactory(provider);
        using var context = new DatabaseContext($"Data Source=test;EmulatedProduct={provider}", factory);
        var gateway = new TableGateway<PortableAdvancedTypeEntity, int>(context);
        using var document = JsonDocument.Parse("{\"enabled\":true}");
        var entity = new PortableAdvancedTypeEntity
        {
            Id = 1,
            Payload = document,
            Bytes = new byte[] { 0, 1, 2 },
            Content = new MemoryStream(new byte[] { 9, 8, 7, 6 }),
            Notes = new StringReader("streamed notes")
        };

        using var container = gateway.BuildCreate(entity);

        var values = new List<object?>();
        for (var i = 0; i < container.ParameterCount; i++)
        {
            values.Add(container.GetParameterValue("i" + i));
        }

        Assert.DoesNotContain(values, v => v is Stream);
        Assert.DoesNotContain(values, v => v is TextReader);
        Assert.DoesNotContain(values, v => v is JsonDocument);
        Assert.Contains(values, v => v is byte[] b && b.Length == 4 && b[0] == 9);
        Assert.Contains(values, v => v is string s && s == "streamed notes");
    }

    // BP-124: live 3.0 integration test OracleIntervalRoundTripTests failed on 2.0.6 with
    // "CLR type 'String' is not compatible with DbType.Int64". ODP.NET reports an
    // OracleDbType.IntervalYM parameter's DbType as Int64 while it carries the provider string,
    // so re-materializing it (BuildCreate clones a cached template container) failed portable
    // DbType validation. OdpLikeFactory reproduces that provider behavior over fakeDb.
    [Fact]
    public void BuildCreate_OracleIntervalsDeclaredAsDbTypeObject_DoNotThrow()
    {
        var factory = new OdpLikeFactory();
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Oracle", factory);
        var gateway = new TableGateway<OracleIntervalEntity, int>(context);
        var entity = new OracleIntervalEntity
        {
            Id = 1,
            YearMonth = new IntervalYearMonth(3, 6),
            DaySecond = new IntervalDaySecond(5, new TimeSpan(12, 30, 45))
        };

        using var first = gateway.BuildCreate(entity);
        using var container = gateway.BuildCreate(entity);

        Assert.Equal(3, container.ParameterCount);
    }

    // BP-124 (3.0 81eef2b): ODP.NET binds INTERVAL parameters from Oracle's own literal format
    // ("+YYYY-MM" / "+DDDDDDDDD HH:MI:SS.FFFFFF"), not ISO-8601 durations.
    [Fact]
    public void OracleIntervals_UseOracleLiteralFormat()
    {
        var oracle = CreateDialect(SupportedDatabase.Oracle);

        var yearMonth = oracle.CreateDbParameter("ym", DbType.Object, new IntervalYearMonth(2, 3));
        var daySecond = oracle.CreateDbParameter("ds", DbType.Object,
            new IntervalDaySecond(4, new TimeSpan(5, 6, 7) + TimeSpan.FromTicks(5000000)));

        Assert.Equal("+0002-03", yearMonth.Value);
        Assert.Equal("+000000004 05:06:07.500000", daySecond.Value);
    }

    // BP-124: the read half of PortableAdvancedTypeRoundTripTests - Stream/TextReader properties
    // must hydrate from the provider's byte[]/string column values.
    [Fact]
    public async Task RetrieveOne_StreamAndTextReaderProperties_HydrateFromProviderValues()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite });
        var execConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        execConn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = 1,
                ["payload"] = "{\"enabled\":true}",
                ["bytes"] = new byte[] { 0, 1, 2 },
                ["content"] = new byte[] { 9, 8, 7, 6 },
                ["notes"] = "streamed notes"
            }
        });
        factory.Connections.Add(execConn);
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite", factory);
        var gateway = new TableGateway<PortableAdvancedTypeEntity, int>(context);

        var actual = await gateway.RetrieveOneAsync(1);

        Assert.NotNull(actual);
        Assert.True(actual!.Payload.RootElement.GetProperty("enabled").GetBoolean());
        using var copy = new MemoryStream();
        await actual.Content.CopyToAsync(copy);
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, copy.ToArray());
        Assert.Equal("streamed notes", await actual.Notes.ReadToEndAsync());
    }

    // BP-124: the read half of OracleIntervalRoundTripTests. ODP.NET surfaces INTERVAL YEAR TO
    // MONTH as its numeric month count (Int64) and INTERVAL DAY TO SECOND as TimeSpan.
    [Fact]
    public async Task RetrieveOne_OracleIntervals_HydrateFromOdpProviderValues()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Oracle });
        var execConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Oracle };
        execConn.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?>
            {
                ["id"] = 1,
                ["year_month"] = 42L,
                ["day_second"] = new TimeSpan(5, 12, 30, 45)
            }
        });
        factory.Connections.Add(execConn);
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Oracle", factory);
        var gateway = new TableGateway<OracleIntervalEntity, int>(context);

        var actual = await gateway.RetrieveOneAsync(1);

        Assert.NotNull(actual);
        Assert.Equal(new IntervalYearMonth(3, 6), actual!.YearMonth);
        Assert.Equal(new IntervalDaySecond(5, new TimeSpan(12, 30, 45)), actual.DaySecond);
    }

    public enum OdpLikeDbType
    {
        Varchar2 = 126,
        IntervalDS = 183,
        IntervalYM = 184
    }

    private sealed class OdpLikeParameter : fakeDbParameter
    {
        private OdpLikeDbType _oracleDbType = OdpLikeDbType.Varchar2;

        public OdpLikeDbType OracleDbType
        {
            get => _oracleDbType;
            set
            {
                _oracleDbType = value;
                // Mirrors ODP.NET: IntervalYM surfaces as DbType.Int64, IntervalDS as Object.
                DbType = value == OdpLikeDbType.IntervalYM ? DbType.Int64 : DbType.Object;
            }
        }
    }

    private sealed class OdpLikeFactory : System.Data.Common.DbProviderFactory
    {
        private readonly fakeDbFactory _inner = new(SupportedDatabase.Oracle);

        public override System.Data.Common.DbConnection CreateConnection() => _inner.CreateConnection()!;
        public override System.Data.Common.DbCommand CreateCommand() => _inner.CreateCommand()!;
        public override System.Data.Common.DbConnectionStringBuilder CreateConnectionStringBuilder() =>
            _inner.CreateConnectionStringBuilder()!;
        public override System.Data.Common.DbParameter CreateParameter() => new OdpLikeParameter();
    }

    [pengdows.crud.attributes.Table("portable_advanced_types")]
    private sealed class PortableAdvancedTypeEntity
    {
        [pengdows.crud.attributes.Id]
        [pengdows.crud.attributes.Column("id", DbType.Int32)]
        public int Id { get; set; }

        [pengdows.crud.attributes.Column("payload", DbType.String)]
        public JsonDocument Payload { get; set; } = null!;

        [pengdows.crud.attributes.Column("bytes", DbType.Binary)]
        public byte[] Bytes { get; set; } = Array.Empty<byte>();

        [pengdows.crud.attributes.Column("content", DbType.Object)]
        public Stream Content { get; set; } = null!;

        [pengdows.crud.attributes.Column("notes", DbType.Object)]
        public TextReader Notes { get; set; } = null!;
    }

    [pengdows.crud.attributes.Table("interval_roundtrip")]
    private sealed class OracleIntervalEntity
    {
        [pengdows.crud.attributes.Id]
        [pengdows.crud.attributes.Column("id", DbType.Int32)]
        public int Id { get; set; }

        [pengdows.crud.attributes.Column("year_month", DbType.Object)]
        public IntervalYearMonth YearMonth { get; set; }

        [pengdows.crud.attributes.Column("day_second", DbType.Object)]
        public IntervalDaySecond DaySecond { get; set; }
    }

    private static ISqlDialect CreateDialect(SupportedDatabase provider)
    {
        return SqlDialectFactory.CreateDialectForType(
            provider,
            new fakeDbFactory(provider),
            NullLogger<SqlDialect>.Instance);
    }
}
