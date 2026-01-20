using System;
using System.Data;
using System.IO;
using System.Text.Json;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests;

public class ParameterBindingRulesBranchTests
{
    private enum SampleEnum
    {
        A = 1,
        B = 2
    }

    [Fact]
    public void ApplyBindingRules_DateTimeOffsetAndTimeSpan()
    {
        var dtoParam = new fakeDbParameter();
        Assert.True(ParameterBindingRules.ApplyBindingRules(dtoParam, typeof(DateTimeOffset), DateTimeOffset.UtcNow, SupportedDatabase.Sqlite));
        Assert.Equal(DbType.DateTimeOffset, dtoParam.DbType);

        var tsParam = new fakeDbParameter();
        Assert.True(ParameterBindingRules.ApplyBindingRules(tsParam, typeof(TimeSpan), TimeSpan.FromMinutes(5), SupportedDatabase.Sqlite));
        Assert.Equal(DbType.Time, tsParam.DbType);
    }

    [Fact]
    public void ApplyBindingRules_Enum_UsesDefaultString()
    {
        var parameter = new fakeDbParameter();
        Assert.True(ParameterBindingRules.ApplyBindingRules(parameter, typeof(SampleEnum), SampleEnum.B, SupportedDatabase.Unknown));
        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("B", parameter.Value);
    }

    [Fact]
    public void ApplyBindingRules_Array_HandlesDuckDbAndNull()
    {
        var duckParam = new fakeDbParameter();
        var values = new[] { 1, 2, 3 };
        Assert.True(ParameterBindingRules.ApplyBindingRules(duckParam, values.GetType(), values, SupportedDatabase.DuckDB));
        Assert.Equal(DbType.Object, duckParam.DbType);
        Assert.Same(values, duckParam.Value);

        var nullParam = new fakeDbParameter();
        Assert.True(ParameterBindingRules.ApplyBindingRules(nullParam, typeof(int[]), null, SupportedDatabase.DuckDB));
        Assert.Equal(DbType.Object, nullParam.DbType);
        Assert.Equal(DBNull.Value, nullParam.Value);
    }

    [Fact]
    public void ApplyBindingRules_LargeObjects_HandleStreamingAndDefaults()
    {
        var largeBytes = new byte[90000];
        var streamParam = new fakeDbParameter();
        Assert.True(ParameterBindingRules.ApplyBindingRules(streamParam, typeof(byte[]), largeBytes, SupportedDatabase.SqlServer));
        Assert.IsType<string>(streamParam.Value);
        Assert.Equal(DbType.String, streamParam.DbType);

        var smallBytes = new byte[] { 1, 2, 3 };
        var smallParam = new fakeDbParameter();
        Assert.True(ParameterBindingRules.ApplyBindingRules(smallParam, typeof(byte[]), smallBytes, SupportedDatabase.SqlServer));
        Assert.Equal(DbType.String, smallParam.DbType);

        var jsonParam = new fakeDbParameter();
        using var doc = JsonDocument.Parse("{\"a\":1}");
        Assert.True(ParameterBindingRules.ApplyBindingRules(jsonParam, typeof(JsonDocument), doc, SupportedDatabase.SqlServer));
        Assert.Equal(DbType.String, jsonParam.DbType);

        var smallText = "short";
        var textParam = new fakeDbParameter();
        Assert.True(ParameterBindingRules.ApplyBindingRules(textParam, typeof(string), smallText, SupportedDatabase.SqlServer));
        Assert.Equal(DbType.String, textParam.DbType);

        var nullParam = new fakeDbParameter();
        Assert.True(ParameterBindingRules.ApplyBindingRules(nullParam, typeof(string), null, SupportedDatabase.SqlServer));
        Assert.Equal(DBNull.Value, nullParam.Value);
    }

    [Fact]
    public void ApplyLargeObjectBinding_LargeByteArray_UsesStream()
    {
        var method = typeof(ParameterBindingRules).GetMethod(
            "ApplyLargeObjectBinding",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        var parameter = new fakeDbParameter();
        var bytes = new byte[90000];

        method!.Invoke(null, new object[] { parameter, typeof(byte[]), bytes, SupportedDatabase.SqlServer });

        Assert.IsType<MemoryStream>(parameter.Value);
        Assert.Equal(DbType.Binary, parameter.DbType);
    }
}
