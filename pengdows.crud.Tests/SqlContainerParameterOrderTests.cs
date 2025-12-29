using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using pengdows.crud.dialects;
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

    private sealed class PositionalDialect : SqlDialect
    {
        public override string ParameterMarker => "?";
        public override bool SupportsNamedParameters => false;
        public override SupportedDatabase DatabaseType => SupportedDatabase.Unknown;
        public PositionalDialect() : base(new fakeDbFactory(SupportedDatabase.Unknown), NullLogger<SqlDialect>.Instance) { }
    }

    [Fact]
    public async Task PositionalDialect_BindsByParamSequence()
    {
        var dialect = new PositionalDialect();
        var dummyConn = new FakeTrackedConnection(new fakeDbConnection(), new DataTable(), new Dictionary<string, object>());
        dialect.DetectDatabaseInfo(dummyConn);
        var dsi = new DataSourceInformation(dialect);
        var ctx = new Mock<IDatabaseContext>();
        ctx.SetupGet(c => c.DataSourceInfo).Returns(dsi);
        ctx.SetupGet(c => c.SupportsNamedParameters).Returns(false);
        ctx.SetupGet(c => c.MaxParameterLimit).Returns(100);
        ctx.SetupGet(c => c.DatabaseProductName).Returns(dsi.DatabaseProductName);
        ctx.SetupGet(c => c.DisablePrepare).Returns(true);
        ctx.SetupGet(c => c.ForceManualPrepare).Returns((bool?)null);
        ctx.As<ISqlDialectProvider>().SetupGet(p => p.Dialect).Returns(dialect);

        using var container = SqlContainer.CreateForDialect(ctx.Object, dialect, "SELECT {P}b, {P}a");
        var rendered = container.RenderParams(container.Query.ToString());
        container.Query.Clear().Append(rendered);
        var pA = dialect.CreateDbParameter("a", DbType.Int32, 1);
        pA.ParameterName = "a";
        var pB = dialect.CreateDbParameter("b", DbType.Int32, 2);
        pB.ParameterName = "b";
        container.AddParameter(pA); // intentionally add out of encounter order
        container.AddParameter(pB);

        using var tracked = new FakeTrackedConnection(new fakeDbConnection(), new DataTable(), new Dictionary<string, object>());
        var method = typeof(SqlContainer).GetMethod(
            "PrepareAndCreateCommandAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var task = (Task<DbCommand>)method!.Invoke(container, new object[]
        {
            tracked,
            CommandType.Text,
            ExecutionType.Read,
            CancellationToken.None
        })!;
        var cmd = await task;
        try
        {
            Assert.Equal(2, container.ParamSequence.Count);
            Assert.Equal("b", container.ParamSequence[0]);
            Assert.Equal("a", container.ParamSequence[1]);
        }
        finally
        {
            cmd.Dispose();
        }
    }

    [Fact]
    public async Task NamedDialect_IgnoresParamSequence()
    {
        using var container = Context.CreateSqlContainer("SELECT {P}b, {P}a") as SqlContainer;
        var dialect = ((ISqlDialectProvider)Context).Dialect;
        var rendered = container!.RenderParams(container.Query.ToString());
        container.Query.Clear().Append(rendered);
        var pA = dialect.CreateDbParameter("a", DbType.Int32, 1);
        var pB = dialect.CreateDbParameter("b", DbType.Int32, 2);
        container.AddParameter(pA);
        container.AddParameter(pB);

        using var tracked = Context.GetConnection(ExecutionType.Read);
        var method = typeof(SqlContainer).GetMethod(
            "PrepareAndCreateCommandAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var task = (Task<DbCommand>)method!.Invoke(container, new object[]
        {
            tracked,
            CommandType.Text,
            ExecutionType.Read,
            CancellationToken.None
        })!;
        var cmd = await task;
        try
        {
            Assert.Equal("a", cmd.Parameters[0].ParameterName);
            Assert.Equal("b", cmd.Parameters[1].ParameterName);
        }
        finally
        {
            cmd.Dispose();
        }
    }
}
