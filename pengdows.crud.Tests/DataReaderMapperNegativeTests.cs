using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using pengdows.crud.fakeDb;
using pengdows.crud.Tests.Logging;
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
        var reader = new fakeDbDataReader(rows);

        var originalLogger = TypeCoercionHelper.Logger;
        try
        {
            TypeCoercionHelper.Logger = NullLogger.Instance;

            var result = await DataReaderMapper.LoadObjectsFromDataReaderAsync<SampleEntity>(reader);

            Assert.Single(result);
            Assert.Equal("Alice", result[0].Name);
            Assert.Equal(0, result[0].Age); // default due to failed conversion
        }
        finally
        {
            TypeCoercionHelper.Logger = originalLogger;
        }
    }

    [Fact]
    public async Task LoadAsync_NonStrict_LogsMappingWarning()
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

        var loggerProvider = new ListLoggerProvider();
        using var loggerFactory = new LoggerFactory(new[] { loggerProvider });
        var logger = loggerFactory.CreateLogger("DataReaderMapper");
        var originalLogger = TypeCoercionHelper.Logger;

        try
        {
            TypeCoercionHelper.Logger = logger;
            var reader = new fakeDbDataReader(rows);

            await DataReaderMapper.LoadAsync<SampleEntity>(reader, MapperOptions.Default);
        }
        finally
        {
            TypeCoercionHelper.Logger = originalLogger;
        }

        Assert.Contains(loggerProvider.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Failed to map column"));
    }

    [Fact]
    public async Task LoadAsync_Strict_DoesNotLogMappingWarning()
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

        var loggerProvider = new ListLoggerProvider();
        using var loggerFactory = new LoggerFactory(new[] { loggerProvider });
        var logger = loggerFactory.CreateLogger("DataReaderMapper");
        var originalLogger = TypeCoercionHelper.Logger;

        try
        {
            TypeCoercionHelper.Logger = logger;
            var reader = new fakeDbDataReader(rows);
            var options = new MapperOptions(true);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                DataReaderMapper.LoadAsync<SampleEntity>(reader, options));
        }
        finally
        {
            TypeCoercionHelper.Logger = originalLogger;
        }

        Assert.DoesNotContain(loggerProvider.Entries, entry => entry.Level == LogLevel.Warning);
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

        var reader = new fakeDbDataReader(rows);
        var options = new MapperOptions(true);
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

        var reader = new fakeDbDataReader(rows);
        var options = new MapperOptions(true);
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
            return DBNull.Value;
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

        public object this[int i] => DBNull.Value;
        public object this[string name] => DBNull.Value;
    }
}
