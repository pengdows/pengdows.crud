namespace pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests;

// ONE shared test body, run through every EF Core provider confirmed to fully support real SQL
// generation, real parameter binding, and SaveChanges round-tripping against fakeDb — no
// per-provider test files, no duplicated logic. Every assertion below is path-agnostic
// (it searches ExecutedReaderCommands and ExecutedNonQueryCommands together, and covers every
// confirmed rows-affected-reporting mechanism at once) specifically so the same test body works
// unmodified for SQLite, SQL Server, MySQL, MariaDB, and Snowflake, despite those providers
// using different internal execution paths — see EfProviders.cs for what was actually confirmed
// per provider and how each was verified.
public sealed class EfProviderDeepTests
{
    [Theory]
    [MemberData(nameof(EfProviders.DeepTestCapable), MemberType = typeof(EfProviders))]
    public async Task RealSql_IsGeneratedAndCaptured_WithActualBoundParameterValue_NoRealDatabaseEngine(
        SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        var connection = (fakeDbConnection)factory.CreateConnection()!;

        var builder = new DbContextOptionsBuilder<CustomerContext>();
        EfProviders.Configure(database, builder, connection);
        await using var db = new CustomerContext(builder.Options);

        // Path-agnostic "1 row was affected" signal for the insert, covering every confirmed
        // mechanism at once: SQLite/SQL Server/MySQL read a row's scalar value ("SELECT
        // changes()"-equivalent) — the queued row covers that. Snowflake reads
        // DbDataReader.RecordsAffected directly instead (see
        // fakeDbDataReader.RecordsAffectedOverride) — the same queued response's recordsAffected
        // parameter covers that too. A queued NonQueryResult(1) covers any provider using the
        // plain ExecuteNonQueryAsync path. Id is ValueGeneratedNever (see CustomerContext below),
        // so no identity value needs to be read back — only the affected-row-count check matters.
        connection.EnqueueNonQueryResult(1);
        connection.EnqueueReaderResult(
            [new Dictionary<string, object?> { ["Value"] = 1 }],
            recordsAffected: 1);

        db.Add(new Customer { Id = 1, Name = "Ada", IsActive = true });
        await db.SaveChangesAsync();

        // Queued AFTER the insert, not before: fakeDb's reader queue is FIFO and shared across
        // every command — queuing this row earlier risks a reader-path INSERT consuming it
        // instead of the SELECT below.
        connection.EnqueueReaderResult(
        [
            new Dictionary<string, object?> { ["Id"] = 1, ["IsActive"] = true, ["Name"] = "Ada" }
        ]);

        // Real LINQ-to-SQL translation, including local-variable parameterization.
        var name = "Ada";
        var results = await db.Customers.Where(c => c.IsActive && c.Name == name).ToListAsync();

        Assert.Single(results);
        Assert.Equal("Ada", results[0].Name);

        // Path-agnostic: don't assume which of reader/nonquery this provider's INSERT used.
        var allCommands = connection.ExecutedReaderCommands.Concat(connection.ExecutedNonQueryCommands).ToList();

        Assert.Contains(allCommands, c => c.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase));

        var selectCommand = Assert.Single(
            allCommands,
            c => c.CommandText.Contains("SELECT", StringComparison.OrdinalIgnoreCase)
                && c.CommandText.Contains("WHERE", StringComparison.OrdinalIgnoreCase));

