using System;
using System.IO;
using pengdows.crud.fakeDb;
using pengdows.crud.types.coercion;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// REV-065: a Stream value is bound from its beginning (a seekable stream is rewound, so a
/// MemoryStream written and passed without seeking back sends its content), and a MemoryStream is
/// copied once, not buffered into a second MemoryStream first.
/// </summary>
[Collection("AllocationSerial")]
public sealed class LargeObjectParameterTests
{
    [Fact]
    public void SeekableStream_IsReadFromItsBeginning()
    {
        var stream = new MemoryStream();
        stream.Write(new byte[] { 1, 2, 3 });
        var parameter = new fakeDbParameter();

        Assert.True(LargeObjectParameter.TryMaterialize(parameter, stream));

        Assert.Equal(new byte[] { 1, 2, 3 }, parameter.Value);
    }

    [Fact]
    public void MemoryStream_IsCopiedOnce()
    {
        var content = new byte[64 * 1024];
        var stream = new MemoryStream(content);
        LargeObjectParameter.TryMaterialize(new fakeDbParameter(), stream);

        var before = GC.GetAllocatedBytesForCurrentThread();
        LargeObjectParameter.TryMaterialize(new fakeDbParameter(), stream);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < content.Length + 4096, $"{allocated} B for a {content.Length} B stream");
    }
}
