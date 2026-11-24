using System;
using System.IO;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests.typesystem;

/// <summary>
/// Review finding (Ring 2): ByteArrayCoercion caught every exception from reading a Stream and reported "cannot convert",
/// so a dropped connection during a blob read lost its IOException. An I/O failure is not a conversion failure: it must
/// surface as the IOException itself.
/// </summary>
public class ByteArrayStreamFailureTests
{
    private sealed class FailingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new IOException("connection reset while reading the blob");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public void ByteArrayCoercion_StreamWithIoFailure_ThrowsTheIoException()
    {
        using var stream = new FailingStream();
        var src = new DbValue(stream, typeof(Stream));

        var ex = Assert.Throws<IOException>(() => new ByteArrayCoercion().TryRead(in src, out _));
        Assert.Contains("connection reset", ex.Message);
    }
}
