using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// SingleWriter mode's writer-preference turnstile is only meaningful when the reader and
/// writer governors share the same underlying connection pool — sharing a turnstile across
/// two independent pools (e.g. a read replica) would incorrectly gate replica reads behind
/// primary writes. <see cref="DatabaseContext"/> gates turnstile creation on
/// <c>sharesTurnstile</c>, computed by hashing the reader/writer connection strings — but
/// those strings are deliberately mutated differently for reader vs. writer during
/// <c>InitializeReadOnlyConnectionResources</c> (pooling stripped from the reader, an "-rw"
/// ApplicationName suffix and MaxPoolSize=1 applied to the writer) even when the caller supplied
/// only ONE connection string and never asked for a separate read pool.
/// </summary>
public class SingleWriterTurnstileActivationTests
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static object? GetGovernorField(DatabaseContext context, string governorFieldName, string innerFieldName)
    {
        var governorField = typeof(DatabaseContext).GetField(governorFieldName, AnyInstance);
        Assert.NotNull(governorField);
        var governor = governorField!.GetValue(context);
        Assert.NotNull(governor);

        var innerField = typeof(PoolGovernor).GetField(innerFieldName, AnyInstance);
        Assert.NotNull(innerField);
        return innerField!.GetValue(governor);
    }

    [Fact]
    public void SingleWriter_SingleConnectionString_ActivatesSharedTurnstile()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter,
            ReadWriteMode = ReadWriteMode.ReadWrite
            // No dedicated ReadOnlyConnectionString — the common/default configuration.
        };

        using var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var writerTurnstile = GetGovernorField(context, "_writerGovernor", "_turnstile");
        var readerTurnstile = GetGovernorField(context, "_readerGovernor", "_turnstile");

        // BUG today: writer/reader connection strings diverge during initialization
        // (pooling stripped from reader, "-rw" suffix + MaxPoolSize=1 on writer) even though
        // the caller only supplied one connection string — so the pool-key hash comparison
        // that gates turnstile creation sees them as different pools, and no turnstile is
        // ever created for the single-connection-string SingleWriter case.
        Assert.NotNull(writerTurnstile);
        Assert.Same(writerTurnstile, readerTurnstile);
    }

    [Fact]
    public void SingleWriter_ExplicitReadOnlyConnectionString_DoesNotShareTurnstile()
    {
        // Guard: when the caller explicitly points reads at a different connection string
        // (e.g. a read replica), the turnstile must NOT be shared — displacing replica reads
        // behind primary writes would be incorrect, since they're independent targets.
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=primary.db;EmulatedProduct=Sqlite",
            ReadOnlyConnectionString = "Data Source=replica.db;EmulatedProduct=Sqlite",
            DbMode = DbMode.SingleWriter,
            ReadWriteMode = ReadWriteMode.ReadWrite
        };

        using var context = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        var writerTurnstile = GetGovernorField(context, "_writerGovernor", "_turnstile");
        Assert.Null(writerTurnstile);
    }
}
