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

    private class SampleEntity
    {
        public string? Name { get; set; }
        public int Age { get; set; }
        public bool IsActive { get; set; }
    }
}
