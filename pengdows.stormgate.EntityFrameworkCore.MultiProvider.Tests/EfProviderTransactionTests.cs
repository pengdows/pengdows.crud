namespace pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests;

// Extends EfProviderDeepTests' "Tier 2" claim across another ADO.NET abstraction boundary:
// DbTransaction. Proving SaveChangesAsync succeeds does not prove a provider stays generic
// against an explicit, caller-managed transaction — a provider could accept a plain DbConnection/
// DbCommand/DbDataReader and still do `(ProviderTransaction)transaction` somewhere in its own
// commit/rollback path. fakeDb already ships a real fakeDbTransaction with injectable commit and
// rollback failures (SetTransactionCommitException/SetTransactionRollbackException) specifically
// for this: this file proves an explicit BeginTransactionAsync -> SaveChangesAsync -> CommitAsync
// round-trip succeeds, and separately that a genuine commit/rollback failure propagates as the
// real injected exception rather than being masked by an InvalidCastException from a provider
// trying to unwrap the transaction to its own concrete type.
public sealed class EfProviderTransactionTests
{
    [Theory]
    [MemberData(nameof(EfProviders.DeepTestCapable), MemberType = typeof(EfProviders))]
    public async Task ExplicitTransaction_CommitAsync_SucceedsAndPersistsTheWrite_NoRealDatabaseEngine(
        SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        var connection = (fakeDbConnection)factory.CreateConnection()!;

        var builder = new DbContextOptionsBuilder<TxContext>();
        EfProviders.Configure(database, builder, connection);
        await using var db = new TxContext(builder.Options);

        await using var tx = await db.Database.BeginTransactionAsync();

        connection.EnqueueNonQueryResult(1);
        connection.EnqueueReaderResult([new Dictionary<string, object?> { ["Value"] = 1 }], recordsAffected: 1);

        db.Add(new TxEntity { Id = 1, Value = "Ada" });
        await db.SaveChangesAsync();

        await tx.CommitAsync();

        var allCommands = connection.ExecutedReaderCommands.Concat(connection.ExecutedNonQueryCommands).ToList();
        Assert.Contains(allCommands, c => c.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(EfProviders.DeepTestCapable), MemberType = typeof(EfProviders))]
    public async Task ExplicitTransaction_CommitAsync_WhenTheRealCommitFails_PropagatesTheRealFailure_NotAMiscastException(
        SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        var connection = (fakeDbConnection)factory.CreateConnection()!;

        var commitFailure = new InvalidOperationException("simulated commit failure");
        connection.SetTransactionCommitException(commitFailure);

        var builder = new DbContextOptionsBuilder<TxContext>();
        EfProviders.Configure(database, builder, connection);
        await using var db = new TxContext(builder.Options);

        await using var tx = await db.Database.BeginTransactionAsync();

        connection.EnqueueNonQueryResult(1);
        connection.EnqueueReaderResult([new Dictionary<string, object?> { ["Value"] = 1 }], recordsAffected: 1);

        db.Add(new TxEntity { Id = 1, Value = "Ada" });
        await db.SaveChangesAsync();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => tx.CommitAsync());

        var found = FindException(thrown, commitFailure);
        Assert.True(
            found,
            $"Expected the injected commit failure ('{commitFailure.Message}') to surface somewhere in " +
            $"the thrown exception chain, but got {thrown.GetType().FullName}: {thrown.Message}" +
            (thrown.InnerException != null ? $" (inner: {thrown.InnerException.GetType().FullName}: {thrown.InnerException.Message})" : ""));

        // The specific failure mode this guards against: a provider that tries to cast the
        // DbTransaction to its own concrete type before ever reaching the real Commit() call
        // would surface an InvalidCastException here instead of (or wrapping over) the real one.
        Assert.False(
            thrown is InvalidCastException || thrown.InnerException is InvalidCastException,
            $"Provider appears to cast DbTransaction to a concrete type: {thrown}");
    }

    [Theory]
    [MemberData(nameof(EfProviders.DeepTestCapable), MemberType = typeof(EfProviders))]
    public async Task ExplicitTransaction_RollbackAsync_WhenTheRealRollbackFails_PropagatesTheRealFailure_NotAMiscastException(
        SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        var connection = (fakeDbConnection)factory.CreateConnection()!;

        var rollbackFailure = new InvalidOperationException("simulated rollback failure");
        connection.SetTransactionRollbackException(rollbackFailure);

        var builder = new DbContextOptionsBuilder<TxContext>();
        EfProviders.Configure(database, builder, connection);
        await using var db = new TxContext(builder.Options);

        await using var tx = await db.Database.BeginTransactionAsync();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => tx.RollbackAsync());

        var found = FindException(thrown, rollbackFailure);
        Assert.True(
            found,
            $"Expected the injected rollback failure ('{rollbackFailure.Message}') to surface somewhere in " +
            $"the thrown exception chain, but got {thrown.GetType().FullName}: {thrown.Message}" +
            (thrown.InnerException != null ? $" (inner: {thrown.InnerException.GetType().FullName}: {thrown.InnerException.Message})" : ""));

        Assert.False(
            thrown is InvalidCastException || thrown.InnerException is InvalidCastException,
            $"Provider appears to cast DbTransaction to a concrete type: {thrown}");
    }

    private static bool FindException(Exception? exception, Exception target)
    {
        while (exception != null)
        {
            if (ReferenceEquals(exception, target) || exception.Message == target.Message)
            {
                return true;
            }

            exception = exception.InnerException;
        }

        return false;
    }

    private sealed class TxContext(DbContextOptions<TxContext> options) : DbContext(options)
    {
        public DbSet<TxEntity> TxEntities => Set<TxEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TxEntity>().Property(e => e.Id).ValueGeneratedNever();
        }
    }

    private sealed class TxEntity
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;
    }
}
