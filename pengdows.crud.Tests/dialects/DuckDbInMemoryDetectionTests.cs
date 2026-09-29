using DuckDB.NET.Data;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.dialects;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests.dialects;

/// <summary>
/// DuckDB.NET's own in-memory connection string is <c>DataSource=:memory:</c>
/// (<c>DuckDBConnectionStringBuilder.InMemoryConnectionString</c>), and its builder rewrites every
/// data-source alias, including <c>Data Source</c>, to <c>DataSource</c>. Detection matched only
/// <c>data source=:memory:</c>, so a real in-memory DuckDB context resolved to SingleWriter: every
/// connection opened its own private in-memory database and tables vanished between operations
/// (found while testing TYPE-001 against real DuckDB.NET 1.5.6).
/// </summary>
public sealed class DuckDbInMemoryDetectionTests
{
    private static DuckDbDialect CreateDialect() =>
        new(new fakeDbFactory(SupportedDatabase.DuckDB), NullLogger<DuckDbDialect>.Instance);

    [Theory]
    [InlineData("DataSource=:memory:", InMemoryKind.Isolated)]
    [InlineData("Data Source=:memory:", InMemoryKind.Isolated)]
    [InlineData("datasource = :memory:", InMemoryKind.Isolated)]
    [InlineData("DataSource=:memory:?cache=shared", InMemoryKind.Shared)]
    [InlineData("Data Source=:memory:?cache=shared", InMemoryKind.Shared)]
    [InlineData("DataSource=analytics.duckdb", InMemoryKind.None)]
    [InlineData("Data Source=analytics.duckdb", InMemoryKind.None)]
    public void DetectInMemoryKind_RecognizesEveryDataSourceSpelling(string connectionString, InMemoryKind expected)
    {
        Assert.Equal(expected, CreateDialect().DetectInMemoryKind(connectionString));
    }

    [Theory]
    [InlineData("DataSource=:memory:")]
    [InlineData("Data Source=:memory:")]
    public async System.Threading.Tasks.Task RealDuckDbInMemoryContext_UsesOneConnection_SoTablesPersist(string connectionString)
    {
        await using var context = new DatabaseContext(connectionString, DuckDBClientFactory.Instance);

        Assert.Equal(DbMode.SingleConnection, context.ConnectionMode);
        await using (var ddl = context.CreateSqlContainer("CREATE TABLE kept (id INTEGER)"))
        {
            await ddl.ExecuteNonQueryAsync();
        }

        await using var count = context.CreateSqlContainer("SELECT COUNT(*) FROM kept");
        Assert.Equal(0L, await count.ExecuteScalarRequiredAsync<long>());
    }
}
