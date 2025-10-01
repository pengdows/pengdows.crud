using System;
using System.Data;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests;

public class ParameterBindingRulesTests
{
    private enum SampleEnum
    {
        None = 0,
        Value = 1
    }

    [Fact]
    public void ApplyBindingRules_DateTime_SetsDbType()
    {
        var parameter = new fakeDbParameter();
        var value = new DateTime(2024, 1, 1, 8, 30, 0, DateTimeKind.Utc);

        var applied = ParameterBindingRules.ApplyBindingRules(parameter, typeof(DateTime), value, SupportedDatabase.PostgreSql);

        Assert.True(applied);
        Assert.Equal(value, parameter.Value);
        Assert.Equal(DbType.DateTime, parameter.DbType);
    }

    [Fact]
    public void ApplyBindingRules_Boolean_NormalizesForMariaDb()
    {
        var parameter = new fakeDbParameter();

        var applied = ParameterBindingRules.ApplyBindingRules(parameter, typeof(bool), true, SupportedDatabase.MariaDb);

        Assert.True(applied);
        Assert.Equal(DbType.Byte, parameter.DbType);
        Assert.Equal((byte)1, parameter.Value);
    }

    [Fact]
    public void ApplyBindingRules_Enum_UsesProviderConvention()
    {
        var pgParameter = new fakeDbParameter();
        var sqlParameter = new fakeDbParameter();

        Assert.True(ParameterBindingRules.ApplyBindingRules(pgParameter, typeof(SampleEnum), SampleEnum.Value, SupportedDatabase.PostgreSql));
        Assert.True(ParameterBindingRules.ApplyBindingRules(sqlParameter, typeof(SampleEnum), SampleEnum.Value, SupportedDatabase.SqlServer));

        Assert.Equal(DbType.String, pgParameter.DbType);
        Assert.Equal("Value", pgParameter.Value);
        Assert.Equal(DbType.Int32, sqlParameter.DbType);
        Assert.Equal(1, sqlParameter.Value);
    }

    [Fact]
    public void ApplyBindingRules_Array_UsesNativeArraysForPostgres()
    {
        var parameter = new fakeDbParameter();
        var values = new[] { 1, 2, 3 };

        var applied = ParameterBindingRules.ApplyBindingRules(parameter, values.GetType(), values, SupportedDatabase.PostgreSql);

        Assert.True(applied);
        Assert.Same(values, parameter.Value);
        Assert.Equal(DbType.Object, parameter.DbType);
    }

    [Fact]
    public void ApplyBindingRules_Array_FallsBackToJsonElsewhere()
    {
        var parameter = new fakeDbParameter();
        var values = new[] { "a", "b" };

        var applied = ParameterBindingRules.ApplyBindingRules(parameter, values.GetType(), values, SupportedDatabase.SqlServer);

        Assert.True(applied);
        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("[\"a\",\"b\"]", parameter.Value);
    }

    [Fact]
    public void ApplyBindingRules_LargeBinary_FallsBackToJson()
    {
        var parameter = new fakeDbParameter();
        var data = new byte[10];

        var applied = ParameterBindingRules.ApplyBindingRules(parameter, data.GetType(), data, SupportedDatabase.SqlServer);

        Assert.True(applied);
        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal("\"AAAAAAAAAAAAAA==\"", parameter.Value);
        
    }

    [Fact]
    public void ApplyBindingRules_LargeString_SetsSize()
    {
        var parameter = new fakeDbParameter();
        var large = new string('x', 9000);

        var applied = ParameterBindingRules.ApplyBindingRules(parameter, large.GetType(), large, SupportedDatabase.SqlServer);

        Assert.True(applied);
        Assert.Equal(DbType.String, parameter.DbType);
        Assert.Equal(-1, parameter.Size);
    }

    [Fact]
    public void ApplyBindingRules_BooleanNull_YieldsDbNull()
    {
        var parameter = new fakeDbParameter();

        var applied = ParameterBindingRules.ApplyBindingRules(parameter, typeof(bool?), null, SupportedDatabase.MySql);

        Assert.True(applied);
        Assert.Equal(DBNull.Value, parameter.Value);
        Assert.Equal(DbType.Boolean, parameter.DbType);
    }
}
