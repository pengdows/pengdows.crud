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
        var conn = (fakeDbConnection)factory.CreateConnection();
        conn.ConnectionString = "EmulatedProduct=MySql";
        conn.SetScalarResultForCommand("SELECT @@aurora_version", "3.04.0.1");
        conn.BlockSynchronousCommandExecution = true;

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
        var conn = (fakeDbConnection)factory.CreateConnection();
        conn.ConnectionString = "EmulatedProduct=MySql";
        conn.SetScalarResultForCommand("SELECT @@aurora_version", "3.04.0.1");
        conn.BlockSynchronousCommandExecution = true;

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
        var conn = (fakeDbConnection)factory.CreateConnection();
        conn.ConnectionString = "EmulatedProduct=MySql";
        conn.SetScalarResultForCommand("SELECT @@aurora_version", "3.04.0.1");
        conn.SetAsyncOnlyScalarFailure("SELECT @@aurora_version", new InvalidOperationException(
            "ExecuteScalarAsync() was called for the identification probe — the synchronous " +
            "CreateDialect() entry point must use the sync ExecuteScalar() overload instead."));

        conn.Open();
        var tracked = new TrackedConnection(conn);

        var dialect = SqlDialectFactory.CreateDialect(tracked, factory, NullLoggerFactory.Instance);

        Assert.Equal(SupportedDatabase.AuroraMySql, dialect.DatabaseType);
    }

}
