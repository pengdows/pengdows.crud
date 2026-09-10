using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests.@internal;

/// <summary>
/// <see cref="DatabaseDetectionService"/> has two independently hand-written probe
/// implementations — <c>DetectFlavorWithDetail</c> (sync, used by <c>DatabaseContext</c>'s normal
/// constructor) and <c>DetectFlavorWithDetailAsync</c> (async, used by
/// <c>DataSourceInformation.CreateAsync</c>) — with zero shared probe code (see CLAUDE.md's
/// "Adding a New Database" checklist item 15). This file asserts <see cref="DatabaseDetectionService.DetectProduct"/>
/// (sync) and <see cref="DatabaseDetectionService.DetectProductAsync"/> (async) agree for the same
/// connection, across a representative matrix of scenarios, so any future edit to one path without
/// the other fails loudly here instead of silently drifting until a real outage (as happened with
/// Spanner: the sync path was missing its discriminator probe entirely, and separately, the async
/// path's surviving probe used a narrower gate than the sync path's — both are covered below).
/// </summary>
public class DatabaseDetectionSyncAsyncParityTests
{
    private static (fakeDbFactory factory, fakeDbConnection conn) CreateConnection(SupportedDatabase emulated)
    {
        var factory = new fakeDbFactory(emulated);
        var conn = (fakeDbConnection)factory.CreateConnection();
        conn.ConnectionString = $"EmulatedProduct={emulated}";
        return (factory, conn);
    }

