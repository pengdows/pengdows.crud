#region

using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SqlContainerParameterNormalizationTests : SqlLiteContextTestBase
{
    [Theory]
    [InlineData("@gamma")]
    [InlineData(":gamma")]
    [InlineData("?gamma")]
    [InlineData("$gamma")]
    public void AddParameterWithValue_StripsDialectPrefix_WhenStoring(string rawName)
    {
        using var sc = Context.CreateSqlContainer("SELECT 1");

        sc.AddParameterWithValue(rawName, DbType.Int32, 123);

        var param = GetSingleParameter(sc);
        Assert.Equal("gamma", param.ParameterName);
    }

    [Theory]
    [InlineData("@beta")]
    [InlineData(":beta")]
    [InlineData("?beta")]
    [InlineData("$beta")]
    public void AddParameter_StripsDialectPrefix_WhenStoring(string rawName)
    {
        using var sc = Context.CreateSqlContainer("SELECT 1");
        var param = new SqliteParameter
        {
            ParameterName = rawName,
            DbType = DbType.Int32,
            Value = 7
        };

        sc.AddParameter(param);

        var stored = GetSingleParameter(sc);
        Assert.Equal("beta", stored.ParameterName);
    }

    [Fact]
    public void GetAndSetParameterValue_NormalizesNameMarkers()
    {
        using var sc = Context.CreateSqlContainer("SELECT 1");

        // Add a known parameter name
        sc.AddParameterWithValue("p0", DbType.Int32, 123);

        // All marker variants should resolve to the same parameter
        Assert.Equal(123, sc.GetParameterValue("p0"));
        Assert.Equal(123, sc.GetParameterValue("@p0"));
        Assert.Equal(123, sc.GetParameterValue(":p0"));
        Assert.Equal(123, sc.GetParameterValue("$p0"));

        // Setting via a different marker updates the underlying parameter
        sc.SetParameterValue(":p0", 456);
        Assert.Equal(456, sc.GetParameterValue("p0"));
        Assert.Equal(456, sc.GetParameterValue("@p0"));
        Assert.Equal(456, sc.GetParameterValue("$p0"));
    }

    private static DbParameter GetSingleParameter(ISqlContainer container)
    {
        var sqlContainer = Assert.IsType<SqlContainer>(container);
        var field = typeof(SqlContainer).GetField("_parameters", BindingFlags.Instance | BindingFlags.NonPublic);
        var dictionary = (IDictionary<string, DbParameter>)field!.GetValue(sqlContainer)!;
        return Assert.Single(dictionary.Values);
    }
}
