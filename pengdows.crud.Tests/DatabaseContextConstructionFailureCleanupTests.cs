using System;
using System.Reflection;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
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

    // TEST-017: session initialization is one of the five constructor phases the tracker
    // previously had no fakeDb fault-injection hook for. This closes it: fakeDbFactory's
    // SetCommandFailure(commandText, exception) matches on exact command text, so it can fail
    // ONLY the session-settings command without also breaking dialect detection, which runs
    // earlier on the exact same persistent connection under DbMode.SingleConnection.
    [Fact]
    public void Construction_SessionInitializationFails_DisposesPersistentConnectionAndPropagates()
    {
        var connectionString = "Data Source=test017-session-init;EmulatedProduct=Sqlite;Mode=Memory;Cache=Shared";
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            SessionInitializationFailureMode = SessionInitializationFailureMode.FailClosed
        };

        // Probe construction (no injected failure) to learn the exact session-settings command
        // text this configuration produces, without hardcoding/guessing dialect-specific SQL.
        string sessionSettingsText;
        using (var probe = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.Sqlite)))
        {
            var field = typeof(DatabaseContext).GetField(
                "_cachedReadWriteSessionSettings", BindingFlags.NonPublic | BindingFlags.Instance);
            sessionSettingsText = (string)field!.GetValue(probe)!;
        }

        Assert.False(string.IsNullOrWhiteSpace(sessionSettingsText),
            "Test setup problem: this configuration produced no session-settings command, so " +
            "this test cannot inject a failure into it.");

        var failingFactory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var injected = new InvalidOperationException("session init boom");
        failingFactory.SetCommandFailure(sessionSettingsText, injected);

        var thrown = Assert.Throws<ConnectionException>(() => new DatabaseContext(config, failingFactory));
        Assert.Same(injected, thrown.InnerException);

        Assert.NotEmpty(failingFactory.CreatedConnections);
        Assert.All(failingFactory.CreatedConnections, c => Assert.True(c.DisposeCount > 0));
    }

    // TEST-017: "original exception preserved when cleanup itself throws" was previously only
    // "verifiable by inspection" (the outer catch's `catch { /* preserve */ }` around cleanup
    // swallows unconditionally, and the surrounding catch always rethrows the original exception
    // afterward) — this makes it a real, TDD-proven regression test using the new
    // fakeDbFactory.ThrowOnDataSourceDispose hook to make the internally-created DbDataSource
    // throw during DisposeOwnedDataSources(), while the construction itself fails for an
    // unrelated, pre-existing reason (EnforceUniqueConnectionString).
    [Fact]
    public void Construction_FailsAndCleanupDataSourceDisposeThrows_OriginalExceptionStillPropagates()
    {
        var connectionString = "Data Source=core028-cleanup-throws-test;EmulatedProduct=Sqlite";
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            EnforceUniqueConnectionString = true
        };

        var factory = new fakeDbFactory(SupportedDatabase.Sqlite) { SupportsNativeDataSource = true };

        // First context legitimately claims the connection string.
        using var first = new DatabaseContext(config, factory);

        // From here on, every internally-created DbDataSource throws when disposed.
        factory.ThrowOnDataSourceDispose = new NotSupportedException("cleanup boom — must not surface");

        // Second construction fails inside ClaimUniqueConnectionStrings (the original,
        // pre-existing failure reason), and the outer catch's cleanup attempt then hits the
        // throwing DbDataSource.Dispose() above.
        var thrown = Assert.Throws<InvalidOperationException>(() => new DatabaseContext(config, factory));

        Assert.DoesNotContain("cleanup boom", thrown.Message, StringComparison.Ordinal);
        Assert.True(
            factory.CreatedDataSources[^1].WasDisposed,
            "The disposal attempt must still have been made even though it threw.");
    }
}
