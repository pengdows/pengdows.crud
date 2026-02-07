#region

using System.Data;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

/// <summary>
/// Tests for ParameterNameComparer hash and equality behavior.
/// These tests ensure the optimized hash implementation maintains correct behavior.
/// </summary>
public class ParameterNameComparerPerformanceTests : SqlLiteContextTestBase
{
    [Fact]
    public void ParameterNameComparer_HandlesMarkerPrefixes_CaseInsensitive()
    {
        using var sc = Context.CreateSqlContainer("SELECT 1");

        // Add parameters with different markers - they should all map to the same parameter
        sc.AddParameterWithValue("p0", DbType.Int32, 100);

        // All these should resolve to the same underlying parameter
        Assert.Equal(100, sc.GetParameterValue("p0"));
        Assert.Equal(100, sc.GetParameterValue("@p0"));
        Assert.Equal(100, sc.GetParameterValue(":p0"));
        Assert.Equal(100, sc.GetParameterValue("?p0"));
        Assert.Equal(100, sc.GetParameterValue("$p0"));
    }

    [Fact]
    public void ParameterNameComparer_IsCaseSensitive()
    {
        using var sc = Context.CreateSqlContainer("SELECT 1");

        sc.AddParameterWithValue("UserId", DbType.Int32, 42);
        sc.AddParameterWithValue("userId", DbType.Int32, 43);  // Different case = different parameter
        sc.AddParameterWithValue("USERID", DbType.Int32, 44);

        // Case-sensitive: each variation is a distinct parameter
        Assert.Equal(42, sc.GetParameterValue("UserId"));
        Assert.Equal(43, sc.GetParameterValue("userId"));
        Assert.Equal(44, sc.GetParameterValue("USERID"));

        // Markers are stripped but case is preserved
        Assert.Equal(42, sc.GetParameterValue("@UserId"));
        Assert.Equal(43, sc.GetParameterValue(":userId"));
    }

    [Fact]
    public void ParameterNameComparer_HashCollision_DoesNotOccurForCommonNames()
    {
        using var sc = Context.CreateSqlContainer("SELECT 1");

        // Add multiple parameters with similar but distinct names
        sc.AddParameterWithValue("id", DbType.Int32, 1);
        sc.AddParameterWithValue("Id", DbType.Int32, 2);  // Different case = different parameter
        sc.AddParameterWithValue("name", DbType.String, "Alice");
        sc.AddParameterWithValue("value", DbType.Int32, 100);
        sc.AddParameterWithValue("count", DbType.Int32, 5);

        // Case-sensitive: "id" and "Id" are different parameters
        Assert.Equal(1, sc.GetParameterValue("id"));
        Assert.Equal(2, sc.GetParameterValue("Id"));

        // Different names remain distinct
        Assert.Equal("Alice", sc.GetParameterValue("name"));
        Assert.Equal(100, sc.GetParameterValue("value"));
        Assert.Equal(5, sc.GetParameterValue("count"));
    }

    [Fact]
    public void ParameterNameComparer_HandlesLongNames()
    {
        using var sc = Context.CreateSqlContainer("SELECT 1");

        var longName = "VeryLongParameterNameWith128Characters_" + new string('X', 85);
        sc.AddParameterWithValue(longName, DbType.String, "test");

        // Same name retrieves correctly
        Assert.Equal("test", sc.GetParameterValue(longName));

        // With marker prefix
        Assert.Equal("test", sc.GetParameterValue("@" + longName));
        Assert.Equal("test", sc.GetParameterValue(":" + longName));
    }

    [Fact]
    public void ParameterNameComparer_HandlesSpecialCharacters()
    {
        using var sc = Context.CreateSqlContainer("SELECT 1");

        // Underscores and digits are common in parameter names
        sc.AddParameterWithValue("param_1", DbType.Int32, 1);
        sc.AddParameterWithValue("param_2", DbType.Int32, 2);
        sc.AddParameterWithValue("user_id_123", DbType.Int32, 123);

        Assert.Equal(1, sc.GetParameterValue("param_1"));
        Assert.Equal(2, sc.GetParameterValue("param_2"));
        Assert.Equal(123, sc.GetParameterValue("user_id_123"));

        // With markers
        Assert.Equal(1, sc.GetParameterValue("@param_1"));
        Assert.Equal(123, sc.GetParameterValue(":user_id_123"));
    }

    [Fact]
    public void ParameterNameComparer_DifferentMarkersWithSameName_AreEqual()
    {
        using var sc = Context.CreateSqlContainer("SELECT 1");

        sc.AddParameterWithValue("@myParam", DbType.Int32, 10);

        // All marker variations should update the same parameter
        sc.SetParameterValue(":myParam", 20);
        Assert.Equal(20, sc.GetParameterValue("$myParam"));

        sc.SetParameterValue("?myParam", 30);
        Assert.Equal(30, sc.GetParameterValue("myParam"));
    }

    [Fact]
    public void ParameterNameComparer_EmptyAndNull_Handled()
    {
        using var sc = Context.CreateSqlContainer("SELECT 1");

        // Adding parameter with null name should auto-generate a name
        var param1 = sc.AddParameterWithValue(null, DbType.Int32, 100);
        Assert.NotNull(param1.ParameterName);
        Assert.NotEmpty(param1.ParameterName);

        // Can retrieve by the generated name
        var value = sc.GetParameterValue(param1.ParameterName);
        Assert.Equal(100, value);
    }

    [Theory]
    [InlineData("p0", "@p0")]
    [InlineData("name", "@name")]
    [InlineData("name", ":name")]
    [InlineData("count", "$count")]
    [InlineData("value", "?value")]
    public void ParameterNameComparer_EquivalentNames_ResolveToSameParameter(string name1, string name2)
    {
        using var sc = Context.CreateSqlContainer("SELECT 1");

        sc.AddParameterWithValue(name1, DbType.Int32, 999);

        // Both names should resolve to the same parameter (markers are stripped)
        Assert.Equal(999, sc.GetParameterValue(name1));
        Assert.Equal(999, sc.GetParameterValue(name2));

        // Setting via name2 should update the same parameter
        sc.SetParameterValue(name2, 777);
        Assert.Equal(777, sc.GetParameterValue(name1));
    }

    [Fact]
    public void ParameterNameComparer_HighVolumeParameterSet_NoCollisions()
    {
        using var sc = Context.CreateSqlContainer("SELECT 1");

        // Add many parameters with systematic names
        for (int i = 0; i < 100; i++)
        {
            sc.AddParameterWithValue($"p{i}", DbType.Int32, i);
        }

        // Verify all can be retrieved correctly
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(i, sc.GetParameterValue($"p{i}"));
            Assert.Equal(i, sc.GetParameterValue($"@p{i}"));
            Assert.Equal(i, sc.GetParameterValue($":p{i}"));
        }
    }
}
