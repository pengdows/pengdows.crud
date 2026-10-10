using System.Collections;
using System.Data.Common;
using BenchmarkDotNet.Attributes;
using pengdows.crud.fakeDb;
using pengdows.crud.@internal;
using pengdows.crud.wrappers;

namespace CrudBenchmarks;

/// <summary>
/// What every query pays around its real work, with no metrics collector (the default): a tracked reader built, read
/// and disposed, and a tracked connection opened and closed. No database is involved, so what is timed is the wrapper
/// code itself, and what a change to it must not slow down.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 15)]
public class TrackedWrapperLifecycleBenchmarks
{
    private TrackedConnection _connection = null!;
    private readonly IAsyncDisposable _locker = new NoopDisposable();

    [GlobalSetup]
    public void Setup() => _connection = new TrackedConnection(new fakeDbConnection());

    [GlobalCleanup]
    public void Cleanup() => _connection.Dispose();

    [Benchmark]
    public int ReaderLifecycle()
    {
        var reader = new TrackedReader(new OneRowReader(), _connection, _locker, false);
        var rows = 0;
        while (reader.Read())
        {
            rows++;
        }

        reader.Dispose();
        return rows;
    }

    [Benchmark]
    public bool ConnectionLifecycle()
    {
        using var connection = new TrackedConnection(new fakeDbConnection());
        connection.Open();
        connection.Close();
        return connection.State == System.Data.ConnectionState.Closed;
    }

    private sealed class NoopDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // One row and then the end, with nothing behind it: a provider reader built by a database driver would cost far
    // more than the wrapper being timed, so the reader here costs one small allocation.
    private sealed class OneRowReader : DbDataReader
    {
        private bool _read;

        public override bool Read()
        {
            var first = !_read;
            _read = true;
            return first;
        }

        public override int FieldCount => 1;
        public override bool HasRows => true;
        public override bool IsClosed => false;
        public override int Depth => 0;
        public override int RecordsAffected => -1;
        public override bool NextResult() => false;
        public override string GetName(int ordinal) => "id";
        public override int GetOrdinal(string name) => 0;
        public override Type GetFieldType(int ordinal) => typeof(int);
        public override string GetDataTypeName(int ordinal) => "int";
        public override bool IsDBNull(int ordinal) => false;
        public override object GetValue(int ordinal) => 1;
        public override int GetInt32(int ordinal) => 1;
        public override object this[int ordinal] => GetValue(ordinal);
        public override object this[string name] => GetValue(0);
        public override IEnumerator GetEnumerator() => throw new NotSupportedException();
        public override bool GetBoolean(int ordinal) => throw new NotSupportedException();
        public override byte GetByte(int ordinal) => throw new NotSupportedException();
        public override long GetBytes(int o, long d, byte[]? b, int bo, int l) => throw new NotSupportedException();
        public override char GetChar(int ordinal) => throw new NotSupportedException();
        public override long GetChars(int o, long d, char[]? b, int bo, int l) => throw new NotSupportedException();
        public override DateTime GetDateTime(int ordinal) => throw new NotSupportedException();
        public override decimal GetDecimal(int ordinal) => throw new NotSupportedException();
        public override double GetDouble(int ordinal) => throw new NotSupportedException();
        public override float GetFloat(int ordinal) => throw new NotSupportedException();
        public override Guid GetGuid(int ordinal) => throw new NotSupportedException();
        public override short GetInt16(int ordinal) => throw new NotSupportedException();
        public override long GetInt64(int ordinal) => throw new NotSupportedException();
        public override string GetString(int ordinal) => throw new NotSupportedException();
        public override int GetValues(object[] values) => throw new NotSupportedException();
    }
}
