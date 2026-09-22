namespace pengdows.stormgate.EntityFrameworkCore.MultiProvider.Tests;

// Extends EfProviderDeepTests' "Tier 2" claim to multi-row SaveChangesAsync. A single-entity
// insert can force an entirely different, simpler code path than several entities saved in one
// call — providers that support modification-command batching combine multiple INSERTs into
// fewer round trips, which means different result-set/affected-row correlation logic than a
// single insert exercises. This matters concretely here: the Npgsql failure documented in
// EfProviderDeepTests (NpgsqlModificationCommandBatch.Consume casting the reader to a concrete
// type) lives specifically inside that batch-consumption code — a provider excluded from
// DeepTestCapable for that reason already fails on ONE row, but a provider that stays generic for
// one row is not thereby proven to stay generic once its batching path activates. This file
// proves several entities inserted in a single SaveChangesAsync round-trip correctly for every
// DeepTestCapable provider.
public sealed class EfProviderBatchingTests
{
    private const int BatchSize = 4;

    [Theory]
    [MemberData(nameof(EfProviders.DeepTestCapable), MemberType = typeof(EfProviders))]
    public async Task SaveChangesAsync_WithSeveralEntitiesInOneCall_InsertsAllOfThem_NoRealDatabaseEngine(
        SupportedDatabase database)
    {
        var factory = new fakeDbFactory(database);
        var connection = (fakeDbConnection)factory.CreateConnection()!;

        var builder = new DbContextOptionsBuilder<BatchContext>();
        EfProviders.Configure(database, builder, connection);
        await using var db = new BatchContext(builder.Options);

        // Generous, path-agnostic queueing: a provider may issue one combined multi-row
        // statement, or BatchSize separate statements, and may report success via a scalar
        // "changes()"-style read, a reader's RecordsAffected, or a plain ExecuteNonQueryAsync
        // return value. Queueing BatchSize of each covers every shape without needing to know in
        // advance which one this provider's batching implementation actually uses.
        for (var i = 0; i < BatchSize; i++)
        {
            connection.EnqueueNonQueryResult(1);
            connection.EnqueueReaderResult([new Dictionary<string, object?> { ["Value"] = 1 }], recordsAffected: 1);
        }

        for (var i = 1; i <= BatchSize; i++)
        {
            db.Add(new BatchEntity { Id = i, Value = $"Row{i}" });
        }

        await db.SaveChangesAsync();

        var allCommands = connection.ExecutedReaderCommands.Concat(connection.ExecutedNonQueryCommands).ToList();
        var insertCommands = allCommands.Where(c => c.CommandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.NotEmpty(insertCommands);

        // Path-agnostic on shape (one combined statement vs. BatchSize separate ones): every one
        // of the BatchSize rows' distinctive values must appear somewhere across whichever
        // INSERT command(s) were actually issued, proving no row was silently dropped by the
        // batching path.
        for (var i = 1; i <= BatchSize; i++)
        {
            var expectedValue = $"Row{i}";
            Assert.Contains(
                insertCommands,
                c => c.Parameters.Any(p => Equals(p.Value, expectedValue)));
        }
    }

    private sealed class BatchContext(DbContextOptions<BatchContext> options) : DbContext(options)
    {
        public DbSet<BatchEntity> BatchEntities => Set<BatchEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BatchEntity>().Property(e => e.Id).ValueGeneratedNever();
        }
    }

    private sealed class BatchEntity
    {
        public int Id { get; set; }

        public string Value { get; set; } = string.Empty;
    }
}
