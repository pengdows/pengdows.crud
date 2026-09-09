using System.Collections.Generic;
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
}
