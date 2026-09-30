using System;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Review 2026-09-29 (Abrash: hidden cost on a hot path). Under PreventDatabaseUnload, which Best
/// selects for LocalDB, Firebird and Db2 LUW, every connection acquisition checks sentinel health and
/// every release asks whether the connection is a sentinel. Both copied the sentinel list into a new
/// array under a lock (plus a LINQ closure on release). The list is now an immutable array swapped
/// under the lock on change, so these per-operation reads are lock-free and allocation-free.
/// </summary>
public sealed class SentinelSnapshotAllocationTests
{
    private static DatabaseContext CreateFirebirdPreventUnload() => new(new DatabaseContextConfiguration
    {
        ConnectionString = "Data Source=localhost;Database=/data/test.fdb;EmulatedProduct=Firebird",
        DbMode = DbMode.Best
    }, new fakeDbFactory(SupportedDatabase.Firebird));

    [Fact]
    public void GetSentinelSnapshot_AndIsSentinel_DoNotAllocatePerCall()
    {
        using var context = CreateFirebirdPreventUnload();
        Assert.Equal(DbMode.PreventDatabaseUnload, context.ConnectionMode);
        var sentinel = context.GetSentinelSnapshot()[0].Connection;
        Assert.True(context.IsSentinel(sentinel));

        var before = GC.GetAllocatedBytesForCurrentThread();
        var found = 0;
        for (var i = 0; i < 1000; i++)
        {
            found += context.GetSentinelSnapshot().Count;
            if (context.IsSentinel(sentinel))
            {
                found++;
            }
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(found > 0);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void IsSentinel_FalseForAnOrdinaryConnection()
    {
        using var context = CreateFirebirdPreventUnload();
        var ordinary = context.FactoryCreateConnection();
        try
        {
            Assert.False(context.IsSentinel(ordinary));
        }
        finally
        {
            ordinary.Dispose();
        }
    }
}
