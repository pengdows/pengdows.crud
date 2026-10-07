using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// PERF-031: every container asked the context's logger factory for a new ILogger&lt;ISqlContainer&gt;
/// (a Logger&lt;T&gt; wrapper, its category name and the factory's lock), about 0.3 µs per operation.
/// The context makes it once; Logger&lt;T&gt; forwards to the factory's own logger, so providers added
/// to the factory later still receive its messages.
/// </summary>
public sealed class SqlContainerLoggerCacheTests
{
    [Fact]
    public void CreateSqlContainerLogger_ReturnsOneLoggerPerContext()
    {
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite));

        Assert.Same(context.CreateSqlContainerLogger(), context.CreateSqlContainerLogger());
    }

    [Fact]
    public void CachedLogger_ReachesAProviderAddedToTheFactoryLater()
    {
        using var factory = new LoggerFactory();
        using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite"
        }, new fakeDbFactory(SupportedDatabase.Sqlite), factory);
        var logger = context.CreateSqlContainerLogger();
        var provider = new RecordingProvider();

        factory.AddProvider(provider);
        logger.LogWarning("after");

        Assert.Contains("after", provider.Messages);
    }

    private sealed class RecordingProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new Recorder(Messages);
        public void Dispose() { }

        private sealed class Recorder(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state);
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Add(formatter(state, exception));
        }
    }
}
