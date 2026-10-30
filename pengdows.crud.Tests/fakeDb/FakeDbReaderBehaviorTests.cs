using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    [Fact]
    public async Task GetFieldValueAsync_returns_exact_provider_values_without_convert_change_type()
    {
        var bytes = new byte[] { 1, 2, 3 };
        using var reader = new fakeDbDataReader(
            [new Dictionary<string, object> { ["Value"] = bytes }]);

        Assert.True(await reader.ReadAsync());

        Assert.Same(bytes, await reader.GetFieldValueAsync<byte[]>(0, CancellationToken.None));
    }

    [Fact]
    public async Task GetFieldValueAsync_honors_an_already_canceled_token()
    {
        using var reader = new fakeDbDataReader(
            [new Dictionary<string, object> { ["Value"] = "value" }]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => reader.GetFieldValueAsync<string>(0, cancellation.Token));
    }

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
