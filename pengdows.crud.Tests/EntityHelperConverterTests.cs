using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud.attributes;
using pengdows.crud.fakeDb;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests;

public class EntityHelperConverterTests : SqlLiteContextTestBase
{
    public EntityHelperConverterTests()
    {
        TypeMap.Register<EnumEntity>();
        TypeMap.Register<JsonEntity>();
        TypeMap.Register<ByteArrayEntity>();
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
    public void MapReaderToObject_InvalidJson_ReturnsNull()
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
        var entity = helper.MapReaderToObject(reader);
        Assert.Null(entity.Data);
    }

    [Fact]
    public void MapReaderToObject_ByteArray_UsesGetBytes()
    {
        var helper = new EntityHelper<ByteArrayEntity, int>(Context);
        var bytes = new byte[] { 1, 2, 3, 4 };
        using var reader = CreateNonDbReader(bytes);
        reader.Read();

        var entity = helper.MapReaderToObject(reader);

        Assert.Equal(bytes, entity.Data);
        Assert.True(reader.BytesReadCount > 0);
    }

    [Fact]
    public void MapReaderToObject_ByteArray_GetBytesFailure_Throws()
    {
        var helper = new EntityHelper<ByteArrayEntity, int>(Context);
        using var reader = CreateThrowingReader(new byte[] { 1, 2, 3 });
        reader.Read();

        Assert.Throws<InvalidOperationException>(() => helper.MapReaderToObject(reader));
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

        [Json] [Column("Data", DbType.String)] public Payload? Data { get; set; }
    }

    private class Payload
    {
        public string Name { get; set; } = string.Empty;
    }

    [Table("ByteArrayEntity")]
    private class ByteArrayEntity
    {
        [Id(false)]
        [Column("Id", DbType.Int32)]
        public int Id { get; set; }

        [Column("Data", DbType.Binary)]
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    internal sealed class FakeTrackedReader : fakeDbDataReader, ITrackedReader
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

    private static NonDbTrackedReader CreateNonDbReader(byte[] data)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Data", typeof(byte[]));
        table.Rows.Add(1, data);
        return new NonDbTrackedReader(new DataTableReader(table));
    }

    private static NonDbTrackedReader CreateThrowingReader(byte[] data)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Columns.Add("Data", typeof(byte[]));
        table.Rows.Add(1, data);
        return new ThrowingTrackedReader(new DataTableReader(table));
    }

    private class NonDbTrackedReader : ITrackedReader
    {
        private readonly IDataReader _inner;
        private bool _disposed;
        private int _bytesReadCount;

        public NonDbTrackedReader(IDataReader inner)
        {
            _inner = inner;
        }

        public int BytesReadCount => _bytesReadCount;

        public int FieldCount => _inner.FieldCount;

        public object this[int i] => _inner[i];

        public object this[string name] => _inner[name];

        public int Depth => _inner.Depth;

        public bool IsClosed => _inner.IsClosed;

        public int RecordsAffected => _inner.RecordsAffected;

        public void Close()
        {
            _inner.Close();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _inner.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public bool GetBoolean(int i)
        {
            return _inner.GetBoolean(i);
        }

        public byte GetByte(int i)
        {
            return _inner.GetByte(i);
        }

        public virtual long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length)
        {
            _bytesReadCount++;
            return _inner.GetBytes(i, fieldOffset, buffer, bufferoffset, length);
        }

        public char GetChar(int i)
        {
            return _inner.GetChar(i);
        }

        public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length)
        {
            return _inner.GetChars(i, fieldoffset, buffer, bufferoffset, length);
        }

        public IDataReader GetData(int i)
        {
            return _inner.GetData(i);
        }

        public string GetDataTypeName(int i)
        {
            return _inner.GetDataTypeName(i);
        }

        public DateTime GetDateTime(int i)
        {
            return _inner.GetDateTime(i);
        }

        public decimal GetDecimal(int i)
        {
            return _inner.GetDecimal(i);
        }

        public double GetDouble(int i)
        {
            return _inner.GetDouble(i);
        }

        public Type GetFieldType(int i)
        {
            return _inner.GetFieldType(i);
        }

        public float GetFloat(int i)
        {
            return _inner.GetFloat(i);
        }

        public Guid GetGuid(int i)
        {
            return _inner.GetGuid(i);
        }

        public short GetInt16(int i)
        {
            return _inner.GetInt16(i);
        }

        public int GetInt32(int i)
        {
            return _inner.GetInt32(i);
        }

        public long GetInt64(int i)
        {
            return _inner.GetInt64(i);
        }

        public string GetName(int i)
        {
            return _inner.GetName(i);
        }

        public int GetOrdinal(string name)
        {
            return _inner.GetOrdinal(name);
        }

        public DataTable? GetSchemaTable()
        {
            return _inner.GetSchemaTable();
        }

        public string GetString(int i)
        {
            return _inner.GetString(i);
        }

        public object GetValue(int i)
        {
            return _inner.GetValue(i);
        }

        public int GetValues(object[] values)
        {
            return _inner.GetValues(values);
        }

        public bool IsDBNull(int i)
        {
            return _inner.IsDBNull(i);
        }

        public bool NextResult()
        {
            return _inner.NextResult();
        }

        public bool Read()
        {
            return _inner.Read();
        }

        public Task<bool> ReadAsync()
        {
            return Task.FromResult(Read());
        }

        public Task<bool> ReadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Read());
        }
    }

    private sealed class ThrowingTrackedReader : NonDbTrackedReader
    {
        public ThrowingTrackedReader(IDataReader inner) : base(inner)
        {
        }

        public override long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length)
        {
            throw new InvalidOperationException("GetBytes failed.");
        }
    }
}
