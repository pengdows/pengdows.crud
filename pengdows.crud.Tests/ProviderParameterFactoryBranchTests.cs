using System;
using System.Data;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using pengdows.crud.types.valueobjects;
using Xunit;

namespace pengdows.crud.Tests;

public class ProviderParameterFactoryBranchTests
{
    [Fact]
    public void PostgreSqlOptimizations_SetNpgsqlDbType()
    {
        var guidParam = new NpgsqlParameterStub();
        ProviderParameterFactory.TryConfigureParameter(guidParam, typeof(Guid), Guid.NewGuid(), SupportedDatabase.PostgreSql);
        Assert.Equal(27, guidParam.NpgsqlDbType);

        var stringArrayParam = new NpgsqlParameterStub();
        ProviderParameterFactory.TryConfigureParameter(stringArrayParam, typeof(string[]), new[] { "a", "b" }, SupportedDatabase.PostgreSql);
        Assert.Equal((1 << 30) | 16, stringArrayParam.NpgsqlDbType);

        var intArrayParam = new NpgsqlParameterStub();
        ProviderParameterFactory.TryConfigureParameter(intArrayParam, typeof(int[]), new[] { 1, 2 }, SupportedDatabase.PostgreSql);
        Assert.Equal((1 << 30) | 1, intArrayParam.NpgsqlDbType);

        using var doc = JsonDocument.Parse("{\"a\":1}");
        var jsonParam = new NpgsqlParameterStub();
        ProviderParameterFactory.TryConfigureParameter(jsonParam, typeof(JsonElement), doc.RootElement, SupportedDatabase.PostgreSql);
        Assert.Equal(14, jsonParam.NpgsqlDbType);

        var hstoreParam = new NpgsqlParameterStub();
        ProviderParameterFactory.TryConfigureParameter(hstoreParam, typeof(HStore), new HStore(new System.Collections.Generic.Dictionary<string, string?>()), SupportedDatabase.PostgreSql);
        Assert.Equal(37, hstoreParam.NpgsqlDbType);

        var intRangeParam = new NpgsqlParameterStub();
        ProviderParameterFactory.TryConfigureParameter(intRangeParam, typeof(Range<int>), new Range<int>(1, 2), SupportedDatabase.PostgreSql);
        Assert.Equal(33, intRangeParam.NpgsqlDbType);

        var dateRangeParam = new NpgsqlParameterStub();
        ProviderParameterFactory.TryConfigureParameter(dateRangeParam, typeof(Range<DateTime>), new Range<DateTime>(DateTime.UtcNow, DateTime.UtcNow.AddDays(1)), SupportedDatabase.PostgreSql);
        Assert.Equal(35, dateRangeParam.NpgsqlDbType);
    }

    [Fact]
    public void SqlServerOptimizations_HandleCommonTypes()
    {
        var guidParam = new fakeDbParameter();
        ProviderParameterFactory.TryConfigureParameter(guidParam, typeof(Guid), Guid.NewGuid(), SupportedDatabase.SqlServer);
        Assert.Equal(DbType.Guid, guidParam.DbType);

        var jsonParam = new fakeDbParameter();
        ProviderParameterFactory.TryConfigureParameter(jsonParam, typeof(JsonValue), new JsonValue("{\"a\":1}"), SupportedDatabase.SqlServer);
        Assert.Equal(DbType.String, jsonParam.DbType);
        Assert.Equal(-1, jsonParam.Size);

        var rowVersionParam = new fakeDbParameter { Size = 8 };
        ProviderParameterFactory.TryConfigureParameter(rowVersionParam, typeof(byte[]), new byte[8], SupportedDatabase.SqlServer);
        Assert.Equal(DbType.Binary, rowVersionParam.DbType);
        Assert.Equal(8, rowVersionParam.Size);

        var moneyParam = new fakeDbParameter { DbType = DbType.Currency };
        ProviderParameterFactory.TryConfigureParameter(moneyParam, typeof(decimal), 12.3m, SupportedDatabase.SqlServer);
        Assert.Equal(DbType.Decimal, moneyParam.DbType);
    }

