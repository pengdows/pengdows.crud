namespace pengdows.crud.threading;

public interface ILockerAsync : IAsyncDisposable
{
    Task LockAsync();
}