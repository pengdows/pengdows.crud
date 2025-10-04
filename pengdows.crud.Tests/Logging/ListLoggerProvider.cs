#region

using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

#endregion

namespace pengdows.crud.Tests.Logging;

public sealed record LogEntry(string Category, LogLevel Level, EventId EventId, string Message, Exception? Exception);

public sealed class ListLoggerProvider : ILoggerProvider
{
    public readonly ConcurrentQueue<LogEntry> Entries = new();

    public ILogger CreateLogger(string categoryName) => new ListLogger(categoryName, Entries);
    public void Dispose() { }

    private sealed class ListLogger(string category, ConcurrentQueue<LogEntry> entries) : ILogger
    {
        private sealed class NullScope : IDisposable { public static readonly IDisposable Instance = new NullScope(); public void Dispose() { } }
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => entries.Enqueue(new LogEntry(category, logLevel, eventId, formatter(state, exception), exception));
    }
}
