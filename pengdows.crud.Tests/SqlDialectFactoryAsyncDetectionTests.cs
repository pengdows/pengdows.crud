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
/// Proves <see cref="SqlDialectFactory.CreateDialectAsync"/> genuinely resolves the database
/// product through <see cref="pengdows.crud.@internal.DatabaseDetectionService.DetectProductAsync"/>
/// instead of silently falling back to the synchronous <c>DetectProduct</c>/<c>ExecuteScalar()</c>
/// path. The decorator connection below throws on sync <c>ExecuteScalar()</c> so only the async
/// flavor probe (used to distinguish Aurora MySQL from plain MySQL) can succeed.
/// </summary>
public class SqlDialectFactoryAsyncDetectionTests
{
    [Fact]
    public async Task CreateDialectAsync_AuroraMySql_ResolvesViaAsyncFlavorProbe()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var inner = (fakeDbConnection)factory.CreateConnection();
        inner.ConnectionString = "EmulatedProduct=MySql";
        inner.SetScalarResultForCommand("SELECT @@aurora_version", "3.04.0.1");

        await using var conn = new SyncScalarBlockedConnection(inner);
        await conn.OpenAsync();
        var tracked = new TrackedConnection(conn);

        var dialect = await SqlDialectFactory.CreateDialectAsync(tracked, factory, NullLoggerFactory.Instance);

        // If CreateDialectAsync fell back to the sync detection path, the aurora probe's
        // ExecuteScalar() call would throw internally, be swallowed by the probe's own
        // catch block, and detection would resolve to plain MySql instead.
        Assert.Equal(SupportedDatabase.AuroraMySql, dialect.DatabaseType);
    }

    /// <summary>
    /// <see cref="SqlDialect.DetectDatabaseInfoAsync"/> has its own, separate fallback to
    /// <c>DatabaseDetectionService.DetectProduct</c> (sync) when version/name-based inference
    /// can't tell a flavor apart from the dialect's already-assumed base type — the same class
    /// of bug as <see cref="SqlDialectFactory.CreateDialectAsync"/>'s outer detection call, just
    /// one layer deeper. A plain MySqlDialect's inference can't distinguish Aurora from version
    /// text alone, so it always hits this fallback.
    /// </summary>
    [Fact]
    public async Task DetectDatabaseInfoAsync_AuroraMySqlFallback_UsesAsyncFlavorProbe()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        var inner = (fakeDbConnection)factory.CreateConnection();
        inner.ConnectionString = "EmulatedProduct=MySql";
        inner.SetScalarResultForCommand("SELECT @@aurora_version", "3.04.0.1");

        await using var conn = new SyncScalarBlockedConnection(inner);
        await conn.OpenAsync();
        var tracked = new TrackedConnection(conn);

        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.MySql, factory, NullLogger<SqlDialect>.Instance);
        var info = await dialect.DetectDatabaseInfoAsync(tracked);

        Assert.Equal(SupportedDatabase.AuroraMySql, info.DatabaseType);
    }

    /// <summary>
    /// Locks down the other half of the contract: the fully synchronous
    /// <see cref="SqlDialectFactory.CreateDialect(ITrackedConnection, DbProviderFactory, ILoggerFactory)"/>
    /// entry point (used by <c>DatabaseContext</c>'s sync constructor via each
    /// <c>IConnectionStrategy.HandleDialectDetection</c>) must keep resolving product identification
    /// through the genuinely synchronous <c>ExecuteScalar()</c> probe, not silently start routing
    /// through <c>ExecuteScalarAsync</c> (which would mean the sync construction path now blocks on
    /// async I/O it never needed to touch). The probe-blocking decorator here throws only from
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
    /// Minimal DbConnection decorator wrapping a real <see cref="fakeDbConnection"/> so schema
    /// lookups and version detection behave normally, except that any command's synchronous
    /// <see cref="DbCommand.ExecuteScalar"/> throws — forcing detection code to route through
    /// <see cref="DbCommand.ExecuteScalarAsync(CancellationToken)"/> to succeed.
    /// </summary>
    private sealed class SyncScalarBlockedConnection : DbConnection
    {
        private readonly fakeDbConnection _inner;

        public SyncScalarBlockedConnection(fakeDbConnection inner)
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

        protected override DbCommand CreateDbCommand() => new SyncScalarBlockedCommand((DbCommand)_inner.CreateCommand());

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class SyncScalarBlockedCommand : DbCommand
    {
        private readonly DbCommand _inner;

        public SyncScalarBlockedCommand(DbCommand inner)
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

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => _inner.ExecuteReader(behavior);

        public override object? ExecuteScalar()
            => throw new InvalidOperationException(
                "Sync ExecuteScalar() was called — async detection must use ExecuteScalarAsync().");

        public override Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken)
            => _inner.ExecuteScalarAsync(cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Inverse of <see cref="SyncScalarBlockedConnection"/>: every call behaves normally except
    /// that <c>ExecuteScalarAsync</c> throws specifically for the Aurora-MySQL identification
    /// probe's command text. Used to prove a sync caller never needs (and doesn't accidentally
    /// start using) the async overload for that probe.
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
