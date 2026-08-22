using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
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

    // EnforceUniqueConnectionString defaults to false, and the misconfiguration it guards
    // against (two DatabaseContexts double-admitting connections against one pool) is silent
    // when off — nothing is logged today. These tests cover a default, always-on warning
    // (never a throw, never a behavior change) so the misconfiguration is at least visible
    // without requiring the opt-in flag.

    [Fact]
    public void DuplicateConnectionString_NotEnforced_LogsWarning()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connectionString = $"Data Source=warn-test-1;EmulatedProduct={SupportedDatabase.Sqlite}";
        var loggerFactory = new RecordingLoggerFactory();

        using var first = new DatabaseContext(BuildConfig(connectionString, enforce: false), factory, loggerFactory);
        using var second = new DatabaseContext(BuildConfig(connectionString, enforce: false), factory, loggerFactory);

        Assert.Contains(loggerFactory.Entries, IsDuplicateConnectionStringWarning);
    }

    [Fact]
    public void DistinctConnectionStrings_NotEnforced_DoesNotLogWarning()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var loggerFactory = new RecordingLoggerFactory();

        using var first = new DatabaseContext(
            BuildConfig($"Data Source=warn-test-2a;EmulatedProduct={SupportedDatabase.Sqlite}", enforce: false),
            factory, loggerFactory);
        using var second = new DatabaseContext(
            BuildConfig($"Data Source=warn-test-2b;EmulatedProduct={SupportedDatabase.Sqlite}", enforce: false),
            factory, loggerFactory);

        Assert.DoesNotContain(loggerFactory.Entries, IsDuplicateConnectionStringWarning);
    }

    [Fact]
    public void DuplicateConnectionString_AfterFirstDisposed_NotEnforced_DoesNotLogWarning()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connectionString = $"Data Source=warn-test-3;EmulatedProduct={SupportedDatabase.Sqlite}";
        var loggerFactory = new RecordingLoggerFactory();

        var first = new DatabaseContext(BuildConfig(connectionString, enforce: false), factory, loggerFactory);
        first.Dispose();

        loggerFactory.Entries.Clear();

        using var second = new DatabaseContext(BuildConfig(connectionString, enforce: false), factory, loggerFactory);

        Assert.DoesNotContain(loggerFactory.Entries, IsDuplicateConnectionStringWarning);
    }

    [Fact]
    public void FailedEnforcedConstruction_DoesNotLeakWarnRegistration()
    {
        // Mirrors ConstructionFailure_AfterPartialClaim_DoesNotLeakClaim, but for the
        // always-on warning registration rather than the opt-in enforcement claim: a
        // rejected construction must not leave its connection string(s) permanently
        // marked as "in use" by a context that never finished constructing.
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var collidingReadOnlyConnectionString =
            $"Data Source=warn-test-4-ro;EmulatedProduct={SupportedDatabase.Sqlite}";
        var writeConnectionStringForFailedAttempt =
            $"Data Source=warn-test-4-write-attempt;EmulatedProduct={SupportedDatabase.Sqlite}";
        var loggerFactory = new RecordingLoggerFactory();

        using var readOnlyOwner = new DatabaseContext(
            BuildConfig($"Data Source=warn-test-4a;EmulatedProduct={SupportedDatabase.Sqlite}", enforce: true,
                collidingReadOnlyConnectionString),
            factory, loggerFactory);

        Assert.Throws<InvalidOperationException>(() =>
            new DatabaseContext(
                BuildConfig(writeConnectionStringForFailedAttempt, enforce: true, collidingReadOnlyConnectionString),
                factory, loggerFactory));

        loggerFactory.Entries.Clear();

        using var retry = new DatabaseContext(
            BuildConfig(writeConnectionStringForFailedAttempt, enforce: false), factory, loggerFactory);

        Assert.DoesNotContain(loggerFactory.Entries, IsDuplicateConnectionStringWarning);
    }

    // Regression: ComputePoolKeyHash redacts sensitive connection-string values (User Id,
    // Password, etc.) down to the literal constant "REDACTED" before hashing for the pool-key
    // used by both EnforceUniqueConnectionString's throw path and the always-on warning path.
    // Two different tenants sharing the same server+database but DIFFERENT credentials therefore
    // hash to the SAME pool key — with enforcement on, the second tenant's construction wrongly
    // throws even though they are genuinely different connection pools (different credentials
    // legitimately mean different pools in ADO.NET's own provider-level pooling).
    [Fact]
    public void DifferentCredentials_SameServerAndDatabase_Enforced_DoesNotThrow()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var tenantAConnectionString =
            $"Data Source=shared-db;User Id=tenantA;Password=secretA;EmulatedProduct={SupportedDatabase.Sqlite}";
        var tenantBConnectionString =
            $"Data Source=shared-db;User Id=tenantB;Password=secretB;EmulatedProduct={SupportedDatabase.Sqlite}";

        using var tenantA = new DatabaseContext(BuildConfig(tenantAConnectionString, enforce: true), factory);
        using var tenantB = new DatabaseContext(BuildConfig(tenantBConnectionString, enforce: true), factory);
    }

    [Fact]
    public void DifferentCredentials_SameServerAndDatabase_NotEnforced_DoesNotLogWarning()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var tenantAConnectionString =
            $"Data Source=shared-db-2;User Id=tenantA;Password=secretA;EmulatedProduct={SupportedDatabase.Sqlite}";
        var tenantBConnectionString =
            $"Data Source=shared-db-2;User Id=tenantB;Password=secretB;EmulatedProduct={SupportedDatabase.Sqlite}";
        var loggerFactory = new RecordingLoggerFactory();

        using var tenantA = new DatabaseContext(BuildConfig(tenantAConnectionString, enforce: false), factory,
            loggerFactory);
        using var tenantB = new DatabaseContext(BuildConfig(tenantBConnectionString, enforce: false), factory,
            loggerFactory);

        Assert.DoesNotContain(loggerFactory.Entries, IsDuplicateConnectionStringWarning);
    }

    [Fact]
    public void IdenticalCredentials_SameServerAndDatabase_Enforced_StillThrows()
    {
        // Regression guard for the fix above: making credentials distinguishing must not weaken
        // detection of a genuinely identical connection string (same credentials too).
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connectionString =
            $"Data Source=shared-db-3;User Id=tenantA;Password=secretA;EmulatedProduct={SupportedDatabase.Sqlite}";

        using var first = new DatabaseContext(BuildConfig(connectionString, enforce: true), factory);

        Assert.Throws<InvalidOperationException>(() =>
            new DatabaseContext(BuildConfig(connectionString, enforce: true), factory));
    }

    // Regression: RegisterAllForWarning's per-key loop logs via the caller's ILogger, which the
    // codebase already treats elsewhere as a potentially-broken sink (see the async first-open
    // handlers' own comment on this exact concern). If LogWarning itself throws mid-loop, the
    // field assignment in the constructor never completes, so the constructor's failure-path
    // cleanup (which unregisters via that same field) never runs either — the already-registered
    // key(s) would leak in the static registry until overwritten by a later registrant. The fix
    // makes RegisterAllForWarning swallow a logging failure internally instead, matching this
    // type's own "never throws" documented contract.
    [Fact]
    public void DuplicateConnectionString_NotEnforced_LoggerThrows_ConstructionStillSucceeds()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var connectionString = $"Data Source=warn-throwing-logger;EmulatedProduct={SupportedDatabase.Sqlite}";

        using var first = new DatabaseContext(BuildConfig(connectionString, enforce: false), factory);

        using var second = new DatabaseContext(BuildConfig(connectionString, enforce: false), factory,
            new ThrowingLoggerFactory());
    }

    private sealed class ThrowingLoggerFactory : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName)
        {
            return new ThrowingLogger();
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class ThrowingLogger : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return NoopDisposable.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning &&
                    formatter(state, exception).Contains("already using this connection string",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Simulated broken logging sink");
                }
            }

            private sealed class NoopDisposable : IDisposable
            {
                public static readonly NoopDisposable Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }

    private static bool IsDuplicateConnectionStringWarning((LogLevel Level, string Message) entry)
    {
        return entry.Level == LogLevel.Warning &&
               entry.Message.Contains("already using this connection string", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName)
        {
            return new RecordingLogger(Entries);
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly List<(LogLevel Level, string Message)> _entries;

            public RecordingLogger(List<(LogLevel Level, string Message)> entries)
            {
                _entries = entries;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return NoopDisposable.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }

            private sealed class NoopDisposable : IDisposable
            {
                public static readonly NoopDisposable Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
