using Microsoft.EntityFrameworkCore;
using pengdows.crud.enums;

namespace pengdows.stormgate.Tests;

public sealed class EntityFrameworkCoreFakeDbSqlCaptureTests
{
    // What makes this "unheard of": EF Core's own InMemory provider never generates SQL at
    // all — LINQ is evaluated directly against in-memory collections, so it can't catch a
    // bad translation, a wrong column mapping, or a query that doesn't actually work against
    // the target dialect. Here, EF Core's *real* SQLite provider runs its full pipeline —
    // DDL generation, change tracking, parameter binding, SQL generation with correct ANSI
    // quoting — and fakeDb intercepts only at the ADO.NET boundary below all of that. Every
    // SQL string captured and asserted on below was produced by EF Core itself, not written
    // by this test, and no real database process (not even an in-process SQLite engine) is
    // ever touched.
    [Fact]
    public async Task RealEntityFrameworkCoreSql_IsGeneratedAndCaptured_AgainstFakeDbWithNoRealDatabase()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        await using var gate = StormGate.Create(
            factory,
            "Data Source=not-a-real-database.db",
            maxConcurrentOpens: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(100));
        await using var connection = await gate.OpenAsync();

        // The gated `connection` StormGate hands back is a wrapper (PermitConnection) with no
        // visibility into fakeDb's recording APIs. Reach past it to the actual fakeDbConnection
        // fakeDbFactory just created, so canned results can be queued and captured SQL can be
        // inspected afterward — the wrapper itself is never aware this is happening.
        var fake = factory.CreatedConnections[^1];

        // EnsureCreatedAsync first asks "do any tables already exist?" via a real scalar query
        // against sqlite_master. Left unconfigured, fakeDb's default scalar answer (42) would
        // make EF Core believe the schema already exists and skip DDL entirely — so the fake
        // database must be told, in SQLite's own words, that it is empty.
        fake.SetScalarResultForCommand(
            "SELECT COUNT(*) FROM \"sqlite_master\" WHERE \"type\" = 'table' AND \"rootpage\" IS NOT NULL;",
            0);

        // SQLite's EF Core provider inserts via "INSERT ... RETURNING", executed as a reader
        // (not ExecuteNonQuery) so it can read the generated identity back. Queue that result
        // first — it is consumed before the SELECT below.
        fake.EnqueueReaderResult([new Dictionary<string, object?> { ["Id"] = 1 }]);

        // The row(s) the fake database will answer the LINQ query with — entirely independent
        // of what was "inserted" above, because nothing was actually persisted anywhere.
        fake.EnqueueReaderResult(
        [
            new Dictionary<string, object?> { ["Id"] = 1, ["IsActive"] = true, ["Name"] = "Ada" }
        ]);

        var options = new DbContextOptionsBuilder<CustomerContext>()
            .UseSqlite(connection, contextOwnsConnection: false)
            .Options;

        await using var db = new CustomerContext(options);

        // Real DDL generation — EF Core inspects the model and emits an actual CREATE TABLE.
        await db.Database.EnsureCreatedAsync();

        // Real change tracking + INSERT generation.
        db.Customers.Add(new Customer { Name = "Ada", IsActive = true });
        await db.SaveChangesAsync();

        // Real LINQ-to-SQL translation, including local-variable parameterization.
        var name = "Ada";
        var results = await db.Customers.Where(c => c.IsActive && c.Name == name).ToListAsync();

        Assert.Single(results);
        Assert.Equal("Ada", results[0].Name);

        // Everything below is asserting on SQL text EF Core itself produced — proof the real
        // query/DDL/parameter-binding pipeline ran, not just that a canned value came back.
        Assert.Contains(fake.ExecutedNonQueryTexts, sql =>
            sql.Contains("CREATE TABLE") && sql.Contains("\"Customers\""));

        Assert.Contains(fake.ExecutedReaderTexts, sql =>
            sql.Contains("INSERT INTO \"Customers\"") && sql.Contains("RETURNING \"Id\""));

        var selectSql = Assert.Single(fake.ExecutedReaderTexts, sql =>
            sql.Contains("FROM \"Customers\" AS \"c\""));

        // "@__name_0" appearing as a named token — rather than the literal 'Ada' — is what a
        // real parameter binder produces; a translator doing plain string interpolation could
        // never emit this. (See RealEntityFrameworkCoreSql_BindsActualParameterValue_NotJustTheNameToken
        // below for proof of the actual bound value, captured before EF Core's post-execution
        // command disposal clears it.)
        Assert.Contains("WHERE \"c\".\"IsActive\" AND \"c\".\"Name\" = @__name_0", selectSql);
    }

    // The prior test could only assert on the "@__name_0" name token surviving in the captured
    // SQL text, because EF Core disposes each DbCommand (clearing its Parameters) before
    // ToListAsync returns. fakeDb now snapshots parameter name/value pairs at execution time,
    // before that disposal — so this closes that gap: proof that EF Core's real parameter binder
    // bound the literal runtime value "Ada", not just proof that a named-parameter token exists.
    [Fact]
    public async Task RealEntityFrameworkCoreSql_BindsActualParameterValue_NotJustTheNameToken()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        await using var gate = StormGate.Create(
            factory,
            "Data Source=not-a-real-database.db",
            maxConcurrentOpens: 1,
            acquireTimeout: TimeSpan.FromMilliseconds(100));
        await using var connection = await gate.OpenAsync();

        var fake = factory.CreatedConnections[^1];
        fake.EnqueueReaderResult(
        [
            new Dictionary<string, object?> { ["Id"] = 1, ["IsActive"] = true, ["Name"] = "Ada" }
        ]);

        var options = new DbContextOptionsBuilder<CustomerContext>()
            .UseSqlite(connection, contextOwnsConnection: false)
            .Options;
        await using var db = new CustomerContext(options);

        var name = "Ada";
        var results = await db.Customers.Where(c => c.IsActive && c.Name == name).ToListAsync();

        Assert.Single(results);

        var executed = Assert.Single(
            fake.ExecutedReaderCommands,
            c => c.CommandText.Contains("FROM \"Customers\" AS \"c\""));
        var bound = Assert.Single(executed.Parameters, p => p.Name == "@__name_0");
        Assert.Equal("Ada", bound.Value);
    }

    private sealed class CustomerContext(DbContextOptions<CustomerContext> options) : DbContext(options)
    {
        public DbSet<Customer> Customers => Set<Customer>();
    }

    private sealed class Customer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }
}
