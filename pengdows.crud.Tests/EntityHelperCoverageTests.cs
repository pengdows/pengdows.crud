using System;
using System.Reflection;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class EntityHelperCoverageTests
{
    [Fact]
    public void BuildUpsert_UsesDuplicate_ForMySql()
    {
        var factory = new FakeDbFactory(SupportedDatabase.MySql);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.MySql}", factory);
           var helper = new EntityHelper<TestEntity, int>(context);
        var entity = new TestEntity { Id = 1, Name = "foo" };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        Assert.Contains("ON DUPLICATE KEY UPDATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildUpsert_UsesOnConflict_ForPostgres14()
    {
        var factory = new FakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}", factory);
        var info = (DataSourceInformation)context.DataSourceInfo;
        var prop = typeof(DataSourceInformation).GetProperty("DatabaseProductVersion", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        prop!.SetValue(info, "PostgreSQL 14.0");
        var helper = new EntityHelper<SampleEntity, int>(context);
        var entity = new SampleEntity { Id = 1, MaxValue = 5, modeColumn = DbMode.Standard };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MERGE INTO", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("PostgreSQL 15.2", true, 15)]
    [InlineData("", false, 0)]
    public void TryParseMajorVersion_Works(string input, bool expectedResult, int expectedMajor)
    {
        var method = typeof(EntityHelper<SampleEntity, int>).GetMethod("TryParseMajorVersion", BindingFlags.NonPublic | BindingFlags.Static)!;
        object[] parameters = { input, 0 };
        var result = (bool)method.Invoke(null, parameters)!;
        Assert.Equal(expectedResult, result);
        Assert.Equal(expectedMajor, (int)parameters[1]);
    }
}
