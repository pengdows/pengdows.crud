namespace pengdow.crud.threading;

public interface ILockerAsync : IAsyncDisposable
{
    Task LockAsync();
}