        // Proof of the actual bound parameter VALUE, not just a name token in the SQL text —
        // EF Core disposes each DbCommand (clearing its Parameters) before the awaited call
        // returns, so this is only observable because fakeDb snapshots parameters at execution
        // time, before that disposal.
        var bound = Assert.Single(selectCommand.Parameters, p => Equals(p.Value, "Ada"));
        Assert.Equal("Ada", bound.Value);
    }

    [Theory]
    [MemberData(nameof(EfProviders.DeepTestCapable), MemberType = typeof(EfProviders))]
    public async Task SaveChangesAsync_Throws_DbUpdateConcurrencyException_WhenZeroRowsAffected_NoRealDatabaseEngine(
        SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        var connection = (fakeDbConnection)factory.CreateConnection()!;

        var builder = new DbContextOptionsBuilder<CustomerContext>();
        EfProviders.Configure(database, builder, connection);
        await using var db = new CustomerContext(builder.Options);

        var customer = new Customer { Id = 1, Name = "Ada", IsActive = true };
        db.Attach(customer);
        customer.Name = "Grace";

        // Covers every confirmed rows-affected-reporting mechanism at once, without the test
        // needing to know which one this specific provider uses: SQLite/SQL Server/MySQL read a
        // row's scalar value ("SELECT changes()"-equivalent) — an empty reader result reports 0.
        // Snowflake reads DbDataReader.RecordsAffected directly — which defaults to 0 unless
        // overridden, so the same empty reader result covers it too. A queued NonQueryResult(0)
        // covers any provider using the plain ExecuteNonQueryAsync path instead.
        connection.EnqueueNonQueryResult(0);
        connection.EnqueueReaderResult(Array.Empty<Dictionary<string, object?>>());

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [MemberData(nameof(EfProviders.DeepTestCapable), MemberType = typeof(EfProviders))]
    public async Task SaveChangesAsync_Throws_DbUpdateException_WrappingTheProviderFailure_NoRealDatabaseEngine(
        SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        var connection = (fakeDbConnection)factory.CreateConnection()!;

        var builder = new DbContextOptionsBuilder<CustomerContext>();
        EfProviders.Configure(database, builder, connection);
        await using var db = new CustomerContext(builder.Options);

        var customer = new Customer { Id = 1, Name = "Ada", IsActive = true };
        db.Attach(customer);
        customer.Name = "Grace";

        var providerFailure = new InvalidOperationException("simulated provider failure");

        // Path-agnostic across every confirmed rows-affected mechanism at once: a plain
        // ExecuteNonQueryAsync path throws via SetNonQueryExecuteException; a row-reading reader
        // (SQLite/SQL Server/MySQL) throws via FailException on its first Read(); a
        // RecordsAffected-reading reader (Snowflake) throws via RecordsAffectedException, which
        // FailException alone can't reach since that mechanism never calls Read() at all.
        // Whichever this provider actually uses throws the same injected exception; the rest of
        // the queued setup simply goes unused.
        connection.SetNonQueryExecuteException(providerFailure);
        connection.EnqueueReaderResult(new fakeDbDataReader(Array.Empty<Dictionary<string, object>>())
        {
            FailAfterReadCount = 0,
            FailException = providerFailure,
            RecordsAffectedException = providerFailure
        });

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Same(providerFailure, thrown.InnerException);
    }

    // ---- Known, confirmed provider limitations — not fixable by extending fakeDb. ----
    // Each of these providers casts a fakeDb ADO.NET object to its own concrete provider type
    // somewhere in its real pipeline. Satisfying that would mean fakeDb literally becoming that
    // provider's type, which defeats the point of an external, provider-agnostic fake. These
    // tests exist to keep the limitation an executable, regression-checked fact instead of a
    // comment that silently rots — if a provider update ever changes this behavior, the test
    // fails loudly instead of the claim quietly going stale.

    [Fact]
    public async Task PostgreSql_CannotCompleteSaveChanges_BecauseNpgsqlCastsTheReaderToItsConcreteType()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var connection = (fakeDbConnection)factory.CreateConnection()!;

        var builder = new DbContextOptionsBuilder<CustomerContext>();
        EfProviders.Configure(SupportedDatabase.PostgreSql, builder, connection);
        await using var db = new CustomerContext(builder.Options);

        db.Add(new Customer { Id = 1, Name = "Ada", IsActive = true });

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        var cast = Assert.IsType<InvalidCastException>(thrown.InnerException);
        Assert.Contains("NpgsqlDataReader", cast.Message);
    }

    [Fact]
    public async Task Firebird_CannotBindAnyStringParameter_BecauseItsProviderCastsToItsConcreteType()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        var connection = (fakeDbConnection)factory.CreateConnection()!;

        var builder = new DbContextOptionsBuilder<CustomerContext>();
        EfProviders.Configure(SupportedDatabase.Firebird, builder, connection);
        await using var db = new CustomerContext(builder.Options);

        // Reads and non-string writes work fine for Firebird — confirmed separately. Only a
        // STRING-valued parameter triggers this, so a string WHERE-clause parameter on a plain
        // read reproduces it without needing a successful write first.
        var name = "Ada";
        var thrown = await Assert.ThrowsAsync<InvalidCastException>(
            () => db.Customers.Where(c => c.Name == name).ToListAsync());
        Assert.Contains("FbParameter", thrown.Message);
    }

    [Fact]
    public async Task Oracle_CannotExecuteAnyCommand_BecauseItsProviderCastsToItsConcreteType()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        var connection = (fakeDbConnection)factory.CreateConnection()!;

        var builder = new DbContextOptionsBuilder<CustomerContext>();
        EfProviders.Configure(SupportedDatabase.Oracle, builder, connection);
        await using var db = new CustomerContext(builder.Options);

        // Unlike Firebird's narrower limitation above, even a plain parameterless read fails —
        // this isn't specific to writes or to any particular parameter type.
        var thrown = await Assert.ThrowsAsync<InvalidCastException>(() => db.Customers.ToListAsync());
        Assert.Contains("OracleCommand", thrown.Message);
    }

    [Fact]
    public async Task Db2_CannotExecuteAnyCommand_BecauseItsProviderCastsToItsConcreteType()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Db2);
        var connection = (fakeDbConnection)factory.CreateConnection()!;

        var builder = new DbContextOptionsBuilder<CustomerContext>();
        EfProviders.Configure(SupportedDatabase.Db2, builder, connection);
        await using var db = new CustomerContext(builder.Options);

        var thrown = await Assert.ThrowsAsync<InvalidCastException>(() => db.Customers.ToListAsync());
        Assert.Contains("DB2Command", thrown.Message);
    }

    private sealed class CustomerContext(DbContextOptions<CustomerContext> options) : DbContext(options)
    {
        public DbSet<Customer> Customers => Set<Customer>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Sidesteps every provider's identity/sequence-readback dialect differences entirely
            // — this file is about SQL generation, parameter binding, and error translation, not
            // identity-generation syntax.
            modelBuilder.Entity<Customer>().Property(c => c.Id).ValueGeneratedNever();
        }
    }

    private sealed class Customer
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }
}
