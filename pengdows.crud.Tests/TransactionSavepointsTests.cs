#region

using System.Data;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class TransactionSavepointsTests
{
    [Fact]
    public async Task Savepoints_Supported_DoesNotThrow()
    {
        using var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.Sqlite}",
            new fakeDbFactory(SupportedDatabase.Sqlite));
        await using var tx = ctx.BeginTransaction(IsolationLevel.ReadCommitted);

        await tx.SavepointAsync("sp1");
        await tx.RollbackToSavepointAsync("sp1");
    }

    [Fact]
    public async Task Savepoints_Unsupported_NoOp()
    {
        using var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.MySql}",
            new fakeDbFactory(SupportedDatabase.MySql));
        await using var tx = ctx.BeginTransaction(IsolationLevel.ReadCommitted);

        await tx.SavepointAsync("sp1");
        await tx.RollbackToSavepointAsync("sp1");
    }
}