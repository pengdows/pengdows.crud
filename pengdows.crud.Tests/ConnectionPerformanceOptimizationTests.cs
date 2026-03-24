using System;
using System.Data.Common;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using pengdows.crud.metrics;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

[CollectionDefinition(nameof(ConnectionPerformanceOptimizationTests), DisableParallelization = true)]
public sealed class ConnectionPerformanceOptimizationTestsCollection
{
}

[Collection(nameof(ConnectionPerformanceOptimizationTests))]
public sealed class ConnectionPerformanceOptimizationTests
{
    [Fact]
    public void TrackedConnection_LocalState_LazyPrepareCache_AllocatesOnMark()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var inner = factory.CreateConnection();
        using var tracked = new TrackedConnection(inner);
        var state = tracked.LocalState;

        var preparedField = typeof(TrackedConnection).GetField("_prepared",
            BindingFlags.NonPublic | BindingFlags.Instance);
        var orderField = typeof(TrackedConnection).GetField("_order",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(preparedField);
        Assert.NotNull(orderField);

        Assert.Null(preparedField!.GetValue(state));
        Assert.Null(orderField!.GetValue(state));

        Assert.False(state.IsAlreadyPreparedForShape("SELECT 1"));

        Assert.Null(preparedField.GetValue(state));
        Assert.Null(orderField.GetValue(state));

        var (added, evicted) = state.MarkShapePrepared("SELECT 1");
        Assert.True(added);
        Assert.Equal(0, evicted);

        Assert.NotNull(preparedField.GetValue(state));
        Assert.NotNull(orderField.GetValue(state));
    }

    [Fact]
    public void TrackedConnection_Name_IsLazy_WhenDebugDisabled()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var inner = factory.CreateConnection();
        inner.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        var logger = new TestLogger<TrackedConnection>(LogLevel.Error);
        using var tracked = new TrackedConnection(inner, logger: logger);

        var nameField = typeof(TrackedConnection).GetField("_name",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(nameField);

        tracked.Dispose();

        var nameValue = nameField!.GetValue(tracked) as string;
        Assert.True(string.IsNullOrEmpty(nameValue));
    }

    [Fact]
    public void TrackedConnection_Name_UsesCounter()
    {
        var counterField = typeof(TrackedConnection).GetField("_nameCounter",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(counterField);
        counterField!.SetValue(null, 0L);

        var nameMethod = typeof(TrackedConnection).GetMethod("GetName",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(nameMethod);

        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var logger = new TestLogger<TrackedConnection>(LogLevel.Debug);

        var inner1 = factory.CreateConnection();
        inner1.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        using var first = new TrackedConnection(inner1, logger: logger);
        var name1 = (string)nameMethod!.Invoke(first, null)!;

        var inner2 = factory.CreateConnection();
        inner2.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        using var second = new TrackedConnection(inner2, logger: logger);
        var name2 = (string)nameMethod.Invoke(second, null)!;

        Assert.NotEqual(name1, name2);
        Assert.StartsWith("c", name1, StringComparison.Ordinal);
        Assert.StartsWith("c", name2, StringComparison.Ordinal);

        var id1 = long.Parse(name1.AsSpan(1), CultureInfo.InvariantCulture);
        var id2 = long.Parse(name2.AsSpan(1), CultureInfo.InvariantCulture);
        Assert.True(id2 > id1);
    }

    [Fact]
    public void TrackedConnection_Name_UsesApplicationName_FromConnectionString()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Host=localhost;Database=test;EmulatedProduct=PostgreSql",
            ProviderName = SupportedDatabase.PostgreSql.ToString(),
            ApplicationName = "perfapp",
            DbMode = DbMode.Standard
        };

        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);
        using var conn = ctx.GetConnection(ExecutionType.Write);

