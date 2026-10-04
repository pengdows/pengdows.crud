using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.attributes;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// Targeted tests for uncovered paths in MySqlDialect, SqlServerDialect, FirebirdDialect,
/// and TableGateway.Upsert.cs (JSON columns, multi-key joins, Firebird data types).
/// </summary>
public class CoveragePush_DialectMissingPathsTests
{
    // =========================================================================
    // MySqlDialect — ShouldDisablePrepareOn base path (line 164)
    // =========================================================================

    // =========================================================================
    // MySqlDialect — TryGetProviderErrorCode returns null (line 309)
    // =========================================================================

    // =========================================================================
    // MySqlDialect — PrepareConnectionStringForDataSource with isMySqlConnector=true (lines 262-275)
    // =========================================================================

    // =========================================================================
    // MySqlDialect — DetermineStandardCompliance version branches (lines 337-340)
    // =========================================================================

    [Fact]
    public void MySql_DetermineStandardCompliance_Version6_ReturnsSql2003()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance);

        // Line 337: >= 6 → Sql2003
        var result = dialect.DetermineStandardCompliance(new Version(6, 0, 0));
        Assert.Equal(SqlStandardLevel.Sql2003, result);
    }

    [Fact]
    public void MySql_DetermineStandardCompliance_Version5_ReturnsSql99()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance);

        // Line 338: >= 5 → Sql99
        var result = dialect.DetermineStandardCompliance(new Version(5, 7, 0));
        Assert.Equal(SqlStandardLevel.Sql99, result);
    }

    [Fact]
    public void MySql_DetermineStandardCompliance_OldVersion_ReturnsSql92()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance);

        // Line 339: _ → Sql92
        var result = dialect.DetermineStandardCompliance(new Version(4, 0, 0));
        Assert.Equal(SqlStandardLevel.Sql92, result);
    }

    // =========================================================================
    // MySqlDialect — TryEnterReadOnlyTransactionAsync (line 369)
    // =========================================================================

    // =========================================================================
    // SqlServerDialect — BuildBatchUpdateSql NULL value path (line 174)
    // =========================================================================

    // =========================================================================
    // SqlServerDialect — IsSnapshotIsolationOn (lines 293-300)
    // =========================================================================

    // =========================================================================
    // SqlServerDialect — IsReadCommittedSnapshotOn override (lines 282-289)
    // =========================================================================

    // =========================================================================
    // FirebirdDialect — GetGeneratedKeyPlan (line 220)
    // =========================================================================

    // =========================================================================
    // FirebirdDialect — ParseVersion with LI-V format (lines 383-387)
    // =========================================================================

    // =========================================================================
    // FirebirdDialect — ParseVersion with Firebird x.y format (lines 394-397)
    // =========================================================================

    // =========================================================================
    // FirebirdDialect — GetDatabaseVersion exercises async version query path (lines 409-415)
    // =========================================================================

    // =========================================================================
    // TableGateway.Upsert — JSON column in BuildUpsertOnConflict (line 173)
    // SQLite: SupportsInsertOnConflict=true, SupportsMerge=false → BuildUpsertOnConflict path
    // =========================================================================

    // =========================================================================
    // TableGateway.Upsert — JSON column in MySQL BuildUpsertOnDuplicate (line 280)
    // =========================================================================

    // =========================================================================
    // TableGateway.Upsert — JSON column in Firebird BuildFirebirdMergeUpsert (line 427)
    // =========================================================================

    // =========================================================================
    // TableGateway.Upsert — Multi-key MERGE join AND clause (line 360)
    // =========================================================================

    // =========================================================================
    // TableGateway.Upsert — Firebird multi-key comma in MATCHING clause (line 450)
    // =========================================================================

    // =========================================================================
    // Test entities
    // =========================================================================

    [Table("pg_json_upsert")]
    private sealed class PgJsonUpsertEntity
    {
        [Id(true)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Json]
        [Column("data", DbType.String)]
        public string Data { get; set; } = "{}";
    }

    [Table("mysql_json_upsert")]
    private sealed class MySqlJsonUpsertEntity
    {
        [Id(true)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Json]
        [Column("data", DbType.String)]
        public string Data { get; set; } = "{}";
    }

    [Table("firebird_json_upsert")]
    private sealed class FirebirdJsonUpsertEntity
    {
        [Id(true)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Json]
        [Column("data", DbType.String)]
        public string Data { get; set; } = "{}";
    }

    [Table("ss_multi_pk")]
    private sealed class SqlServerMultiPkEntity
    {
        [Id(true)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("key1", DbType.Int32)]
        public int Key1 { get; set; }

        [PrimaryKey(2)]
        [Column("key2", DbType.Int32)]
        public int Key2 { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Table("fb_multi_pk")]
    private sealed class FirebirdMultiPkEntity
    {
        [Id(true)]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [PrimaryKey(1)]
        [Column("key1", DbType.Int32)]
        public int Key1 { get; set; }

        [PrimaryKey(2)]
        [Column("key2", DbType.Int32)]
        public int Key2 { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    // BP-119 (3.0 3eb997c): MySQL error 1295 "This command is not supported in the prepared
    // statement protocol yet" must trigger the disable-prepare fallback like 1461 does.
    [Fact]
    public void MySql_ShouldDisablePrepareOn_UnsupportedPreparedStatement_ReturnsTrue()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance);

        Assert.True(dialect.ShouldDisablePrepareOn(
            new Exception("This command is not supported in the prepared statement protocol yet")));
        Assert.True(dialect.ShouldDisablePrepareOn(new Bp119MySqlExceptionWithNumber(1295, "error 1295")));
        Assert.False(dialect.ShouldDisablePrepareOn(new Bp119MySqlExceptionWithNumber(1064, "syntax error")));
    }

    private sealed class Bp119MySqlExceptionWithNumber : Exception
    {
        public int Number { get; }
        public Bp119MySqlExceptionWithNumber(int number, string message) : base(message) => Number = number;
    }
}
