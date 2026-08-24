using IBM.EntityFrameworkCore;

namespace pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests;

// Item 3 from the independent architecture review: "the raw StormGate wrapper returns a generic
// PermitCommand/PermitConnection. Some EF providers cast DbCommand to their concrete command
// types. Oracle and Db2 in particular looked likely to fail through the raw wrapper even though
// the provider itself works normally."
//
// EfProviderDeepTests already proves — against a bare fakeDbConnection, no StormGate involved —
// that Postgres/Firebird/Oracle/Db2's own EF Core implementations cast the generic ADO.NET object
// handed to them to their own concrete provider type (NpgsqlDataReader/FbParameter/OracleCommand/
// DB2Command) somewhere in their real pipeline. That cast target is the *runtime type of the
// object EF Core is holding*, not anything about where that object came from. A fakeDbCommand
// fails the cast for exactly the same reason a StormGate PermitCommand would: neither is the
// provider's own concrete type, only the provider's own driver ever constructs an instance that
// survives the cast.
//
// That makes this a genuine, production-relevant gap — NOT a fakeDb testing artifact like the
// Tier 2 table in EfProviders.cs. Wrapping a REAL Oracle/Db2/Postgres/Firebird connection in raw
// StormGate before handing it to EF Core would crash in production against a real server the
// same way, because the outer object EF's own cast sees is StormGate's PermitCommand either way.
// This is exactly why pengdows.stormgate.EntityFrameworkCore's StormGateConnectionInterceptor
// exists: it never wraps the command/connection/reader pipeline at all, so it has no exposure to
// this failure mode for any provider.
//
// Reproduced here through fakeDb (consistent with this project's existing verification style for
// this exact class of finding) specifically to isolate the variable under test: is the crash
// caused by wrapping in PermitCommand, or is it a fakeDb-specific quirk? The SQLite positive
// control below proves it is specifically about wrapping providers that cast — a provider that
// accepts a generic DbCommand (SQLite, confirmed Tier 2-safe) keeps working fine even through the
// StormGate wrapper.
public sealed class EfProviderRawStormGateTests
{
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task Oracle_RawStormGateWrappedConnection_CannotExecuteAnyCommand_SameCastFailureAsUnwrappedFakeDb()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: AcquireTimeout);
        await using var connection = await stormGate.OpenAsync();

        var builder = new DbContextOptionsBuilder<RawStormGateCustomerContext>();
        builder.UseOracle(connection, contextOwnsConnection: false);
        await using var db = new RawStormGateCustomerContext(builder.Options);

        var thrown = await Assert.ThrowsAsync<InvalidCastException>(() => db.Customers.ToListAsync());
        Assert.Contains("OracleCommand", thrown.Message);
    }

    [Fact]
    public async Task Db2_RawStormGateWrappedConnection_CannotExecuteAnyCommand_SameCastFailureAsUnwrappedFakeDb()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Db2);
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: AcquireTimeout);
        await using var connection = await stormGate.OpenAsync();

        var builder = new DbContextOptionsBuilder<RawStormGateCustomerContext>();
        builder.UseDb2(connection, _ => { });
        await using var db = new RawStormGateCustomerContext(builder.Options);

        var thrown = await Assert.ThrowsAsync<InvalidCastException>(() => db.Customers.ToListAsync());
        Assert.Contains("DB2Command", thrown.Message);
    }

    [Fact]
    public async Task PostgreSql_RawStormGateWrappedConnection_CannotCompleteSaveChanges_SameCastFailureAsUnwrappedFakeDb()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: AcquireTimeout);
        await using var connection = await stormGate.OpenAsync();

        var builder = new DbContextOptionsBuilder<RawStormGateCustomerContext>();
        builder.UseNpgsql(connection, contextOwnsConnection: false);
        await using var db = new RawStormGateCustomerContext(builder.Options);

        db.Add(new RawStormGateCustomer { Id = 1, Name = "Ada", IsActive = true });

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var cast = Assert.IsType<InvalidCastException>(thrown.InnerException);
        Assert.Contains("NpgsqlDataReader", cast.Message);
    }

    [Fact]
    public async Task Firebird_RawStormGateWrappedConnection_CannotBindAnyStringParameter_SameCastFailureAsUnwrappedFakeDb()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: AcquireTimeout);
        await using var connection = await stormGate.OpenAsync();

        var builder = new DbContextOptionsBuilder<RawStormGateCustomerContext>();
        builder.UseFirebird(connection);
        await using var db = new RawStormGateCustomerContext(builder.Options);

        var name = "Ada";
        var thrown = await Assert.ThrowsAsync<InvalidCastException>(
            () => db.Customers.Where(c => c.Name == name).ToListAsync());
        Assert.Contains("FbParameter", thrown.Message);
    }

    [Fact]
    public async Task Sqlite_RawStormGateWrappedConnection_WorksNormally_ProvingTheFailureIsAboutProviderCastingNotWrapping()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        using var stormGate = StormGate.Create(factory, "Data Source=fake", maxConcurrentOpens: 1, acquireTimeout: AcquireTimeout);
        await using var connection = await stormGate.OpenAsync();

        var fake = factory.CreatedConnections.Single();
        fake.EnqueueNonQueryResult(1);
        fake.EnqueueReaderResult([new Dictionary<string, object?> { ["Value"] = 1 }], recordsAffected: 1);

        var builder = new DbContextOptionsBuilder<RawStormGateCustomerContext>();
        builder.UseSqlite(connection, contextOwnsConnection: false);
        await using var db = new RawStormGateCustomerContext(builder.Options);

        db.Add(new RawStormGateCustomer { Id = 1, Name = "Ada", IsActive = true });

        // No InvalidCastException — SQLite's EF provider never casts the generic DbCommand/reader
        // it's handed to a concrete type, so it works identically whether the connection came
        // straight from fakeDb or through StormGate's PermitConnection/PermitCommand wrapper.
        await db.SaveChangesAsync();

        Assert.Contains(
            fake.ExecutedReaderCommands.Concat(fake.ExecutedNonQueryCommands),
            c => c.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class RawStormGateCustomerContext(DbContextOptions<RawStormGateCustomerContext> options)
        : DbContext(options)
    {
        public DbSet<RawStormGateCustomer> Customers => Set<RawStormGateCustomer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<RawStormGateCustomer>().Property(c => c.Id).ValueGeneratedNever();
        }
    }

    private sealed class RawStormGateCustomer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }
}
