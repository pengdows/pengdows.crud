#if NET10_0_OR_GREATER
using DuckDB.EFCore.Extensions;
using DuckDB.NET.Data;

namespace pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests;

// DuckDB.EFCore (github.com/denis-ivanov/DuckDB.EFCore, net10.0-only) is a SEPARATE, independent
// EF Core provider from EnergyExemplar.EntityFrameworkCore.DuckDb tested elsewhere in this suite —
// see the .csproj comment next to its PackageReference. Unlike EnergyExemplar's package, its
// UseDuckDB(DbContextOptionsBuilder, DbConnection, bool, ...) DOES accept an arbitrary
// DbConnection (confirmed via ilspycmd decompilation), so it was added to EfProviders'
// ConnectionControlCapable (Tier 1) fakeDb-driven MemberData — and failed immediately:
//
//   System.InvalidCastException: Unable to cast object of type 'pengdows.crud.fakeDb.fakeDbConnection'
//   to type 'DuckDB.NET.Data.DuckDBConnection'.
//   at DuckDB.EFCore.Storage.Internal.DuckDBRelationalConnection.OpenDbConnectionAsync(...)
//
// So DuckDB.EFCore's own OpenDbConnectionAsync unconditionally casts to its own concrete
// DuckDBConnection type, the exact same class of hardcoded-concrete-type failure documented for
// Npgsql/Firebird/Oracle/Db2 above — just one layer earlier (at connection-open time, not
// command-creation time), so it doesn't even survive Tier 1. That was removed from
// ConnectionControlCapable accordingly (see EfProviders.cs).
//
// Per the same "fakeDb-injectable vs. StormGate-compatible are two different questions" reasoning
// already established for EnergyExemplar's package (see DuckDbInterceptorRealProviderTests), a
// provider casting to its own concrete connection type says nothing about whether
// DbConnectionInterceptor — which fires on EF Core's own connection lifecycle events regardless of
// who constructed the connection — can actually govern it against a REAL DuckDB connection. This
// test proves directly, with a genuine DuckDB.NET.Data.DuckDBConnection (no fakeDb, no Docker,
// real embedded DuckDB engine) passed straight into UseDuckDB(connection, ...), that it can.
public sealed class DuckDBEFCoreInterceptorRealProviderTests
{
    [Fact]
    public async Task StormGateConnectionInterceptor_GatesRealDuckDBEFCoreConnectionOpens_AndSaturationBlocksASecondConcurrentOpen()
    {
        using var stormGate = StormGate.Create(
            DuckDBClientFactory.Instance,
            "Data Source=:memory:",
            maxConcurrentOpens: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);

        var connection1 = new DuckDBConnection("Data Source=:memory:");
        var connection2 = new DuckDBConnection("Data Source=:memory:");

        await using var context1 = CreateDuckDbEfCoreContext(connection1, interceptor);
        await using var context2 = CreateDuckDbEfCoreContext(connection2, interceptor);

        await context1.Database.OpenConnectionAsync();
        Assert.Equal(
            "DuckDB.NET.Data.DuckDBConnection",
            context1.Database.GetDbConnection().GetType().FullName);

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => context2.Database.OpenConnectionAsync());
        var saturation = StormGateSaturationTestHelpers.FindStormGateSaturationTimeout(thrown);
        Assert.NotNull(saturation);

        await context1.Database.CloseConnectionAsync();

        // The permit is released; a fresh open now succeeds — proves the interceptor is actually
        // governing this connection's lifecycle, not merely failing to interfere with it.
        await context2.Database.OpenConnectionAsync();
        await context2.Database.CloseConnectionAsync();
    }

    private static DuckDBEfCoreProbeContext CreateDuckDbEfCoreContext(
        DuckDBConnection connection,
        StormGateConnectionInterceptor interceptor)
    {
        var builder = new DbContextOptionsBuilder<DuckDBEfCoreProbeContext>();
        builder.UseDuckDB(connection, contextOwnsConnection: false);
        builder.UseStormGate(interceptor);
        return new DuckDBEfCoreProbeContext(builder.Options);
    }

    private sealed class DuckDBEfCoreProbeContext(DbContextOptions<DuckDBEfCoreProbeContext> options) : DbContext(options);
}
#endif
