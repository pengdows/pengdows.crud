#region

using System;
using System.Collections.Generic;
using System.Data;
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

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_SwallowsMappingErrors()
    {
        var reader = new FakeDbDataReader(new[]
        {
            new Dictionary<string, object>
            {
                ["Name"] = "John",
                ["Age"] = "not-a-number",
                ["IsActive"] = true
            }
        });

        var result = await DataReaderMapper.LoadObjectsFromDataReaderAsync<SampleEntity>(reader);

        Assert.Single(result);
        Assert.Equal("John", result[0].Name);
        Assert.Equal(0, result[0].Age); // default due to conversion failure
        Assert.True(result[0].IsActive);
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_NullReader_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await DataReaderMapper.LoadObjectsFromDataReaderAsync<SampleEntity>(null!));
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_ThrowsForNonDbDataReader()
    {
        var reader = new NonDbDataReader();
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await DataReaderMapper.LoadObjectsFromDataReaderAsync<SampleEntity>(reader));
    }

    private class NonDbDataReader : IDataReader
    {
        public int FieldCount => 0;
        public bool IsClosed => false;
        public int RecordsAffected => 0;
        public int Depth => 0;

        public void Close()
        {
        }

        public DataTable GetSchemaTable()
        {
            throw new NotImplementedException();
        }

        public bool NextResult()
        {
            return false;
        }

        public bool Read()
        {
            return false;
        }

        public void Dispose()
        {
        }

        public string GetName(int i)
        {
            return string.Empty;
        }

        public string GetDataTypeName(int i)
        {
            return string.Empty;
        }

        public Type GetFieldType(int i)
        {
            return typeof(object);
        }

        public object GetValue(int i)
        {
            return null;
        }

        public int GetValues(object[] values)
        {
            return 0;
        }

        public int GetOrdinal(string name)
        {
            return -1;
        }

        public bool GetBoolean(int i)
        {
            return false;
        }

        public byte GetByte(int i)
        {
            return 0;
        }

        public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length)
        {
            return 0;
        }

        public char GetChar(int i)
        {
            return '\0';
        }

        public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length)
        {
            return 0;
        }

        public Guid GetGuid(int i)
        {
            return Guid.Empty;
        }

        public short GetInt16(int i)
        {
            return 0;
        }

        public int GetInt32(int i)
        {
            return 0;
        }

        public long GetInt64(int i)
        {
            return 0;
        }

        public float GetFloat(int i)
        {
            return 0;
        }

        public double GetDouble(int i)
        {
            return 0;
        }

        public string GetString(int i)
        {
            return string.Empty;
        }

        public decimal GetDecimal(int i)
        {
            return 0;
        }

        public DateTime GetDateTime(int i)
        {
            return DateTime.MinValue;
        }

        public IDataReader GetData(int i)
        {
            return this;
        }

        public bool IsDBNull(int i)
        {
            return true;
        }

        public object this[int i] => null;
        public object this[string name] => null;
    }

    private class SampleEntity
    {
        public string Name { get; set; }
        public int Age { get; set; }
        public bool IsActive { get; set; }
    }
}