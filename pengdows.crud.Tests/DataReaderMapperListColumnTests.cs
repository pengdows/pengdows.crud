using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// TYPE-002, found live on DuckDB: DuckDB.NET reads a LIST column as List&lt;T&gt; (List&lt;T?&gt;
/// for nullable elements). The gateway converted it to an array property, but DataReaderMapper
/// assigned null.
/// </summary>
public sealed class DataReaderMapperListColumnTests
{
    private sealed class Mapped
    {
        public int[]? Ints { get; set; }
        public string[]? Texts { get; set; }
        public List<int>? IntList { get; set; }
    }

    public static TheoryData<object, object, object> Lists() => new()
    {
        { new List<int> { 1, -2, 3 }, new List<string> { "a", "b" }, new List<int> { 4 } },
        { new List<int?> { 1, -2, 3 }, new List<string?> { "a", "b" }, new List<int?> { 4 } },
    };

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task LoadAsync_ListColumns_MapToArrayAndListProperties(object ints, object texts, object intList)
    {
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.DuckDB });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.DuckDB };
        exec.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["Ints"] = ints, ["Texts"] = texts, ["IntList"] = intList }
        });
        factory.Connections.Add(exec);
        await using var context = new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Data Source=test.duckdb;EmulatedProduct=DuckDB",
            DbMode = DbMode.Standard
        }, factory);
        await using var sc = context.CreateSqlContainer("SELECT 1");
        await using var reader = await sc.ExecuteReaderAsync();

        var row = Assert.Single(await DataReaderMapper.LoadAsync<Mapped>(reader, new MapperOptions(Strict: true)));

        Assert.Equal(new[] { 1, -2, 3 }, row.Ints);
        Assert.Equal(new[] { "a", "b" }, row.Texts);
        Assert.Equal(new List<int> { 4 }, row.IntList);
    }
}
