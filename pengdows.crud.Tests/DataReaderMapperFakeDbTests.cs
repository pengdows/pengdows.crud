using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using pengdows.crud;
using pengdows.crud.enums;
using pengdows.crud.fakeDb;
using Xunit;

namespace pengdows.crud.Tests;

public class DataReaderMapperFakeDbTests : IAsyncLifetime
{
    private fakeDbFactory _factory = null!;
    private fakeDbConnection _connection = null!;

    public Task InitializeAsync()
    {
        _factory = new fakeDbFactory(SupportedDatabase.Sqlite);
        _connection = (fakeDbConnection)_factory.CreateConnection();
        _connection.ConnectionString = "Data Source=test;EmulatedProduct=Sqlite";
        _connection.Open();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _connection.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_BindsColumnsToProperties()
    {
        _connection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["Id"] = 1, ["Name"] = "alpha" }
        });

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT Id, Name";
        await using var reader = (DbDataReader)await command.ExecuteReaderAsync(CommandBehavior.Default);

        var objects = await DataReaderMapper.LoadObjectsFromDataReaderAsync<EntityDto>(reader);

        Assert.Single(objects);
        Assert.Equal(1, objects[0].Id);
        Assert.Equal("alpha", objects[0].Name);
    }

    [Fact]
    public async Task StreamAsync_WithNamePolicy_RespectsCustomNames()
    {
        _connection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["foo_name"] = 42 }
        });

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT foo_name";
        await using var reader = (DbDataReader)await command.ExecuteReaderAsync(CommandBehavior.Default);

        var options = new MapperOptions
        {
            NamePolicy = s => s.Replace("_", string.Empty)
        };

        var list = await DataReaderMapper.LoadAsync<SnakeCaseDto>(reader, options);

        Assert.Single(list);
        Assert.Equal(42, list[0].FooName);
    }

    [Fact]
    public async Task LoadAsync_StrictMode_ThrowsWhenCoercionFails()
    {
        _connection.EnqueueReaderResult(new[]
        {
            new Dictionary<string, object?> { ["Count"] = "NaN" }
        });

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT Count";
        await using var reader = (DbDataReader)await command.ExecuteReaderAsync(CommandBehavior.Default);

        var options = new MapperOptions { Strict = true };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DataReaderMapper.LoadAsync<CountDto>(reader, options));
    }

    [Fact]
    public async Task LoadObjectsFromDataReaderAsync_WithNonDbReader_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => DataReaderMapper.LoadObjectsFromDataReaderAsync<EntityDto>(new NonDbReader()));
    }

    private sealed class EntityDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
    }

    private sealed class SnakeCaseDto
    {
        public int FooName { get; set; }
    }

    private sealed class CountDto
    {
        public int Count { get; set; }
    }

    private sealed class NonDbReader : IDataReader
    {
        public object this[int i] => throw new NotSupportedException();
        public object this[string name] => throw new NotSupportedException();
        public int Depth => throw new NotSupportedException();
        public bool IsClosed => true;
        public int RecordsAffected => throw new NotSupportedException();
        public int FieldCount => throw new NotSupportedException();
        public void Close() => throw new NotSupportedException();
        public void Dispose() { }
        public bool GetBoolean(int i) => throw new NotSupportedException();
        public byte GetByte(int i) => throw new NotSupportedException();
        public long GetBytes(int i, long fieldOffset, byte[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();
        public char GetChar(int i) => throw new NotSupportedException();
        public long GetChars(int i, long fieldoffset, char[]? buffer, int bufferoffset, int length) => throw new NotSupportedException();
        public IDataReader GetData(int i) => throw new NotSupportedException();
        public string GetDataTypeName(int i) => throw new NotSupportedException();
        public DateTime GetDateTime(int i) => throw new NotSupportedException();
        public decimal GetDecimal(int i) => throw new NotSupportedException();
        public double GetDouble(int i) => throw new NotSupportedException();
        public Type GetFieldType(int i) => throw new NotSupportedException();
        public float GetFloat(int i) => throw new NotSupportedException();
        public Guid GetGuid(int i) => throw new NotSupportedException();
        public short GetInt16(int i) => throw new NotSupportedException();
        public int GetInt32(int i) => throw new NotSupportedException();
        public long GetInt64(int i) => throw new NotSupportedException();
        public string GetName(int i) => throw new NotSupportedException();
        public int GetOrdinal(string name) => throw new NotSupportedException();
        public DataTable GetSchemaTable() => throw new NotSupportedException();
        public string GetString(int i) => throw new NotSupportedException();
        public object GetValue(int i) => throw new NotSupportedException();
        public int GetValues(object[] values) => throw new NotSupportedException();
        public bool IsDBNull(int i) => throw new NotSupportedException();
        public bool NextResult() => throw new NotSupportedException();
        public bool Read() => throw new NotSupportedException();
    }
}
