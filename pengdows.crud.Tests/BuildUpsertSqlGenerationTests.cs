using System;
using System.Collections.Generic;
using pengdows.crud.enums;
using pengdows.crud.FakeDb;
using pengdows.crud.Tests.Mocks;
using Xunit;

namespace pengdows.crud.Tests;

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
    public void BuildUpsert_CompositeKeys_ListAllInConflictClause()
    {
        TypeMap.Register<CompositeKeyEntity>();
        var helper = new EntityHelper<CompositeKeyEntity, int>(Context);
        var entity = new CompositeKeyEntity { Key1 = 1, Key2 = 2, Value = "v" };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        Assert.Contains("ON CONFLICT (\"Key1\", \"Key2\")", sql);
    }

}
