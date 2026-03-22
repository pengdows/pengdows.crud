#region

using System;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.infrastructure;
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
        Assert.True(ctx.IsReadOnlyConnection);
        Assert.Throws<InvalidOperationException>(() => ctx.AssertIsWriteConnection());
    }

    [Fact]
    public void ReadOnlyStandardMode_WritePool_IsForbidden_AtConnectionAcquisition()
    {
        // The write pool governor is set to MaxSlots=0 (forbidden) for ReadOnly contexts.
        // Attempting to acquire a write connection must throw PoolForbiddenException
        // at the pool layer — before any SQL reaches the database.
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            DbMode = DbMode.Standard,
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite));

        var ex = Assert.Throws<PoolForbiddenException>(() => ctx.GetConnection(ExecutionType.Write));
        Assert.Equal(PoolLabel.Writer, ex.PoolLabel);
    }

    [Fact]
    public void SwitchingFromReadOnlyToReadWrite_EnablesReadAndWrite()
    {
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Sqlite",
            ReadWriteMode = ReadWriteMode.ReadOnly
        };
        using var ctx = new DatabaseContext(cfg, new fakeDbFactory(SupportedDatabase.Sqlite));

        ctx.ReadWriteMode = ReadWriteMode.ReadWrite;

        Assert.Equal(ReadWriteMode.ReadWrite, ctx.ReadWriteMode);
        Assert.False(ctx.IsReadOnlyConnection);
        ctx.AssertIsReadConnection();
        ctx.AssertIsWriteConnection();
    }
}