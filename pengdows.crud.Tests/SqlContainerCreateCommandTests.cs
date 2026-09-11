using System;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.infrastructure;
using Xunit;

namespace pengdows.crud.Tests;

public class SqlContainerCreateCommandTests : SqlLiteContextTestBase
{
    [Fact]
    public async Task CreateCommand_SetsCommandTextAndParameters()
    {
        await using var connection = Context.GetConnection(ExecutionType.Write);
        await connection.OpenAsync();

        using var container = Context.CreateSqlContainer("SELECT {P}id");
        container.AddParameterWithValue("id", DbType.Int32, 1);

        using var command = container.CreateCommand(connection);

        var expected = $"SELECT {container.MakeParameterName("id")}";
        Assert.Equal(expected, command.CommandText);
        Assert.Single(command.Parameters);

        var param = Assert.IsAssignableFrom<DbParameter>(command.Parameters[0]);
        Assert.Equal("id", param.ParameterName);
    }

    [Theory]
    [InlineData(SupportedDatabase.SqlServer)]
    [InlineData(SupportedDatabase.Firebird)]
    public async Task CreateCommand_DoesNotCloneParameters(SupportedDatabase product)
    {
        var factory = new fakeDbFactory(product);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = $"Data Source=test;EmulatedProduct={product}",
            ProviderName = product.ToString(),
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);
        await using var connection = context.GetConnection(ExecutionType.Read);
        await connection.OpenAsync();

        using var container = context.CreateSqlContainer("SELECT 1");
        var parameter = container.AddParameterWithValue("p0", DbType.Int32, 1);

        using var command = container.CreateCommand(connection);

        var cmdParam = Assert.Single(command.Parameters);
        Assert.Same(parameter, cmdParam);
    }

    [Fact]
    public async Task CreateCommand_EmptyQuery_ReturnsRawCommand()
    {
        await using var connection = Context.GetConnection(ExecutionType.Write);
        await connection.OpenAsync();

        using var container = Context.CreateSqlContainer();
        container.AddParameterWithValue("id", DbType.Int32, 1);

        using var command = container.CreateCommand(connection);

        Assert.Equal(string.Empty, command.CommandText);
        Assert.Empty(command.Parameters);
    }

    [Fact]
    public async Task CreateCommand_PositionalDialect_NoParamSequence_BindsAllParametersInInsertionOrder()
    {
        // Regression: confirmed live against a real Informix container ("ERROR [07001]
        // [Informix][Informix ODBC Driver]Wrong number of parameters."). TableGateway builds
        // INSERT SQL by calling dialect.MakeParameterName(name) directly and appending its
        // result into the query text (see TableGateway.Core.cs) — it never uses the {P}NAME
        // placeholder form, so RenderParams()/ParamSequence never run. For a positional
        // dialect (SupportsNamedParameters == false), MakeParameterName always returns "?"
        // regardless of name, and AddParametersToCommand's positional path relied entirely on
        // ParamSequence to know which/how-many parameters to bind — leaving it empty and
        // binding zero parameters against N "?" markers in the rendered SQL.
        var factory = new fakeDbFactory(SupportedDatabase.Informix);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Informix",
            ProviderName = SupportedDatabase.Informix.ToString(),
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);
        await using var connection = context.GetConnection(ExecutionType.Write);
        await connection.OpenAsync();

        using var container = context.CreateSqlContainer();
        var p0 = container.AddParameterWithValue("i0", DbType.Int32, 1);
        var p1 = container.AddParameterWithValue("i1", DbType.String, "alpha");
        container.Query.Append("INSERT INTO t (a, b) VALUES (")
            .Append(container.MakeParameterName(p0))
            .Append(", ")
            .Append(container.MakeParameterName(p1))
            .Append(')');

        using var command = container.CreateCommand(connection);

        Assert.Equal("INSERT INTO t (a, b) VALUES (?, ?)", command.CommandText);
        Assert.Equal(2, command.Parameters.Count);
        Assert.Equal(1, ((DbParameter)command.Parameters[0]!).Value);
        Assert.Equal("alpha", ((DbParameter)command.Parameters[1]!).Value);
    }

    [Fact]
    public async Task Clone_PositionalDialect_SetParameterValue_FindsParameterByOriginalName()
    {
        // Regression: confirmed live against a real Informix container ("Parameter 'p0' not
        // found."). TableGateway.BuildWhereInternal (the RetrieveOneAsync(TRowID) template
        // path) creates a parameter named "p0" via dialect.CreateDbParameter, adds it with
        // AddParameter, then later clones the cached template and calls
        // SetParameterValue("p0", id) on the clone. SqlDialect.CreateDbParameter used to force
        // parameter.ParameterName = string.Empty for any positional (!SupportsNamedParameters)
        // dialect — meant to reflect that the rendered SQL text never references the name (it's
        // always literally "?"), but SqlContainer.AddParameter treats an empty name as "no name
        // given" and silently generates a random one instead, so "p0" was never actually the
        // dictionary key anything got stored under. The fix: dialects must retain the caller's
        // original name for pengdows.crud's own internal bookkeeping (dictionary key, Clone,
        // SetParameterValue) — real positional ADO.NET providers bind by ordinal position and
        // tolerate any non-empty ParameterName being present, so nothing about the wire protocol
        // needs the name blanked.
        var factory = new fakeDbFactory(SupportedDatabase.Informix);
        var cfg = new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test;EmulatedProduct=Informix",
            ProviderName = SupportedDatabase.Informix.ToString(),
            DbMode = DbMode.Standard
        };
        await using var context = new DatabaseContext(cfg, factory);
        await using var connection = context.GetConnection(ExecutionType.Read);
        await connection.OpenAsync();

        using var template = context.CreateSqlContainer();
        template.Query.Append("SELECT * FROM t WHERE id = ").Append(template.MakeParameterName("p0"));
        var original = template.CreateDbParameter("p0", DbType.Int64, 1L);
        template.AddParameter(original);

        using var clone = (SqlContainer)template.Clone(context);
        clone.SetParameterValue("p0", 42L);

        using var command = clone.CreateCommand(connection);
        var param = Assert.Single(command.Parameters);
        Assert.Equal(42L, ((DbParameter)param!).Value);
    }

    [Fact]
    public async Task CreateCommand_NoPlaceholder_ClearsStaleParameterSequence()
    {
        await using var connection = Context.GetConnection(ExecutionType.Write);
        await connection.OpenAsync();

        using var container = Assert.IsType<SqlContainer>(Context.CreateSqlContainer("SELECT {P}id"));
        container.AddParameterWithValue("id", DbType.Int32, 1);
        using var first = container.CreateCommand(connection);
        Assert.NotEmpty(container.ParamSequence);

        container.Query.Clear().Append("SELECT 1");
        using var second = container.CreateCommand(connection);

        Assert.Empty(container.ParamSequence);
        Assert.Equal("SELECT 1", second.CommandText);
    }
}
