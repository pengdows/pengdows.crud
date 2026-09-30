using System;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using pengdows.crud.Tests.Logging;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-019 (maintainer decision 2026-09-30: log at Debug): connection-string parse fallbacks and
/// optional capability probes swallowed their exception with a bare catch. The fallbacks stay (a
/// malformed string passes through unchanged and the provider reports it; a failed probe uses its
/// default), but the swallowed exception is logged at Debug.
/// </summary>
public sealed class FallbackCatchLoggingTests
{
    // An unterminated quoted value: DbConnectionStringBuilder rejects it.
    private const string Malformed = "Data Source='unterminated;Pooling=true";

    private static (ListLoggerProvider Logs, ILogger Logger, LoggerFactory Factory) NewLogger()
    {
        var logs = new ListLoggerProvider();
        var factory = new LoggerFactory(new[] { logs });
        return (logs, factory.CreateLogger("test"), factory);
    }

    private static void AssertLoggedAtDebug(ListLoggerProvider logs) =>
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Debug && e.Exception != null);

    public static TheoryData<string> PoolingHelpers() => new()
    {
        nameof(ConnectionPoolingConfiguration.ApplyApplicationName),
        nameof(ConnectionPoolingConfiguration.ApplyApplicationNameSuffix),
        nameof(ConnectionPoolingConfiguration.ApplyPoolDiscriminator),
        nameof(ConnectionPoolingConfiguration.ApplyMaxPoolSize),
        nameof(ConnectionPoolingConfiguration.ClampMinPoolSize),
        nameof(ConnectionPoolingConfiguration.EnsureMinimumPoolSize),
        nameof(ConnectionPoolingConfiguration.StripUnsupportedMaxPoolSize),
        nameof(ConnectionPoolingConfiguration.StripPoolingSetting)
    };

    [Theory]
    [MemberData(nameof(PoolingHelpers))]
    public void ConnectionStringHelper_MalformedString_ReturnsItUnchangedAndLogs(string helper)
    {
        var (logs, logger, factory) = NewLogger();
        using var _ = factory;

        var result = helper switch
        {
            nameof(ConnectionPoolingConfiguration.ApplyApplicationName) =>
                ConnectionPoolingConfiguration.ApplyApplicationName(Malformed, "app", "Application Name", logger: logger),
            nameof(ConnectionPoolingConfiguration.ApplyApplicationNameSuffix) =>
                ConnectionPoolingConfiguration.ApplyApplicationNameSuffix(Malformed, "Application Name", "-ro", "app", logger: logger),
            nameof(ConnectionPoolingConfiguration.ApplyPoolDiscriminator) =>
                ConnectionPoolingConfiguration.ApplyPoolDiscriminator(Malformed, "Workstation ID", "ro", logger: logger),
            nameof(ConnectionPoolingConfiguration.ApplyMaxPoolSize) =>
                ConnectionPoolingConfiguration.ApplyMaxPoolSize(Malformed, 10, "Max Pool Size", logger: logger),
            nameof(ConnectionPoolingConfiguration.ClampMinPoolSize) =>
                ConnectionPoolingConfiguration.ClampMinPoolSize(Malformed, "Min Pool Size", 5, 2, logger: logger),
            nameof(ConnectionPoolingConfiguration.EnsureMinimumPoolSize) =>
                ConnectionPoolingConfiguration.EnsureMinimumPoolSize(Malformed, "Min Pool Size", null, 10, 2, logger: logger),
            nameof(ConnectionPoolingConfiguration.StripUnsupportedMaxPoolSize) =>
                ConnectionPoolingConfiguration.StripUnsupportedMaxPoolSize(Malformed.Replace("Pooling=true", "Max Pool Size=5"), null, logger: logger),
            nameof(ConnectionPoolingConfiguration.StripPoolingSetting) =>
                ConnectionPoolingConfiguration.StripPoolingSetting(Malformed, "Pooling", logger: logger),
            _ => throw new ArgumentOutOfRangeException(nameof(helper))
        };

        Assert.StartsWith("Data Source='unterminated", result);
        AssertLoggedAtDebug(logs);
    }

    [Fact]
    public void PoolingConfigReader_MalformedString_FallsBackAndLogsThroughTheDialect()
    {
        var (logs, logger, factory) = NewLogger();
        using var _ = factory;
        var dialect = (SqlDialect)SqlDialectFactory.CreateDialectForType(SupportedDatabase.SqlServer,
            new fakeDbFactory(SupportedDatabase.SqlServer), logger);

        var config = PoolingConfigReader.GetEffectivePoolConfig(dialect, Malformed);
        Assert.Null(PoolingConfigReader.GetExplicitMaxPoolSize(dialect, Malformed));

        Assert.Equal(PoolConfigSource.DialectDefault, config.Source);
        Assert.Equal(2, logs.Entries.Count(e => e.Level == LogLevel.Debug && e.Exception != null));
    }

    [Fact]
    public void Sqlite_DetectInMemoryKind_MalformedString_LogsAndStillAnswers()
    {
        var (logs, logger, factory) = NewLogger();
        using var _ = factory;
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.Sqlite,
            new fakeDbFactory(SupportedDatabase.Sqlite), logger);

        dialect.DetectInMemoryKind(Malformed);

        AssertLoggedAtDebug(logs);
    }

    [Fact]
    public async Task Sqlite_GetProductNameAsync_ProbeFailure_ReturnsNullAndLogs()
    {
        var (logs, logger, factory) = NewLogger();
        using var _ = factory;
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.Sqlite,
            new fakeDbFactory(SupportedDatabase.Sqlite), logger);
        var connection = new Mock<ITrackedConnection>();
        connection.Setup(c => c.CreateCommand()).Throws(new InvalidOperationException("probe failed"));

        Assert.Null(await ((SqlDialect)dialect).GetProductNameAsync(connection.Object));
        AssertLoggedAtDebug(logs);
    }

    [Fact]
    public void SqlServer_CompatibilityLevelProbeFailure_ReturnsNullAndLogs()
    {
        var (logs, logger, factory) = NewLogger();
        using var _ = factory;
        var dialect = SqlDialectFactory.CreateDialectForType(SupportedDatabase.SqlServer,
            new fakeDbFactory(SupportedDatabase.SqlServer), logger);
        var connection = new Mock<IDbConnection>();
        connection.Setup(c => c.CreateCommand()).Throws(new InvalidOperationException("probe failed"));
        var probe = dialect.GetType().GetMethod("TryGetCompatibilityLevel",
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.Null(probe.Invoke(probe.IsStatic ? null : dialect, new object[] { connection.Object }));
        AssertLoggedAtDebug(logs);
    }
}