    public static IEnumerable<object[]> Scenarios()
    {
        yield return new object[] { "PlainMySql", SupportedDatabase.MySql, (string?)null!, (string?)null!, SupportedDatabase.MySql };
        yield return new object[]
            { "AuroraMySql", SupportedDatabase.MySql, "SELECT @@aurora_version", "3.04.0.1", SupportedDatabase.AuroraMySql };
        yield return new object[]
            { "SingleStore", SupportedDatabase.MySql, "SELECT @@memsql_version", "9.1.1", SupportedDatabase.SingleStore };
        yield return new object[] { "PlainPostgreSql", SupportedDatabase.PostgreSql, (string?)null!, (string?)null!, SupportedDatabase.PostgreSql };
        yield return new object[]
        {
            "SpannerViaPostgreSqlBase", SupportedDatabase.PostgreSql, "SHOW SPANNER.OPTIMIZER_VERSION", "",
            SupportedDatabase.Spanner
        };
        yield return new object[]
        {
            // The real-world discrepancy: schema-based classification lands on Unknown (not
            // PostgreSql) before the flavor probes run, yet the connection still answers the
            // Spanner discriminator probe. The sync path's isPgFamily gate (detected == PostgreSql
            // || detected == Unknown) catches this; the async path's narrower
            // `detected == SupportedDatabase.PostgreSql` gate — before this fix — did not.
            "SpannerViaUnknownBase", SupportedDatabase.Unknown, "SHOW SPANNER.OPTIMIZER_VERSION", "",
            SupportedDatabase.Spanner
        };
        yield return new object[]
        {
            "YugabyteViaPgSettings", SupportedDatabase.PostgreSql,
            "SELECT name FROM pg_settings WHERE name = 'yb_enable_optimizer_statistics' LIMIT 1",
            "yb_enable_optimizer_statistics", SupportedDatabase.YugabyteDb
        };
        yield return new object[]
            { "AuroraPostgreSql", SupportedDatabase.PostgreSql, "SELECT aurora_version()", "1.2.3", SupportedDatabase.AuroraPostgreSql };
        // MySql-family "via Unknown base" gate — the same real-world shape as
        // SpannerViaUnknownBase above (schema-based classification lands on Unknown before the
        // flavor probes run), but exercised for the MySql-family gate instead of the PG-family
        // one. Both sync and async use `detected == MySql || detected == Unknown` for this gate
        // today, so this currently passes on both sides — it exists to keep it that way.
        yield return new object[]
            { "AuroraMySqlViaUnknownBase", SupportedDatabase.Unknown, "SELECT @@aurora_version", "3.04.0.1", SupportedDatabase.AuroraMySql };
        yield return new object[]
            { "SingleStoreViaUnknownBase", SupportedDatabase.Unknown, "SELECT @@memsql_version", "9.1.1", SupportedDatabase.SingleStore };
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task SyncAndAsyncDetection_AgreeOnResult(
        string _, SupportedDatabase emulated, string? probeCommand, string? probeResult, SupportedDatabase expected)
    {
        var (syncFactory, syncConn) = CreateConnection(emulated);
        if (probeCommand != null)
        {
            syncConn.SetScalarResultForCommand(probeCommand, probeResult!);
        }

        var syncResult = DatabaseDetectionService.DetectProduct(syncConn, syncFactory);

        var (asyncFactory, asyncConn) = CreateConnection(emulated);
        if (probeCommand != null)
        {
            asyncConn.SetScalarResultForCommand(probeCommand, probeResult!);
        }

        var asyncResult = await DatabaseDetectionService.DetectProductAsync(asyncConn, asyncFactory);

        Assert.Equal(expected, syncResult);
        Assert.Equal(expected, asyncResult);
        Assert.Equal(syncResult, asyncResult);
    }

    public static IEnumerable<object[]> ServerVersionMarkerScenarios()
    {
        // The ServerVersion-based markers run before any family gate and don't consult `detected`
        // at all in either implementation — the base emulated product here is deliberately the
        // "wrong" family to prove the marker wins regardless.
        yield return new object[] { SupportedDatabase.MySql, "5.7.25-TiDB-v6.5.0", SupportedDatabase.TiDb };
        yield return new object[] { SupportedDatabase.PostgreSql, "12.4-YB-2.9.0.0", SupportedDatabase.YugabyteDb };
        yield return new object[] { SupportedDatabase.PostgreSql, "v22.1.0 (Cockroach)", SupportedDatabase.CockroachDb };
    }

    [Theory]
    [MemberData(nameof(ServerVersionMarkerScenarios))]
    public async Task SyncAndAsyncDetection_AgreeOnResult_ForServerVersionMarker(
        SupportedDatabase emulated, string serverVersion, SupportedDatabase expected)
    {
        var (syncFactory, syncConn) = CreateConnection(emulated);
        syncConn.SetServerVersion(serverVersion);
        var syncResult = DatabaseDetectionService.DetectProduct(syncConn, syncFactory);

        var (asyncFactory, asyncConn) = CreateConnection(emulated);
        asyncConn.SetServerVersion(serverVersion);
        var asyncResult = await DatabaseDetectionService.DetectProductAsync(asyncConn, asyncFactory);

        Assert.Equal(expected, syncResult);
        Assert.Equal(expected, asyncResult);
        Assert.Equal(syncResult, asyncResult);
    }

    [Fact]
    public async Task SyncAndAsyncDetection_ProduceIdenticalProbeAttemptTrails()
    {
        // Guards structural parity, not just the final resolved product: two implementations
        // could agree on the answer while taking a different path to get there. Locking the
        // probe-name trail down means any future edit to one path without the other fails here
        // even if it happens not to change the final resolved product for this scenario.
        var (syncFactory, syncConn) = CreateConnection(SupportedDatabase.MySql);
        syncConn.SetScalarResultForCommand("SELECT @@aurora_version", "3.04.0.1");
        var syncResult = DatabaseDetectionService.DetectFromConnectionWithDetail(syncConn);

        var (asyncFactory, asyncConn) = CreateConnection(SupportedDatabase.MySql);
        asyncConn.SetScalarResultForCommand("SELECT @@aurora_version", "3.04.0.1");
        var asyncResult = await DatabaseDetectionService.DetectFromConnectionWithDetailAsync(asyncConn);

        Assert.Equal(SupportedDatabase.AuroraMySql, syncResult.ResolvedProduct);
        Assert.Equal(syncResult.ResolvedProduct, asyncResult.ResolvedProduct);
        Assert.Equal(
            syncResult.Attempts.Select(a => (a.ProbeName, a.Succeeded)),
            asyncResult.Attempts.Select(a => (a.ProbeName, a.Succeeded)));
    }
}
