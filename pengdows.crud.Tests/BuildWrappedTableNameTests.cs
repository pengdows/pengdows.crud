using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Verifies that BuildWrappedTableName respects SupportsNamespaces — dialects that do not
/// support schemas must omit the schema prefix even when [Table("Name","Schema")] is set.
/// Regression: the original code unconditionally included the schema, producing invalid SQL
/// like "HangFire"."Job" on Firebird and SQLite.
/// </summary>
public class BuildWrappedTableNameTests
{
    [Table("Job", "HangFire")]
    private class SchemaEntity
    {
        [Id(false)]
        [Column("id", DbType.Int64)]
        public long Id { get; set; }

        [Column("name", DbType.String)]
        public string Name { get; set; } = string.Empty;
    }

    [Fact]
    public void BuildCreate_Firebird_DropsSchemaPrefix_WhenDialectDoesNotSupportNamespaces()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Firebird", factory);
        var gateway = new TableGateway<SchemaEntity, long>(context);
        var entity = new SchemaEntity { Name = "test" };

        using var sc = gateway.BuildCreate(entity);
        var sql = sc.Query.ToString();

        Assert.DoesNotContain("HangFire", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JOB", sql, StringComparison.OrdinalIgnoreCase); // Firebird uppercases
    }

    [Fact]
    public void BuildCreate_Sqlite_DropsSchemaPrefix_WhenDialectDoesNotSupportNamespaces()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var context = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", factory);
        var gateway = new TableGateway<SchemaEntity, long>(context);
        var entity = new SchemaEntity { Name = "test" };

        using var sc = gateway.BuildCreate(entity);
        var sql = sc.Query.ToString();

        Assert.DoesNotContain("HangFire", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Job", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildCreate_PostgreSql_IncludesSchemaPrefix_WhenDialectSupportsNamespaces()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", factory);
        var gateway = new TableGateway<SchemaEntity, long>(context);
        var entity = new SchemaEntity { Name = "test" };

        using var sc = gateway.BuildCreate(entity);
        var sql = sc.Query.ToString();

        Assert.Contains("HangFire", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Job", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildCreate_SqlServer_IncludesSchemaPrefix_WhenDialectSupportsNamespaces()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=SqlServer", factory);
        var gateway = new TableGateway<SchemaEntity, long>(context);
        var entity = new SchemaEntity { Name = "test" };

        using var sc = gateway.BuildCreate(entity);
        var sql = sc.Query.ToString();

        Assert.Contains("HangFire", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Job", sql, StringComparison.OrdinalIgnoreCase);
    }

    // ── WrappedTableName property (used by hangfire gateways) ─────────────────

    [Fact]
    public void WrappedTableName_Firebird_DropsSchemaPrefix_WhenDialectDoesNotSupportNamespaces()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=Firebird", factory);
        var gateway = new TableGateway<SchemaEntity, long>(context);

        Assert.DoesNotContain("HangFire", gateway.WrappedTableName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Job", gateway.WrappedTableName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WrappedTableName_PostgreSql_IncludesSchemaPrefix_WhenDialectSupportsNamespaces()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var context = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", factory);
        var gateway = new TableGateway<SchemaEntity, long>(context);

        Assert.Contains("HangFire", gateway.WrappedTableName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Job", gateway.WrappedTableName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildWrappedTableName_MultiDbEnvironment_PostgreSqlIncludesSchema_SqliteOmitsSchema()
    {
        var pgFactory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var pgContext = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", pgFactory);
        var pgGateway = new TableGateway<SchemaEntity, long>(pgContext);

        var sqliteFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var sqliteContext = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", sqliteFactory);
        var sqliteGateway = new TableGateway<SchemaEntity, long>(sqliteContext);

        var pgTableName = pgGateway.WrappedTableName;
        var sqliteTableName = sqliteGateway.WrappedTableName;

        Assert.Contains("HangFire", pgTableName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Job", pgTableName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("HangFire", sqliteTableName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Job", sqliteTableName, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(pgTableName, sqliteTableName);
    }

    // -------------------------------------------------------------------------
    // Regression: CountAllAsync/CountWhereAsync/CountWhereNullAsync/CountWhereEqualsAsync
    // rendered the table name via the constructor-time WrappedTableName property (bound to
    // the gateway's own _dialect) instead of BuildWrappedTableName(ctx.Dialect) — the pattern
    // every other SQL-building method in BaseTableGateway/TableGateway already uses. A gateway
    // constructed against one dialect but called with a context for a different dialect (the
    // documented purpose of the trailing IDatabaseContext? parameter — see docs/gateway-counts.md)
    // silently emitted a COUNT statement whose table name matched the *constructor's* dialect
    // instead of the passed one. Column identifiers were already correctly wrapped via
    // sc.WrapObjectName(column), which resolves against the passed context — only the table
    // name itself was stale. Verified here the same way MultiTenantDialectTests verifies other
    // SQL-building methods: construct against SQLite (drops the schema prefix), pass a
    // PostgreSQL context (includes it), and confirm the schema prefix that reaches the fake
    // connection is PostgreSQL's, not SQLite's.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CountAllAsync_PassedPostgresContext_UsesPostgresWrappedTableName()
    {
        var sqliteFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var sqliteContext = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", sqliteFactory);
        var gateway = new TableGateway<SchemaEntity, long>(sqliteContext);

        var pgFactory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var pgContext = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", pgFactory);

        await gateway.CountAllAsync(pgContext);

        var countSql = pgFactory.CreatedConnections
            .SelectMany(c => c.ExecutedReaderCommands)
            .Select(cmd => cmd.CommandText)
            .Single(t => t.Contains("COUNT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("HangFire", countSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CountWhereAsync_PassedPostgresContext_UsesPostgresWrappedTableName()
    {
        var sqliteFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var sqliteContext = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", sqliteFactory);
        var gateway = new TableGateway<SchemaEntity, long>(sqliteContext);

        var pgFactory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var pgContext = new DatabaseContext("Data Source=test;EmulatedProduct=PostgreSql", pgFactory);

        await gateway.CountWhereAsync("name", "test", context: pgContext);

        var countSql = pgFactory.CreatedConnections
            .SelectMany(c => c.ExecutedReaderCommands)
            .Select(cmd => cmd.CommandText)
            .Single(t => t.Contains("COUNT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("HangFire", countSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CountAllAsync_DefaultContext_StillUsesConstructorDialect()
    {
        // Sanity check: the fix must not regress the no-override case — default context still
        // renders through the gateway's own constructor dialect (SQLite, no schema prefix).
        var sqliteFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var sqliteContext = new DatabaseContext("Data Source=:memory:;EmulatedProduct=Sqlite", sqliteFactory);
        var gateway = new TableGateway<SchemaEntity, long>(sqliteContext);

        await gateway.CountAllAsync();

        var countSql = sqliteFactory.CreatedConnections
            .SelectMany(c => c.ExecutedReaderCommands)
            .Select(cmd => cmd.CommandText)
            .Single(t => t.Contains("COUNT", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("HangFire", countSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Job", countSql, StringComparison.OrdinalIgnoreCase);
    }
}
