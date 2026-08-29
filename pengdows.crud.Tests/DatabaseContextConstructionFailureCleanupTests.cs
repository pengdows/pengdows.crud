using System;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// CORE-028: InitializeReadOnlyConnectionResources can create internally-owned writer/reader
/// DbDataSource instances well before later construction steps (governor setup, unique-claim
/// registration) run. When one of those later steps throws, the constructor's catch blocks
/// unwound persistent connections and pool governors but never disposed those data sources —
/// since a failed constructor never returns an object for the caller to Dispose, this was a
/// permanent per-rejected-construction leak of whatever provider resources the internally-created
/// DbDataSource holds.
/// </summary>
public class DatabaseContextConstructionFailureCleanupTests
{
    [Fact]
    public void Construction_FailsClaimingUniqueConnectionString_DisposesInternallyCreatedDataSource()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite) { SupportsNativeDataSource = true };
        var connectionString = $"Data Source=core028-test;EmulatedProduct={SupportedDatabase.Sqlite}";

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            EnforceUniqueConnectionString = true
        };

        // First context legitimately claims the connection string.
        using var first = new DatabaseContext(config, factory);

        var dataSourcesBeforeFailedAttempt = factory.CreatedDataSources.Count;

        // Second construction with the same connection string + EnforceUniqueConnectionString
        // fails inside ClaimUniqueConnectionStrings — but only AFTER
        // InitializeReadOnlyConnectionResources has already created its own internally-owned
        // DbDataSource via the factory.
        Assert.Throws<InvalidOperationException>(() => new DatabaseContext(config, factory));

        Assert.True(factory.CreatedDataSources.Count > dataSourcesBeforeFailedAttempt,
            "Test setup problem: the failed construction attempt never created a DbDataSource, " +
            "so this test cannot prove anything about disposing it.");

        var leakedDataSource = factory.CreatedDataSources[^1];
        Assert.True(leakedDataSource.WasDisposed,
            "The DbDataSource created internally during a failed DatabaseContext construction " +
            "must be disposed — it can never be reached through the caller otherwise.");
    }

    // TEST-017: the flip side of CORE-028's proof — a DbDataSource the CALLER supplied (via the
    // public DatabaseContext(configuration, dataSource, factory, loggerFactory) constructor
    // overload) is not owned by the context and must survive a failed construction untouched.
    // _dataSourceProvided is exactly the flag DisposeOwnedDataSources() already checks for this,
    // per the comment on the internal catch block above — this proves it holds for a real,
    // publicly-reachable caller-supplied-DbDataSource construction path, not just the
    // internally-created one CORE-028 covered.
    [Fact]
    public void Construction_FailsClaimingUniqueConnectionString_DoesNotDisposeCallerSuppliedDataSource()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite) { SupportsNativeDataSource = true };
        var connectionString = $"Data Source=core028-caller-supplied-test;EmulatedProduct={SupportedDatabase.Sqlite}";

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            EnforceUniqueConnectionString = true
        };

        // First context legitimately claims the connection string.
        using var first = new DatabaseContext(config, factory);

        var callerSuppliedDataSource = factory.CreateDataSource(connectionString);

        // Second construction, supplying the caller-owned data source directly, fails inside
        // ClaimUniqueConnectionStrings — exactly like the internally-created case, just with an
        // externally-owned DbDataSource this time.
        Assert.Throws<InvalidOperationException>(
            () => new DatabaseContext(config, callerSuppliedDataSource, factory));

        Assert.False(((FakeDbDataSource)callerSuppliedDataSource).WasDisposed,
            "A DbDataSource the caller supplied and still owns must never be disposed by a failed " +
            "DatabaseContext construction — only internally-created data sources are the " +
            "context's responsibility to clean up.");
    }
}
