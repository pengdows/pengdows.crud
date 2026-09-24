using System.Collections.Generic;
using System.Data;
using System.Linq;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Upsert SQL for [Version] entities, checked per dialect. Every one of these shapes failed or
/// lost updates against a real database (see MergeConflictTests.VersionedEntity_StaleUpsert_DetectsConflict):
/// - the version increment's right-hand side was unqualified, which PostgreSQL MERGE and the
///   PostgreSQL-family ON CONFLICT (CockroachDB, YugabyteDB) reject as ambiguous, since the MERGE
///   source / EXCLUDED row has the same column;
/// - Oracle's MERGE has no "WHEN MATCHED AND condition" form, so the version check must be a
///   WHERE on the UPDATE branch;
/// - SQLite and DuckDB support DO UPDATE ... WHERE but never emitted it, so a stale upsert won.
/// </summary>
public class UpsertVersionSqlTests
{
    [Table("versioned_entities")]
    public class VersionedRow
    {
        [Id][Column("id", DbType.Int64)] public long Id { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
        [Version][Column("version", DbType.Int32)] public int Version { get; set; }
    }

    [Table("versioned_pairs")]
    public class VersionedPair
    {
        [PrimaryKey(1)][Column("a", DbType.Int32)] public int A { get; set; }
        [PrimaryKey(2)][Column("b", DbType.Int32)] public int B { get; set; }
        [Column("name", DbType.String)] public string Name { get; set; } = "";
        [Version][Column("version", DbType.Int32)] public int Version { get; set; }
    }

    private static DatabaseContext Context(SupportedDatabase db) =>
        new($"Data Source=test;EmulatedProduct={db}", new fakeDbFactory(db));

    private static string UpsertSql(SupportedDatabase db)
    {
        var gw = new TableGateway<VersionedRow, long>(Context(db));
        using var sc = gw.BuildUpsert(new VersionedRow { Id = 1, Name = "x", Version = 1 });
        return sc.Query.ToString();
    }

    private static string PkUpsertSql(SupportedDatabase db)
    {
        var gw = new PrimaryKeyTableGateway<VersionedPair>(Context(db));
        using var sc = gw.BuildUpsert(new VersionedPair { A = 1, B = 2, Name = "x", Version = 1 });
        return sc.Query.ToString();
    }

    private static string BatchUpsertSql(SupportedDatabase db)
    {
        var gw = new TableGateway<VersionedRow, long>(Context(db));
        var containers = gw.BuildBatchUpsert(new List<VersionedRow>
        {
            new() { Id = 1, Name = "x", Version = 1 },
            new() { Id = 2, Name = "y", Version = 1 }
        });
        return containers.Single().Query.ToString();
    }

    private static string PkBatchUpsertSql(SupportedDatabase db)
    {
        var gw = new PrimaryKeyTableGateway<VersionedPair>(Context(db));
        var containers = gw.BuildBatchUpsert(new List<VersionedPair>
        {
            new() { A = 1, B = 2, Name = "x", Version = 1 },
            new() { A = 1, B = 3, Name = "y", Version = 1 }
        });
        return containers.Single().Query.ToString();
    }

    public static TheoryData<SupportedDatabase> MergeDialects() => new()
    {
        SupportedDatabase.PostgreSql, SupportedDatabase.SqlServer, SupportedDatabase.Oracle,
        SupportedDatabase.Db2, SupportedDatabase.Snowflake
    };

    public static TheoryData<SupportedDatabase> OnConflictDialects() => new()
    {
        SupportedDatabase.CockroachDb, SupportedDatabase.YugabyteDb, SupportedDatabase.Sqlite,
        SupportedDatabase.DuckDB
    };

    [Theory]
    [MemberData(nameof(MergeDialects))]
    public void Merge_VersionIncrementReadsTargetAlias(SupportedDatabase db)
    {
        foreach (var sql in new[] { UpsertSql(db), PkUpsertSql(db) })
        {
            Assert.Contains("= t.\"version\" + 1", sql);
        }
    }

    [Theory]
    [MemberData(nameof(OnConflictDialects))]
    public void OnConflict_VersionIncrementIsQualifiedAndGuarded(SupportedDatabase db)
    {
        foreach (var (sql, table) in new[]
                 {
                     (UpsertSql(db), "versioned_entities"), (PkUpsertSql(db), "versioned_pairs"),
                     (BatchUpsertSql(db), "versioned_entities"), (PkBatchUpsertSql(db), "versioned_pairs")
                 })
        {
            Assert.Contains($"\"version\" = \"{table}\".\"version\" + 1", sql);
            Assert.Contains($"WHERE \"{table}\".\"version\" = EXCLUDED.\"version\"", sql);
        }
    }

    [Fact]
    public void PostgreSqlBatchUpsert_UsesExcludedAndQualifiedVersion()
    {
        // PostgreSQL 15+ uses MERGE for single rows but ON CONFLICT for batches.
        foreach (var (sql, table) in new[]
                 {
                     (BatchUpsertSql(SupportedDatabase.PostgreSql), "versioned_entities"),
                     (PkBatchUpsertSql(SupportedDatabase.PostgreSql), "versioned_pairs")
                 })
        {
            Assert.Contains("\"name\" = EXCLUDED.\"name\"", sql);
            Assert.Contains($"\"version\" = \"{table}\".\"version\" + 1", sql);
            Assert.DoesNotContain("t.", sql);
        }
    }

    [Fact]
    public void Oracle_MergeVersionCheckIsUpdateWhere()
    {
        foreach (var sql in new[] { UpsertSql(SupportedDatabase.Oracle), PkUpsertSql(SupportedDatabase.Oracle) })
        {
            Assert.DoesNotContain("WHEN MATCHED AND", sql);
            Assert.Contains("WHEN MATCHED THEN UPDATE SET", sql);
            Assert.Contains("WHERE t.\"version\" = s.\"version\" WHEN NOT MATCHED", sql);
        }
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.Db2)]
    [InlineData(SupportedDatabase.Snowflake)]
    public void AnsiMerge_KeepsWhenMatchedAndVersionCheck(SupportedDatabase db)
    {
        foreach (var sql in new[] { UpsertSql(db), PkUpsertSql(db) })
        {
            Assert.Contains("WHEN MATCHED AND t.\"version\" = s.\"version\" THEN UPDATE SET", sql);
        }
    }
}
