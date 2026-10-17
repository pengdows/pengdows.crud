using System.Collections.Concurrent;

namespace pengdows.stormgate.Tests;

/// <summary>
/// An <see cref="ILogger"/> that keeps what it was asked to log. Moq is never used in this repository.
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

/// <summary>
/// A <see cref="DbDataSource"/> whose next opens are scripted: a queued connection is returned, a queued failure is thrown, and
/// once the script is spent each open hands out a new fakeDb connection. Counts the opens it was asked for.
/// </summary>
internal sealed class ScriptedDataSource : DbDataSource
{
    private readonly ConcurrentQueue<Func<DbConnection>> _script = new();
    private int _openCount;

    public int OpenCount => Volatile.Read(ref _openCount);

    public override string ConnectionString => "scripted";

    /// <summary>The next open returns <paramref name="connection"/>.</summary>
    public ScriptedDataSource Returns(DbConnection connection)
    {
        _script.Enqueue(() => connection);
        return this;
    }

    /// <summary>The next open fails with <paramref name="failure"/>.</summary>
    public ScriptedDataSource Throws(Exception failure)
    {
        _script.Enqueue(() => throw failure);
        return this;
    }

    protected override ValueTask<DbConnection> OpenDbConnectionAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _openCount);
        try
        {
            return new ValueTask<DbConnection>(_script.TryDequeue(out var next) ? next() : new fakeDbConnection());
        }
        catch (Exception ex)
        {
            return ValueTask.FromException<DbConnection>(ex);
        }
    }

    protected override DbConnection CreateDbConnection() => new fakeDbConnection();
}
