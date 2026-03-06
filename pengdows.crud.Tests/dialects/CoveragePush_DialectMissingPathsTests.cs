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

    [Fact]
    public void MySql_ShouldDisablePrepareOn_BaseReturnsTrue_ReturnsTrue()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance);

        // Base returns true for NotSupportedException and InvalidOperationException
        Assert.True(dialect.ShouldDisablePrepareOn(new NotSupportedException("base veto")));
        Assert.True(dialect.ShouldDisablePrepareOn(new InvalidOperationException("base veto")));
    }

    // =========================================================================
    // MySqlDialect — TryGetProviderErrorCode returns null (line 309)
    // =========================================================================

    [Fact]
    public void MySql_ShouldDisablePrepareOn_ExceptionWithNoNumberProperty_ReturnsFalse()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance);

        // Plain Exception has no "Number" property → TryGetProviderErrorCode returns null (line 309)
        // IsMaxPreparedStatementLimit returns false → ShouldDisablePrepareOn returns false
        var result = dialect.ShouldDisablePrepareOn(new Exception("unrelated error"));
        Assert.False(result);
    }

    // =========================================================================
    // MySqlDialect — PrepareConnectionStringForDataSource with isMySqlConnector=true (lines 262-275)
    // =========================================================================

    [Fact]
    public void MySql_PrepareConnectionStringForDataSource_WithMySqlConnector_InjectsConnectionReset()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: true);

        var cs = "Server=localhost;Database=test;User=root;Password=pwd;";
        var result = dialect.PrepareConnectionStringForDataSource(cs);

        // Lines 271-275: Should inject ConnectionReset=false and return modified string
        Assert.Contains("ConnectionReset", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MySql_PrepareConnectionStringForDataSource_WithExistingConnectionReset_DoesNotOverride()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var dialect = new MySqlDialect(factory, NullLogger<MySqlDialect>.Instance, isMySqlConnector: true);

        // ConnectionReset already present → ContainsKey returns true → no injection (line 269 false branch)
        var cs = "Server=localhost;Database=test;User=root;Password=pwd;ConnectionReset=True;";
        var result = dialect.PrepareConnectionStringForDataSource(cs);

        Assert.Contains("ConnectionReset", result, StringComparison.OrdinalIgnoreCase);
    }

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

    [Fact]
    public async Task MySql_TryEnterReadOnlyTransactionAsync_ExecutesReadOnlySql()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        factory.EnableDataPersistence = true;
        using var ctx = new DatabaseContext(
            "Server=localhost;Database=test;EmulatedProduct=MySql", factory);

        using var txn = ctx.BeginTransaction();

        // Line 369: returns TryExecuteReadOnlySqlAsync(transaction, SetSessionReadOnlySql, ...)
        await ctx.Dialect.TryEnterReadOnlyTransactionAsync(txn, CancellationToken.None);

        // Success: no exception thrown
        txn.Rollback();
    }

    // =========================================================================
    // SqlServerDialect — BuildBatchUpdateSql NULL value path (line 174)
    // =========================================================================

    [Fact]
    public void SqlServer_BuildBatchUpdateSql_NullGetValue_WritesNullLiteral()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var ctx = new DatabaseContext(
            "Data Source=sql-server;EmulatedProduct=SqlServer", factory);

        using var query = new SqlQueryBuilder();
        // getValue returns null → line 174: query.Append("NULL")
        ctx.Dialect.BuildBatchUpdateSql(
            "\"t\"",
            new[] { "\"col\"" },
            new[] { "\"id\"" },
            1,
            query,
            (row, col) => null);

        var sql = query.ToString();
        Assert.Contains("NULL", sql, StringComparison.Ordinal);
        Assert.Contains("MERGE INTO", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqlServer_BuildBatchUpdateSql_DBNullGetValue_WritesNullLiteral()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var ctx = new DatabaseContext(
            "Data Source=sql-server;EmulatedProduct=SqlServer", factory);

        using var query = new SqlQueryBuilder();
        // getValue returns DBNull.Value → same NULL path (line 172-174)
        ctx.Dialect.BuildBatchUpdateSql(
            "\"t\"",
            new[] { "\"col\"" },
            new[] { "\"id\"" },
            1,
            query,
            (row, col) => DBNull.Value);

        var sql = query.ToString();
        Assert.Contains("NULL", sql, StringComparison.Ordinal);
    }

    // =========================================================================
    // SqlServerDialect — IsSnapshotIsolationOn (lines 293-300)
    // =========================================================================

    [Fact]
    public void SqlServer_IsSnapshotIsolationOn_ReturnsFalse_ForFakeDb()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var ctx = new DatabaseContext(
            "Data Source=sql-server;EmulatedProduct=SqlServer", factory);

        var conn = ctx.GetConnection(ExecutionType.Read);
        try
        {
            // Lines 293-300: executes SnapshotIsolationQuery, fakeDb returns null → 0 → false
            var result = ctx.Dialect.IsSnapshotIsolationOn(conn);
            Assert.False(result);
        }
        finally
        {
            ctx.CloseAndDisposeConnection(conn);
        }
    }

    // =========================================================================
    // SqlServerDialect — IsReadCommittedSnapshotOn override (lines 282-289)
    // =========================================================================

    [Fact]
    public void SqlServer_IsReadCommittedSnapshotOn_ReturnsFalse_ForFakeDb()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var ctx = new DatabaseContext(
            "Data Source=sql-server;EmulatedProduct=SqlServer", factory);

        var conn = ctx.GetConnection(ExecutionType.Read);
        try
        {
            // Lines 284-288: executes RcsiQuery, fakeDb returns null → 0 → false
            var result = ctx.Dialect.IsReadCommittedSnapshotOn(conn);
            Assert.False(result);
        }
        finally
        {
            ctx.CloseAndDisposeConnection(conn);
        }
    }

    // =========================================================================
    // FirebirdDialect — GetGeneratedKeyPlan (line 220)
    // =========================================================================

    [Fact]
    public void Firebird_GetGeneratedKeyPlan_ReturnsReturning()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        using var ctx = new DatabaseContext(
            "Data Source=test;EmulatedProduct=Firebird", factory);

        // Line 220: public override GeneratedKeyPlan GetGeneratedKeyPlan() => GeneratedKeyPlan.Returning
        var plan = ctx.Dialect.GetGeneratedKeyPlan();
        Assert.Equal(GeneratedKeyPlan.Returning, plan);
    }

    // =========================================================================
    // FirebirdDialect — ParseVersion with LI-V format (lines 383-387)
    // =========================================================================

    [Fact]
    public void Firebird_ParseVersion_LiVFormat_ParsesCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        using var ctx = new DatabaseContext(
            "Data Source=test;EmulatedProduct=Firebird", factory);

        // Lines 383-387: Regex matches LI-V3.0.0, int.TryParse, return new Version(major, minor, build)
        var version = ctx.Dialect.ParseVersion("LI-V3.0.0");
        Assert.NotNull(version);
        Assert.Equal(3, version.Major);
        Assert.Equal(0, version.Minor);
    }

    // =========================================================================
    // FirebirdDialect — ParseVersion with Firebird x.y format (lines 394-397)
    // =========================================================================

    [Fact]
    public void Firebird_ParseVersion_FirebirdFormat_ParsesCorrectly()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        using var ctx = new DatabaseContext(
            "Data Source=test;EmulatedProduct=Firebird", factory);

        // Lines 394-397: Regex matches "Firebird 4.0", int.TryParse, return new Version(major, minor)
        var version = ctx.Dialect.ParseVersion("Firebird 4.0");
        Assert.NotNull(version);
        Assert.Equal(4, version.Major);
        Assert.Equal(0, version.Minor);
    }

    // =========================================================================
    // FirebirdDialect — GetDatabaseVersion exercises async version query path (lines 409-415)
    // =========================================================================

    [Fact]
    public void Firebird_GetDatabaseVersion_ReturnsEmulatedVersion()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        using var ctx = new DatabaseContext(
            "Data Source=test;EmulatedProduct=Firebird", factory);

        var conn = ctx.GetConnection(ExecutionType.Read);
        try
        {
            // Lines 409-415: executes EngineVersionQuery → fakeDb returns "4.0.0" → returns it
            var versionStr = ctx.Dialect.GetDatabaseVersion(conn);
            Assert.Equal("4.0.0", versionStr);
        }
        finally
        {
            ctx.CloseAndDisposeConnection(conn);
        }
    }

    // =========================================================================
    // TableGateway.Upsert — JSON column in BuildUpsertOnConflict (line 173)
    // SQLite: SupportsInsertOnConflict=true, SupportsMerge=false → BuildUpsertOnConflict path
    // =========================================================================

    [Fact]
    public void Sqlite_BuildUpsert_WithJsonColumn_HitsOnConflictJsonPath()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var ctx = new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=Sqlite", factory);

        var gateway = new TableGateway<PgJsonUpsertEntity, int>(ctx);
        var entity = new PgJsonUpsertEntity { Id = 1, Data = "{}" };

        // Line 173: IsJsonType=true → dialect.RenderJsonArgument called in BuildUpsertOnConflict
        using var container = gateway.BuildUpsert(entity);
        var sql = container.Query.ToString();

        Assert.Contains("ON CONFLICT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INSERT INTO", sql, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // TableGateway.Upsert — JSON column in MySQL BuildUpsertOnDuplicate (line 280)
    // =========================================================================

    [Fact]
    public void MySql_BuildUpsert_WithJsonColumn_CallsRenderJsonArgument()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        using var ctx = new DatabaseContext(
            "Server=localhost;EmulatedProduct=MySql", factory);

        var gateway = new TableGateway<MySqlJsonUpsertEntity, int>(ctx);
        var entity = new MySqlJsonUpsertEntity { Id = 1, Data = "{}" };

        // Line 280: IsJsonType=true → dialect.RenderJsonArgument called (base pass-through)
        using var container = gateway.BuildUpsert(entity);
        var sql = container.Query.ToString();

        Assert.Contains("ON DUPLICATE KEY UPDATE", sql, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // TableGateway.Upsert — JSON column in Firebird BuildFirebirdMergeUpsert (line 427)
    // =========================================================================

    [Fact]
    public void Firebird_BuildUpsert_WithJsonColumn_CallsRenderJsonArgument()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        using var ctx = new DatabaseContext(
            "Data Source=test;EmulatedProduct=Firebird", factory);

        var gateway = new TableGateway<FirebirdJsonUpsertEntity, int>(ctx);
        var entity = new FirebirdJsonUpsertEntity { Id = 1, Data = "{}" };

        // Line 427: IsJsonType=true → dialect.RenderJsonArgument called
        using var container = gateway.BuildUpsert(entity);
        var sql = container.Query.ToString();

        Assert.Contains("UPDATE OR INSERT INTO", sql, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // TableGateway.Upsert — Multi-key MERGE join AND clause (line 360)
    // =========================================================================

    [Fact]
    public void SqlServer_BuildUpsert_CompositeKey_IncludesAndInJoinCondition()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var ctx = new DatabaseContext(
            "Data Source=sql-server;EmulatedProduct=SqlServer", factory);

        var gateway = new TableGateway<SqlServerMultiPkEntity, int>(ctx);
        var entity = new SqlServerMultiPkEntity { Id = 1, Key1 = 10, Key2 = 20, Name = "test" };

        // Line 360: join.Append(SqlFragments.And) — 2 PK columns → i=1 branch triggers AND
        using var container = gateway.BuildUpsert(entity);
        var sql = container.Query.ToString();

        Assert.Contains("MERGE INTO", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(" AND ", sql, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    // TableGateway.Upsert — Firebird multi-key comma in MATCHING clause (line 450)
    // =========================================================================

    [Fact]
    public void Firebird_BuildUpsert_CompositeKey_IncludesCommaInMatchingClause()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        using var ctx = new DatabaseContext(
            "Data Source=test;EmulatedProduct=Firebird", factory);

        var gateway = new TableGateway<FirebirdMultiPkEntity, int>(ctx);
        var entity = new FirebirdMultiPkEntity { Id = 1, Key1 = 10, Key2 = 20, Name = "test" };

        // Line 450: sc.Query.Append(", ") — 2 PK columns in MATCHING → comma between them
        using var container = gateway.BuildUpsert(entity);
        var sql = container.Query.ToString();

        Assert.Contains("UPDATE OR INSERT INTO", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MATCHING", sql, StringComparison.OrdinalIgnoreCase);
        // MATCHING clause: MATCHING ("key1", "key2") → contains comma
        Assert.Matches(@"MATCHING\s*\([^)]+,[^)]+\)", sql);
    }

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
}
