namespace pengdows.crud.fakeDb;

/// <summary>
/// The ordered record of provider-contract members called on a fakeDb connection, command or transaction. A member that runs on
/// behalf of another (an async member calling its sync twin, Dispose closing the connection) is run <see cref="Unlogged"/>, so
/// the log shows what the caller did and by which route, not the fake's internal plumbing.
/// </summary>
internal sealed class OperationLog
{
    private readonly object _gate = new();
    private readonly List<string> _entries = new();
    private int _suppressed;

    public void Record(string entry)
    {
        lock (_gate)
        {
            if (_suppressed == 0)
            {
                _entries.Add(entry);
            }
        }
    }

    public string[] Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    public T Unlogged<T>(Func<T> run)
    {
        Suppress(1);
        try
        {
            return run();
        }
        finally
        {
            Suppress(-1);
        }
    }

    public void Unlogged(Action run)
    {
        Suppress(1);
        try
        {
            run();
        }
        finally
        {
            Suppress(-1);
        }
    }

    private void Suppress(int by)
    {
        lock (_gate)
        {
            _suppressed += by;
        }
    }
}
