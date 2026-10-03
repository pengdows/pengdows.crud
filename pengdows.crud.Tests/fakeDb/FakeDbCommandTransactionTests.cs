using System.Data;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

public sealed class FakeDbCommandTransactionTests
{
    [Fact]
    public void TransactionSetThroughIDbCommand_IsVisibleOnTheCommand()
    {
        using var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Sqlite };
        connection.ConnectionString = "Data Source=test;EmulatedProduct=Sqlite";
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var command = (fakeDbCommand)connection.CreateCommand();

        ((IDbCommand)command).Transaction = transaction;

        Assert.Same(transaction, command.Transaction);
    }
}
