using System.Collections.Concurrent;

namespace pengdows.stormgate.Tests;

/// <summary>
/// An <see cref="ILogger"/> that keeps what it was asked to log.
/// </summary>
internal sealed class RecordingLogger : ILogger
{
    public ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Enqueue((logLevel, formatter(state, exception), exception));
}
