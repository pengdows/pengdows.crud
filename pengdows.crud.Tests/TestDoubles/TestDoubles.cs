using System;
using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;

namespace pengdows.crud.Tests;

/// <summary>
/// Real objects for the places a test needs an argument but not behaviour from it. Moq is never used in this repository:
/// fakeDb is the test provider, and anything it cannot do is added to fakeDb, not mocked.
/// </summary>
internal static class TestDoubles
{
    /// <summary>A real tracked connection over a fakeDb connection.</summary>
    public static TrackedConnection Connection() => new(new fakeDbConnection());

    /// <summary>An <see cref="IAsyncDisposable"/> that does nothing and counts being disposed.</summary>
    public static CountingAsyncDisposable Disposable() => new();
}

/// <summary>An <see cref="IAsyncDisposable"/> that records how many times it was disposed.</summary>
internal sealed class CountingAsyncDisposable : IAsyncDisposable
{
    private int _disposed;

    public int DisposeCount => _disposed;

    public ValueTask DisposeAsync()
    {
        _disposed++;
        return ValueTask.CompletedTask;
    }
}
