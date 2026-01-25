#region

using System;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class ReadWriteModeBehaviorTests
{
    [Fact]
    public void WriteOnlyMode_IsResetToReadWrite()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            ReadWriteMode = ReadWriteMode.WriteOnly
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite));
        Assert.Equal(ReadWriteMode.ReadWrite, ctx.ReadWriteMode);
        ctx.AssertIsReadConnection();
        ctx.AssertIsWriteConnection();
    }

    [Fact]
    public void ReadOnlyMode_RemainsAndBlocksWrites()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite));
        Assert.Equal(ReadWriteMode.ReadOnly, ctx.ReadWriteMode);
        Assert.Throws<InvalidOperationException>(() => ctx.AssertIsWriteConnection());
    }
}