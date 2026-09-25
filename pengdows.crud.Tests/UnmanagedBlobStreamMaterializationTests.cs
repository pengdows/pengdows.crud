using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using pengdows.crud.fakeDb;
using System.Runtime.InteropServices;
using pengdows.crud.attributes;
using pengdows.crud.enums;
using pengdows.crud.types.coercion;
using pengdows.crud.types.converters;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// CONFIRMED live (DuckDB.NET, PortableAdvancedTypeRoundTripTests): DuckDB returns a BLOB as an
/// UnmanagedMemoryStream over memory owned by the data reader. Handing that stream to the entity
/// meant the caller read zeros once the reader moved on. Both read paths must copy it into a
/// managed stream while the reader is still alive.
/// </summary>
public class UnmanagedBlobStreamMaterializationTests
{
    private static readonly byte[] Expected = { 9, 8, 7, 6 };

    [Fact]
    public void BlobStreamCoercion_UnmanagedStream_IsCopiedBeforeReaderReleasesBuffer()
    {
        using var buffer = new ReaderOwnedBuffer(Expected);

        Assert.True(new BlobStreamCoercion().TryRead(new DbValue(buffer.Stream), out var value));
        buffer.Invalidate();

        Assert.Equal(Expected, ReadAll(value));
    }

    [Fact]
    public void BlobStreamConverter_UnmanagedStream_IsCopiedBeforeReaderReleasesBuffer()
    {
        using var buffer = new ReaderOwnedBuffer(Expected);

        Assert.True(new BlobStreamConverter().TryConvertFromProvider(buffer.Stream, SupportedDatabase.DuckDB,
            out var value));
        buffer.Invalidate();

        Assert.Equal(Expected, ReadAll(value));
    }

    // The live failure: DuckDB reports the column's field type as Stream, so the compiled mapper's
    // same-type fast path assigned the provider stream to the property without any coercion.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompiledMapper_UnmanagedStream_IsCopiedBeforeReaderReleasesBuffer(bool reportFieldTypeAsStream)
    {
        using var buffer = new ReaderOwnedBuffer(Expected);
        var reader = new StreamFieldReader(buffer.Stream, reportFieldTypeAsStream);

        var result = await DataReaderMapper.LoadAsync<StreamHolder>(reader, MapperOptions.Default);
        buffer.Invalidate();

        var holder = Assert.Single(result);
        Assert.Equal(Expected, ReadAll(holder.Content!));
    }

    // Same failure through TableGateway, which maps with CompiledMapperFactory rather than
    // DataReaderMapper.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TableGateway_UnmanagedStream_IsCopiedBeforeReaderReleasesBuffer(bool reportFieldTypeAsStream)
    {
        using var buffer = new ReaderOwnedBuffer(Expected);
        var factory = new fakeDbFactory(SupportedDatabase.DuckDB);
        factory.Connections.Add(new fakeDbConnection { EmulatedProduct = SupportedDatabase.DuckDB });
        var execConn = new fakeDbConnection { EmulatedProduct = SupportedDatabase.DuckDB };
        execConn.EnqueueReaderResult(new StreamFieldReader(buffer.Stream, reportFieldTypeAsStream, "content",
            new Dictionary<string, object> { ["id"] = 1 }));
        factory.Connections.Add(execConn);
        using var context = new DatabaseContext("Data Source=test;EmulatedProduct=DuckDB", factory);
        var gateway = new TableGateway<StreamEntity, int>(context);

        var actual = await gateway.RetrieveOneAsync(1);
        buffer.Invalidate();

        Assert.NotNull(actual);
        Assert.Equal(Expected, ReadAll(actual!.Content));
    }

    [Table("stream_entity")]
    public sealed class StreamEntity
    {
        [Id]
        [Column("id", DbType.Int32)]
        public int Id { get; set; }

        [Column("content", DbType.Object)]
        public Stream Content { get; set; } = null!;
    }

    public sealed class StreamHolder
    {
        public Stream? Content { get; set; }
    }

    private sealed class StreamFieldReader : fakeDbDataReader
    {
        private readonly bool _reportFieldTypeAsStream;
        private readonly string _streamColumn;

        public StreamFieldReader(Stream value, bool reportFieldTypeAsStream, string streamColumn = "Content",
            Dictionary<string, object>? otherColumns = null)
            : base(new[] { Row(value, streamColumn, otherColumns) })
        {
            _reportFieldTypeAsStream = reportFieldTypeAsStream;
            _streamColumn = streamColumn;
        }

        private static Dictionary<string, object> Row(Stream value, string streamColumn,
            Dictionary<string, object>? otherColumns)
        {
            var row = new Dictionary<string, object>(otherColumns ?? new Dictionary<string, object>())
            {
                [streamColumn] = value
            };
            return row;
        }

        public override Type GetFieldType(int ordinal) =>
            _reportFieldTypeAsStream && GetName(ordinal) == _streamColumn ? typeof(Stream) : base.GetFieldType(ordinal);
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    // Stands in for the DuckDB reader's vector memory: the stream is a view over it, and the
    // reader zeroes/reuses that memory once it advances.
    private sealed class ReaderOwnedBuffer : IDisposable
    {
        private readonly NativeBuffer _buffer;
        private readonly int _length;

        public ReaderOwnedBuffer(byte[] bytes)
        {
            _length = bytes.Length;
            _buffer = new NativeBuffer(bytes.Length);
            _buffer.WriteArray(0, bytes, 0, bytes.Length);
            Stream = new UnmanagedMemoryStream(_buffer, 0, bytes.Length);
        }

        public UnmanagedMemoryStream Stream { get; }

        public void Invalidate() => _buffer.WriteArray(0, new byte[_length], 0, _length);

        public void Dispose()
        {
            Stream.Dispose();
            _buffer.Dispose();
        }
    }

    private sealed class NativeBuffer : SafeBuffer
    {
        public NativeBuffer(int length) : base(true)
        {
            SetHandle(Marshal.AllocHGlobal(length));
            Initialize((ulong)length);
        }

        protected override bool ReleaseHandle()
        {
            Marshal.FreeHGlobal(handle);
            return true;
        }
    }
}
