#region

using System.Data;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class SqlContainerParameterNormalizationTests : SqlLiteContextTestBase
{
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
}

