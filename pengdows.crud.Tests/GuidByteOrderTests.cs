using System;
using Xunit;

namespace pengdows.crud.Tests;

/// <summary>
/// DRY-009 (2): every 16-byte form a provider can return (byte[], ReadOnlyMemory, ArraySegment) is
/// decoded in the dialect's Guid byte order. An ArraySegment fell through to the registered Guid
/// coercion, which knows only .NET's mixed-endian order.
/// </summary>
public class GuidByteOrderTests
{
    private static readonly byte[] Bytes =
        { 0x01, 0x90, 0xf3, 0xa1, 0x7b, 0x2c, 0x7d, 0x3e, 0x8f, 0x40, 0x12, 0x34, 0x56, 0x78, 0x9a, 0xbc };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryByteForm_DecodesInTheDialectsOrder(bool bigEndian)
    {
        var options = TypeCoercionOptions.Default with { GuidBytesBigEndian = bigEndian };
        var expected = new Guid(Bytes, bigEndian);

        Assert.Equal(expected, TypeCoercionHelper.Coerce(Bytes, typeof(byte[]), typeof(Guid), options));
        Assert.Equal(expected, TypeCoercionHelper.Coerce(new ReadOnlyMemory<byte>(Bytes), typeof(ReadOnlyMemory<byte>), typeof(Guid), options));
        Assert.Equal(expected, TypeCoercionHelper.Coerce(new ArraySegment<byte>(Bytes), typeof(ArraySegment<byte>), typeof(Guid), options));
    }
}
