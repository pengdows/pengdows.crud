using System.Threading.Tasks;
using DuckDB.NET.Data;
using Xunit;

namespace pengdows.crud.Tests;

public class DuckDbMemoryDatabaseIsolationTests
{
    [Fact]
    public async Task TwoMemoryContexts_HaveSeparateDatabases_ByDefault()
    {
        // GIVEN two separate contexts pointing to :memory:
        // This confirms that DuckDB :memory: connections are isolated by default, 
        // matching the behavior of SQLite :memory: connections.
        var factory = DuckDBClientFactory.Instance;
        
        using var context1 = new DatabaseContext("Data Source=:memory:", factory);
        using var context2 = new DatabaseContext("Data Source=:memory:", factory);

        // WHEN we create a table in the first context
        await using (var sc1 = context1.CreateSqlContainer("CREATE TABLE test_isolation (id INTEGER)"))
        {
            await sc1.ExecuteNonQueryAsync();
        }

        // THEN the table should NOT exist in the second context
        await using (var sc2 = context2.CreateSqlContainer("SELECT count(*) FROM information_schema.tables WHERE table_name = 'test_isolation'"))
        {
            var count = await sc2.ExecuteScalarRequiredAsync<long>();
            Assert.Equal(0, count);
        }
        
        // AND we should be able to create the same table in the second context without conflict
        // (If they were shared, this would throw "Table already exists")
        await using (var sc2Create = context2.CreateSqlContainer("CREATE TABLE test_isolation (id INTEGER)"))
        {
            await sc2Create.ExecuteNonQueryAsync();
        }
    }
}
