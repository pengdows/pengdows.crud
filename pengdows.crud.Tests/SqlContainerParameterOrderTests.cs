using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Threading;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlContainerParameterOrderTests : SqlLiteContextTestBase
{
    [Fact]
    public void DbCommandParameters_AreAdded_InSameOrder_AsAppended()
    {
        using var container = Context.CreateSqlContainer("SELECT 1");

        // Add parameters in a specific order with mixed prefix styles
        container.AddParameterWithValue("beta", DbType.Int32, 2);
        container.AddParameterWithValue("alpha", DbType.String, "a");
        container.AddParameterWithValue(":gamma", DbType.Int32, 3);

        using var tracked = Context.GetConnection(ExecutionType.Read);

        // Invoke the internal prepare to populate DbCommand.Parameters while keeping control
        var method = typeof(SqlContainer).GetMethod(
            "PrepareAndCreateCommandAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var taskObj = method!.Invoke(container, new object[]
        {
            tracked,
            CommandType.Text,
            ExecutionType.Read,
            CancellationToken.None
        })!;

        // Result is Task<DbCommand>; accessing Result will wait for completion
        var resultProp = taskObj.GetType().GetProperty("Result");
        Assert.NotNull(resultProp);

        var cmd = (DbCommand)resultProp!.GetValue(taskObj)!;
        try
        {
            // Ensure three parameters exist
            Assert.Equal(3, cmd.Parameters.Count);

            // Names should be normalized (no @/:/?) and preserve add order
            Assert.Equal("beta", cmd.Parameters[0].ParameterName);
            Assert.Equal("alpha", cmd.Parameters[1].ParameterName);
            Assert.Equal("gamma", cmd.Parameters[2].ParameterName);
        }
        finally
        {
            cmd.Dispose();
        }
    }

    [Fact]
    public void StoredProcArgs_UseDialectNames_InAddOrder()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        var ctx = new DatabaseContext($"Data Source=test;EmulatedProduct={SupportedDatabase.PostgreSql}", factory);
        using var container = ctx.CreateSqlContainer("my_proc");

        // Add explicitly named parameters out of lexicographic order
        container.AddParameterWithValue("b", DbType.Int32, 2);
        container.AddParameterWithValue("a", DbType.Int32, 1);
        container.AddParameterWithValue("c", DbType.Int32, 3);

        var wrapped = container.WrapForStoredProc(ExecutionType.Read);

        // Expect SELECT form with dialect-specific marker, preserving add order
        Assert.Equal("SELECT * FROM \"my_proc\"(:b, :a, :c)", wrapped);
    }
}