        var nameMethod = typeof(TrackedConnection).GetMethod("GetName",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(nameMethod);

        var name = (string)nameMethod!.Invoke(conn, null)!;
        Assert.StartsWith("perfapp-rw", name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrackedConnection_OpenTiming_Skipped_WhenNoMetricsAndDebugDisabled()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var inner = factory.CreateConnection();
        inner.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        var logger = new TestLogger<TrackedConnection>(LogLevel.Information);
        await using var tracked = new TrackedConnection(inner, logger: logger);

        var calls = 0;
        TrackedConnection.OpenTimingHook = () => Interlocked.Increment(ref calls);
        try
        {
            await tracked.OpenAsync();
            tracked.Close();
        }
        finally
        {
            TrackedConnection.OpenTimingHook = null;
        }

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task TrackedConnection_OpenTiming_Enabled_WhenMetricsPresent()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var inner = factory.CreateConnection();
        inner.ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}";
        var logger = new TestLogger<TrackedConnection>(LogLevel.Information);
        var metrics = new MetricsCollector(MetricsOptions.Default);
        await using var tracked = new TrackedConnection(inner, logger: logger, metricsCollector: metrics);

        var calls = 0;
        TrackedConnection.OpenTimingHook = () => Interlocked.Increment(ref calls);
        try
        {
            await tracked.OpenAsync();
            tracked.Close();
        }
        finally
        {
            TrackedConnection.OpenTimingHook = null;
        }

        Assert.Equal(1, calls);
    }

    [Fact]
    public void ConnectionString_Redaction_NeverInvokedDuringAcquisition_WhenWarningDisabled()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.Standard
        };

        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var logger = new TestLogger<IDatabaseContext>(LogLevel.Error);
        using var loggerFactory = new TestLoggerFactory(logger);
        using var ctx = new DatabaseContext(config, factory, loggerFactory);

        var calls = 0;
        DatabaseContext.RedactionHook = () => Interlocked.Increment(ref calls);
        try
        {
            using var _ = ctx.GetConnection(ExecutionType.Read);
        }
        finally
        {
            DatabaseContext.RedactionHook = null;
        }

        Assert.Equal(0, calls);
    }

    [Fact]
    public void ConnectionString_Redaction_NeverInvokedDuringAcquisition_WhenWarningEnabled()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.Standard
        };

        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var logger = new TestLogger<IDatabaseContext>(LogLevel.Warning);
        using var loggerFactory = new TestLoggerFactory(logger);
        using var ctx = new DatabaseContext(config, factory, loggerFactory);

        var calls = 0;
        DatabaseContext.RedactionHook = () => Interlocked.Increment(ref calls);
        try
        {
            using var _ = ctx.GetConnection(ExecutionType.Read);
        }
        finally
        {
            DatabaseContext.RedactionHook = null;
        }

        Assert.Equal(0, calls);
    }

    [Fact]
    public void ExecuteSessionSettings_UsesPreComputedStrings_NoRedundantDialectCalls()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}",
            ProviderName = SupportedDatabase.Sqlite.ToString(),
            DbMode = DbMode.Standard
        };

        var factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        var dialect = new CountingDialect(factory);

        using var ctx = new DatabaseContext(config, factory, NullLoggerFactory.Instance);

        // Inject our counting dialect
        var dialectField = typeof(DatabaseContext).GetField("_dialect", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(dialectField);
        dialectField.SetValue(ctx, dialect);

        using var conn = factory.CreateConnection();
        conn.ConnectionString = config.ConnectionString;

        // Reset counters after initialization
        dialect.ResetCounters();

        ctx.ExecuteSessionSettings(conn, readOnly: false);
        ctx.ExecuteSessionSettings(conn, readOnly: false);

        // Should be 0 because ExecuteSessionSettings uses pre-computed strings, not the dialect methods.
        Assert.Equal(0, dialect.FinalCalls);

        ctx.ExecuteSessionSettings(conn, readOnly: true);
        ctx.ExecuteSessionSettings(conn, readOnly: true);

        Assert.Equal(0, dialect.FinalCalls);
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        private readonly LogLevel _minLevel;

        public TestLogger(LogLevel minLevel)
        {
            _minLevel = minLevel;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= _minLevel;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _ = formatter;
        }
    }

    private sealed class TestLoggerFactory : ILoggerFactory
    {
        private readonly ILogger _logger;

        public TestLoggerFactory(ILogger logger)
        {
            _logger = logger;
        }

        public void Dispose()
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return _logger;
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed class CountingDialect : Sql92Dialect
    {
        private int _finalCalls;

        public CountingDialect(DbProviderFactory factory)
            : base(factory, NullLoggerFactory.Instance.CreateLogger<SqlDialect>())
        {
        }

        public int FinalCalls => _finalCalls;

        public void ResetCounters()
        {
            _finalCalls = 0;
        }

        public override string GetFinalSessionSettings(bool readOnly)
        {
            Interlocked.Increment(ref _finalCalls);
            return readOnly ? "SET READONLY=1" : "SET READONLY=0";
        }
    }
}
