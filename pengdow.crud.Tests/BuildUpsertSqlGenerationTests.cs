using System;
using pengdow.crud.enums;
using pengdow.crud.FakeDb;
using Xunit;

namespace pengdow.crud.Tests;

public class BuildUpsertSqlGenerationTests : SqlLiteContextTestBase
{
    [Fact]
    public void BuildUpsert_UsesOnConflict_ForSqlite()
    {
        TypeMap.Register<SampleEntity>();
        var helper = new EntityHelper<SampleEntity, int>(Context);
        var entity = new SampleEntity { Id = 1, MaxValue = 5, modeColumn = DbMode.Standard };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("INSERT INTO", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildUpsert_UsesMerge_ForPostgres15()
    {
        var factory = new FakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}", factory);

        var info = (DataSourceInformation)context.DataSourceInfo;
        var prop = typeof(DataSourceInformation).GetProperty("DatabaseProductVersion", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        prop!.SetValue(info, "PostgreSQL 15.0");

        TypeMap.Register<SampleEntity>();
        var helper = new EntityHelper<SampleEntity, int>(context);
        var entity = new SampleEntity { Id = 1, MaxValue = 5, modeColumn = DbMode.Standard };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        Assert.Contains("MERGE INTO", sql, StringComparison.OrdinalIgnoreCase);
    }
}
