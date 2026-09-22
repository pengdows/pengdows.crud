using EnergyExemplar.EntityFrameworkCore.DuckDb;

namespace pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests;

// Item 4 from the independent architecture review: "'Cannot inject fakeDb because the provider
// exposes no arbitrary DbConnection seam' does not prove that an EF DbConnectionInterceptor
// cannot govern real DuckDB connections. That needs a real-provider interceptor test. So DuckDB
// was really 'deep fakeDb substitutability unproven/unavailable,' not 'StormGate incompatible.'"
//
// EfProviders.cs's Tier 1 ("ConnectionControlCapable") is tested exclusively through fakeDb
// injection — every provider there is proven by handing UseXxx(...) a fakeDbConnection and
// checking the interceptor governs it. DuckDB fails that specific test because
// EnergyExemplar.EntityFrameworkCore.DuckDb's UseDuckDb has no DbConnection-accepting overload at
// all — only a DuckDbConnectionOptions/connection-string object. That is a fact about this
// package's configuration API surface, not about whether DbConnectionInterceptor — which fires on
// EF Core's own internal connection lifecycle events, regardless of who constructed the
// connection or how — can gate it.
//
// Reflecting over the package (see the csproj comment next to its PackageReference) shows
// UseDuckDb is a thin layer over Microsoft.EntityFrameworkCore.Sqlite: the object
// DbContext.Database.GetDbConnection() returns for a UseDuckDb-configured context is a genuine
// Microsoft.Data.Sqlite.SqliteConnection, with DuckDB's own engine substituted in only via its
// native SQLite-ABI-compatible library. Since SQLite is already fully proven Tier 1 AND Tier 2
// compatible with StormGateConnectionInterceptor elsewhere in this suite, and DuckDB opens the
// exact same connection type, this test proves directly — against a real, embedded DuckDB engine,
// no Docker, no fakeDb — that admission control governs it too.
public sealed class DuckDbInterceptorRealProviderTests
{
    [Fact]
    public async Task StormGateConnectionInterceptor_GatesRealDuckDbConnectionOpens_AndSaturationBlocksASecondConcurrentOpen()
    {
        using var stormGate = StormGate.Create(
            Microsoft.Data.Sqlite.SqliteFactory.Instance,
            "Data Source=:memory:",
            maxConcurrentOpens: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(150));
        var interceptor = new StormGateConnectionInterceptor(stormGate);

        await using var context1 = CreateDuckDbContext(interceptor);
        await using var context2 = CreateDuckDbContext(interceptor);

        await context1.Database.OpenConnectionAsync();
        Assert.Equal(
            "Microsoft.Data.Sqlite.SqliteConnection",
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

    private static DuckDbProbeContext CreateDuckDbContext(StormGateConnectionInterceptor interceptor)
    {
        var builder = new DbContextOptionsBuilder<DuckDbProbeContext>();
        DuckDbOptionsExtensions.UseDuckDb(
            builder,
            (Action<DuckDbConnectionOptions>)(o => o.ConnectionString = "Data Source=:memory:"),
            null!);
        builder.UseStormGate(interceptor);
        return new DuckDbProbeContext(builder.Options);
    }

    private sealed class DuckDbProbeContext(DbContextOptions<DuckDbProbeContext> options) : DbContext(options);
}
