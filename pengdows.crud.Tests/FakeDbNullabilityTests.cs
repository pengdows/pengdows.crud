using System.Threading.Tasks;
using pengdows.crud.FakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class FakeDbNullabilityTests
{
    [Fact]
    public void FakeDbParameter_PropertiesAllowNull()
    {
        var p = new FakeDbParameter
        {
            ParameterName = null,
            SourceColumn = null,
            Value = null
        };

        Assert.Null(p.ParameterName);
        Assert.Null(p.SourceColumn);
        Assert.Null(p.Value);

        p.ParameterName = "p1";
        p.SourceColumn = "c1";
        p.Value = 5;

        Assert.Equal("p1", p.ParameterName);
        Assert.Equal("c1", p.SourceColumn);
        Assert.Equal(5, p.Value);
    }

    [Fact]
    public async Task FakeDbCommand_CommandTextAllowsNullAndExecuteScalarAsync()
    {
        var cmd = new FakeDbCommand();
        cmd.CommandText = null;
        Assert.Null(cmd.CommandText);
        cmd.CommandText = "SELECT 1";
        Assert.Equal("SELECT 1", cmd.CommandText);

        var conn = new FakeDbConnection();
        var cmdWithConn = new FakeDbCommand(conn);

        var defaultResult = await cmdWithConn.ExecuteScalarAsync(default);
        Assert.Equal(42, defaultResult);

        conn.ScalarResults.Enqueue(7);
        var queuedResult = await cmdWithConn.ExecuteScalarAsync(default);
        Assert.Equal(7, queuedResult);
    }
}
