#region

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using pengdow.crud.FakeDb;
using Xunit;

#endregion

namespace pengdow.crud.Tests;

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

        public void Close() { }
        public DataTable GetSchemaTable() => throw new NotImplementedException();
        public bool NextResult() => false;
        public bool Read() => false;

        public void Dispose() { }
        public string GetName(int i) => string.Empty;
        public string GetDataTypeName(int i) => string.Empty;
        public Type GetFieldType(int i) => typeof(object);
        public object GetValue(int i) => null;
        public int GetValues(object[] values) => 0;
        public int GetOrdinal(string name) => -1;
        public bool GetBoolean(int i) => false;
        public byte GetByte(int i) => 0;
        public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferOffset, int length) => 0;
        public char GetChar(int i) => '\0';
        public long GetChars(int i, long fieldOffset, char[]? buffer, int bufferOffset, int length) => 0;
        public Guid GetGuid(int i) => Guid.Empty;
        public short GetInt16(int i) => 0;
        public int GetInt32(int i) => 0;
        public long GetInt64(int i) => 0;
        public float GetFloat(int i) => 0;
        public double GetDouble(int i) => 0;
        public string GetString(int i) => string.Empty;
        public decimal GetDecimal(int i) => 0;
        public DateTime GetDateTime(int i) => DateTime.MinValue;
        public IDataReader GetData(int i) => this;
        public bool IsDBNull(int i) => true;

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