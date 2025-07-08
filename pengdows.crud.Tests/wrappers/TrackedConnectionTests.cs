#region
using System.Threading.Tasks;
using pengdows.crud.FakeDb;
using pengdows.crud.threading;
using pengdows.crud.wrappers;
using Xunit;

#endregion

namespace pengdows.crud.Tests.wrappers;

public class TrackedConnectionTests
{
    [Fact]
    public void GetLock_NoSharedConnection_ReturnsNoOpInstance()
    {
        using var conn = new FakeDbConnection();
        using var tracked = new TrackedConnection(conn);

        var locker1 = tracked.GetLock();
        var locker2 = tracked.GetLock();

        Assert.Same(NoOpAsyncLocker.Instance, locker1);
        Assert.Same(locker1, locker2);
    }

    [Fact]
    public async Task GetLock_SharedConnection_ReturnsRealAsyncLocker()
    {
        using var conn = new FakeDbConnection();
        using var tracked = new TrackedConnection(conn, isSharedConnection: true);

        await using var locker = tracked.GetLock();

        Assert.IsType<RealAsyncLocker>(locker);
        await locker.LockAsync();
        await locker.DisposeAsync();
    }

    [Fact]
    public void GetLock_SharedConnection_ReturnsNewInstanceEachTime()
    {
        using var conn = new FakeDbConnection();
        using var tracked = new TrackedConnection(conn, isSharedConnection: true);

        var first = tracked.GetLock();
        var second = tracked.GetLock();

        Assert.IsType<RealAsyncLocker>(first);
        Assert.IsType<RealAsyncLocker>(second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Open_InvokesOnFirstOpen_OnlyOnce()
    {
        using var conn = new FakeDbConnection();
        var count = 0;
        using var tracked = new TrackedConnection(conn, onFirstOpen: _ => count++);

        tracked.Open();
        tracked.Close();
        tracked.Open();

        Assert.Equal(1, count);
        Assert.True(tracked.WasOpened);
    }
}
