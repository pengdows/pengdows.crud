using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using testbed;
using Xunit;
namespace pengdows.crud.Tests;
public class TestTableSchemaTests
{
    [Fact]
    public async Task IdColumn_IsPrimaryKey()
    {
        var cs = "Data Source=" + Path.GetTempFileName();
        await using var db = new DatabaseContext(cs, SqliteFactory.Instance, null);
        var services = new ServiceCollection();
        services.AddScoped<IAuditValueResolver, StringAuditContextProvider>();
        var sp = services.BuildServiceProvider();
        var provider = new TestProvider(db, sp);
        await provider.CreateTable();

        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('test_table');";
        var columns = new Dictionary<string, long>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1);
            var pk = reader.GetInt64(5);
            columns[name] = pk;
        }

        Assert.Equal(1, columns["id"]);
    }

    [Fact]
    public async Task NameColumn_IsNotPrimaryKey()
    {
        var cs = "Data Source=" + Path.GetTempFileName();
        await using var db = new DatabaseContext(cs, SqliteFactory.Instance, null);
        var services = new ServiceCollection();
        services.AddScoped<IAuditValueResolver, StringAuditContextProvider>();
        var sp = services.BuildServiceProvider();
        var provider = new TestProvider(db, sp);
        await provider.CreateTable();

        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info('test_table');";
        await using var reader = await cmd.ExecuteReaderAsync();
        long pkValue = -1;
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1);
            if (name == "name")
            {
                pkValue = reader.GetInt64(5);
            }
        }

        Assert.Equal(0, pkValue);
    }
}
