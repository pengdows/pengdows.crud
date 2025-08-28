using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.FakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DataReaderMapperNegativeTests
{
    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_InvalidFieldConversion_Ignored()
    {
        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "Alice",
                ["Age"] = "NaN",
                ["IsActive"] = true
            }
        };
        var reader = new FakeDbDataReader(rows);

        var result = await DataReaderMapper.LoadObjectsFromDataReaderAsync<SampleEntity>(reader);

        Assert.Single(result);
        Assert.Equal("Alice", result[0].Name);
        Assert.Equal(0, result[0].Age); // default due to failed conversion
    }

    [Fact]
    public async Task LoadAsync_WithStrict_ThrowsOnInvalidConversion()
    {
        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "Alice",
                ["Age"] = "NaN",
                ["IsActive"] = true
            }
        };

        var reader = new FakeDbDataReader(rows);
        var options = new MapperOptions(Strict: true);
        IDataReaderMapper mapper = new DataReaderMapper();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mapper.LoadAsync<SampleEntity>(reader, options));
    }

    [Fact]
    public async Task StreamAsync_WithStrict_ThrowsOnInvalidConversion()
    {
        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "Alice",
                ["Age"] = "NaN",
                ["IsActive"] = true
            }
        };

        var reader = new FakeDbDataReader(rows);
        var options = new MapperOptions(Strict: true);
        var stream = DataReaderMapper.StreamAsync<SampleEntity>(reader, options);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in stream)
            {
            }
        });
    }

    private class SampleEntity
    {
        public string? Name { get; set; }
        public int Age { get; set; }
        public bool IsActive { get; set; }
    }
}
