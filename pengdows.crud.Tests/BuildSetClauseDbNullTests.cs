#region

using System;
using System.Threading.Tasks;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class BuildSetClauseDbNullTests : SqlLiteContextTestBase
{
    private readonly EntityHelper<DbNullEntity, int> _helper;

    public BuildSetClauseDbNullTests()
    {
        TypeMap.Register<DbNullEntity>();
        _helper = new EntityHelper<DbNullEntity, int>(Context);
    }

    [Fact]
    public async Task BuildUpdateAsync_WithDbNull_SetsColumnToNull()
    {
        var entity = new DbNullEntity { Id = 1, Data = DBNull.Value };
        var sc = await _helper.BuildUpdateAsync(entity);
        var sql = sc.Query.ToString();
        Assert.Contains("\"Data\" = NULL", sql);
        Assert.Equal(1, sc.ParameterCount);
    }

    [Fact]
    public async Task BuildUpdateAsync_WithValue_UsesParameter()
    {
        var entity = new DbNullEntity { Id = 1, Data = "value" };
        var sc = await _helper.BuildUpdateAsync(entity);
        var sql = sc.Query.ToString();
        Assert.DoesNotContain("= NULL", sql);
        Assert.Equal(2, sc.ParameterCount);
    }
}
