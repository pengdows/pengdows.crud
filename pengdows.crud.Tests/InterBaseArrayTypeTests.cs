using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.configuration;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Moq;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// InterBase ARRAY columns (confirmed live, InterBase 15, InterBaseSql.Data.InterBaseClient 10.0.3):
/// IBParameter infers its type from the value and has no array mapping ("Unknown type:
/// System.Int32[]") unless IBDbType is set to Array before the value. Reads report System.Array and
/// return the declared bounds, so INTEGER [1:5] comes back as a non-zero-based Int32[*].
/// </summary>
[Collection("AllocationSerial")]
public sealed class InterBaseArrayTypeTests
{
    [Table("arrays")]
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

    private static Array OneBased<T>(params T[] values)
    {
        var array = Array.CreateInstance(typeof(T), new[] { values.Length }, new[] { 1 });
        Array.Copy(values, 0, array, 1, values.Length);
        return array;
    }

    private static DatabaseContext Context(fakeDbFactory factory, params Dictionary<string, object?>[] rows)
    {
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.InterBase });
        var exec = new fakeDbConnection { EmulatedProduct = SupportedDatabase.InterBase };
        exec.EnqueueReaderResult(new fakeDbDataReader(rows.Select(r => r.ToDictionary(p => p.Key, p => p.Value!)))
        {
            ReportedFieldTypes = new Dictionary<string, Type> { ["nums"] = typeof(Array), ["names"] = typeof(Array) }
        });
        factory.Connections.Add(exec);
        return new DatabaseContext(new DatabaseContextConfiguration
        {
            ConnectionString = "DataSource=x;Database=/x.ib;EmulatedProduct=InterBase",
            DbMode = DbMode.Standard
        }, factory);
    }

    [Fact]
    public void BuildCreate_Arrays_BindAsInterBaseArrays()
    {
        var factory = new fakeDbFactory(SupportedDatabase.InterBase) { EmulatesInterBaseParameterMetadata = true };
        var context = Context(factory);

        var sc = new TableGateway<Row, int>(context).BuildCreate(new Row { Id = 1, Nums = new[] { 1, -2, 3 }, Names = new[] { "a", "bb" } });

        Assert.Equal(new[] { 1, -2, 3 }, sc.GetParameterValue("i1"));
        Assert.Equal(new[] { "a", "bb" }, sc.GetParameterValue("i2"));
    }

    [Fact]
    public void CreateDbParameter_ByteArray_StaysBinary()
    {
        var factory = new fakeDbFactory(SupportedDatabase.InterBase) { EmulatesInterBaseParameterMetadata = true };
        var dialect = Context(factory).Dialect;

        var parameter = (fakeDbInterBaseParameter)dialect.CreateDbParameter("p", DbType.Binary, new byte[] { 1, 2 });

        Assert.Equal(DbType.Binary, parameter.DbType);
        Assert.NotEqual(fakeIBDbType.Array, parameter.IBDbType);
    }

    [Fact]
    public async Task RetrieveOneAsync_NonZeroBasedArrays_ReadAsArrays()
    {
        var context = Context(new fakeDbFactory(SupportedDatabase.InterBase),
            new Dictionary<string, object?> { ["id"] = 1, ["nums"] = OneBased(1, -2, 3, 4, 5), ["names"] = new[] { "a", "bb", "ccc" } });

        var row = await new TableGateway<Row, int>(context).RetrieveOneAsync(1);

        Assert.Equal(new[] { 1, -2, 3, 4, 5 }, row!.Nums);
        Assert.Equal(new[] { "a", "bb", "ccc" }, row.Names);
    }

    [Fact]
    public async Task DataReaderMapper_NonZeroBasedArrays_ReadAsArrays()
    {
        var context = Context(new fakeDbFactory(SupportedDatabase.InterBase),
            new Dictionary<string, object?> { ["nums"] = OneBased(1, -2, 3), ["names"] = OneBased("x", "y") });
        await using var sc = context.CreateSqlContainer("SELECT 1");
        await using var reader = await sc.ExecuteReaderAsync();

        var row = Assert.Single(await DataReaderMapper.LoadAsync<Mapped>(reader, new MapperOptions(Strict: true)));

        Assert.Equal(new[] { 1, -2, 3 }, row.Nums);
        Assert.Equal(new[] { "x", "y" }, row.Names);
    }

    [Table("arrays")]
    public sealed class NumsOnly
    {
        [Id] [Column("id", DbType.Int32)] public int Id { get; set; }
        [Column("nums", DbType.Object)] public int[]? Nums { get; set; }
    }

    private static long AllocatedPerRow(Func<int, object> nums) =>
        AllocationMeasurement.Lowest(() => AllocatedPerRowOnce(nums));

    private static long AllocatedPerRowOnce(Func<int, object> nums)
    {
        var rows = Enumerable.Range(0, 400)
            .Select(i => new Dictionary<string, object> { ["id"] = i, ["nums"] = nums(i) }).ToList();
        var context = new DatabaseContext("DataSource=x;Database=/x.ib;EmulatedProduct=InterBase", new fakeDbFactory(SupportedDatabase.InterBase));
        var gateway = new TableGateway<NumsOnly, int>(context);
        TrackedReader Open() => new(new fakeDbDataReader(rows)
            {
                ReportedFieldTypes = new Dictionary<string, Type> { ["nums"] = typeof(Array) }
            }, new Mock<ITrackedConnection>().Object, Mock.Of<IAsyncDisposable>(), false);

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

    // A non-zero-based array must be copied into a zero-based one (an int[5] is 48 B); nothing else
    // should be allocated per row. The general sequence coercion it fell back to reflected per row.
    [Fact]
    public void NonZeroBasedArray_AllocatesOnlyTheCopy()
    {
        var oneBased = AllocatedPerRow(i => OneBased(i, 2, 3, 4, 5));
        var zeroBased = AllocatedPerRow(i => new[] { i, 2, 3, 4, 5 });

        Assert.True(oneBased <= zeroBased + 48, $"one-based {oneBased} B/row, zero-based {zeroBased} B/row");
    }
}
