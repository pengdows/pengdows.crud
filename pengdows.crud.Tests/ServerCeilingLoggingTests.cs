using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class ServerCeilingLoggingTests
{
    private const string ConnectionString = "Host=db1;Database=d;Username=u;Password=p";

    [Fact]
    public void WhenClampingWasRequestedAndTheLimitCannotBeRead_ItIsLoggedAsAWarning()
    {
        var logs = new RecordingLoggerFactory();

        using var context = Create(logs, c => c.ClampPoolsToServerConnectionLimit = true, maxConnections: "n/a");

        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("is unknown"));
    }

    [Fact]
    public void WhenClampingIsOff_AnUnreadableLimitIsNotWarnedAbout()
    {
        var logs = new RecordingLoggerFactory();

        using var context = Create(logs, _ => { }, maxConnections: "n/a");

        Assert.DoesNotContain(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("is unknown"));
    }

    [Fact]
    public void HeadroomWithAnUnknownServerLimit_IsWarnedAboutOnce_NotOncePerPoolSizeResolution()
    {
        var logs = new RecordingLoggerFactory();

        using var context = Create(logs, c =>
        {
            c.ClampPoolsToServerConnectionLimit = true;
            c.ResourceConnectionHeadroom = 2;
        }, maxConnections: "n/a");

        Assert.Equal(1, logs.Entries.Count(e => e.Level == LogLevel.Warning
                                               && e.Message.Contains("ResourceConnectionHeadroom")));
    }

    private static DatabaseContext Create(
        RecordingLoggerFactory logs,
        Action<DatabaseContextConfiguration> configure,
        string maxConnections)
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var connection = new fakeDbConnection();
        connection.ScalarResultsByCommand["SHOW max_connections"] = maxConnections;
        connection.ScalarResultsByCommand["SHOW reserved_connections"] = "0";
        connection.ScalarResultsByCommand["SHOW superuser_reserved_connections"] = "3";
        factory.Connections.Add(connection);

        var config = new DatabaseContextConfiguration
        {
            ConnectionString = ConnectionString,
            DbMode = DbMode.Standard
        };
        configure(config);
        return new DatabaseContext(config, factory, logs);
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        private readonly object _lock = new();
        private readonly List<(LogLevel Level, string Message)> _entries = new();

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_lock)
                {
                    return _entries.ToArray();
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private void Add(LogLevel level, string message)
        {
            lock (_lock)
            {
                _entries.Add((level, message));
            }
        }

        private sealed class RecordingLogger : ILogger
        {
            private readonly RecordingLoggerFactory _owner;

            public RecordingLogger(RecordingLoggerFactory owner)
            {
                _owner = owner;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                _owner.Add(logLevel, formatter(state, exception));
            }
        }
    }
}
