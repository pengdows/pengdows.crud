using System;
using Microsoft.Extensions.Logging;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using pengdows.crud.Tests.Logging;
using Xunit;

namespace pengdows.crud.Tests.dialects;

public sealed class MySqlDialectProviderWarningTests
{
    [Fact]
    public void Constructor_WithMySqlDataFactory_LogsPreferredProviderWarning()
    {
        var provider = new ListLoggerProvider();
        using var loggerFactory = new LoggerFactory(new[] { provider });

        _ = new MySqlDialect(
            MySql.Data.MySqlClient.MySqlClientFactory.Instance,
            loggerFactory.CreateLogger<MySqlDialect>());

        Assert.Contains(provider.Entries,
            entry => entry.Level == LogLevel.Warning &&
                     entry.Message.Contains("MySqlConnector", StringComparison.Ordinal) &&
                     entry.Message.Contains("MySql.Data", StringComparison.Ordinal));
    }

    [Fact]
    public void Constructor_WithFakeDbFactory_DoesNotLogPreferredProviderWarning()
    {
        var provider = new ListLoggerProvider();
        using var loggerFactory = new LoggerFactory(new[] { provider });

        _ = new MySqlDialect(
            new fakeDbFactory(SupportedDatabase.MySql),
            loggerFactory.CreateLogger<MySqlDialect>());

        Assert.DoesNotContain(provider.Entries,
            entry => entry.Level == LogLevel.Warning &&
                     entry.Message.Contains("MySqlConnector", StringComparison.Ordinal) &&
                     entry.Message.Contains("MySql.Data", StringComparison.Ordinal));
    }
}
