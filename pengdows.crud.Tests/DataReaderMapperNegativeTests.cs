using System;
using System.Collections.Generic;
using System.Data;
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

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_NonDbDataReader_Throws()
    {
        IDataReader reader = new NonDbDataReader();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await DataReaderMapper.LoadObjectsFromDataReaderAsync<SampleEntity>(reader);
        });
    }

    [Fact]
    public async Task StreamAsync_NonDbDataReader_Throws()
    {
        IDataReader reader = new NonDbDataReader();

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var _ in DataReaderMapper.StreamAsync<SampleEntity>(reader))
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
        public object? GetValue(int i) => null;
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

        public object? this[int i] => null;
        public object? this[string name] => null;
    }
}
