using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// fakeDb records, in order, each provider-contract member that was called on its connection, command and transaction
/// (<c>Operations</c>), so a test of a wrapper can assert what it delegated and by which route (a sync member is a different
/// entry from its async twin) without a mock. It also overrides the async members the base classes silently route through the
/// sync ones (BeginDbTransactionAsync, DisposeAsync), which is what a real provider with native async does.
/// </summary>
public class FakeDbOperationLogTests
{
    private static fakeDbConnection OpenConnection()
    {
        var connection = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.Sqlite,
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite"
        };
        connection.Open();
        connection.ClearOperations();
        return connection;
    }

    [Fact]
    public async Task Connection_RecordsEveryContractMemberInOrder()
    {
        var connection = new fakeDbConnection
        {
            EmulatedProduct = SupportedDatabase.Sqlite,
            ConnectionString = "Data Source=:memory:;EmulatedProduct=Sqlite"
        };

        connection.Open();
        await connection.OpenAsync();
        Assert.Throws<NotSupportedException>(() => connection.ChangeDatabase("other"));
        connection.BeginTransaction(IsolationLevel.ReadCommitted).Dispose();
        await using (await connection.BeginTransactionAsync(IsolationLevel.Serializable))
        {
        }

        connection.CreateCommand().Dispose();
        connection.Close();
        await connection.CloseAsync();
        connection.Dispose();
        await connection.DisposeAsync();

        Assert.Equal(
            new[]
            {
                "Open", "OpenAsync", "ChangeDatabase(other)", "BeginTransaction(ReadCommitted)",
                "BeginTransactionAsync(Serializable)", "CreateCommand", "Close", "CloseAsync", "Dispose", "DisposeAsync"
            },
            connection.Operations.Where(o => !o.StartsWith("Transaction", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public async Task BeginTransactionAsync_IsItsOwnEntry_NotTheSyncOne()
    {
        var connection = OpenConnection();

        await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted);

        Assert.Contains("BeginTransactionAsync(ReadCommitted)", connection.Operations);
        Assert.DoesNotContain("BeginTransaction(ReadCommitted)", connection.Operations);
    }

    [Fact]
    public void ClearOperations_EmptiesTheLog()
    {
        var connection = OpenConnection();
        connection.Close();
        Assert.NotEmpty(connection.Operations);

        connection.ClearOperations();

        Assert.Empty(connection.Operations);
    }

    [Fact]
    public async Task Command_RecordsEveryContractMemberAndTheRouteTaken()
    {
        var connection = OpenConnection();
        var recorded = (fakeDbCommand)connection.CreateCommand();
        DbCommand command = recorded; // the DbCommand contract, with its default CancellationToken arguments
        command.CommandText = "SELECT 1";

        command.Cancel();
        command.Prepare();
        await command.PrepareAsync();
        command.ExecuteNonQuery();
        await command.ExecuteNonQueryAsync();
        command.ExecuteScalar();
        await command.ExecuteScalarAsync();
        command.ExecuteReader(CommandBehavior.SchemaOnly).Dispose();
        (await command.ExecuteReaderAsync(CommandBehavior.Default)).Dispose();
        command.CreateParameter();
        command.Dispose();
        await command.DisposeAsync();

        Assert.Equal(
            new[]
            {
                "Cancel", "Prepare", "PrepareAsync", "ExecuteNonQuery", "ExecuteNonQueryAsync", "ExecuteScalar",
                "ExecuteScalarAsync", "ExecuteReader(SchemaOnly)", "ExecuteReaderAsync(Default)", "CreateParameter", "Dispose",
                "DisposeAsync"
            },
            recorded.Operations.ToArray());
    }

    [Fact]
    public async Task Transaction_RecordsEveryContractMemberAndTheRouteTaken()
    {
        var connection = OpenConnection();
        var transaction = (fakeDbTransaction)connection.BeginTransaction();

        transaction.Commit();
        transaction.Rollback();
        await transaction.CommitAsync();
        await transaction.RollbackAsync();
        transaction.Dispose();
        await transaction.DisposeAsync();

        Assert.Equal(
            new[] { "Commit", "Rollback", "CommitAsync", "RollbackAsync", "Dispose", "DisposeAsync" },
            transaction.Operations.ToArray());
    }

    [Fact]
    public void OperationsAreIndependentPerObject()
    {
        var connection = OpenConnection();
        var first = (fakeDbCommand)connection.CreateCommand();
        var second = (fakeDbCommand)connection.CreateCommand();

        first.Cancel();

        Assert.Equal(new[] { "Cancel" }, first.Operations.ToArray());
        Assert.Empty(second.Operations);
    }
}
