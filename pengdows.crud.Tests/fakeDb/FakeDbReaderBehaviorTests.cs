using System.Data;
using System.Linq;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.fakeDb;

/// <summary>
/// fakeDb records the <see cref="CommandBehavior"/> each reader was opened with, so tests can assert
/// how a library asked the provider to manage the connection.
/// </summary>
public class FakeDbReaderBehaviorTests
{
    [Theory]
    [InlineData(CommandBehavior.Default)]
    [InlineData(CommandBehavior.CloseConnection)]
    [InlineData(CommandBehavior.CloseConnection | CommandBehavior.SingleRow)]
    public void ExecuteReader_RecordsTheBehavior(CommandBehavior behavior)
    {
        using var connection = new fakeDbConnection { ConnectionString = "Data Source=file.db;EmulatedProduct=Sqlite" };
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";

        using (command.ExecuteReader(behavior))
        {
        }

        Assert.Equal(behavior, connection.ExecutedReaderBehaviors.Single());
    }
}
