using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
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

    [Fact]
    public void BuildUpsert_OnConflict_BumpsVersion()
    {
        TypeMap.Register<TestEntity>();
        var helper = new EntityHelper<TestEntity, int>(Context);
        var entity = new TestEntity { Id = 1, Name = "v" };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        var wrapped = Context.WrapObjectName("Version");
        Assert.Contains($"{wrapped} = {wrapped} + 1", sql);
    }

    [Fact]
    public void BuildUpsert_OnDuplicate_BumpsVersion()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory);
        TypeMap.Register<TestEntity>();
        var helper = new EntityHelper<TestEntity, int>(context);
        var entity = new TestEntity { Id = 1, Name = "v" };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        var wrapped = context.WrapObjectName("Version");
        Assert.Contains($"{wrapped} = {wrapped} + 1", sql);
    }

    [Fact]
    public void BuildUpsert_Merge_BumpsVersion()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);
        TypeMap.Register<TestEntity>();
        var helper = new EntityHelper<TestEntity, int>(context);
        var entity = new TestEntity { Id = 1, Name = "v" };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        var wrapped = context.WrapObjectName("Version");
        Assert.Contains($"t.{wrapped} = t.{wrapped} + 1", sql);
    }

    [Fact]
    public void BuildUpsert_ByteArrayVersion_DoesNotBump()
    {
        TypeMap.Register<ByteVersionEntity>();
        var helper = new EntityHelper<ByteVersionEntity, int>(Context);
        var entity = new ByteVersionEntity { Id = 1, Name = "v" };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        Assert.DoesNotContain("+ 1", sql);
    }

    [Table("ByteVersion")]
    private class ByteVersionEntity
    {
        [Id]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Version]
        [Column("Version", DbType.Binary)]
        public byte[] Version { get; set; } = Array.Empty<byte>();
    }
}
