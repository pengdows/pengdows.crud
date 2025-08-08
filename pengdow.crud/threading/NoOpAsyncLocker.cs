namespace pengdow.crud.threading;

internal sealed class NoOpAsyncLocker : ILockerAsync
{
    public static readonly NoOpAsyncLocker Instance = new();

    private NoOpAsyncLocker()
    {
    }

    public Task LockAsync()
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}