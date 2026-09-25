using System;
using System.Data.Common;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public sealed class PreventDatabaseUnloadTests
{
    [Fact]
    public void KeepAlive_IsCompatibilityAlias()
    {
#pragma warning disable CS0618 // KeepAlive is the obsolete alias under test
        Assert.Equal(DbMode.PreventDatabaseUnload, DbMode.KeepAlive);
#pragma warning restore CS0618
        Assert.Equal(1, (int)DbMode.PreventDatabaseUnload);
    }

    [Fact]
    public void PreventDatabaseUnload_RaisesPoolBelowTwo()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=db;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.PreventDatabaseUnload,
            MaxConcurrentReads = 1,
            MaxConcurrentWrites = 1
        };

        using var context = new DatabaseContext(config, factory);

        Assert.Equal(2, context.GetPoolStatisticsSnapshot(PoolLabel.Reader).MaxSlots);
        Assert.Equal(2, context.GetPoolStatisticsSnapshot(PoolLabel.Writer).MaxSlots);
        var connectionString = new DbConnectionStringBuilder { ConnectionString = context.RawConnectionString };
        Assert.Equal(2, Convert.ToInt32(connectionString["Max Pool Size"]));
        Assert.Equal(2, Convert.ToInt32(connectionString["Min Pool Size"]));
    }

    [Fact]
    public void ReadOnlyPreventDatabaseUnload_DisablesWriterMinimumAndRetainsReaderMinimum()
    {
        var config = new DatabaseContextConfiguration
        {
            ConnectionString = "Server=db;Database=test;EmulatedProduct=SqlServer",
            DbMode = DbMode.PreventDatabaseUnload,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };

        using var context = new DatabaseContext(config, new fakeDbFactory(SupportedDatabase.SqlServer));

        Assert.True(context.GetPoolStatisticsSnapshot(PoolLabel.Writer).Forbidden);
        Assert.Equal(100, context.GetPoolStatisticsSnapshot(PoolLabel.Reader).MaxSlots);
        var connectionString = new DbConnectionStringBuilder { ConnectionString = context.RawReaderConnectionString };
        Assert.Equal(2, Convert.ToInt32(connectionString["Min Pool Size"]));
    }
}
