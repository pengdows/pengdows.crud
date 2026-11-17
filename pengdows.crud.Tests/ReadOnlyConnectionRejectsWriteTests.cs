using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// G7b (CONCURRENCY.md): "the database rejects writes on read-only connections." This was marked
/// provider-dependent and untestable with fakeDb -- fakeDb modeled no read-only concept at all.
/// It now does (fakeDbConnection.IsReadOnly, computed from the real per-dialect read-only
/// connection-string marker when a test opts in via EnforceReadOnlyFromConnectionString -- off by
/// default, like every other fakeDb failure injection, so it cannot change behavior for the many
/// existing tests that send write-shaped SQL through a default/read-intent overload purely to get
/// a RETURNING value back, with no interest in read/write routing), so this is testable
/// deterministically, and it completes the story SingleWriterReadIntentBypassesWriteSerializationTests
/// started:
///
/// - File SQLite gets Mode=ReadOnly on its read-intent connection string (SqliteDialect), and a
///   write-shaped statement sent through the default (read-intent) overload is now rejected --
///   not by the governor (it still isn't; this is G6's point), but by the connection itself,
///   exactly as docs/read-only-enforcement.md's live-checked matrix says real SQLite does.
/// - Shared in-memory SQLite does NOT get Mode=ReadOnly (SqliteDialect skips memory databases),
///   so the same statement goes through uncaught -- the actual gap
///   SingleWriterReadIntentBypassesWriteSerializationTests demonstrated at the governor level.
/// </summary>
public sealed class ReadOnlyConnectionRejectsWriteTests
{
    [Fact(Timeout = 10000)]
    public async Task FileSqlite_WriteShapedStatement_SentWithDefaultReadIntent_IsRejectedByTheConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            // A real file path (not :memory:/mode=memory), so SqliteDialect actually applies
            // Mode=ReadOnly to the read-intent connection string.
            ConnectionString = "Data Source=/tmp/pengdows.readonly-reject.probe.sqlite;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter,
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.Sqlite,
            EnforceReadOnlyFromConnectionString = true
        };
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(conn);

        using var sc = ctx.CreateSqlContainer("UPDATE test SET id = 1");

        // ExecuteReaderAsync(CommandType.Text) with no ExecutionType defaults to Read, exactly
        // the "forgot to say Write" scenario G6 describes -- but unlike the shared-memory case,
        // the connection fakeDb hands back here is read-only, so the write is rejected.
        var ex = await Assert.ThrowsAsync<ReadOnlyViolationException>(
            () => sc.ExecuteReaderAsync(CommandType.Text).AsTask());

        Assert.True(ex is IReadOnlyViolation);
    }

    [Fact(Timeout = 10000)]
    public async Task SharedInMemorySqlite_WriteShapedStatement_SentWithDefaultReadIntent_IsNotRejected()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=file:shared-memory?mode=memory&cache=shared;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter,
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.Sqlite,
            EnforceReadOnlyFromConnectionString = true
        };
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(conn);

        using var sc = ctx.CreateSqlContainer("UPDATE test SET id = 1");

        // Same mistake, same mechanism -- but SqliteDialect never applies Mode=ReadOnly to a
        // memory database, so fakeDb's connection is NOT read-only and nothing here catches it.
        // This is the gap; it must succeed, not throw.
        using var reader = await sc.ExecuteReaderAsync(CommandType.Text);
        Assert.NotNull(reader);
    }

    [Fact]
    public void FakeDbConnection_IsReadOnly_DetectsSqliteModeReadOnly_ButNotSqliteModeReadWrite()
    {
        var readOnly = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.Sqlite,
            EnforceReadOnlyFromConnectionString = true,
            ConnectionString = "Data Source=/tmp/probe.sqlite;Mode=ReadOnly"
        };
        Assert.True(readOnly.IsReadOnly);

        var readWrite = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.Sqlite,
            EnforceReadOnlyFromConnectionString = true,
            ConnectionString = "Data Source=/tmp/probe.sqlite;Mode=ReadWrite"
        };
        Assert.False(readWrite.IsReadOnly);

        var noModeAtAll = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.Sqlite,
            EnforceReadOnlyFromConnectionString = true,
            ConnectionString = "Data Source=/tmp/probe.sqlite"
        };
        Assert.False(noModeAtAll.IsReadOnly);

        var optedOut = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.Sqlite,
            ConnectionString = "Data Source=/tmp/probe.sqlite;Mode=ReadOnly"
        };
        Assert.False(optedOut.IsReadOnly);
    }

    [Fact(Timeout = 10000)]
    public async Task Access_WriteShapedStatement_SentWithDefaultReadIntent_IsRejectedByTheConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Access);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=C:\\pengdows.readonly-reject.probe.accdb",
            DbMode = DbMode.SingleWriter,
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.Access,
            EnforceReadOnlyFromConnectionString = true
        };
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(conn);

        using var sc = ctx.CreateSqlContainer("UPDATE test SET id = 1");

        var ex = await Assert.ThrowsAsync<ReadOnlyViolationException>(
            () => sc.ExecuteReaderAsync(CommandType.Text).AsTask());
        Assert.True(ex is IReadOnlyViolation);
    }

    [Fact(Timeout = 10000)]
    public async Task FlatFile_WriteShapedStatement_SentWithDefaultReadIntent_IsRejectedByTheConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.FlatFile);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=/tmp/pengdows.readonly-reject.probe.flatfile",
            DbMode = DbMode.SingleWriter,
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.FlatFile,
            EnforceReadOnlyFromConnectionString = true
        };
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(conn);

        using var sc = ctx.CreateSqlContainer("UPDATE test SET id = 1");

        var ex = await Assert.ThrowsAsync<ReadOnlyViolationException>(
            () => sc.ExecuteReaderAsync(CommandType.Text).AsTask());
        Assert.True(ex is IReadOnlyViolation);
    }

    [Fact(Timeout = 10000)]
    public async Task DuckDb_ExplicitReadOnlyContext_WriteShapedStatement_SentWithDefaultReadIntent_IsRejectedByTheConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=/tmp/pengdows.readonly-reject.probe.duckdb",
            DbMode = DbMode.SingleWriter,
            // G7's DuckDB exception: a *writable* context never gets a read-only DuckDB
            // connection for read intent (ShouldUseReadOnlyForReadIntent returns false). Only an
            // explicitly ReadWriteMode.ReadOnly context does -- this is that one case.
            ReadWriteMode = ReadWriteMode.ReadOnly,
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.DuckDB,
            EnforceReadOnlyFromConnectionString = true
        };
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(conn);

        using var sc = ctx.CreateSqlContainer("UPDATE test SET id = 1");

        var ex = await Assert.ThrowsAsync<ReadOnlyViolationException>(
            () => sc.ExecuteReaderAsync(CommandType.Text).AsTask());
        Assert.True(ex is IReadOnlyViolation);
    }

    [Fact]
    public void FakeDbConnection_IsReadOnly_DetectsEachProductsRealMarker()
    {
        Assert.True(new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.Access,
            EnforceReadOnlyFromConnectionString = true,
            ConnectionString = "Data Source=C:\\probe.accdb;Mode=Read"
        }.IsReadOnly);
        Assert.False(new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.Access,
            EnforceReadOnlyFromConnectionString = true,
            ConnectionString = "Data Source=C:\\probe.accdb;Mode=ReadWrite"
        }.IsReadOnly);

        Assert.True(new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.FlatFile,
            EnforceReadOnlyFromConnectionString = true,
            ConnectionString = "Data Source=/tmp/probe.flatfile;readonly=true"
        }.IsReadOnly);
        Assert.False(new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.FlatFile,
            EnforceReadOnlyFromConnectionString = true,
            ConnectionString = "Data Source=/tmp/probe.flatfile"
        }.IsReadOnly);

        Assert.True(new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.DuckDB,
            EnforceReadOnlyFromConnectionString = true,
            ConnectionString = "Data Source=/tmp/probe.duckdb;access_mode=READ_ONLY"
        }.IsReadOnly);
        Assert.False(new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.DuckDB,
            EnforceReadOnlyFromConnectionString = true,
            ConnectionString = "Data Source=/tmp/probe.duckdb"
        }.IsReadOnly);

        Assert.True(new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.PostgreSql,
            EnforceReadOnlyFromConnectionString = true,
            ConnectionString = "Host=localhost;Database=test;Options='-c default_transaction_read_only=on'"
        }.IsReadOnly);
        Assert.False(new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.PostgreSql,
            EnforceReadOnlyFromConnectionString = true,
            ConnectionString = "Host=localhost;Database=test"
        }.IsReadOnly);
    }

    [Fact(Timeout = 10000)]
    public async Task PostgreSql_WriteShapedStatement_SentWithDefaultReadIntent_IsRejectedByTheConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;Username=user;Password=pass;EmulatedProduct=PostgreSql",
            DbMode = DbMode.Standard,
            EnableMetrics = true
        };

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var conn = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.PostgreSql,
            EnforceReadOnlyFromConnectionString = true
        };
        conn.EnqueueReaderResult(new[] { new Dictionary<string, object?> { ["id"] = 1 } });
        factory.Connections.Add(conn);

        using var sc = ctx.CreateSqlContainer("UPDATE test SET id = 1");

        var ex = await Assert.ThrowsAsync<ReadOnlyViolationException>(
            () => sc.ExecuteReaderAsync(CommandType.Text).AsTask());
        Assert.True(ex is IReadOnlyViolation);
    }
}
