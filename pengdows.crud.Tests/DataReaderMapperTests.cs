#region

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using pengdows.crud.FakeDb;
using Xunit;

#endregion

namespace pengdows.crud.Tests;

public class DataReaderMapperTests
{
    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_MapsMatchingFields()
    {
        var reader = new FakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "John",
                ["Age"] = 30,
                ["IsActive"] = true
            }
        });

        var result = await DataReaderMapper.LoadObjectsFromDataReaderAsync<SampleEntity>(reader);

        Assert.Single(result);
        Assert.Equal("John", result[0].Name);
        Assert.Equal(30, result[0].Age);
        Assert.True(result[0].IsActive);
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_IgnoresUnmappedFields()
    {
        var reader = new FakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Unrelated"] = "Ignore",
                ["Name"] = "Jane"
            }
        });

        var result = await DataReaderMapper.LoadObjectsFromDataReaderAsync<SampleEntity>(reader);

        Assert.Single(result);
        Assert.Equal("Jane", result[0].Name);
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_HandlesDbNullsGracefully()
    {
        var reader = new FakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = DBNull.Value,
                ["Age"] = DBNull.Value,
                ["IsActive"] = DBNull.Value
            }
        });

        var result = await DataReaderMapper.LoadObjectsFromDataReaderAsync<SampleEntity>(reader);

        Assert.Single(result);
        Assert.Null(result[0].Name);
        Assert.Equal(0, result[0].Age); // default(int)
        Assert.False(result[0].IsActive); // default(bool)
    }

    private class SampleEntity
    {
        public string Name { get; set; }
        public int Age { get; set; }
        public bool IsActive { get; set; }
    }
}