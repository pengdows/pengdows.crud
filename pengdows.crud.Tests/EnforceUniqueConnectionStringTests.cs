using System;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// EnforceUniqueConnectionString is an opt-in safety net: two DatabaseContext instances run
/// independent PoolGovernor admission control, so if they're both pointed at the same physical
/// connection string, their combined admitted connections can exceed what the underlying
/// provider pool was sized for. This is a construction-time guard against that misconfiguration,
/// not a runtime coordination mechanism — left off by default so tests (including this library's
/// own, and anyone using fakeDb) can freely reuse connection strings.
/// </summary>
public class EnforceUniqueConnectionStringTests
{
    private static DatabaseContextConfiguration BuildConfig(string connectionString, bool enforce,
        string? readOnlyConnectionString = null)
    {
        return new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            ReadOnlyConnectionString = readOnlyConnectionString ?? string.Empty,
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            EnforceUniqueConnectionString = enforce
        };
    }

    [Fact]
    public void SecondContext_SameConnectionString_Enforced_Throws()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connectionString = $"Data Source=enforce-test-1;EmulatedProduct={SupportedDatabase.Sqlite}";

        using var first = new DatabaseContext(BuildConfig(connectionString, enforce: true), factory);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new DatabaseContext(BuildConfig(connectionString, enforce: true), factory));

        Assert.Contains("connection string", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SecondContext_SameConnectionString_NotEnforced_Succeeds()
    {
        // Default behavior (EnforceUniqueConnectionString = false, current 2.0 behavior) —
        // must not be affected by the new registry at all.
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connectionString = $"Data Source=enforce-test-2;EmulatedProduct={SupportedDatabase.Sqlite}";

        using var first = new DatabaseContext(BuildConfig(connectionString, enforce: false), factory);
        using var second = new DatabaseContext(BuildConfig(connectionString, enforce: false), factory);
    }

    [Fact]
    public void SecondContext_SameConnectionString_AfterFirstDisposed_Succeeds()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connectionString = $"Data Source=enforce-test-3;EmulatedProduct={SupportedDatabase.Sqlite}";

        var first = new DatabaseContext(BuildConfig(connectionString, enforce: true), factory);
        first.Dispose();

        using var second = new DatabaseContext(BuildConfig(connectionString, enforce: true), factory);
    }

    [Fact]
    public void TwoContexts_DifferentConnectionStrings_Enforced_Succeeds()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);

        using var first = new DatabaseContext(
            BuildConfig($"Data Source=enforce-test-4a;EmulatedProduct={SupportedDatabase.Sqlite}", enforce: true),
            factory);
        using var second = new DatabaseContext(
            BuildConfig($"Data Source=enforce-test-4b;EmulatedProduct={SupportedDatabase.Sqlite}", enforce: true),
            factory);
    }

    [Fact]
    public void SecondContext_SameReadOnlyConnectionString_Enforced_Throws()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var sharedReadOnlyConnectionString = $"Data Source=enforce-test-5-ro;EmulatedProduct={SupportedDatabase.Sqlite}";

        using var first = new DatabaseContext(
            BuildConfig($"Data Source=enforce-test-5a;EmulatedProduct={SupportedDatabase.Sqlite}", enforce: true,
                sharedReadOnlyConnectionString),
            factory);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new DatabaseContext(
                BuildConfig($"Data Source=enforce-test-5b;EmulatedProduct={SupportedDatabase.Sqlite}", enforce: true,
                    sharedReadOnlyConnectionString),
                factory));

        Assert.Contains("connection string", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstructionFailure_AfterPartialClaim_DoesNotLeakClaim()
    {
        // If the SECOND connection string this context wants to claim (ReadOnlyConnectionString)
        // collides, the FIRST claim (the write connection string) it already grabbed in this same
        // attempt must be rolled back — otherwise a failed construction would permanently block
        // that connection string for everyone else.
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var collidingReadOnlyConnectionString =
            $"Data Source=enforce-test-6-ro;EmulatedProduct={SupportedDatabase.Sqlite}";
        var writeConnectionStringForFailedAttempt =
            $"Data Source=enforce-test-6-write-attempt;EmulatedProduct={SupportedDatabase.Sqlite}";

        using var readOnlyOwner = new DatabaseContext(
            BuildConfig($"Data Source=enforce-test-6a;EmulatedProduct={SupportedDatabase.Sqlite}", enforce: true,
                collidingReadOnlyConnectionString),
            factory);

        Assert.Throws<InvalidOperationException>(() =>
            new DatabaseContext(
                BuildConfig(writeConnectionStringForFailedAttempt, enforce: true, collidingReadOnlyConnectionString),
                factory));

        // The write connection string from the failed attempt above must not still be claimed —
        // a brand new context using it (with enforcement on) should succeed.
        using var retry = new DatabaseContext(
            BuildConfig(writeConnectionStringForFailedAttempt, enforce: true), factory);
    }

    [Fact]
    public void SecondContext_SameConnectionString_SingleConnectionMode_Enforced_DoesNotLeakOpenedConnection()
    {
        // Regression: DbMode.SingleConnection opens+session-inits its persistent connection before
        // ClaimUniqueConnectionStrings can throw for a duplicate connection string
        // (DatabaseContext.Initialization.cs). The constructor's catch only logs+rethrows — the
        // second (rejected) context's already-opened connection was never disposed, a real leak on
        // every rejected duplicate construction under SingleConnection mode.
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connectionString = $"Data Source=enforce-test-7;EmulatedProduct={SupportedDatabase.Sqlite}";

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = connectionString,
            DbMode = DbMode.SingleConnection,
            ReadWriteMode = ReadWriteMode.ReadWrite,
            EnforceUniqueConnectionString = true
        };

        using var first = new DatabaseContext(config, factory);

        Assert.Throws<InvalidOperationException>(() => new DatabaseContext(config, factory));

        // The second (rejected) context's persistent connection must have been disposed, not leaked.
        var secondContextConnection = factory.CreatedConnections[^1];
        Assert.True(secondContextConnection.DisposeCount > 0);
    }
}
