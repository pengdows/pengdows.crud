using System;
using System.Collections.Generic;
using System.Data;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class BuildUpsertSqlGenerationTests : SqlLiteContextTestBase
{
    [Fact]
    public void BuildUpsert_UsesOnConflict_ForSqlite()
    {
        TypeMap.Register<SampleEntity>();
        var helper = new TableGateway<SampleEntity, int>(Context);
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
        var helper = new TableGateway<CompositeKeyEntity, int>(Context);
        var entity = new CompositeKeyEntity { Key1 = 1, Key2 = 2, Value = "v" };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        Assert.Contains("ON CONFLICT (\"Key1\", \"Key2\")", sql);
    }

    [Fact]
    public void BuildUpsert_OnConflict_BumpsVersion()
    {
        TypeMap.Register<TestEntity>();
        var helper = new TableGateway<TestEntity, int>(Context);
        var entity = new TestEntity { Id = 1, Name = "v" };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        var wrapped = Context.WrapObjectName("Version");
        // Qualified with the table: EXCLUDED has the same column, and PostgreSQL-family engines
        // reject the unqualified reference as ambiguous.
        Assert.Contains($"{wrapped} = {Context.WrapObjectName("Test")}.{wrapped} + 1", sql);
    }

    [Fact]
    public void BuildUpsert_OnDuplicate_BumpsVersion()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory);
        TypeMap.Register<TestEntity>();
        var helper = new TableGateway<TestEntity, int>(context);
        var entity = new TestEntity { Id = 1, Name = "v" };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        var wrapped = context.WrapObjectName("Version");
        // Qualified with the target table: the "AS incoming" row alias makes a bare reference ambiguous.
        Assert.Contains($"{wrapped} = {context.WrapObjectName("Test")}.{wrapped} + 1", sql);
    }

    [Fact]
    public void BuildUpsert_Merge_BumpsVersion()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);
        TypeMap.Register<TestEntity>();
        var helper = new TableGateway<TestEntity, int>(context);
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
        var helper = new TableGateway<ByteVersionEntity, int>(Context);
        var entity = new ByteVersionEntity { Id = 1, Name = "v" };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        Assert.DoesNotContain("+ 1", sql);
    }

    // DRY-016: the single-row ON CONFLICT upsert guarded an opaque (byte[]/RowVersion) version with
    // WHERE t.version = EXCLUDED.version. The database generates that value, so the guard compared it
    // with whatever the INSERT carried and an existing row was never updated; the batch upsert
    // (UpsertOnConflictVersionWhere) and every other path leave an opaque version unguarded.
    [Fact]
    public void BuildUpsert_ByteArrayVersion_OnConflict_HasNoVersionGuard()
    {
        TypeMap.Register<ByteVersionEntity>();
        var helper = new TableGateway<ByteVersionEntity, int>(Context);

        var sql = helper.BuildUpsert(new ByteVersionEntity { Id = 1, Name = "v" }).Query.ToString();

        Assert.Contains("ON CONFLICT", sql);
        Assert.DoesNotContain("EXCLUDED." + Context.WrapObjectName("Version"), sql);
        Assert.DoesNotContain(" WHERE ", sql);
    }

    // An opaque version left null is the database's to set: create and upsert leave it alone (one
    // InitializeVersion for every path, DRY-016).
    [Fact]
    public void BuildCreateAndUpsert_NullByteArrayVersion_LeaveItNull()
    {
        TypeMap.Register<ByteVersionEntity>();
        var helper = new TableGateway<ByteVersionEntity, int>(Context);

        var created = new ByteVersionEntity { Id = 1, Name = "v", Version = null! };
        using (helper.BuildCreate(created))
        {
        }

        var upserted = new ByteVersionEntity { Id = 2, Name = "v", Version = null! };
        using (helper.BuildUpsert(upserted))
        {
        }

        Assert.Null(created.Version);
        Assert.Null(upserted.Version);
    }

    [Fact]
    public void BuildUpsert_OnConflict_UpdateSet_IsStable()
    {
        TypeMap.Register<UpsertLiteEntity>();
        var helper = new TableGateway<UpsertLiteEntity, int>(Context);
        var entity = new UpsertLiteEntity { Id = 1, Name = "v", Version = 1 };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        var dialect = Context.GetDialect();
        var columns = BuildInsertColumns(Context);
        var values = BuildInsertValues(dialect);
        var updateSet = BuildConflictUpdateSet(Context, dialect);
        var table = Context.WrapObjectName("UpsertLite");
        var version = Context.WrapObjectName("Version");
        var expected = $"INSERT INTO {table} ({columns}) VALUES ({values}) " +
                       $"ON CONFLICT ({Context.WrapObjectName("Id")}) DO UPDATE SET {updateSet}" +
                       $" WHERE {table}.{version} = EXCLUDED.{version}";
        Assert.Equal(expected, sql);
    }

    [Fact]
    public void BuildUpsert_OnDuplicate_UpdateSet_IsStable()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<UpsertLiteEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory, typeMap);
        var helper = new TableGateway<UpsertLiteEntity, int>(context);
        var entity = new UpsertLiteEntity { Id = 1, Name = "v", Version = 1 };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        var dialect = context.GetDialect();
        var columns = BuildInsertColumns(context);
        var values = BuildInsertValues(dialect);
        var updateSet = BuildConflictUpdateSet(context, dialect);
        var expected = $"INSERT INTO {context.WrapObjectName("UpsertLite")} ({columns}) VALUES ({values}) " +
                       $"AS {context.WrapObjectName("incoming")} ON DUPLICATE KEY UPDATE {updateSet}";
        Assert.Equal(expected, sql);
    }

    [Fact]
    public void BuildUpsert_Merge_UpdateSet_IsStable()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<UpsertLiteEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory, typeMap);
        var helper = new TableGateway<UpsertLiteEntity, int>(context);
        var entity = new UpsertLiteEntity { Id = 1, Name = "v", Version = 1 };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        var dialect = context.GetDialect();
        var table = context.WrapObjectName("UpsertLite");
        var wrappedId = context.WrapObjectName("Id");
        var wrappedName = context.WrapObjectName("Name");
        var wrappedVersion = context.WrapObjectName("Version");
        var values = BuildInsertValues(dialect);
        var srcColumns = string.Join(", ", new[] { wrappedId, wrappedName, wrappedVersion });
        var insertValues = string.Join(", ", new[] { $"s.{wrappedId}", $"s.{wrappedName}", $"s.{wrappedVersion}" });
        var updateSet = BuildMergeUpdateSet(context, dialect);
        // UpsertLiteEntity has a [Version] column → WHEN MATCHED arm includes version guard.
        var expected = $"MERGE INTO {table} t USING (VALUES ({values})) AS s ({srcColumns}) ON " +
                       $"t.{wrappedId} = s.{wrappedId} WHEN MATCHED AND t.{wrappedVersion} = s.{wrappedVersion} THEN UPDATE SET {updateSet} " +
                       $"WHEN NOT MATCHED THEN INSERT ({srcColumns}) VALUES ({insertValues});";
        Assert.Equal(expected, sql);
    }

    [Fact]
    public void BuildUpsert_Merge_DoesNotUpdateConflictKey()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<UpsertNaturalKeyEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory, typeMap);
        var helper = new TableGateway<UpsertNaturalKeyEntity, int>(context);
        var entity = new UpsertNaturalKeyEntity { Id = 1, NaturalKey = "k", Value = 7 };

        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();

        var dialect = context.GetDialect();
        var targetPrefix = dialect.MergeUpdateRequiresTargetAlias ? "t." : "";
        var wrappedKey = context.WrapObjectName("NaturalKey");
        var wrappedValue = context.WrapObjectName("Value");

        var updateSetStart = sql.IndexOf("UPDATE SET ", StringComparison.Ordinal);
        var updateSetEnd = sql.IndexOf(" WHEN NOT MATCHED", StringComparison.Ordinal);
        Assert.True(updateSetStart >= 0 && updateSetEnd > updateSetStart);
        var updateSet = sql.Substring(updateSetStart, updateSetEnd - updateSetStart);

        Assert.DoesNotContain($"{targetPrefix}{wrappedKey} = s.{wrappedKey}", updateSet);
        Assert.Contains($"{targetPrefix}{wrappedValue} = s.{wrappedValue}", updateSet);
    }

    /// <summary>
    /// Regression test for a real bug found while validating <c>EmitsAnsiMergeSyntax</c> against a
    /// live PostgreSQL 18 Testcontainers instance: for dialects where
    /// <see cref="ISqlDialect.MergeUpdateRequiresTargetAlias"/> is false (PostgreSQL, DuckDB), the
    /// version-increment fragment used the same unqualified prefix on BOTH sides of
    /// <c>"version" = "version" + 1</c>. Postgres's real MERGE parser rejects the bare RHS
    /// reference with "column reference is ambiguous" because both the target table (t) and the
    /// MERGE source (s) expose a "version" column — fakeDb never parses SQL, so this was invisible
    /// to unit tests until it hit a real PostgreSQL server. The RHS must always be qualified with
    /// the target alias "t." (always declared via "MERGE INTO ... t"), regardless of
    /// MergeUpdateRequiresTargetAlias, which only governs whether the LHS is aliased.
    /// </summary>
    [Fact]
    public void BuildUpsert_Merge_BumpsVersion_ForDialectWithoutTargetAlias_QualifiesCurrentValueWithTargetAlias()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", factory);
        Assert.True(context.GetDialect().SupportsMerge, "Test assumes fakeDb's emulated PostgreSQL version satisfies SupportsMerge (>=15).");
        Assert.False(context.GetDialect().MergeUpdateRequiresTargetAlias);

        TypeMap.Register<TestEntity>();
        var helper = new TableGateway<TestEntity, int>(context);
        var entity = new TestEntity { Id = 1, Name = "v" };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();
        var wrapped = context.WrapObjectName("Version");

        // LHS unqualified (Postgres MERGE forbids target alias on the SET target),
        // RHS qualified with "t." so it unambiguously reads the target's current value.
        Assert.Contains($"{wrapped} = t.{wrapped} + 1", sql);
        Assert.DoesNotContain($" {wrapped} = {wrapped} + 1", sql);
    }

    [Fact]
    public void BuildUpsert_Merge_Oracle_UsesSelectFromDual()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<UpsertLiteEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Oracle", factory, typeMap);
        var helper = new TableGateway<UpsertLiteEntity, int>(context);
        var entity = new UpsertLiteEntity { Id = 1, Name = "v", Version = 1 };
        var sc = helper.BuildUpsert(entity);
        var sql = sc.Query.ToString();

        Assert.Contains("USING (SELECT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FROM DUAL", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("USING (VALUES", sql, StringComparison.OrdinalIgnoreCase);
        Assert.False(sql.EndsWith(";", StringComparison.Ordinal));
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

    // MySQL 8.0.19+ "INSERT ... AS incoming ON DUPLICATE KEY UPDATE" puts the incoming row in scope
    // under a name, so an unqualified [Version] reference in the increment is ambiguous (MySQL error
    // 1052, verified live). Every ON DUPLICATE KEY path — single and batch, both gateways — shares
    // one cached fragment per gateway, so each gateway's single and batch SQL is asserted.
    [Fact]
    public void OnDuplicateKeyWithRowAlias_QualifiesVersionIncrement_OnBothGatewaysAndBatch()
    {
        var typeMap = new TypeMapRegistry();
        typeMap.Register<UpsertLiteEntity>();
        typeMap.Register<UpsertPkVersionEntity>();
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory, typeMap);
        var dialect = context.GetDialect();
        Assert.False(string.IsNullOrEmpty(dialect.UpsertIncomingAlias));
        var version = context.WrapObjectName("Version");

        var gateway = new TableGateway<UpsertLiteEntity, int>(context);
        var rows = new List<UpsertLiteEntity>
        {
            new() { Id = 1, Name = "a", Version = 1 },
            new() { Id = 2, Name = "b", Version = 1 }
        };
        var liteTable = context.WrapObjectName("UpsertLite");
        Assert.Contains($"{version} = {liteTable}.{version} + 1", gateway.BuildUpsert(rows[0]).Query.ToString());
        Assert.Contains($"{version} = {liteTable}.{version} + 1",
            gateway.BuildBatchUpsert(rows)[0].Query.ToString());

        var pkGateway = new PrimaryKeyTableGateway<UpsertPkVersionEntity>(context);
        var pkRows = new List<UpsertPkVersionEntity>
        {
            new() { Code = "a", Name = "a", Version = 1 },
            new() { Code = "b", Name = "b", Version = 1 }
        };
        var pkTable = context.WrapObjectName("UpsertPkVersion");
        Assert.Contains($"{version} = {pkTable}.{version} + 1", pkGateway.BuildUpsert(pkRows[0]).Query.ToString());
        Assert.Contains($"{version} = {pkTable}.{version} + 1",
            pkGateway.BuildBatchUpsert(pkRows)[0].Query.ToString());
    }

    [Table("UpsertPkVersion")]
    private class UpsertPkVersionEntity
    {
        [PrimaryKey][Column("Code", DbType.String)] public string Code { get; set; } = string.Empty;

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Version]
        [Column("Version", DbType.Int32)]
        public int Version { get; set; }
    }

    private static string BuildConflictUpdateSet(IDatabaseContext context, ISqlDialect dialect)
    {
        var wrappedName = context.WrapObjectName("Name");
        var wrappedVersion = context.WrapObjectName("Version");
        // The target's version must be qualified whenever the incoming row is also in scope under a
        // name: ON CONFLICT's EXCLUDED, and MySQL 8.0.19+'s "INSERT ... AS incoming" row alias.
        // MySQL rejects the unqualified form there with "Column 'version' in field list is
        // ambiguous" (verified live).
        var target = dialect.SupportsInsertOnConflict || !string.IsNullOrEmpty(dialect.UpsertIncomingAlias)
            ? context.WrapObjectName("UpsertLite") + "."
            : "";
        return $"{wrappedName} = {dialect.UpsertIncomingColumn("Name")}, " +
               $"{wrappedVersion} = {target}{wrappedVersion} + 1";
    }

    private static string BuildMergeUpdateSet(IDatabaseContext context, ISqlDialect dialect)
    {
        var targetPrefix = dialect.MergeUpdateRequiresTargetAlias ? "t." : "";
        var wrappedName = context.WrapObjectName("Name");
        var wrappedVersion = context.WrapObjectName("Version");
        return $"{targetPrefix}{wrappedName} = s.{wrappedName}, " +
               $"{targetPrefix}{wrappedVersion} = t.{wrappedVersion} + 1";
    }

    [Table("ByteVersion")]
    private class ByteVersionEntity
    {
        [Id][Column("Id", DbType.Int32)] public int Id { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Version]
        [Column("Version", DbType.Binary)]
        public byte[] Version { get; set; } = Array.Empty<byte>();
    }

    [Table("UpsertLite")]
    private class UpsertLiteEntity
    {
        [Id][Column("Id", DbType.Int32)] public int Id { get; set; }

        [Column("Name", DbType.String)] public string Name { get; set; } = string.Empty;

        [Version]
        [Column("Version", DbType.Int32)]
        public int Version { get; set; }
    }

    [Table("UpsertNaturalKey")]
    private class UpsertNaturalKeyEntity
    {
        [Id][Column("Id", DbType.Int32)] public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("NaturalKey", DbType.String)]
        public string NaturalKey { get; set; } = string.Empty;

        [Column("Value", DbType.Int32)] public int Value { get; set; }
    }
}
