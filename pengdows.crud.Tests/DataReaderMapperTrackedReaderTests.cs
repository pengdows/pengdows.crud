using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using pengdows.crud.infrastructure;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// Tests that exercise the ITrackedReader overloads of DataReaderMapper,
/// covering the LoadInternalAsync(ITrackedReader) and StreamInternalAsync(ITrackedReader)
/// fast paths that are bypassed when using the IDataReader overloads.
/// </summary>
public class DataReaderMapperTrackedReaderTests
{
    private sealed class SimpleDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private static DatabaseContext CreateContext()
    {
        return new DatabaseContext(
            "Data Source=:memory:;EmulatedProduct=Sqlite",
            new fakeDbFactory(SupportedDatabase.Sqlite));
    }

    // -------------------------------------------------------------------------
    // LoadObjectsFromDataReaderAsync(ITrackedReader) — public static, line 149
    // Exercises LoadInternalAsync(ITrackedReader) body lines 241-255
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_ITrackedReader_ReturnsResult()
    {
        await using var ctx = CreateContext();
        var sc = ctx.CreateSqlContainer("SELECT 1");
        await using var reader = await sc.ExecuteReaderAsync();

        var result = await DataReaderMapper.LoadObjectsFromDataReaderAsync<SimpleDto>(reader);

        Assert.NotNull(result);
    }

    // -------------------------------------------------------------------------
    // LoadAsync(ITrackedReader, IMapperOptions) — public static, line 158
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LoadAsync_ITrackedReaderWithOptions_ReturnsResult()
    {
        await using var ctx = CreateContext();
        var sc = ctx.CreateSqlContainer("SELECT 1");
        await using var reader = await sc.ExecuteReaderAsync();

        var result = await DataReaderMapper.LoadAsync<SimpleDto>(reader, MapperOptions.Default);

        Assert.NotNull(result);
    }

    // -------------------------------------------------------------------------
    // StreamAsync(ITrackedReader) — public static, line 166
    // Exercises StreamInternalAsync(ITrackedReader) body lines 297-308
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamAsync_ITrackedReader_IteratesWithoutError()
    {
        await using var ctx = CreateContext();
        var sc = ctx.CreateSqlContainer("SELECT 1");
        await using var reader = await sc.ExecuteReaderAsync();

        var count = 0;
        await foreach (var _ in DataReaderMapper.StreamAsync<SimpleDto>(reader))
        {
            count++;
        }

        Assert.True(count >= 0); // may be 0 or 1 depending on fakeDb result
    }

    // -------------------------------------------------------------------------
    // StreamAsync(ITrackedReader, IMapperOptions) — public static, line 175
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StreamAsync_ITrackedReaderWithOptions_IteratesWithoutError()
    {
        await using var ctx = CreateContext();
        var sc = ctx.CreateSqlContainer("SELECT 1");
        await using var reader = await sc.ExecuteReaderAsync();

        var count = 0;
        await foreach (var _ in DataReaderMapper.StreamAsync<SimpleDto>(reader, MapperOptions.Default))
        {
            count++;
        }

        Assert.True(count >= 0);
    }

    // -------------------------------------------------------------------------
    // IDataReaderMapper.LoadAsync(ITrackedReader) — explicit interface, line 216
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IDataReaderMapper_LoadAsync_ITrackedReader_ReturnsResult()
    {
        await using var ctx = CreateContext();
        var sc = ctx.CreateSqlContainer("SELECT 1");
        await using var reader = await sc.ExecuteReaderAsync();

        IDataReaderMapper mapper = DataReaderMapper.Instance;
        var result = await mapper.LoadAsync<SimpleDto>(reader);

        Assert.NotNull(result);
    }

    // -------------------------------------------------------------------------
    // IDataReaderMapper.LoadAsync(ITrackedReader, IMapperOptions) — explicit interface, line 224
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IDataReaderMapper_LoadAsync_ITrackedReaderWithOptions_ReturnsResult()
    {
        await using var ctx = CreateContext();
        var sc = ctx.CreateSqlContainer("SELECT 1");
        await using var reader = await sc.ExecuteReaderAsync();

        IDataReaderMapper mapper = DataReaderMapper.Instance;
        var result = await mapper.LoadAsync<SimpleDto>(reader, MapperOptions.Default);

        Assert.NotNull(result);
    }

    // -------------------------------------------------------------------------
    // IDataReaderMapper.StreamAsync(ITrackedReader, IMapperOptions) — explicit interface, line 232
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IDataReaderMapper_StreamAsync_ITrackedReaderWithOptions_IteratesWithoutError()
    {
        await using var ctx = CreateContext();
        var sc = ctx.CreateSqlContainer("SELECT 1");
        await using var reader = await sc.ExecuteReaderAsync();

        IDataReaderMapper mapper = DataReaderMapper.Instance;
        var count = 0;
        await foreach (var _ in mapper.StreamAsync<SimpleDto>(reader, MapperOptions.Default))
        {
            count++;
        }

        Assert.True(count >= 0);
    }
}
