using System;
using System.Runtime.CompilerServices;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-065: the process-wide connection-string registry held each context strongly, so a context
/// that was never disposed (and everything it owns) could never be collected.
/// </summary>
[Collection("NormalizationCacheSerial")]
public sealed class UniqueConnectionStringRegistryRootingTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAndAbandon(string connectionString)
    {
        var context = new DatabaseContext(connectionString, new fakeDbFactory(SupportedDatabase.Sqlite));
        return new WeakReference(context);
    }

    [Fact]
    public void AnUndisposedContext_IsNotKeptAliveByTheRegistry()
    {
        var weak = CreateAndAbandon($"Data Source=rooting-{Guid.NewGuid():N};EmulatedProduct=Sqlite");

        for (var i = 0; i < 3 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(weak.IsAlive);
    }
}
