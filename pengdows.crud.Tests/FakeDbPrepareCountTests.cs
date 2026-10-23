using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>fakeDbCommand counts Prepare and PrepareAsync calls, so tests can see whether a command was prepared.</summary>
public sealed class FakeDbPrepareCountTests
{
    [Fact]
    public async Task PrepareCount_CountsEveryPrepareCall()
    {
        using var connection = new fakeDbConnection { EmulatedProduct = SupportedDatabase.MySql };
        connection.Open(); // a closed connection refuses Prepare, as every provider does
        using var command = (fakeDbCommand)connection.CreateCommand();
        Assert.Equal(0, command.PrepareCount);

        command.Prepare();
        await command.PrepareAsync(default);

        Assert.Equal(2, command.PrepareCount);
    }
}
