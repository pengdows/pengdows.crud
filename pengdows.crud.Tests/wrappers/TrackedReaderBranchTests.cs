using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using pengdows.crud.@internal;
using pengdows.crud.fakeDb;
using pengdows.crud.metrics;
using pengdows.crud.wrappers;
using Xunit;

namespace pengdows.crud.Tests.wrappers;

public class TrackedReaderBranchTests
{
    [Fact]
    public void Read_WhenDisposeThrows_StillReturnsFalse()
    {
        var reader = new ReadFalseDisposeThrowReader();
        var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false);

        var result = tracked.Read();

        Assert.False(result);
    }

    [Fact]
    public void GetValue_WhenTimestampFallbackAvailable_ReturnsDateTime()
    {
        using var reader = new ThrowingValueReader("timestamp without time zone", throwOnDataTypeName: false);
        using var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false);

        var value = tracked.GetValue(0);

        Assert.IsType<DateTime>(value);
    }

    [Fact]
    public void GetValue_WhenFallbackPathFails_RethrowsOriginalException()
    {
        using var reader = new ThrowingValueReader("timestamp without time zone", throwOnDataTypeName: true);
        using var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false);

        var ex = Assert.Throws<InvalidOperationException>(() => tracked.GetValue(0));
        Assert.Contains("GetValue failed", ex.Message);
    }

    [Fact]
    public void GetDateTime_WhenTimestampFallbackAvailable_ReturnsDateTime()
    {
        using var reader = new ThrowingDateTimeReader("timestamp without time zone", throwOnDataTypeName: false);
        using var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false);

        var value = tracked.GetDateTime(0);

        Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc), value);
    }

    [Fact]
    public void GetDateTime_WhenFallbackPathFails_RethrowsOriginalException()
    {
        using var reader = new ThrowingDateTimeReader("timestamp without time zone", throwOnDataTypeName: true);
        using var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false);

        var ex = Assert.Throws<InvalidOperationException>(() => tracked.GetDateTime(0));
        Assert.Contains("GetDateTime failed", ex.Message);
    }

    [Fact]
    public void GetFieldType_WhenDateTimeOffsetTimestampWithoutTimeZone_RemapsToDateTime()
    {
        using var reader = new FieldTypeReader(
            returnType: typeof(DateTimeOffset),
            throwOnGetFieldType: false,
            typeName: "timestamp without time zone",
            throwOnDataTypeName: false);
        using var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false);

        var type = tracked.GetFieldType(0);

        Assert.Equal(typeof(DateTime), type);
    }

    [Fact]
    public void GetFieldType_WhenGetFieldTypeThrowsAndTypeNameHasTimestamp_ReturnsDateTime()
    {
        using var reader = new FieldTypeReader(
            returnType: typeof(object),
            throwOnGetFieldType: true,
            typeName: "timestamp",
            throwOnDataTypeName: false);
        using var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false);

        var type = tracked.GetFieldType(0);

        Assert.Equal(typeof(DateTime), type);
    }

    [Fact]
    public void GetFieldType_WhenAllFallbacksFail_RethrowsOriginalException()
    {
        using var reader = new FieldTypeReader(
            returnType: typeof(object),
            throwOnGetFieldType: true,
            typeName: "timestamp",
            throwOnDataTypeName: true);
        using var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false);

        var ex = Assert.Throws<InvalidOperationException>(() => tracked.GetFieldType(0));
        Assert.Contains("GetFieldType failed", ex.Message);
    }

    [Fact]
    public void Dispose_WithAsyncOnlyLocker_UsesAsyncFallbackPath()
    {
        var locker = new AsyncOnlyLocker();
        using var reader = new fakeDbDataReader(new[] { new Dictionary<string, object> { ["Value"] = 1 } });
        using var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(), locker, false);

        tracked.Dispose();

        Assert.True(locker.WasDisposed);
    }

    [Fact]
    public void Dispose_WithNullLocker_DoesNotThrow()
    {
        using var reader = new fakeDbDataReader(new[] { new Dictionary<string, object> { ["Value"] = 1 } });
        var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(), null!, false);

        tracked.Dispose();
    }

    [Fact]
    public void Dispose_WithThrowingCommand_SwallowsCommandCleanupFailures()
    {
        using var reader = new fakeDbDataReader(new[] { new Dictionary<string, object> { ["Value"] = 1 } });
        var command = new ThrowingDbCommand();
        using var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false,
            command: command);

        tracked.Dispose();
    }

    [Fact]
    public void ReadAndDispose_RecordsRowsReadAndRowsAffected()
    {
        var metrics = new MetricsCollector(MetricsOptions.Default);
        using var reader = new RecordsAffectedReader();
        using var tracked = new TrackedReader(reader, Mock.Of<ITrackedConnection>(), Mock.Of<IAsyncDisposable>(), false,
            metricsCollector: metrics);

        Assert.True(tracked.Read());
        Assert.False(tracked.Read());

        var snapshot = metrics.CreateSnapshot();
        Assert.Equal(1, snapshot.RowsReadTotal);
        Assert.Equal(2, snapshot.RowsAffectedTotal);
    }

    private sealed class AsyncOnlyLocker : IAsyncDisposable
    {
        public bool WasDisposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            WasDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ReadFalseDisposeThrowReader : fakeDbDataReader
    {
        public ReadFalseDisposeThrowReader() : base(Array.Empty<Dictionary<string, object>>())
        {
        }

        public override bool Read()
        {
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            throw new InvalidOperationException("dispose failed");
        }
    }

    private sealed class ThrowingValueReader : fakeDbDataReader
    {
        private readonly string _typeName;
        private readonly bool _throwOnDataTypeName;

        public ThrowingValueReader(string typeName, bool throwOnDataTypeName)
            : base(new[] { new Dictionary<string, object> { ["v"] = DateTime.UtcNow } })
        {
            _typeName = typeName;
            _throwOnDataTypeName = throwOnDataTypeName;
        }

        public override object GetValue(int i)
        {
            throw new InvalidOperationException("GetValue failed");
        }

        public override string GetDataTypeName(int i)
        {
            if (_throwOnDataTypeName)
            {
                throw new InvalidOperationException("GetDataTypeName failed");
            }

            return _typeName;
        }

        public override T GetFieldValue<T>(int ordinal)
        {
            if (typeof(T) == typeof(DateTime))
            {
                return (T)(object)new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            }

            return base.GetFieldValue<T>(ordinal);
        }
    }

    private sealed class ThrowingDateTimeReader : fakeDbDataReader
    {
        private readonly string _typeName;
        private readonly bool _throwOnDataTypeName;

        public ThrowingDateTimeReader(string typeName, bool throwOnDataTypeName)
            : base(new[] { new Dictionary<string, object> { ["v"] = DateTime.UtcNow } })
        {
            _typeName = typeName;
            _throwOnDataTypeName = throwOnDataTypeName;
        }

        public override DateTime GetDateTime(int ordinal)
        {
            throw new InvalidOperationException("GetDateTime failed");
        }

        public override string GetDataTypeName(int i)
        {
            if (_throwOnDataTypeName)
            {
                throw new InvalidOperationException("GetDataTypeName failed");
            }

            return _typeName;
        }

        public override T GetFieldValue<T>(int ordinal)
        {
            if (typeof(T) == typeof(DateTime))
            {
                return (T)(object)new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            }

            return base.GetFieldValue<T>(ordinal);
        }
    }

    private sealed class FieldTypeReader : fakeDbDataReader
    {
        private readonly Type _returnType;
        private readonly bool _throwOnGetFieldType;
        private readonly string _typeName;
        private readonly bool _throwOnDataTypeName;

        public FieldTypeReader(Type returnType, bool throwOnGetFieldType, string typeName, bool throwOnDataTypeName)
            : base(new[] { new Dictionary<string, object> { ["v"] = DateTime.UtcNow } })
        {
            _returnType = returnType;
            _throwOnGetFieldType = throwOnGetFieldType;
            _typeName = typeName;
            _throwOnDataTypeName = throwOnDataTypeName;
        }

        public override Type GetFieldType(int ordinal)
        {
            if (_throwOnGetFieldType)
            {
                throw new InvalidOperationException("GetFieldType failed");
            }

            return _returnType;
        }

        public override string GetDataTypeName(int i)
        {
            if (_throwOnDataTypeName)
            {
                throw new InvalidOperationException("GetDataTypeName failed");
            }

            return _typeName;
        }
    }

    private sealed class RecordsAffectedReader : fakeDbDataReader
    {
        public RecordsAffectedReader() : base(new[] { new Dictionary<string, object> { ["Value"] = 1 } })
        {
        }

        public override int RecordsAffected => 2;
    }

    private sealed class ThrowingDbCommand : DbCommand
    {
        private sealed class ThrowingParameterCollection : DbParameterCollection
        {
            public override int Count => 0;
            public override object SyncRoot => this;
            public override int Add(object value) => 0;
            public override void AddRange(Array values) { }
            public override void Clear()
            {
                throw new InvalidOperationException("clear failed");
            }

            public override bool Contains(object value) => false;
            public override bool Contains(string value) => false;
            public override void CopyTo(Array array, int index) { }
            public override System.Collections.IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
            public override int IndexOf(object value) => -1;
            public override int IndexOf(string parameterName) => -1;
            public override void Insert(int index, object value) { }
            public override void Remove(object value) { }
            public override void RemoveAt(int index) { }
            public override void RemoveAt(string parameterName) { }
            protected override DbParameter GetParameter(int index) => throw new IndexOutOfRangeException();
            protected override DbParameter GetParameter(string parameterName) => throw new IndexOutOfRangeException();
            protected override void SetParameter(int index, DbParameter value) { }
            protected override void SetParameter(string parameterName, DbParameter value) { }
        }

        private readonly DbParameterCollection _parameters = new ThrowingParameterCollection();
        [AllowNull]
        public override string CommandText { get; set; } = string.Empty;
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        protected override DbConnection? DbConnection
        {
            get => null;
            set => throw new InvalidOperationException("connection reset failed");
        }

        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => null;
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                throw new InvalidOperationException("dispose failed");
            }

            base.Dispose(disposing);
        }
    }
}
