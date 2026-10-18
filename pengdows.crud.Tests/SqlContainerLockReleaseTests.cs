using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using pengdows.crud.infrastructure;
using pengdows.crud.threading;
using pengdows.crud.Tests.Logging;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Review 2026-09-29. SqlContainer released its context lock and single-connection transaction gate
/// in finally blocks either unguarded (a throwing release skipped the failed-write rollback and the
/// connection cleanup that followed, and masked the original exception) or with an empty catch (a
/// gate left held made later operations wait with no trace). Releases now go through one helper that
/// never throws and logs a failure at Warning.
/// </summary>
public sealed class SqlContainerLockReleaseTests
{
    [Fact]
    public async Task ReleaseLockBestEffort_ReleaseFailure_IsLoggedNotThrown()
    {
        var logs = new ListLoggerProvider();
        using var loggerFactory = new LoggerFactory(new[] { logs });
        var locker = new Mock<ILockerAsync>();
        locker.Setup(l => l.DisposeAsync()).Throws(new ObjectDisposedException("gate"));

        await SqlContainer.ReleaseLockBestEffortAsync(locker.Object, loggerFactory.CreateLogger("test"), "context lock");

        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Exception is ObjectDisposedException);
    }

    [Fact]
    public async Task ReleaseLockBestEffort_ReleasesTheLock()
    {
        var locker = new Mock<ILockerAsync>();
        locker.Setup(l => l.DisposeAsync()).Returns(ValueTask.CompletedTask);

        await SqlContainer.ReleaseLockBestEffortAsync(locker.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, "gate");

        locker.Verify(l => l.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task ReleaseLockBestEffort_NullOrNoOpLocker_DoesNothing()
    {
        await SqlContainer.ReleaseLockBestEffortAsync(null, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, "gate");
        await SqlContainer.ReleaseLockBestEffortAsync(NoOpAsyncLocker.Instance, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, "gate");
    }
}
