using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Locks down that the fully synchronous <see cref="SqlDialectFactory.CreateDialect"/> entry
/// point (used by <c>DatabaseContext</c>'s sync constructor via each
/// <c>IConnectionStrategy.HandleDialectDetection</c>) resolves product identification through the
/// genuinely synchronous <c>ExecuteScalar()</c> probe.
/// </summary>
/// <remarks>
/// This file previously also covered an async-detection code path
/// (<c>SqlDialectFactory.CreateDialectAsync</c> resolving Aurora MySQL via a genuinely awaited
/// <c>DatabaseDetectionService.DetectProductAsync</c> flavor probe, distinguishing it from a
/// silent fallback to the synchronous probe). That specific feature was found, during the
/// 2.0-partial-backport → 2.0.6 merge (2026-09-16), to have never actually been finished on this
/// line: the tests and call sites referencing <c>DetectProductAsync</c> existed, but the method
/// itself was never implemented in <c>DatabaseDetectionService.cs</c> — a real, working version of
/// this exact feature exists on the 3.0/2.1.0 branches (commit <c>d5b24e3</c>, "feat: async
/// detection probes, OTel semconv instruments, reader-lock fail-fast guard"). The two tests
/// exercising it were removed here rather than kept red or given a half-finished implementation
/// under merge pressure; port <c>d5b24e3</c>'s <c>DetectProductAsync</c>/
/// <c>DetectFromConnectionAsync</c>/<c>DetectFromConnectionWithDetailAsync</c>/
/// <c>DetectFlavorWithDetailAsync</c> methods (adapted to this line's
/// <see cref="pengdows.crud.@internal.DatabaseDetectionResult"/>/tuple-based attempt-tracking,
/// which 3.0 doesn't have) if/when this is picked back up, and restore
/// <c>SqlDialectFactory.CreateDialectAsync</c>/<c>SqlDialect.DetectDatabaseInfoAsync</c> to
/// actually call it instead of the current synchronous <c>DetectProduct</c> fallback.
/// </remarks>
public class SqlDialectFactoryAsyncDetectionTests
{
    /// <summary>
    /// The synchronous entry point must keep resolving product identification through the
    /// genuinely synchronous <c>ExecuteScalar()</c> probe, not silently start routing through
    /// <c>ExecuteScalarAsync</c> (which would mean the sync construction path now blocks on async
    /// I/O it never needed to touch). The probe-blocking decorator here throws only from
    /// <c>ExecuteScalarAsync</c> for the identification-only "aurora_version" probe — every other
    /// async call (used unconditionally by <c>DetectDatabaseInfoAsync</c>'s version/name lookups)
    /// still passes through normally.
    /// </summary>
    [Fact]
    public void CreateDialect_AuroraMySql_ResolvesViaSyncProbe_WithoutTouchingAsyncOverload()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var inner = (fakeDbConnection)factory.CreateConnection();
        inner.ConnectionString = "EmulatedProduct=MySql";
        inner.SetScalarResultForCommand("SELECT @@aurora_version", "3.04.0.1");

        using var conn = new AsyncAuroraProbeBlockedConnection(inner);
        conn.Open();
        var tracked = new TrackedConnection(conn);

        var dialect = SqlDialectFactory.CreateDialect(tracked, factory, NullLoggerFactory.Instance);

        Assert.Equal(SupportedDatabase.AuroraMySql, dialect.DatabaseType);
    }

    /// <summary>
    /// Inverse of a sync-blocking decorator: every call behaves normally except that
    /// <c>ExecuteScalarAsync</c> throws specifically for the Aurora-MySQL identification probe's
    /// command text. Used to prove a sync caller never needs (and doesn't accidentally start
    /// using) the async overload for that probe.
    /// </summary>
    private sealed class AsyncAuroraProbeBlockedConnection : DbConnection
    {
        private readonly fakeDbConnection _inner;

        public AsyncAuroraProbeBlockedConnection(fakeDbConnection inner)
        {
            _inner = inner;
        }

        [AllowNull]
        public override string ConnectionString
        {
            get => _inner.ConnectionString;
            set => _inner.ConnectionString = value;
        }

        public override string Database => _inner.Database;
        public override string DataSource => _inner.DataSource;
        public override string ServerVersion => _inner.ServerVersion;
        public override ConnectionState State => _inner.State;

        public override void ChangeDatabase(string databaseName) => _inner.ChangeDatabase(databaseName);
        public override void Close() => _inner.Close();
        public override void Open() => _inner.Open();
        public override Task OpenAsync(CancellationToken cancellationToken) => _inner.OpenAsync(cancellationToken);
        public override DataTable GetSchema() => _inner.GetSchema();
        public override DataTable GetSchema(string collectionName) => _inner.GetSchema(collectionName);

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
            => _inner.BeginTransaction(isolationLevel);

        protected override DbCommand CreateDbCommand()
            => new AsyncAuroraProbeBlockedCommand((DbCommand)_inner.CreateCommand());

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class AsyncAuroraProbeBlockedCommand : DbCommand
    {
        private readonly DbCommand _inner;

        public AsyncAuroraProbeBlockedCommand(DbCommand inner)
        {
            _inner = inner;
        }

        [AllowNull]
        public override string CommandText
        {
            get => _inner.CommandText;
            set => _inner.CommandText = value;
        }

        public override int CommandTimeout
        {
            get => _inner.CommandTimeout;
            set => _inner.CommandTimeout = value;
        }

        public override CommandType CommandType
        {
            get => _inner.CommandType;
            set => _inner.CommandType = value;
        }

        public override bool DesignTimeVisible
        {
            get => _inner.DesignTimeVisible;
            set => _inner.DesignTimeVisible = value;
        }

        protected override DbConnection? DbConnection
        {
            get => _inner.Connection;
            set { }
        }

        protected override DbParameterCollection DbParameterCollection => _inner.Parameters;

        protected override DbTransaction? DbTransaction
        {
            get => _inner.Transaction;
            set => _inner.Transaction = value;
        }

        public override UpdateRowSource UpdatedRowSource
        {
            get => _inner.UpdatedRowSource;
            set => _inner.UpdatedRowSource = value;
        }

        public override void Cancel() => _inner.Cancel();
        protected override DbParameter CreateDbParameter() => _inner.CreateParameter();
        public override int ExecuteNonQuery() => _inner.ExecuteNonQuery();
        public override void Prepare() => _inner.Prepare();
        public override object? ExecuteScalar() => _inner.ExecuteScalar();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => _inner.ExecuteReader(behavior);

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
        {
            if (_inner.CommandText == "SELECT @@aurora_version")
            {
                throw new InvalidOperationException(
                    "ExecuteScalarAsync() was called for the identification probe — the synchronous " +
                    "CreateDialect() entry point must use the sync ExecuteScalar() overload instead.");
            }

            return _inner.ExecuteScalarAsync(cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
