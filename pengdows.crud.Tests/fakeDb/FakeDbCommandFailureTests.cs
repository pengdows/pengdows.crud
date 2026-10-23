using System;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

/// <summary>
/// Session statements (SET, PRAGMA, ALTER SESSION) are executed as no-ops by fakeDb, but a failure
/// registered for one must still be raised, so tests can drive what happens when a session or
/// transaction setting is rejected.
/// </summary>
public class FakeDbCommandFailureTests
{
    [Theory]
    [InlineData("SET TRANSACTION READ ONLY")]
    [InlineData("SET SESSION tx_read_only = 1;")]
    [InlineData("PRAGMA foreign_keys = ON")]
    [InlineData("ALTER SESSION SET NLS_DATE_FORMAT = 'YYYY-MM-DD'")]
    public void SetCommandFailure_OnSessionStatement_IsRaised(string sql)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        var failure = new InvalidOperationException("rejected");
        factory.SetCommandFailure(sql, failure);

        using var connection = factory.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => command.ExecuteNonQuery()));
    }

    [Fact]
    public void EnqueueTransientNonQueryFailures_FailsTheNextNonQueriesInOrder_ThenSucceeds()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.Open();
        var first = new InvalidOperationException("first");
        var second = new InvalidOperationException("second");
        connection.EnqueueTransientNonQueryFailures(first, second);

        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO t VALUES (1)";

        Assert.Same(first, Assert.Throws<InvalidOperationException>(() => command.ExecuteNonQuery()));
        Assert.Same(second, Assert.Throws<InvalidOperationException>(() => command.ExecuteNonQuery()));
        command.ExecuteNonQuery();
    }

    [Fact]
    public void EnqueueTransientNonQueryFailures_AreNotConsumedBySessionStatements()
    {
        var factory = new fakeDbFactory(SupportedDatabase.Oracle);
        var connection = (fakeDbConnection)factory.CreateConnection();
        connection.Open();
        var failure = new InvalidOperationException("dml");
        connection.EnqueueTransientNonQueryFailures(failure);

        using var command = connection.CreateCommand();
        command.CommandText = "SET TRANSACTION READ ONLY";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO t VALUES (1)";

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() => command.ExecuteNonQuery()));
    }

    [Fact]
    public void LastTransaction_CountsCommitAndRollbackCalls()
    {
        var connection = (fakeDbConnection)new fakeDbFactory(SupportedDatabase.Firebird).CreateConnection();
        connection.Open();

        using var committed = connection.BeginTransaction();
        committed.Commit();
        Assert.Same(committed, connection.LastTransaction);
        Assert.Equal(1, connection.LastTransaction!.CommitCallCount);

        using var rolledBack = connection.BeginTransaction();
        rolledBack.Rollback();
        Assert.Same(rolledBack, connection.LastTransaction);
        Assert.Equal(0, connection.LastTransaction!.CommitCallCount);
        Assert.Equal(1, connection.LastTransaction.RollbackCallCount);
    }
}
