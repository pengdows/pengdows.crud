using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.exceptions;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Informix LIST/SET/MULTISET (confirmed live, Informix 15, Informix.Net.Core): a text parameter in
/// collection-literal form is converted to the column ('LIST{1,2}' into a SET or MULTISET column too),
/// and values read back as that text, numbers space-padded ("LIST{1          ,-2         }") and strings
/// single-quoted with '' escapes ("SET{'it''s','a,b'}"). Array properties are written as LIST literals
/// and read by parsing them.
/// </summary>
public sealed class InformixCollectionTypeTests
{
    [Table("colls")]
    public sealed class Row
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("nums", DbType.Object)] public int[]? Nums { get; set; }
        [Column("names", DbType.Object)] public string[]? Names { get; set; }
    }

    public sealed class Mapped
    {
        public int[]? Nums { get; set; }
        public string[]? Names { get; set; }
    }

    private static DatabaseContext Context(params Dictionary<string, object?>[] rows)
    {
        var factory = new fakeDbFactory(SupportedDatabase.Informix);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.Informix });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.Informix };
        exec.EnqueueReaderResult(rows);
        factory.Connections.Add(exec);
        return new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "Server=x;Database=d;EmulatedProduct=Informix",
            DbMode = DbMode.Standard
        }, factory);
    }

    [Fact]
    public void BuildCreate_Arrays_BindAsListLiterals()
    {
        var sc = new TableGateway<Row, int>(Context()).BuildCreate(new Row
        {
            Id = 1,
            Nums = new[] { 1, -2, int.MaxValue },
            Names = new[] { "it's", "a,b", "x\"y", "" }
        });

        Assert.Equal("LIST{1,-2,2147483647}", sc.GetParameterValue("i1"));
        Assert.Equal("LIST{'it''s','a,b','x\"y',''}", sc.GetParameterValue("i2"));
    }

    [Fact]
    public async Task RetrieveOneAsync_CollectionLiterals_ReadAsArrays()
    {
        var context = Context(new Dictionary<string, object?>
        {
            ["id"] = 1,
            ["nums"] = "MULTISET{4          ,-4         ,5          }",
            ["names"] = "SET{'it''s','a,b','x\"y',' sp ',''}"
        });

        var row = await new TableGateway<Row, int>(context).RetrieveOneAsync(1);

        Assert.Equal(new[] { 4, -4, 5 }, row!.Nums);
        Assert.Equal(new[] { "it's", "a,b", "x\"y", " sp ", "" }, row.Names);
    }

    [Fact]
    public async Task RetrieveOneAsync_EmptyList_ReadsAnEmptyArray()
    {
        var context = Context(new Dictionary<string, object?> { ["id"] = 1, ["nums"] = "LIST{}", ["names"] = "LIST{}" });

        var row = await new TableGateway<Row, int>(context).RetrieveOneAsync(1);

        Assert.Empty(row!.Nums!);
        Assert.Empty(row.Names!);
    }

    [Fact]
    public async Task DataReaderMapper_CollectionLiterals_ReadAsArrays()
    {
        var context = Context(new Dictionary<string, object?> { ["nums"] = "LIST{1          ,2          }", ["names"] = "LIST{'a'}" });
        await using var sc = context.CreateSqlContainer("SELECT 1");
        await using var reader = await sc.ExecuteReaderAsync();

        var row = Assert.Single(await DataReaderMapper.LoadAsync<Mapped>(reader, new MapperOptions(Strict: true)));

        Assert.Equal(new[] { 1, 2 }, row.Nums);
        Assert.Equal(new[] { "a" }, row.Names);
    }

    [Theory]
    [InlineData("LIST{'x'}")]
    [InlineData("LIST{1,2")]
    [InlineData("not a collection")]
    public async Task RetrieveOneAsync_UnreadableCollection_FailsLoudly(string text)
    {
        var context = Context(new Dictionary<string, object?> { ["id"] = 1, ["nums"] = text, ["names"] = null });

        await Assert.ThrowsAsync<DataMappingException>(() => new TableGateway<Row, int>(context).RetrieveOneAsync(1).AsTask());
    }

    // Numbers parse from the literal's text without a string per element: only the result array
    // (an int[5] is 48 B) and the entity are allocated per row, as for a provider that returns int[].
    [Fact]
    public void ListLiteral_AllocatesOnlyTheResultArray()
    {
        var literal = AllocatedPerRow(_ => "LIST{1          ,2          ,3          ,4          ,5          }");
        var native = AllocatedPerRow(_ => new[] { 1, 2, 3, 4, 5 });

        Assert.True(literal <= native + 48, $"literal {literal} B/row, int[] {native} B/row");
    }

    [Table("colls")]
    public sealed class NumsOnly
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("nums", DbType.Object)] public int[]? Nums { get; set; }
    }

    private static long AllocatedPerRow(Func<int, object> nums) =>
        AllocationMeasurement.Lowest(() => AllocatedPerRowOnce(nums));

    private static long AllocatedPerRowOnce(Func<int, object> nums)
    {
        var rows = Enumerable.Range(0, 400).Select(i => new Dictionary<string, object> { ["id"] = i, ["nums"] = nums(i) }).ToList();
        var gateway = new TableGateway<NumsOnly, int>(new DatabaseContext("Server=x;Database=d;EmulatedProduct=Informix",
            new fakeDbFactory(SupportedDatabase.Informix)));
        TrackedReader Open() => new(new fakeDbDataReader(rows), new Mock<ITrackedConnection>().Object, Mock.Of<IAsyncDisposable>(), false);

        var warm = Open();
        while (warm.Read())
        {
            gateway.MapReaderToObject(warm);
        }

        var reader = Open();
        reader.Read();
        gateway.MapReaderToObject(reader);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var count = 0;
        while (reader.Read())
        {
            gateway.MapReaderToObject(reader);
            count++;
        }

        return (GC.GetAllocatedBytesForCurrentThread() - before) / count;
    }
}
