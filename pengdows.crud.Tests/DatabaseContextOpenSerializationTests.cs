using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DatabaseContextOpenSerializationTests
{
    [Fact]
    public void DuckDb_RequiresSerializedOpen()
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        using var context = new DatabaseContext("Data Source=test.duckdb", factory);

        Assert.True(context.RequiresSerializedOpen);
    }

    [Fact]
    public void SqlServer_DoesNotRequireSerializedOpen()
    {
        var factory = new fakeDbFactory(SupportedDatabase.SqlServer);
        using var context = new DatabaseContext("Server=localhost;Database=master;", factory);

        Assert.False(context.RequiresSerializedOpen);
    }
}