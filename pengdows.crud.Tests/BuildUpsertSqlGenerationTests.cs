using System;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
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

    [Fact]
    public void BuildUpsert_OnConflict_UpdateSet_IsStable()
    {
        TypeMap.Register<UpsertLiteEntity>();
        var helper = new EntityHelper<UpsertLiteEntity, int>(Context);
        var entity = new UpsertLiteEntity { Id = 1, Name = "v", Version = 1 };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        var dialect = ((ISqlDialectProvider)Context).Dialect;
        var columns = BuildInsertColumns(Context);
        var values = BuildInsertValues(dialect);
        var updateSet = BuildConflictUpdateSet(Context, dialect);
        var expected = $"INSERT INTO {Context.WrapObjectName("UpsertLite")} ({columns}) VALUES ({values}) " +
                       $"ON CONFLICT ({Context.WrapObjectName("Id")}) DO UPDATE SET {updateSet}";
        Assert.Equal(expected, sql);
    }

    [Fact]
    public void BuildUpsert_OnDuplicate_UpdateSet_IsStable()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<UpsertLiteEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory, typeMap);
        var helper = new EntityHelper<UpsertLiteEntity, int>(context);
        var entity = new UpsertLiteEntity { Id = 1, Name = "v", Version = 1 };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        var dialect = ((ISqlDialectProvider)context).Dialect;
        var columns = BuildInsertColumns(context);
        var values = BuildInsertValues(dialect);
        var updateSet = BuildConflictUpdateSet(context, dialect);
        var expected = $"INSERT INTO {context.WrapObjectName("UpsertLite")} ({columns}) VALUES ({values}) " +
                       $"ON DUPLICATE KEY UPDATE {updateSet}";
        Assert.Equal(expected, sql);
    }

    [Fact]
    public void BuildUpsert_Merge_UpdateSet_IsStable()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<UpsertLiteEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory, typeMap);
        var helper = new EntityHelper<UpsertLiteEntity, int>(context);
        var entity = new UpsertLiteEntity { Id = 1, Name = "v", Version = 1 };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        var dialect = ((ISqlDialectProvider)context).Dialect;
        var table = context.WrapObjectName("UpsertLite");
        var wrappedId = context.WrapObjectName("Id");
        var wrappedName = context.WrapObjectName("Name");
        var wrappedVersion = context.WrapObjectName("Version");
        var values = BuildInsertValues(dialect);
        var srcColumns = string.Join(", ", new[] { wrappedId, wrappedName, wrappedVersion });
        var insertValues = string.Join(", ", new[] { $"s.{wrappedId}", $"s.{wrappedName}", $"s.{wrappedVersion}" });
        var updateSet = BuildMergeUpdateSet(context, dialect);
        var expected = $"MERGE INTO {table} t USING (VALUES ({values})) AS s ({srcColumns}) ON " +
                       $"t.{wrappedId} = s.{wrappedId} WHEN MATCHED THEN UPDATE SET {updateSet} " +
                       $"WHEN NOT MATCHED THEN INSERT ({srcColumns}) VALUES ({insertValues});";
        Assert.Equal(expected, sql);
    }

    private static string BuildInsertColumns(IDatabaseContext context)
    {
        return string.Join(", ", new[]
        {
            context.WrapObjectName("Id"),
            context.WrapObjectName("Name"),
            context.WrapObjectName("Version")
        });
    }

    private static string BuildInsertValues(ISqlDialect dialect)
    {
        return string.Join(", ", new[]
        {
            dialect.MakeParameterName("i0"),
            dialect.MakeParameterName("i1"),
            dialect.MakeParameterName("i2")
        });
    }

    private static string BuildConflictUpdateSet(IDatabaseContext context, ISqlDialect dialect)
    {
        var wrappedName = context.WrapObjectName("Name");
        var wrappedVersion = context.WrapObjectName("Version");
        return $"{wrappedName} = {dialect.UpsertIncomingColumn("Name")}, {wrappedVersion} = {wrappedVersion} + 1";
    }

    private static string BuildMergeUpdateSet(IDatabaseContext context, ISqlDialect dialect)
    {
        var targetPrefix = dialect.MergeUpdateRequiresTargetAlias ? "t." : "";
        var wrappedName = context.WrapObjectName("Name");
        var wrappedVersion = context.WrapObjectName("Version");
        return $"{targetPrefix}{wrappedName} = s.{wrappedName}, " +
               $"{targetPrefix}{wrappedVersion} = {targetPrefix}{wrappedVersion} + 1";
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

    [Table("UpsertLite")]
    private class UpsertLiteEntity
    {
        [Id]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Name", DbType.String)]
        public string Name { get; set; } = string.Empty;

        [Version]
        [Column("Version", DbType.Int32)]
        public int Version { get; set; }
    }
}
