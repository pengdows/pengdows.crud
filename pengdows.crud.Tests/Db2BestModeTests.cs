using System;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// CONFIRMED live (Db2 LUW 11.5.8, standalone container): under LUW's default implicit activation a
/// database deactivates when its last connection closes, and the next cold connection costs
/// ~1.1-1.3 s; a PreventDatabaseUnload sentinel (or a DBA's ACTIVATE DATABASE) brings it to ~4 ms.
/// Db2 for z/OS and Db2 for i have no implicit deactivation. So on Db2 LUW, Best selects
/// PreventDatabaseUnload; elsewhere it stays Standard; an explicit Standard is always honored.
/// LUW is recognized by SYSPROC.ENV_GET_INST_INFO(), a table function only Db2 LUW has.
/// </summary>
public class Db2BestModeTests
{
    private static DatabaseContext Create(DbMode mode, bool luw)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Db2);
        if (!luw)
        {
            factory.SetCommandFailure(DatabaseDetectionService.Db2LuwProbeSql,
                new InvalidOperationException("SQL0440N  No authorized routine named \"ENV_GET_INST_INFO\" (z/OS or IBM i)"));
        }

        return new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=localhost:50000;Database=testdb;EmulatedProduct=Db2",
            DbMode = mode
        }, factory);
    }

    [Fact]
    public void Best_OnDb2Luw_SelectsPreventDatabaseUnload()
    {
        using var context = Create(DbMode.Best, luw: true);

        Assert.Equal(DbMode.PreventDatabaseUnload, context.ConnectionMode);
    }

    [Fact]
    public void Best_OnNonLuwDb2_StaysStandard()
    {
        using var context = Create(DbMode.Best, luw: false);

        Assert.Equal(DbMode.Standard, context.ConnectionMode);
    }

    [Theory]
    [InlineData(DbMode.Standard)]
    [InlineData(DbMode.PreventDatabaseUnload)]
    [InlineData(DbMode.SingleWriter)]
    public void ExplicitMode_OnDb2Luw_IsHonored(DbMode requested)
    {
        using var context = Create(requested, luw: true);

        Assert.Equal(requested, context.ConnectionMode);
    }
}
