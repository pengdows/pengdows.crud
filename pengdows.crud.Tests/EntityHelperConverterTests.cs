using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.FakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class EntityHelperConverterTests : SqlLiteContextTestBase
{
    public EntityHelperConverterTests()
    {
        TypeMap.Register<EnumEntity>();
        TypeMap.Register<JsonEntity>();
    }

    [Fact]
    public void MapReaderToObject_ValidEnumString_SetsProperty()
    {
        var helper = new EntityHelper<EnumEntity, int>(Context);
        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["Color"] = "Green"
            }
        };
        using var reader = new FakeTrackedReader(rows);
        reader.Read();
        var entity = helper.MapReaderToObject(reader);
        Assert.Equal(Color.Green, entity.Color);
    }

    [Fact]
    public void MapReaderToObject_InvalidEnumString_Throws()
    {
        var helper = new EntityHelper<EnumEntity, int>(Context);
        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["Color"] = "Orange"
            }
        };
        using var reader = new FakeTrackedReader(rows);
        reader.Read();
        Assert.Throws<ArgumentException>(() => helper.MapReaderToObject(reader));
    }

    [Fact]
    public void MapReaderToObject_ValidJson_Deserializes()
    {
        var helper = new EntityHelper<JsonEntity, int>(Context);
        var json = "{\"Name\":\"Bob\"}";
        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["Data"] = json
            }
        };
        using var reader = new FakeTrackedReader(rows);
        reader.Read();
        var entity = helper.MapReaderToObject(reader);
        Assert.NotNull(entity.Data);
        Assert.Equal("Bob", entity.Data!.Name);
    }

    [Fact]
    public void MapReaderToObject_InvalidJson_Throws()
    {
        var helper = new EntityHelper<JsonEntity, int>(Context);
        var rows = new[]
        {
            new Dictionary<string, object>
            {
                ["Id"] = 1,
                ["Data"] = "{bad}"
            }
        };
        using var reader = new FakeTrackedReader(rows);
        reader.Read();
        Assert.Throws<JsonException>(() => helper.MapReaderToObject(reader));
    }

    [Table("EnumEntity")]
    private class EnumEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [EnumColumn(typeof(Color))]
        [Column("Color", DbType.String)]
        public Color Color { get; set; }
    }

    private enum Color
    {
        Red,
        Green,
        Blue
    }

    [Table("JsonEntity")]
    private class JsonEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Json]
        [Column("Data", DbType.String)]
        public Payload? Data { get; set; }
    }

    private class Payload
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class FakeTrackedReader : FakeDbDataReader, ITrackedReader
    {
        public FakeTrackedReader(IEnumerable<Dictionary<string, object>> rows) : base(rows)
        {
        }

        public new Task<bool> ReadAsync()
        {
            return base.ReadAsync(CancellationToken.None);
        }

        public override ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public override Type GetFieldType(int ordinal)
        {
            var value = GetValue(ordinal);
            return value?.GetType() ?? typeof(object);
        }
    }
}