    [Fact]
    public void MySqlOptimizations_HandleBooleanJsonAndDateTime()
    {
        var boolParam = new fakeDbParameter();
        ProviderParameterFactory.TryConfigureParameter(boolParam, typeof(bool), true, SupportedDatabase.MySql);
        Assert.Equal(DbType.Byte, boolParam.DbType);

        var jsonParam = new fakeDbParameter();
        ProviderParameterFactory.TryConfigureParameter(jsonParam, typeof(JsonValue), new JsonValue("{\"a\":1}"), SupportedDatabase.MySql);
        Assert.Equal(DbType.String, jsonParam.DbType);

        var dtParam = new fakeDbParameter();
        ProviderParameterFactory.TryConfigureParameter(dtParam, typeof(DateTime), DateTime.UtcNow, SupportedDatabase.MySql);
        Assert.Equal(DbType.DateTime, dtParam.DbType);
    }

    [Fact]
    public void OracleOptimizations_HandleDecimalDateTimeAndGuid()
    {
        var decimalParam = new fakeDbParameter();
        ProviderParameterFactory.TryConfigureParameter(decimalParam, typeof(decimal), 12.3m, SupportedDatabase.Oracle);
        Assert.Equal(DbType.Decimal, decimalParam.DbType);
        Assert.Equal((byte)38, decimalParam.Precision);
        Assert.Equal((byte)10, decimalParam.Scale);

        var dtParam = new fakeDbParameter();
        ProviderParameterFactory.TryConfigureParameter(dtParam, typeof(DateTime), DateTime.UtcNow, SupportedDatabase.Oracle);
        Assert.Equal(DbType.DateTime, dtParam.DbType);

        var guidParam = new fakeDbParameter();
        ProviderParameterFactory.TryConfigureParameter(guidParam, typeof(Guid), Guid.NewGuid(), SupportedDatabase.Oracle);
        Assert.Equal(DbType.Binary, guidParam.DbType);
        Assert.Equal(16, guidParam.Size);
    }

    [Fact]
    public void SqliteAndDuckDbOptimizations_HandleSpecialCases()
    {
        var sqliteJson = new fakeDbParameter();
        ProviderParameterFactory.TryConfigureParameter(sqliteJson, typeof(JsonValue), new JsonValue("{\"a\":1}"), SupportedDatabase.Sqlite);
        Assert.Equal(DbType.String, sqliteJson.DbType);

        var sqliteGuid = new fakeDbParameter();
        ProviderParameterFactory.TryConfigureParameter(sqliteGuid, typeof(Guid), Guid.NewGuid(), SupportedDatabase.Sqlite);
        Assert.Equal(DbType.String, sqliteGuid.DbType);

        var duckGuid = new fakeDbParameter();
        ProviderParameterFactory.TryConfigureParameter(duckGuid, typeof(Guid), Guid.NewGuid(), SupportedDatabase.DuckDB);
        Assert.Equal(DbType.Guid, duckGuid.DbType);

        var duckArray = new fakeDbParameter();
        ProviderParameterFactory.TryConfigureParameter(duckArray, typeof(int[]), new[] { 1, 2 }, SupportedDatabase.DuckDB);
        Assert.Equal(DbType.Object, duckArray.DbType);

        var duckJson = new fakeDbParameter();
        ProviderParameterFactory.TryConfigureParameter(duckJson, typeof(JsonValue), new JsonValue("{\"a\":1}"), SupportedDatabase.DuckDB);
        Assert.Equal(DbType.String, duckJson.DbType);
    }

    private sealed class NpgsqlParameterStub : fakeDbParameter
    {
        public int NpgsqlDbType { get; set; }
    }
}
