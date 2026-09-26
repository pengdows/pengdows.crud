using System;
using System.Linq;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// CONFIRMED live (Db2 LUW, IBM.Data.Db2 8.0.0.500): with a caller's Max Pool Size=1, Best resolved
/// to PreventDatabaseUnload, the pool was raised to 2 for the sentinel, and IBM's driver rejected the
/// raised connection string ("Invalid argument": its pool was already created at size 1 by the
/// detection connection). The failed construction also left its already-open writer sentinel
/// undisposed, so later contexts in the process found the one-connection pool exhausted.
/// </summary>
public class PreventDatabaseUnloadPoolFloorTests
{
    private const string Cs = "Data Source=localhost;Database=/data/test.fdb;EmulatedProduct=Firebird";

    // Best must not override an explicit one-connection pool to fit a sentinel.
    [Theory]
    [InlineData(";Max Pool Size=1", DbMode.Standard)]
    [InlineData(";Max Pool Size=2", DbMode.PreventDatabaseUnload)]
    [InlineData("", DbMode.PreventDatabaseUnload)]
    public void Best_WithMaxPoolSizeBelowTwo_StaysStandard(string poolSetting, DbMode expected)
    {
        using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = Cs + poolSetting,
            DbMode = DbMode.Best
        }, new fakeDbFactory(SupportedDatabase.Firebird));

        Assert.Equal(expected, context.ConnectionMode);
    }

    [Fact]
    public void Best_WithMaxConcurrentWritesOne_StaysStandard()
    {
        using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = Cs,
            DbMode = DbMode.Best,
            MaxConcurrentWrites = 1
        }, new fakeDbFactory(SupportedDatabase.Firebird));

        Assert.Equal(DbMode.Standard, context.ConnectionMode);
    }

    [Fact]
    public void FailedConstruction_AfterWriterSentinelOpened_DisposesIt()
    {
        var configuration = new DatabaseContextConfiguration
        {
            ConnectionString = Cs,
            DbMode = DbMode.PreventDatabaseUnload
        };
        string readerConnectionString;
        using (var probe = new DatabaseContext(configuration, new fakeDbFactory(SupportedDatabase.Firebird)))
        {
            readerConnectionString = probe.RawReaderConnectionString;
        }

        var factory = new fakeDbFactory(SupportedDatabase.Firebird);
        factory.SetFailOnOpenForConnectionString(readerConnectionString,
            new InvalidOperationException("reader sentinel open failed"));

        Assert.ThrowsAny<Exception>(() => new DatabaseContext(configuration, factory));

        var opened = factory.CreatedConnections.Where(c => c.OpenCount > 0).ToList();
        Assert.NotEmpty(opened);
        Assert.All(opened, c => Assert.True(c.DisposeCount > 0,
            $"connection ({c.ConnectionString}) left undisposed by the failed construction"));
    }
}
