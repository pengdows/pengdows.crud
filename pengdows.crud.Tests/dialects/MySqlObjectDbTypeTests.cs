using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// MySqlConnector 2.5 and later map DbType.Object to MySqlDbType.Vector (2.4 mapped it to Null). A NULL sent
/// that way to a prepared statement fails on MySQL 8.4 with "Incorrect arguments to mysqld_stmt_execute",
/// whatever the column (confirmed live, MySQL 8.4.11, MySqlConnector 2.6.2; a non-null Object value and an
/// unprepared NULL are fine): no nullable spatial column, declared DbType.Object, could be set to NULL. The
/// MySQL family resets such a parameter's DbType just before execution, so the driver infers it from the value.
/// </summary>
public sealed class MySqlObjectDbTypeTests
{
    private static SqlDialect Dialect(SupportedDatabase database) =>
        (SqlDialect)SqlDialectFactory.CreateDialectForType(database, new fakeDbFactory(database),
            NullLogger<SqlDialect>.Instance);

    private static fakeDbCommand CommandWith(object? value, DbType dbType)
    {
        var command = new fakeDbCommand();
        command.Parameters.Add(new fakeDbParameter { ParameterName = "p", DbType = dbType, Value = value ?? DBNull.Value });
        return command;
    }

    [Theory]
    [InlineData(SupportedDatabase.MySql)]
    [InlineData(SupportedDatabase.MariaDb)]
    [InlineData(SupportedDatabase.AuroraMySql)]
    [InlineData(SupportedDatabase.TiDb)]
    [InlineData(SupportedDatabase.SingleStore)]
    public void MySqlFamily_NullObjectParameter_IsLeftForTheDriverToType(SupportedDatabase database)
    {
        var command = CommandWith(null, DbType.Object);

        Dialect(database).PrepareCommandParameters(command);

        Assert.Equal(new fakeDbParameter().DbType, command.Parameters[0].DbType);
    }

    [Fact]
    public void MySql_NonNullObjectParameter_KeepsItsType()
    {
        var command = CommandWith("x", DbType.Object);

        Dialect(SupportedDatabase.MySql).PrepareCommandParameters(command);

        Assert.Equal(DbType.Object, command.Parameters[0].DbType);
    }

    [Fact]
    public void MySql_NullParameterOfAnotherType_KeepsItsType()
    {
        var command = CommandWith(null, DbType.Int32);

        Dialect(SupportedDatabase.MySql).PrepareCommandParameters(command);

        Assert.Equal(DbType.Int32, command.Parameters[0].DbType);
    }

    [Theory]
    [InlineData(SupportedDatabase.PostgreSql)]
    [InlineData(SupportedDatabase.SqlServer)]
    public void OtherDialects_LeaveANullObjectParameterAlone(SupportedDatabase database)
    {
        var command = CommandWith(null, DbType.Object);

        Dialect(database).PrepareCommandParameters(command);

        Assert.Equal(DbType.Object, command.Parameters[0].DbType);
    }

    // A reused statement gets its NULL through SetParameterValue, after the parameter was created with a value.
    [Fact]
    public async Task MySql_ExecutedCommand_ResetsANullSetOnAnObjectParameter()
    {
        var factory = new fakeDbFactory(SupportedDatabase.MySql);
        await using var context = new DatabaseContext("Data Source=test;EmulatedProduct=MySql", factory);
        await using var container = context.CreateSqlContainer("INSERT INTO t (g) VALUES (@g)");
        var parameter = container.AddParameterWithValue<object?>("g", DbType.Object, "placeholder");
        container.SetParameterValue("g", null);

        await container.ExecuteNonQueryAsync();

        Assert.NotEqual(DbType.Object, parameter.DbType);
    }
}
