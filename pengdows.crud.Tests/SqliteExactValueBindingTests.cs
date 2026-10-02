using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-002, found live on SQLite (Microsoft.Data.Sqlite 9):
/// - A decimal was bound as a double, so -1234567890123456.0123456789 was stored as
///   "-1.23456789012346e+15". It is now bound as exact invariant text, which a REAL/NUMERIC column
///   still converts by affinity and a TEXT column keeps exactly (the driver itself stores
///   DbType.Decimal exactly; it does not store 0 as the old comment said).
/// - Decimal text with an exponent (as earlier releases stored) did not read back into a decimal.
/// - A pooled parameter is reset to an explicit Size of 0, and Microsoft.Data.Sqlite truncates text
///   to an explicit size: a string bound as DbType.Object (a JSON value) was stored as "".
/// </summary>
public sealed class SqliteExactValueBindingTests
{
    private const string Json = "{\"a\":1,\"b\":[true,null]}";

    [Table("exact_rows")]
    private sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("d", DbType.Decimal)] public decimal D { get; set; }
        [Column("j", DbType.Object)] public JsonValue J { get; set; }
        [Column("o", DbType.Object)] public string? O { get; set; }
    }

    private static DatabaseContext Context() =>
        new("Data Source=test;EmulatedProduct=Sqlite", new fakeDbFactory(SupportedDatabase.Sqlite));

    private static Dictionary<string, DbParameter> Params(ISqlContainer sc) =>
        ((IDictionary<string, DbParameter>)typeof(SqlContainer)
            .GetField("_parameters", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sc)!)
        .ToDictionary(kv => kv.Value.ParameterName, kv => kv.Value);

    [Fact]
    public void CreateDbParameter_Decimal_BindsExactInvariantText()
    {
        var p = Context().CreateDbParameter("p", DbType.Decimal, -1234567890123456.0123456789m);

        Assert.Equal("-1234567890123456.0123456789", p.Value);
        Assert.Equal(DbType.String, p.DbType);
    }

    [Fact]
    public void CreateDbParameter_NullDecimal_BindsNull()
    {
        var p = Context().CreateDbParameter<decimal?>("p", DbType.Decimal, null);

        Assert.True(p.Value is null or System.DBNull);
    }

    [Fact]
    public async Task BuildCreate_PooledParameters_StringValuesAreNeverTruncated()
    {
        await using var context = Context();
        var gateway = new TableGateway<Row, int>(context);

        // The second container reuses the first one's pooled parameters, which are reset to Size 0.
        await (await Task.FromResult(gateway.BuildCreate(new Row { Id = 1, J = new JsonValue(Json), O = "object" }))).DisposeAsync();
        await using var sc = gateway.BuildCreate(new Row { Id = 2, J = new JsonValue(Json), O = "object text" });

        foreach (var p in Params(sc).Values.Where(p => p.Value is string))
        {
            Assert.True(p.Size == 0 ? false : p.Size >= ((string)p.Value!).Length,
                $"{p.ParameterName}: Size {p.Size} truncates '{p.Value}'");
        }
    }

    [Theory]
    [InlineData("-1.23456789012346e+15", -1234567890123460)]
    [InlineData("1E-5", 0.00001)]
    [InlineData("-1234567890123456.0123456789", -1234567890123456.0123456789)]
    public async Task RetrieveOneAsync_DecimalStoredAsText_ReadsBack(string stored, double approximate)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        exec.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["id"] = 1, ["d"] = stored, ["j"] = Json, ["o"] = "x" }
        });
        factory.Connections.Add(exec);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard
        }, factory);

        var row = await new TableGateway<Row, int>(context).RetrieveOneAsync(1);

        Assert.Equal(decimal.Parse(stored, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture), row!.D);
        Assert.Equal(approximate, (double)row.D, 6);
    }
}
