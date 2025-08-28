#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdows.crud.FakeDb;
using pengdows.crud.attributes;
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
    public async Task LoadObjectsFromDataReaderAsync_Interface_MapsFields()
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

        IDataReaderMapper mapper = new DataReaderMapper();
        var result = await mapper.LoadObjectsFromDataReaderAsync<SampleEntity>(reader);

        Assert.Single(result);
        Assert.Equal("John", result[0].Name);
        Assert.Equal(30, result[0].Age);
        Assert.True(result[0].IsActive);
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

    [Fact]
    public async Task StreamAsync_StreamsObjects()
    {
        var reader = new FakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "John",
                ["Age"] = 30,
                ["IsActive"] = true
            },
            new Dictionary<string, object>
            {
                ["Name"] = "Jane",
                ["Age"] = 25,
                ["IsActive"] = false
            }
        });

        var results = new List<SampleEntity>();
        await foreach (var item in DataReaderMapper.StreamAsync<SampleEntity>(reader))
        {
            results.Add(item);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("John", results[0].Name);
        Assert.Equal("Jane", results[1].Name);
    }
    [Fact]
    public async Task LoadAsync_WithNamePolicy_MapsSnakeCaseFields()
    {
        var reader = new FakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["first_name"] = "John"
            }
        });

        Func<string, string> snakeToPascal = name =>
        {
            var parts = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
            var result = string.Empty;
            foreach (var part in parts)
            {
                result += char.ToUpperInvariant(part[0]) + part.Substring(1);
            }

            return result;
        };

        var options = new MapperOptions(NamePolicy: snakeToPascal);
        var result = await DataReaderMapper.LoadAsync<SnakeEntity>(reader, options);

        Assert.Single(result);
        Assert.Equal("John", result[0].FirstName);
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_WithoutNamePolicy_IgnoresSnakeCaseFields()
    {
        var reader = new FakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["first_name"] = "John"
            }
        });

        var result = await DataReaderMapper.LoadObjectsFromDataReaderAsync<SnakeEntity>(reader);

        Assert.Single(result);
        Assert.Null(result[0].FirstName);
    }

    [Fact]
    public async Task LoadAsync_ColumnsOnly_MapsAnnotatedProperty()
    {
        var reader = new FakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "Jane",
                ["Age"] = 25
            }
        });

        var options = new MapperOptions(ColumnsOnly: true);
        var result = await DataReaderMapper.LoadAsync<ColumnsOnlyEntity>(reader, options);

        Assert.Single(result);
        Assert.Equal("Jane", result[0].Name);
    }

    [Fact]
    public async Task LoadAsync_ColumnsOnly_IgnoresNonAnnotatedProperty()
    {
        var reader = new FakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "Jane",
                ["Age"] = 25
            }
        });

        var options = new MapperOptions(ColumnsOnly: true);
        var result = await DataReaderMapper.LoadAsync<ColumnsOnlyEntity>(reader, options);

        Assert.Single(result);
        Assert.Equal(0, result[0].Age);
    }

    private class SampleEntity
    {
        public string Name { get; set; }
        public int Age { get; set; }
        public bool IsActive { get; set; }
    }

    private class SnakeEntity
    {
        public string? FirstName { get; set; }
    }

    private class ColumnsOnlyEntity
    {
        [Column("Name", DbType.String)]
        public string? Name { get; set; }

        public int Age { get; set; }
    }
}