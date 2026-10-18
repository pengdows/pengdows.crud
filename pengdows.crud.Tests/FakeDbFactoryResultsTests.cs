using System.Data.Common;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// F1: canned non-query and scalar results set on the factory reach every connection it creates, so a test that cannot reach
/// the connection a context opens (it opens its own) can still script what the database answers.
/// </summary>
public sealed class FakeDbFactoryResultsTests
{
    private static DbCommand Command(fakeDbFactory factory, string sql)
    {
        var connection = factory.CreateConnection();
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    [Fact]
    public void EnqueueNonQueryResult_AnswersTheNextNonQueryOnAnyConnection_InOrder()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.EnqueueNonQueryResult(7);
        factory.EnqueueNonQueryResult(0);

        Assert.Equal(7, Command(factory, "UPDATE t SET a = 1").ExecuteNonQuery());
        Assert.Equal(0, Command(factory, "UPDATE t SET a = 2").ExecuteNonQuery());
        Assert.Equal(1, Command(factory, "UPDATE t SET a = 3").ExecuteNonQuery());
    }

    [Fact]
    public void SetNonQueryResultForCommand_AnswersThatTextEveryTime()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetNonQueryResultForCommand("DELETE FROM t", 42);

        Assert.Equal(42, Command(factory, "DELETE FROM t").ExecuteNonQuery());
        Assert.Equal(42, Command(factory, "DELETE FROM t").ExecuteNonQuery());
        Assert.Equal(1, Command(factory, "DELETE FROM u").ExecuteNonQuery());
    }

    [Fact]
    public void SetDefaultNonQueryResult_AnswersWhenNothingElseDoes_AndQueuedResultsWin()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetDefaultNonQueryResult(0);
        factory.EnqueueNonQueryResult(5);

        Assert.Equal(5, Command(factory, "UPDATE t SET a = 1").ExecuteNonQuery());
        Assert.Equal(0, Command(factory, "UPDATE t SET a = 2").ExecuteNonQuery());
    }

    [Fact]
    public void ConnectionQueues_TakePrecedenceOverTheFactory()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.EnqueueNonQueryResult(9);
        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.EnqueueNonQueryResult(3);
        connection.Open();
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE t SET a = 1";

        Assert.Equal(3, command.ExecuteNonQuery());
        Assert.Equal(9, command.ExecuteNonQuery());
    }

    [Fact]
    public void EnqueueScalarResult_AnswersTheNextScalarOnAnyConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.EnqueueScalarResult(123L);

        Assert.Equal(123L, Command(factory, "SELECT max(id) FROM t").ExecuteScalar());
    }

    [Fact]
    public void SetScalarResultForCommand_AnswersThatTextOnAnyConnection()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        factory.SetScalarResultForCommand("SELECT count(*) FROM t", 8);

        Assert.Equal(8, Command(factory, "SELECT count(*) FROM t").ExecuteScalar());
        Assert.Equal(8, Command(factory, "SELECT count(*) FROM t").ExecuteScalar());
    }

    [Fact]
    public async System.Threading.Tasks.Task FactoryResults_AreSharedAcrossAWholeDatabaseContext()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql);
        await using var context = new DatabaseContext("Host=x;Database=y;EmulatedProduct=PostgreSql", factory);
        factory.SetNonQueryResultForCommand("UPDATE t SET a = 1", 11);

        await using var sc = context.CreateSqlContainer("UPDATE t SET a = 1");

        Assert.Equal(11, await sc.ExecuteNonQueryAsync());
    }

    [Fact]
    public void ConnectionInitializer_ConfiguresEveryConnectionTheFactoryCreates_HoweverManyThereAre()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql)
        {
            ConnectionInitializer = connection =>
            {
                connection.EnqueueNonQueryResult(5);
                connection.ScalarResultsByCommand["SELECT custom()"] = "configured";
            }
        };

        for (var i = 0; i < 25; i++)
        {
            var connection = factory.CreateConnection();
            connection.Open();
            var update = connection.CreateCommand();
            update.CommandText = "UPDATE t SET a = 1";
            var select = connection.CreateCommand();
            select.CommandText = "SELECT custom()";

            Assert.Equal(5, update.ExecuteNonQuery());
            Assert.Equal("configured", select.ExecuteScalar());
        }
    }

    [Fact]
    public void ConnectionInitializer_DoesNotTouchConnectionsTheTestStagedItself()
    {
        var factory = new fakeDbFactory(SupportedDatabase.PostgreSql) { ConnectionInitializer = c => c.EnqueueNonQueryResult(5) };
        var staged = new fakeDbConnection();
        staged.EnqueueNonQueryResult(8);
        factory.Connections.Add(staged);

        var connection = factory.CreateConnection();
        connection.Open();
        var update = connection.CreateCommand();
        update.CommandText = "UPDATE t SET a = 1";

        Assert.Same(staged, connection);
        Assert.Equal(8, update.ExecuteNonQuery());
        Assert.Equal(1, update.ExecuteNonQuery());
    }
}
